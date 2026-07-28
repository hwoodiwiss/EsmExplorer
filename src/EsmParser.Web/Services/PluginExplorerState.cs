using System.Globalization;
using EsmParser.Core;
using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;

namespace EsmParser.Web.Services;

/// <summary>
/// UI state for the explorer: the workspace of loaded plugins, the active plugin,
/// tree expansion/selection, and go-to-reference orchestration. All parsing and
/// resolution logic lives in the core library; this only holds display state.
/// </summary>
public sealed class PluginExplorerState : IAsyncDisposable
{
    /// <summary>Cache sizing for browser-backed sources: 128 KiB pages, at most 16 MiB resident.</summary>
    private const int CachePageSize = 128 * 1024;
    private const int CacheMaxPages = 128;

    /// <summary>Pseudo-offset keying the cached top-level node list in a tree's children map.</summary>
    private const long TopLevelKey = -1;

    private readonly Dictionary<PluginFile, TreeState> _trees = [];

    public PluginWorkspace Workspace { get; } = new();

    public PluginFile? ActivePlugin { get; private set; }

    public PluginNode? SelectedNode { get; private set; }

    /// <summary>The most recent load failure, for display.</summary>
    public ParseError? LastError { get; private set; }

    /// <summary>A user-facing notice (missing master, failed go-to, …).</summary>
    public string? Notice { get; private set; }

    /// <summary>Non-null while a long operation (index build) runs.</summary>
    public string? BusyMessage { get; private set; }

    public double? BusyProgress { get; private set; }

    /// <summary>Set when the tree should scroll a node into view; consumed by the explorer page.</summary>
    public long? PendingScrollOffset { get; private set; }

    public event Action? Changed;

    /// <summary>Adds plugins to the workspace (each wrapped in a page cache); the first success becomes active.</summary>
    public async Task AddSourcesAsync(IEnumerable<IDataSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        LastError = null;

        foreach (IDataSource source in sources)
        {
            var cached = new CachingDataSource(source, CachePageSize, CacheMaxPages);
            ParseResult<PluginFile> result = await Workspace.AddAsync(cached);
            if (result.TryGet(out PluginFile? plugin, out ParseError? error))
            {
                ActivePlugin ??= plugin;
            }
            else
            {
                LastError = error;
            }
        }

        NotifyChanged();
    }

    public async Task RemoveAsync(PluginFile plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        _trees.Remove(plugin);
        if (ReferenceEquals(ActivePlugin, plugin))
        {
            ActivePlugin = null;
            SelectedNode = null;
        }

        await Workspace.RemoveAsync(plugin);
        ActivePlugin ??= Workspace.Plugins.Count > 0 ? Workspace.Plugins[0] : null;
        NotifyChanged();
    }

    public void SetActive(PluginFile plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (!ReferenceEquals(ActivePlugin, plugin))
        {
            ActivePlugin = plugin;
            SelectedNode = null;
            NotifyChanged();
        }
    }

    public void Select(PluginNode? node)
    {
        SelectedNode = node;
        NotifyChanged();
    }

    public void ClearNotice()
    {
        Notice = null;
        NotifyChanged();
    }

    // ---- Tree state (per active plugin) ----

    public bool IsExpanded(PluginNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return ActivePlugin is { } plugin && TreeFor(plugin).Expanded.Contains(node.Offset);
    }

    public IReadOnlyList<PluginNode>? GetCachedChildren(long offsetKey)
        => ActivePlugin is { } plugin ? TreeFor(plugin).Children.GetValueOrDefault(offsetKey) : null;

    public ParseError? GetChildrenError(long offsetKey)
        => ActivePlugin is { } plugin ? TreeFor(plugin).Errors.GetValueOrDefault(offsetKey) : null;

    public IReadOnlyList<PluginNode>? GetTopLevel() => GetCachedChildren(TopLevelKey);

    public ParseError? GetTopLevelError() => GetChildrenError(TopLevelKey);

    public async Task EnsureTopLevelAsync()
    {
        if (ActivePlugin is { } plugin)
        {
            await EnsureChildrenAsync(plugin, TopLevelKey, group: null);
        }
    }

    /// <summary>Toggles a group's expansion, loading its children on first expansion.</summary>
    public async Task ToggleAsync(GroupNode group)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (ActivePlugin is not { } plugin)
        {
            return;
        }

        TreeState tree = TreeFor(plugin);
        if (!tree.Expanded.Remove(group.Offset))
        {
            tree.Expanded.Add(group.Offset);
            await EnsureChildrenAsync(plugin, group.Offset, group);
        }

        NotifyChanged();
    }

    // ---- Go to reference ----

    /// <summary>
    /// Follows a form reference from the active plugin: resolves it across the workspace
    /// (building the target's index on first use), switches to the target plugin, expands
    /// the tree along the record's path, selects it, and requests a scroll.
    /// </summary>
    public async Task GoToAsync(FormId formId)
    {
        if (ActivePlugin is not { } context)
        {
            return;
        }

        Notice = null;
        BusyMessage = string.Create(CultureInfo.InvariantCulture, $"Resolving {formId}…");
        BusyProgress = null;
        NotifyChanged();

        try
        {
            var progress = new DelegateProgress(fraction =>
            {
                if (BusyProgress is not { } current || fraction - current >= 0.01 || fraction >= 1.0)
                {
                    BusyMessage = "Building form index (first reference into this plugin)…";
                    BusyProgress = fraction;
                    NotifyChanged();
                }
            });

            ResolveResult result = await Workspace.ResolveAsync(context, formId, progress);
            switch (result)
            {
                case ResolvedRecord resolved:
                    await RevealAsync(resolved);
                    break;
                case MissingMaster missing:
                    Notice = string.Create(
                        CultureInfo.InvariantCulture,
                        $"{formId} is defined in '{missing.PluginName}', which is not loaded. Open it from the Home page to follow this reference.");
                    break;
                case RecordNotFound:
                    Notice = string.Create(
                        CultureInfo.InvariantCulture,
                        $"No record with form id {formId} exists in {PluginWorkspace.GetFormKey(context, formId).PluginName}.");
                    break;
                case ParseError error:
                    Notice = error.ToString();
                    break;
            }
        }
        finally
        {
            BusyMessage = null;
            BusyProgress = null;
            NotifyChanged();
        }
    }

    /// <summary>Consumes the pending scroll request (called by the page after rendering).</summary>
    public long? TakePendingScroll()
    {
        long? pending = PendingScrollOffset;
        PendingScrollOffset = null;
        return pending;
    }

    public async ValueTask DisposeAsync()
    {
        _trees.Clear();
        await Workspace.DisposeAsync();
    }

    private async Task RevealAsync(ResolvedRecord resolved)
    {
        ActivePlugin = resolved.Plugin;
        TreeState tree = TreeFor(resolved.Plugin);

        await EnsureChildrenAsync(resolved.Plugin, TopLevelKey, group: null);
        foreach (PluginNode node in resolved.Path)
        {
            if (node is GroupNode group)
            {
                tree.Expanded.Add(group.Offset);
                await EnsureChildrenAsync(resolved.Plugin, group.Offset, group);
            }
        }

        SelectedNode = resolved.Record;
        PendingScrollOffset = resolved.Record.Offset;
        NotifyChanged();
    }

    private async Task EnsureChildrenAsync(PluginFile plugin, long offsetKey, GroupNode? group)
    {
        TreeState tree = TreeFor(plugin);
        if (tree.Children.ContainsKey(offsetKey) || tree.Errors.ContainsKey(offsetKey))
        {
            return;
        }

        ParseResult<IReadOnlyList<PluginNode>> result = group is null
            ? await plugin.GetTopLevelNodesAsync()
            : await plugin.GetChildrenAsync(group);

        if (result.TryGet(out IReadOnlyList<PluginNode>? children, out ParseError? error))
        {
            tree.Children[offsetKey] = children;
        }
        else
        {
            tree.Errors[offsetKey] = error;
        }
    }

    private TreeState TreeFor(PluginFile plugin)
    {
        if (!_trees.TryGetValue(plugin, out TreeState? tree))
        {
            tree = new TreeState();
            _trees[plugin] = tree;
        }

        return tree;
    }

    private void NotifyChanged() => Changed?.Invoke();

    private sealed class TreeState
    {
        public HashSet<long> Expanded { get; } = [];

        public Dictionary<long, IReadOnlyList<PluginNode>> Children { get; } = [];

        public Dictionary<long, ParseError> Errors { get; } = [];
    }

    private sealed class DelegateProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }
}
