using System.Text.Json;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// Every key has text in every language, so none shows up as a raw <c>#_key</c>
/// on someone's screen.
/// </summary>
[TestFixture]
public sealed class TranslationTests
{
    private static string BlazorFolder
        => Path.Combine(Repo.Src, "OcadViewer.Wasm", "wwwroot", "i18n");

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
