using System.Text.Json;

using Microsoft.JSInterop;

using OcadViewer.Versioning;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// User settings, stored in the browser exactly as the Angular app stores them.
/// </summary>
/// <remarks>
/// Same <c>localStorage</c> key and same JSON shape, so the two apps agree on a
/// user's language while both are installed. The <c>georeferencing</c> member is
/// read and written back untouched rather than dropped — see
/// <see cref="AppSettings"/>.
/// </remarks>
public sealed class AppSettingsService(IJSRuntime js, Localiser localiser)
{
    private const string StorageKey = "appSettings";

    private readonly IJSRuntime js = js;
    private readonly Localiser localiser = localiser;

    public AppSettings Settings { get; private set; } = new();

    /// <summary>Reads stored settings and applies the language.</summary>
    public async Task InitialiseAsync()
    {
        Settings = await ReadAsync() ?? new AppSettings();
        await localiser.LoadAsync(Settings.LanguageSelection?.SelectedLanguageCode
                                  ?? Localiser.DefaultLanguage);
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        Settings.LanguageSelection ??= new LanguageSelectionSettings();
        Settings.LanguageSelection.SelectedLanguageCode = languageCode;

        await WriteAsync();
        await localiser.LoadAsync(languageCode);
    }

    /// <summary>
    /// Persists everything the settings modal edits, as Angular's Save button does:
    /// one write, both values, nothing applied until then.
    /// </summary>
    public async Task SaveAsync(string languageCode, int? epsgNumber)
    {
        // Angular writes the whole georeferencing group, so a cleared code has to
        // clear the stored one rather than leave the old value behind.
        Settings.Georeferencing ??= new GeoReferenceSettings();
        Settings.Georeferencing.EpsgNumber = epsgNumber;

        await SetLanguageAsync(languageCode ?? Localiser.DefaultLanguage);
    }

    private async Task<AppSettings> ReadAsync()
    {
        try {
            string json = await js.InvokeAsync<string>("localStorage.getItem", StorageKey);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<AppSettings>(json, AngularJson.Options);
        }
        catch (Exception) {
            // Private browsing, cleared storage or a value written by some older
            // version. Falling back to defaults is always better than failing to
            // start over a preference.
            return null;
        }
    }

    private async Task WriteAsync()
    {
        try {
            await js.InvokeVoidAsync("localStorage.setItem", StorageKey,
                                     JsonSerializer.Serialize(Settings, AngularJson.Options));
        }
        catch (Exception) {
        }
    }
}

/// <summary>Mirrors the Angular <c>AppSettings</c> shape.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Read and written back verbatim. Only the diff ever consumed this, and the
    /// diff does not exist here yet — but a user who set an EPSG code in the
    /// Angular app should not have it silently erased by opening this one.
    /// </summary>
    public GeoReferenceSettings Georeferencing { get; set; }

    public LanguageSelectionSettings LanguageSelection { get; set; }
}

public sealed class GeoReferenceSettings
{
    public int? EpsgNumber { get; set; }
}

public sealed class LanguageSelectionSettings
{
    public string SelectedLanguageCode { get; set; }
}
