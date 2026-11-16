using System.Text.Json;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// The translations belong to this app; the Angular app keeps a copy until it is
/// deleted.
/// </summary>
/// <remarks>
/// The copy exists because the Angular production deploy builds
/// <c>ocad-versioner/</c> on its own, and a file outside that folder may not be
/// there when it does. Two copies can drift, so this test is what keeps them one
/// set of strings: a key added to either app's file and not the other fails here,
/// instead of showing up as a raw <c>#_key</c> on someone's screen.
/// </remarks>
[TestFixture]
public sealed class TranslationTests
{
    private static string BlazorFolder
        => Path.Combine(Repo.Src, "OcadViewer.Wasm", "wwwroot", "i18n");

    private static string AngularFolder
        => Path.Combine(Repo.Root.Parent!.FullName, "ocad-versioner", "src", "assets", "i18n");

    [TestCase("en")]
    [TestCase("no")]
    public void AngularCopyMatchesTheBlazorTranslations(string language)
    {
        string angular = Path.Combine(AngularFolder, $"{language}.json");
        if (!Directory.Exists(AngularFolder))
            Assert.Ignore("The Angular app is gone, and its copy with it.");

        Dictionary<string, string> blazor = Read(Path.Combine(BlazorFolder, $"{language}.json"));
        Dictionary<string, string> copy = Read(angular);

        Assert.Multiple(() => {
            Assert.That(copy.Keys.Except(blazor.Keys), Is.Empty,
                $"Keys in ocad-versioner's {language}.json that the Blazor app lacks. "
                + "Add them to map-viewer/src/OcadViewer.Wasm/wwwroot/i18n, which is the original.");
            Assert.That(blazor.Keys.Except(copy.Keys), Is.Empty,
                $"Keys in the Blazor app's {language}.json missing from ocad-versioner's copy.");
            Assert.That(blazor.Where(pair => copy.TryGetValue(pair.Key, out string other) && other != pair.Value)
                              .Select(pair => pair.Key),
                        Is.Empty,
                        $"Keys whose text differs between the two {language}.json files.");
        });
    }

    [Test]
    public void BothLanguagesHaveTheSameKeys()
    {
        Dictionary<string, string> en = Read(Path.Combine(BlazorFolder, "en.json"));
        Dictionary<string, string> no = Read(Path.Combine(BlazorFolder, "no.json"));

        Assert.Multiple(() => {
            Assert.That(en.Keys.Except(no.Keys), Is.Empty, "English keys with no Norwegian text.");
            Assert.That(no.Keys.Except(en.Keys), Is.Empty, "Norwegian keys with no English text.");
        });
    }

    private static Dictionary<string, string> Read(string path)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
}
