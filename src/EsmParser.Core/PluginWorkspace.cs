using EsmParser.Core.Format;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;

namespace EsmParser.Core;

/// <summary>A reference resolved to its defining record, with the tree path leading to it.</summary>
public sealed record ResolvedRecord(PluginFile Plugin, IReadOnlyList<PluginNode> Path)
{
    public RecordNode Record => (RecordNode)Path[^1];
}

/// <summary>The reference points into a master file that is not loaded in the workspace.</summary>
public sealed record MissingMaster(string PluginName);

/// <summary>
/// The outcome of resolving a form reference across the workspace: the defining record,
/// a master that would need loading, a clean miss, or a parse failure along the way.
/// </summary>
public union ResolveResult(ResolvedRecord, MissingMaster, RecordNotFound, ParseError);

/// <summary>
/// A set of plugins loaded together, so that form references can be followed across
/// files via each plugin's master list. Owns the plugins (and their form-id indexes,
/// which are built lazily on first resolution into a file).
/// </summary>
public sealed class PluginWorkspace : IAsyncDisposable
{
    private readonly List<PluginFile> _plugins = [];
    private readonly Dictionary<PluginFile, FormIdIndex> _indexes = [];
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    public IReadOnlyList<PluginFile> Plugins => _plugins;

    /// <summary>
    /// Opens a plugin over <paramref name="source"/> and adds it to the workspace.
    /// Adding a file name that is already loaded returns the existing instance
    /// (and disposes the new source).
    /// </summary>
    public async ValueTask<ParseResult<PluginFile>> AddAsync(IDataSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (FindByName(source.Name) is { } existing)
        {
            await source.DisposeAsync();
            return new Success<PluginFile>(existing);
        }

        ParseResult<PluginFile> opened = await PluginFile.OpenAsync(source, leaveOpen: false, cancellationToken);
        if (!opened.TryGet(out PluginFile? plugin, out ParseError? error))
        {
            return error;
        }

        _plugins.Add(plugin);
        return new Success<PluginFile>(plugin);
    }

    /// <summary>Removes and disposes a plugin (and its index).</summary>
    public async ValueTask RemoveAsync(PluginFile plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (_plugins.Remove(plugin))
        {
            _indexes.Remove(plugin);
            await plugin.DisposeAsync();
        }
    }

    /// <summary>Finds a loaded plugin by file name (case-insensitive), or <see langword="null"/>.</summary>
    public PluginFile? FindByName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        foreach (PluginFile plugin in _plugins)
        {
            if (string.Equals(plugin.Name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return plugin;
            }
        }

        return null;
    }

    /// <summary>
    /// Canonicalizes a stored form id as seen from <paramref name="context"/>: which plugin
    /// defines the form, plus its 24-bit object id.
    /// </summary>
    public static FormKey GetFormKey(PluginFile context, FormId formId)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new FormKey(context.Header.ResolveOrigin(formId, context.Name), formId.ObjectId);
    }

    /// <summary>Masters declared by <paramref name="plugin"/> that are not currently loaded.</summary>
    public IReadOnlyList<string> GetMissingMasters(PluginFile plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var missing = new List<string>();
        foreach (MasterReference master in plugin.Header.Masters)
        {
            if (FindByName(master.FileName) is null)
            {
                missing.Add(master.FileName);
            }
        }

        return missing;
    }

    /// <summary>
    /// Follows a form reference from <paramref name="context"/> to the record that defines it,
    /// building the target plugin's form-id index on first use (progress is reported for that
    /// build, which reads the target's full structure once).
    /// </summary>
    public async ValueTask<ResolveResult> ResolveAsync(
        PluginFile context,
        FormId formId,
        IProgress<double>? indexProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (formId.IsNull)
        {
            return new RecordNotFound();
        }

        FormKey key = GetFormKey(context, formId);
        PluginFile? target = FindByName(key.PluginName);
        if (target is null)
        {
            return new MissingMaster(key.PluginName);
        }

        // The defining record's id as stored in the target file: its own master index
        // (one past its master list) plus the object id.
        var localId = new FormId(((uint)target.Header.Masters.Count << 24) | key.ObjectId);

        ParseResult<FormIdIndex> index = await GetIndexAsync(target, indexProgress, cancellationToken);
        if (!index.TryGet(out FormIdIndex? formIdIndex, out ParseError? indexError))
        {
            return indexError;
        }

        if (formIdIndex.FindOffset(localId) is not { } offset)
        {
            return new RecordNotFound();
        }

        ParseResult<IReadOnlyList<PluginNode>> path = await target.GetPathToOffsetAsync(offset, cancellationToken);
        if (!path.TryGet(out IReadOnlyList<PluginNode>? nodes, out ParseError? pathError))
        {
            return pathError;
        }

        if (nodes[^1] is not RecordNode)
        {
            return new ParseError(
                ParseErrorKind.StructureInvalid,
                $"The index entry for {localId} points at a group rather than a record.",
                offset);
        }

        return new ResolvedRecord(target, nodes);
    }

    /// <summary>Returns the (lazily built, cached) form-id index for a loaded plugin.</summary>
    public async ValueTask<ParseResult<FormIdIndex>> GetIndexAsync(
        PluginFile plugin,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        await _indexGate.WaitAsync(cancellationToken);
        try
        {
            if (_indexes.TryGetValue(plugin, out FormIdIndex? cached))
            {
                return new Success<FormIdIndex>(cached);
            }

            ParseResult<FormIdIndex> built = await FormIdIndex.BuildAsync(plugin, progress, cancellationToken);
            if (built.TryGet(out FormIdIndex? index, out ParseError? error))
            {
                _indexes[plugin] = index;
                return new Success<FormIdIndex>(index);
            }

            return error;
        }
        finally
        {
            _indexGate.Release();
        }
    }

    /// <summary>Whether a form-id index has already been built for <paramref name="plugin"/>.</summary>
    public bool HasIndex(PluginFile plugin) => _indexes.ContainsKey(plugin);

    public async ValueTask DisposeAsync()
    {
        foreach (PluginFile plugin in _plugins)
        {
            await plugin.DisposeAsync();
        }

        _plugins.Clear();
        _indexes.Clear();
        _indexGate.Dispose();
    }
}
