using EsmParser.Core;
using EsmParser.Core.IO;
using EsmParser.Core.Navigation;

namespace EsmParser.Web.Services;

/// <summary>
/// UI state for the currently-loaded plugin: which file is open, which node is
/// selected, and the last load failure. All parsing lives in the core library;
/// this only holds display state.
/// </summary>
public sealed class PluginExplorerState : IAsyncDisposable
{
    /// <summary>Cache sizing for browser-backed sources: 128 KiB pages, at most 16 MiB resident.</summary>
    private const int CachePageSize = 128 * 1024;
    private const int CacheMaxPages = 128;

    public PluginFile? Plugin { get; private set; }

    public ParseError? LastError { get; private set; }

    public PluginNode? SelectedNode { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// Opens a plugin over <paramref name="source"/> (wrapped in a page cache), replacing
    /// any previously-loaded plugin. On failure the source is disposed and the error kept
    /// for display.
    /// </summary>
    public async Task<bool> LoadAsync(IDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        await CloseAsync();

        var cached = new CachingDataSource(source, CachePageSize, CacheMaxPages);
        ParseResult<PluginFile> result = await PluginFile.OpenAsync(cached);
        if (!result.TryGet(out PluginFile? plugin, out ParseError? error))
        {
            await cached.DisposeAsync();
            LastError = error;
            NotifyChanged();
            return false;
        }

        Plugin = plugin;
        LastError = null;
        NotifyChanged();
        return true;
    }

    public void Select(PluginNode? node)
    {
        SelectedNode = node;
        NotifyChanged();
    }

    public async Task CloseAsync()
    {
        if (Plugin is not null)
        {
            await Plugin.DisposeAsync();
        }

        Plugin = null;
        SelectedNode = null;
        LastError = null;
        NotifyChanged();
    }

    public ValueTask DisposeAsync() => new(CloseAsync());

    private void NotifyChanged() => Changed?.Invoke();
}
