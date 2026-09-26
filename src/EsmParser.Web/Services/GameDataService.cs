using BethesdaArchiveParser.Core;
using BethesdaArchiveParser.Core.Reader;
using EsmParser.Web.Interop;
using Microsoft.JSInterop;
using NifViewer.Blazor;

namespace EsmParser.Web.Services;

/// <summary>Resolves loose and BSA-backed assets from a user-granted game Data directory.</summary>
public sealed partial class GameDataService(IJSRuntime jsRuntime, ILogger<GameDataService> logger) : INifDependencyResolver, IAsyncDisposable
{
    private Task<IJSObjectReference>? _module;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ArchivedFile> _files = [with(StringComparer.OrdinalIgnoreCase)];
    private readonly List<string> _archiveErrors = [];

    public string? RootName { get; private set; }
    public bool HasRoot => RootName is not null;
    public bool? IsSupported { get; private set; }
    public int ArchiveCount { get; private set; }
    public IReadOnlyList<string> ArchiveErrors => _archiveErrors;
    public IEnumerable<string> ArchivedModelPaths => _files.Keys.Where(static path => path.EndsWith(".nif", StringComparison.OrdinalIgnoreCase));

    public async Task<bool> InitializeAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            IsSupported ??= await module.InvokeAsync<bool>("isSupported");
            return IsSupported.Value;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    public async Task<bool> PickAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var module = await GetModuleAsync();
            string? name = await module.InvokeAsync<string?>("pickDataRoot");
            if (name is null)
            {
                return false;
            }
            RootName = name;
            _files.Clear();
            _archiveErrors.Clear();
            ArchiveCount = 0;
            var names = await module.InvokeAsync<string[]>("listFiles");
            foreach (string archiveName in names.Where(static n => n.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await using var stream = await SeekablePullFromJSDataStream.OpenAsync(module, archiveName)
                        ?? throw new FileNotFoundException("Archive is no longer available.", archiveName);
                    var archive = await new BsaArchiveReader(stream).ReadBsaArchiveAsync();
                    foreach (var entry in archive.Entries)
                    {
                        string? path = NormalizePath(entry.Path);
                        if (path is not null)
                        {
                            _files[path] = new ArchivedFile(archiveName, entry.File);
                        }
                    }
                    ArchiveCount++;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or JSException)
                {
                    _archiveErrors.Add($"{archiveName}: {ex.Message}");
                    Log.ArchiveFailure(logger, ex, archiveName);
                }
            }
            return true;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]?> ResolveAsync(string path)
    {
        string? normalized = NormalizePath(path);
        if (normalized is null)
        {
            return null;
        }
        await _gate.WaitAsync();
        try
        {
            if (!HasRoot)
            {
                return null;
            }
            var module = await GetModuleAsync();
            // Loose overrides take precedence. Open snapshots are disposed even if parsing fails.
            await using var loose = await SeekablePullFromJSDataStream.OpenAsync(module, normalized);
            if (loose is not null)
            {
                if (loose.Length > Array.MaxLength)
                {
                    throw new InvalidDataException("Asset is too large for the viewer.");
                }
                byte[] bytes = new byte[(int)loose.Length];
                await loose.ReadExactlyAsync(bytes);
                return bytes;
            }
            if (!_files.TryGetValue(normalized, out var entry))
            {
                return null;
            }
            await using var archiveStream = await SeekablePullFromJSDataStream.OpenAsync(module, entry.ArchiveName);
            return archiveStream is null ? null : await new BsaArchiveReader(archiveStream).ReadBsaFileAsync(entry.File);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        string normalized = path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
        while (normalized.StartsWith("data/", StringComparison.Ordinal))
        {
            normalized = normalized[5..];
        }
        if (normalized.Split('/').Any(static part => part.Length == 0 || part is "." or ".." || part.Contains(':')))
        {
            return null;
        }
        return normalized;
    }

    private Task<IJSObjectReference> GetModuleAsync() => _module ??= ImportModuleAsync();

    private async Task<IJSObjectReference> ImportModuleAsync()
    {
        try
        {
            return await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/dataRoot.js");
        }
        catch (JSDisconnectedException)
        {
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await (await _module).DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        _gate.Dispose();
    }

    private sealed record ArchivedFile(string ArchiveName, BsaFileRecord File);

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Warning, "Could not index BSA archive {ArchiveName}.")]
        public static partial void ArchiveFailure(ILogger logger, Exception exception, string archiveName);
    }
}
