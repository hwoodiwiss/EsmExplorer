using BethesdaArchiveParser.Core;
using BethesdaArchiveParser.Core.Reader;
using EsmParser.Web.Interop;
using Microsoft.JSInterop;
using NifViewer.Blazor;

namespace EsmParser.Web.Services;

/// <summary>
/// Grants access to the user's game Data folder via the File System Access API
/// and resolves asset paths (meshes, materials, textures) from it, serving as
/// the dependency resolver for the NIF viewer.
/// </summary>
public sealed partial class GameDataService(IJSRuntime jsRuntime, ILogger<GameDataService> logger) : INifDependencyResolver, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private readonly Dictionary<string, BsaArchive> _rootBsaArchives = [];

    /// <summary>The name of the granted folder, or null when none is granted yet.</summary>
    public string? RootName { get; private set; }

    public bool HasRoot => RootName is not null;

    /// <summary>False when the browser lacks <c>showDirectoryPicker</c> (e.g. Firefox/Safari).</summary>
    public bool? IsSupported { get; private set; }

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

    /// <summary>Shows the directory picker; returns true when a folder was granted.</summary>
    public async Task<bool> PickAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            string? name = await module.InvokeAsync<string?>("pickDataRoot");
            if (name is not null)
            {
                RootName = name;
                var rootFiles = await module.InvokeAsync<RootFileInfo[]>("listFiles");
                var bsaFiles = rootFiles.Where(w => w.Name.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase)).ToList();
                Log.FoundBsaArchives(logger, bsaFiles.Count);
                foreach (var file in bsaFiles)
                {
                    var archive = await ReadRootedBsaArchive(file);
                    if (archive is not null)
                    {
                        Log.ReadBsaArchive(logger, file.Name);
                        _rootBsaArchives[file.Name] = archive;
                    }
                    else
                    {
                        Log.FailedToReadBsaArchive(logger, file.Name);
                    }
                }
            }

            return name is not null;
        }
        catch (JSDisconnectedException)
        {
            return false;
        }
    }

    /// <summary>Resolves a data-relative path (lowercase, forward slashes) to file bytes, or null.</summary>
    public async Task<byte[]?> ResolveAsync(string path)
    {
        if (!HasRoot || string.IsNullOrEmpty(path))
        {
            return null;
        }

        try
        {
            if (SearchArchivesForPath(path) is (string Name, BsaFileRecord file))
            {
                return await ReadRootedBsaArchiveFile(Name, file);
            }

            var module = await GetModuleAsync();
            return await module.InvokeAsync<byte[]?>("getAllFileData", path);
        }
        catch (JSDisconnectedException)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The runtime is gone; nothing to release.
        }
    }

    private async ValueTask<IJSObjectReference> GetModuleAsync()
    {
        try
        {
            return _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/dataRoot.js");
        }
        catch (JSDisconnectedException)
        {
            throw;
        }
    }

    private async ValueTask<BsaArchive?> ReadRootedBsaArchive(RootFileInfo rootFile)
    {
        try
        {
            var module = await GetModuleAsync();
            var jsStreamRef = await module.InvokeAsync<IJSStreamReference>("resolveFile", rootFile.Name);
            using var stream = SeekablePullFromJSDataStream.CreateJSDataStream(jsRuntime, jsStreamRef, jsStreamRef.Length);
            var bsaReader = new BsaArchiveReader(stream);
            return await bsaReader.ReadBsaArchiveAsync();
        }
        catch (JSDisconnectedException ex)
        {
            Log.FailedToReadBsaArchiveFile(logger, ex, rootFile.Name, rootFile.Name);
            throw;
        }
        catch (Exception ex)
        {
            Log.FailedToReadBsaArchiveFile(logger, ex, rootFile.Name, rootFile.Name);
            return null;
        }
    }

    private (string Name, BsaFileRecord file)? SearchArchivesForPath(string path)
    {
        foreach (var archive in _rootBsaArchives.Values)
        {
            if (archive.GetFileByPath(path) is { } file)
            {
                return (path, file);
            }
        }

        return null;
    }


    private async ValueTask<byte[]> ReadRootedBsaArchiveFile(string rootFileName, BsaFileRecord file)
    {
        try
        {
            var module = await GetModuleAsync();
            var jsStreamRef = await module.InvokeAsync<IJSStreamReference>("resolveFile", rootFileName);
            using var stream = SeekablePullFromJSDataStream.CreateJSDataStream(jsRuntime, jsStreamRef, jsStreamRef.Length);
            var bsaReader = new BsaArchiveReader(stream);
            return await bsaReader.ReadBsaFileAsync(file);
        }
        catch (JSDisconnectedException)
        {
            throw;
        }
    }

    private sealed record RootFileInfo(string Name, int Size);

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Information, "Found {Count} BSA archives in the granted root folder.")]
        public static partial void FoundBsaArchives(ILogger logger, int Count);

        [LoggerMessage(2, LogLevel.Information, "Read BSA archive: {ArchiveName}.")]
        public static partial void ReadBsaArchive(ILogger logger, string ArchiveName);

        [LoggerMessage(3, LogLevel.Warning, "Failed to read BSA archive: {ArchiveName}.")]
        public static partial void FailedToReadBsaArchive(ILogger logger, string ArchiveName);

        [LoggerMessage(4, LogLevel.Error, "Failed to read BSA archive file: {ArchiveName} - {FilePath}.")]
        public static partial void FailedToReadBsaArchiveFile(ILogger logger, Exception ex, string ArchiveName, string FilePath);
    }
}
