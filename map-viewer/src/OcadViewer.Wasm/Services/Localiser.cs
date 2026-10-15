using System.Net.Http.Json;
using System.Text.Json;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// Looks up UI strings by the keys the Angular app already uses.
/// </summary>
/// <remarks>
/// Deliberately not Blazor's .resx localisation. The translations exist, they are
/// maintained, and the Angular app is still live - copying them into .resx would
/// mean two copies drifting apart for as long as both apps run. An MSBuild target
/// copies <c>src/assets/i18n/*.json</c> into this app's wwwroot, so there is one
/// source of truth until Angular is deleted.
///
/// Keys keep their <c>#_</c> prefix for the same reason: so a string can be traced
/// to the Angular component it came from without translating names as well.
/// </remarks>
public sealed class Localiser(HttpClient http)
{
    public const string DefaultLanguage = "en";

    /// <summary>Languages the app offers, as code and the name in that language.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("en", "English"),
        ("no", "Norsk")
    ];

    private readonly HttpClient http = http;
    private Dictionary<string, string> strings = [];

    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>Raised when the language changes, so components can re-render.</summary>
    public event Action Changed;

    public async Task LoadAsync(string language)
    {
        language = Languages.Any(l => l.Code == language) ? language : DefaultLanguage;

        try {
            var loaded = await http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{language}.json");
            strings = loaded ?? [];
            Language = language;
        }
        catch (Exception) when (language != DefaultLanguage) {
            // A missing or broken translation should degrade to English, not to a
            // blank interface.
            await LoadAsync(DefaultLanguage);
            return;
        }
        catch (Exception) {
            strings = [];
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// The string for <paramref name="key"/>, or the key itself when it is missing.
    /// </summary>
    /// <remarks>
    /// Returning the key rather than empty is deliberate: a missing translation
    /// shows up as "#_someKey" in the interface, which is obvious and traceable,
    /// where a blank space would just look like a layout bug.
    /// </remarks>
    public string this[string key]
        => strings.TryGetValue(key, out string value) ? value : key;

    /// <summary>
    /// The string for <paramref name="key"/> with <c>{{name}}</c> placeholders
    /// filled in, matching the interpolation ngx-translate uses.
    /// </summary>
    public string Format(string key, params (string Name, object Value)[] arguments)
    {
        string result = this[key];
        foreach ((string name, object value) in arguments)
            result = result.Replace($"{{{{{name}}}}}", value?.ToString() ?? string.Empty);
        return result;
    }
}
