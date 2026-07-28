using Microsoft.JSInterop;

namespace EsmParser.Web.Services;

/// <summary>
/// The user's color-theme preference (light, dark, or follow-the-OS), persisted in
/// browser storage and applied via Bootstrap's <c>data-bs-theme</c> attribute.
/// </summary>
public sealed class ThemeService(IJSRuntime jsRuntime) : IAsyncDisposable
{
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Auto = "auto";

    private IJSObjectReference? _module;

    /// <summary>The stored preference; the OS decides the effective theme when this is <see cref="Auto"/>.</summary>
    public string Preference { get; private set; } = Auto;

    public async Task InitializeAsync()
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");
        Preference = await _module.InvokeAsync<string>("getPreference");
    }

    public async Task SetPreferenceAsync(string preference)
    {
        if (_module is null)
        {
            await InitializeAsync();
        }

        await _module!.InvokeVoidAsync("setPreference", preference);
        Preference = preference;
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
}
