using System.Text.RegularExpressions;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// One rule per test: something that must stay knowable in one place.
/// </summary>
/// <remarks>
/// These read the source rather than the compiled output, because most of what
/// goes wrong here goes wrong between languages: a value that has to mean the
/// same thing in a Razor page, a JavaScript module and a <c>[JSExport]</c> is
/// invisible to the compiler from every side.
///
/// A rule here is not a style preference. Each one marks a decision that has to
/// be written down once, because two copies of it can disagree.
///
/// Matching text can only keep a rule from being broken again; it cannot show
/// the rule is right. Where a contract can be tested by behaviour instead, as
/// the folder layout is in <see cref="VersioningTests"/>, it is.
/// </remarks>
[TestFixture]
public sealed class OrthogonalityTests
{
    private static IReadOnlyList<SourceFile> csharp;
    private static IReadOnlyList<SourceFile> scripts;
    private static IReadOnlyList<SourceFile> all;

    [OneTimeSetUp]
    public void ReadSources()
    {
        csharp = Repo.OurSources(".cs", ".razor");
        scripts = Repo.OurSources(".js");
        all = [.. csharp, .. scripts];

        Assert.That(csharp, Is.Not.Empty, "No C# found; the source tree was not located.");
        Assert.That(scripts, Is.Not.Empty, "No JavaScript found; the source tree was not located.");
    }

    /* ------------------------------------------------------------------ maps */

    [Test]
    public void MapsAreAddressedByNameAndNeverByPaneIndex()
    {
        IReadOnlyList<SourceFile> offenders = Matching(all, @"\bslots?\b", RegexOptions.IgnoreCase);

        Assert.That(offenders, Is.Empty,
            "A map is identified by its version name. A pane index would have to mean the same "
            + "thing in the page, the component, the Leaflet module and the interop at once, and "
            + "would tie a parsed map to a screen position, so both sides would be re-read and "
            + "re-parsed whenever either changed.");
    }

    [Test]
    public void PaneNamesAreSpeltOutInExactlyOnePlacePerLanguage()
    {
        Assert.Multiple(() => {
            Assert.That(NamesOf(Matching(csharp, @"""(left|right)""")), Is.EquivalentTo(new[] { "MapSide.cs" }),
                "Which panes exist is the MapSide enum's business; everything else names a side.");

            Assert.That(NamesOf(Matching(scripts, @"'(left|right)'")), Is.EquivalentTo(new[] { "leafletViewport.js" }),
                "The viewport owns the two panes. Anything above it passes a side through without "
                + "knowing what the sides are.");
        });
    }

    [Test]
    public void TheRendererIsReachedThroughItsShimOnly()
    {
        Assert.That(NamesOf(Matching(csharp, @"\bTileInterop\b")), Is.EquivalentTo(new[] { "TileInterop.cs" }),
            "Components talk to JavaScript, which talks to the shim. A component reaching the "
            + "renderer directly would bypass the map store that keeps parsed maps alive.");
    }

    [Test]
    public void TheShimDoesNothingButMarshal()
    {
        SourceFile shim = Single(csharp, "TileInterop.cs");

        string[] logic = [.. new[] { "Stopwatch", "Directory.", "File.", "FontPack", "SKBitmap", "new Map(" }
            .Where(shim.Mentions)];

        Assert.That(logic, Is.Empty,
            "TileInterop is the JavaScript face of MapStore and nothing else. Timing, staging "
            + "fonts, converting pixel formats and choosing what to keep belong in a project that "
            + "a console can call and a test can reach.");
    }

    /* ------------------------------------------------------- the module graph */

    /// <summary>
    /// What each of our JavaScript modules may import. The same idea as the
    /// project-reference table, for the half of the app the compiler cannot see.
    /// </summary>
    private static readonly Dictionary<string, string[]> AllowedImports = new(StringComparer.Ordinal) {
        ["dotnetExports.js"] = [],
        ["tilePump.js"] = [],
        ["projectFolder.js"] = [],
        ["bootstrapComponents.js"] = [],
        ["appInsights.js"] = [],
        ["changeMask.js"] = [],
        ["hotkeys.js"] = [],
        ["mapTiles.js"] = ["dotnetExports.js"],
        ["leafletViewport.js"] = ["tilePump.js"],
        ["mapWorkspace.js"] = ["projectFolder.js", "mapTiles.js", "leafletViewport.js", "changeMask.js"],
    };

    [Test]
    public void EveryScriptIsAccountedForInTheModuleGraph()
    {
        string[] unknown = [.. scripts.Select(s => s.Name).Where(name => !AllowedImports.ContainsKey(name))];

        Assert.That(unknown, Is.Empty,
            $"A new module exists that {nameof(AllowedImports)} says nothing about. Add it, with "
            + "what it is allowed to import.");
    }

    [Test]
    public void NoScriptImportsSomethingItMayNot()
    {
        List<string> violations = [];

        foreach (SourceFile script in scripts) {
            if (!AllowedImports.TryGetValue(script.Name, out string[] allowed))
                continue;

            foreach (string imported in RelativeImportsOf(script)) {
                if (!allowed.Contains(imported, StringComparer.Ordinal))
                    violations.Add($"{script.Name} imports {imported}");
            }
        }

        Assert.That(violations, Is.Empty,
            "Only mapWorkspace.js may know about more than one of these at a time: it is the "
            + "composition root. In particular the Leaflet module must not reach the .NET runtime, "
            + "or the viewport cannot be loaded, tested or reused without Blazor underneath it.");
    }

    [Test]
    public void OnlyTheTileSourceKnowsThereIsADotNetRuntime()
    {
        IReadOnlyList<SourceFile> offenders = Matching(
            scripts, @"getDotnetRuntime|getAssemblyExports|getTileInterop");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "dotnetExports.js", "mapTiles.js" }),
            "dotnetExports.js resolves the runtime and mapTiles.js wraps it. Above that line, "
            + "tiles come from a plain object with four methods on it.");
    }

    [Test]
    public void OnlyTheFolderModuleTouchesTheFileSystemApi()
    {
        IReadOnlyList<SourceFile> offenders = Matching(
            scripts, @"showDirectoryPicker|getDirectoryHandle|getFileHandle|createWritable|getFile\(");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "projectFolder.js" }),
            "The File System Access API is Chromium-only and permission-bound. Keeping it behind "
            + "one module is what makes the rest of the viewer testable, and what will make "
            + "another way of getting at files possible without touching anything else. How big "
            + "a map file is comes from the same handles, so it is asked for here too.");
    }

    [Test]
    public void ComponentsTalkOnlyToModulesMeantForThem()
    {
        string[] forbidden = ["leafletViewport.js", "mapTiles.js", "tilePump.js", "dotnetExports.js"];
        List<string> violations = [];

        foreach (SourceFile file in csharp) {
            foreach (string module in forbidden) {
                if (file.Mentions($"./js/{module}"))
                    violations.Add($"{file.RelativePath} imports {module}");
            }
        }

        Assert.That(violations, Is.Empty,
            "C# imports mapWorkspace.js, projectFolder.js, bootstrapComponents.js and "
            + "appInsights.js. The modules behind the workspace are its business.");
    }

    [Test]
    public void NoModuleKeepsStateThatOutlivesItsCaller()
    {
        /* Three documented exceptions, each holding a handle to something there is
         * genuinely one of per page, obtained once and never replaced:
         *   projectFolder.js  the picked folder, which cannot cross into .NET and
         *                     cannot be re-obtained without asking the user
         *   dotnetExports.js  the .NET runtime
         *   appInsights.js    the telemetry SDK, once it has loaded
         * What the rule is really after is coordination state - a queue, a latch,
         * a timer, a cache - which is what makes a module secretly unrepeatable.
         * A const holding a Map or a Set is as mutable as a let, so it counts. */
        string[] exempt = ["projectFolder.js", "dotnetExports.js", "appInsights.js"];

        List<string> violations = [];

        foreach (SourceFile script in scripts.Where(s => !exempt.Contains(s.Name, StringComparer.Ordinal))) {
            foreach (string line in Lines(script)) {
                if (Regex.IsMatch(line, @"^(let|var)\s")
                    || Regex.IsMatch(line, @"^(export\s+)?const\s+\w+\s*=\s*new\s+(Weak)?(Map|Set)\b"))
                    violations.Add($"{script.Name}: {line.Trim()}");
            }
        }

        Assert.That(violations, Is.Empty,
            "Module-level mutable state is a singleton with no name: one queue, latch or cache "
            + "shared by every caller on the page, which makes a second viewport quietly "
            + "impossible. Put it inside the factory that owns it.");
    }

    [Test]
    public void NoParsedMapsAreHeldOutsideAStoreSomebodyOwns()
    {
        // A static field or property of type MapStore; a static method returning one is fine.
        IReadOnlyList<SourceFile> offenders = Matching(
            csharp, @"static\s+(readonly\s+)?MapStore\s+\w+\s*(=|;|\{|=>)");

        Assert.That(offenders, Is.Empty,
            "A static MapStore is one set of parsed maps for the whole page, keyed by names that "
            + "are only unique within a project. Each workspace creates its own store, clears it "
            + "when the project changes and disposes it with itself.");
    }

    [Test]
    public void OnlyTheRendererKnowsWhatKindOfChangeAPixelIs()
    {
        IReadOnlyList<SourceFile> offenders = Matching(
            all, @"\b(appeared|vanished|altered)\b", RegexOptions.IgnoreCase);

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "MapStore.cs" }),
            "The renderer classifies each changed pixel, because it is the only thing that can. "
            + "Nothing above it reads that classification: the overlay asks one question - did "
            + "this change - so the only thing crossing the boundary is \"zero means unchanged\".");
    }

    /* ------------------------------------------------- contracts stated once */

    [Test]
    public void TheReleaseNamingRuleIsStatedOnce()
    {
        IReadOnlyList<SourceFile> offenders = Matching(all, @"['""]V['""{]");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "VersionNaming.cs" }),
            "The V<n> rule is a contract with the Angular app, which reads and writes the same "
            + "folders, so it is stated in one place.");
    }

    /// <summary>
    /// The folder layout is a contract with the Angular app, and C# owns all of it.
    /// </summary>
    /// <remarks>
    /// This test only keeps the layout from leaking back out. Whether the layout is
    /// right is tested by behaviour, in <see cref="VersioningTests"/> - which is
    /// possible because nothing about it is left in JavaScript.
    /// </remarks>
    [Test]
    public void TheFolderLayoutIsWrittenDownOnlyInVersioning()
    {
        const string layoutWords = @"['""`](Releases|current|\.ocd|\.json)['""`]";
        string[] owners = ["FolderLayout.cs", "VersionNaming.cs"];

        Assert.Multiple(() => {
            Assert.That(NamesOf(Matching(scripts, layoutWords)), Is.Empty,
                "projectFolder.js is handed paths and works on them. Knowing where releases live, "
                + "what the working file is called or what a version's files are named would put "
                + "half of the Angular contract in a second language again.");

            Assert.That(NamesOf(Matching(csharp, layoutWords)), Is.SubsetOf(owners),
                "Where things are in a project folder is FolderLayout's business, and the names in "
                + "it are VersionNaming's. Ask them for a path rather than building one.");

            Assert.That(NamesOf(Matching(scripts, @"\$\{[^}]*\}\.(ocd|json)")), Is.Empty,
                "A file name built from a version name in JavaScript is the layout restated.");
        });
    }

    [Test]
    public void TheJsonShapeSharedWithAngularIsConfiguredOnce()
    {
        IReadOnlyList<SourceFile> offenders = Matching(csharp, @"JsonNamingPolicy\.CamelCase");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "AngularJson.cs" }),
            "Two byte-identical JsonSerializerOptions, one per service, is the cheapest possible "
            + "way to let two file formats drift apart.");
    }

    [Test]
    public void TheJavaScriptTeardownDanceIsWrittenOnce()
    {
        IReadOnlyList<SourceFile> offenders = Matching(csharp, @"JSDisconnectedException");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "JsModule.cs" }),
            "A page can be torn down while JavaScript is already gone, and a throwing DisposeAsync "
            + "takes the rest of the teardown with it. Every caller releases modules through "
            + "JsModule so the rule is written once.");
    }

    [Test]
    public void TheLanguageSubscriptionIsWrittenOnce()
    {
        IReadOnlyList<SourceFile> offenders = Matching(csharp, @"Changed\s*(\+|-)=");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "LocalisedComponent.cs" }),
            "Subscribe and unsubscribe have to agree, and forgetting the second half leaks the "
            + "component for the lifetime of the app.");
    }

    /* ----------------------------------------------------------------- helper */

    private static IReadOnlyList<SourceFile> Matching(
        IEnumerable<SourceFile> files, string pattern, RegexOptions options = RegexOptions.None)
        => [.. files.Where(file => Regex.IsMatch(file.Code, pattern, options))];

    private static string[] NamesOf(IEnumerable<SourceFile> files)
        => [.. files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static SourceFile Single(IEnumerable<SourceFile> files, string name)
        => files.SingleOrDefault(file => file.Name == name)
           ?? throw new AssertionException($"Expected exactly one {name}.");

    private static IEnumerable<string> Lines(SourceFile file)
        => file.Code.Split('\n').Select(line => line.TrimEnd('\r'));

    /// <summary>Relative module specifiers an ES module imports, as bare file names.</summary>
    private static IEnumerable<string> RelativeImportsOf(SourceFile script)
        => Regex.Matches(script.Code, @"from\s+'(?<module>\.[^']+)'")
                .Select(match => Path.GetFileName(match.Groups["module"].Value));
}
