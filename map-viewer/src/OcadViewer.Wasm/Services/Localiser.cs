using System.Net.Http.Json;
using System.Text.Json;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// Looks up UI strings by the keys the Angular app used.
/// </summary>
/// <remarks>
/// Deliberately not Blazor's .resx localisation. The translations are the Angular
/// app's JSON files, carried over into <c>wwwroot/i18n</c>, which is now the only
/// copy.
///
/// Keys keep their <c>#_</c> prefix so a string can still be traced to the Angular
/// component it came from, in git history, without translating names as well.
/// </remarks>
public sealed class Localiser(HttpClient http)
{
    public const string DefaultLanguage = "en";

    /// <summary>
    /// Languages the app offers, as code and the name in that language. Norwegian
    /// first, because that is the order the Angular settings modal shows the flags
    /// in - the default is still English, which is a separate thing.
    /// </summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Languages =
    [
        ("no", "Norsk"),
        ("en", "English")
    ];

    private readonly HttpClient http = http;
    private Dictionary<string, string> strings = [];

    public string Language { get; private set; } = DefaultLanguage;

    /// <summary>Raised when the language changes, so components can re-render.</summary>
    public event Action Changed;

    /// <summary>Counts loads, so one overtaken by a later choice can tell.</summary>
    private int latestLoad;

    public async Task LoadAsync(string language)
    {
        language = Languages.Any(l => l.Code == language) ? language : DefaultLanguage;
        int load = ++latestLoad;

        try {
            var loaded = await http.GetFromJsonAsync<Dictionary<string, string>>($"i18n/{language}.json");

            // Two quick picks fetch in parallel and can finish in either order;
            // only the last pick may decide what is shown.
            if (load != latestLoad)
                return;

            strings = loaded ?? [];
            Language = language;
        }
        catch (Exception) when (load != latestLoad) {
            // Overtaken while failing: the later pick decides, not a fallback.
            return;
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
        => key is null ? string.Empty
         : strings.TryGetValue(key, out string value) ? value : key;

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
