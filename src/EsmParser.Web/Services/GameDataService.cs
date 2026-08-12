using Microsoft.JSInterop;
using NifViewer.Blazor;

namespace EsmParser.Web.Services;

/// <summary>
/// Grants access to the user's game Data folder via the File System Access API
/// and resolves asset paths (meshes, materials, textures) from it, serving as
/// the dependency resolver for the NIF viewer.
/// </summary>
public sealed class GameDataService(IJSRuntime jsRuntime) : INifDependencyResolver, IAsyncDisposable
{
    private IJSObjectReference? _module;

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
            var module = await GetModuleAsync();
            return await module.InvokeAsync<byte[]?>("resolveFile", path);
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
}
