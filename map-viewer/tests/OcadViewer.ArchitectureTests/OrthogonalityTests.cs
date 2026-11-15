using System.Text.RegularExpressions;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// One rule per test: something that must stay knowable in one place.
/// </summary>
/// <remarks>
/// These read the source rather than the compiled output, because most of what
/// goes wrong here goes wrong between languages. The worst offender this suite was
/// written for - an untyped slot index that meant the same thing in a Razor page,
/// a component, a JavaScript module and a <c>[JSExport]</c> - was invisible to the
/// compiler from every one of those four sides.
///
/// A rule here is not a style preference. Each one marks a place where a single
/// decision had been written down more than once, and so could disagree with
/// itself.
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
            "A map is identified by its version name. The slot index this replaces had to mean "
            + "the same thing in the page, the component, the Leaflet module and the interop at "
            + "once, so a third pane - or a single-map view - could not be added without touching "
            + "all four. It also tied a parsed map to a screen position, which forced both sides "
            + "to be re-read and re-parsed whenever either changed.");
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
            "TileInterop is the JavaScript face of MapStore and nothing else. Everything it used "
            + "to decide for itself - slot bookkeeping, timing, staging fonts, converting pixel "
            + "formats, formatting JSON by hand - belongs in a project that a console can call "
            + "and a test can reach.");
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
            + "composition root. In particular the Leaflet module must not reach the .NET runtime "
            + "- it did, and that made the viewport impossible to load, test or reuse without "
            + "Blazor underneath it.");
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
            scripts, @"showDirectoryPicker|getDirectoryHandle|getFileHandle|createWritable");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "projectFolder.js" }),
            "The File System Access API is Chromium-only and permission-bound. Keeping it behind "
            + "one module is what makes the rest of the viewer testable, and what will make "
            + "another way of getting at files possible without touching anything else.");
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
         *   projectFolder.js  the directory handles, which cannot cross into .NET
         *                     and cannot be re-obtained without asking the user
         *   dotnetExports.js  the .NET runtime
         *   appInsights.js    the telemetry SDK, once it has loaded
         * What the rule is really after is coordination state - a queue, a latch,
         * a timer - which is what makes a module secretly unrepeatable. */
        string[] exempt = ["projectFolder.js", "dotnetExports.js", "appInsights.js"];

        List<string> violations = [];

        foreach (SourceFile script in scripts.Where(s => !exempt.Contains(s.Name, StringComparer.Ordinal))) {
            foreach (string line in Lines(script)) {
                if (Regex.IsMatch(line, @"^(let|var)\s"))
                    violations.Add($"{script.Name}: {line.Trim()}");
            }
        }

        Assert.That(violations, Is.Empty,
            "Module-level mutable state is a singleton with no name. The tile pump used to be "
            + "exactly that - one queue, one latch and one watchdog shared by every map on the "
            + "page - which made a second viewport quietly impossible.");
    }

    [Test]
    public void OnlyTheRendererKnowsWhatKindOfChangeAPixelIs()
    {
        IReadOnlyList<SourceFile> offenders = Matching(
            all, @"\b(appeared|vanished|altered)\b", RegexOptions.IgnoreCase);

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "MapStore.cs" }),
            "The renderer classifies each changed pixel, because it is the only thing that can. "
            + "Nothing above it reads that classification: the overlay asks one question - did "
            + "this change - so the only thing crossing the boundary is \"zero means unchanged\". "
            + "The JavaScript used to carry a copy of the four names to decide which of them "
            + "each filter showed, and a copy of their numbers with no type to keep it honest.");
    }

    /* ------------------------------------------------- contracts stated once */

    [Test]
    public void TheReleaseNamingRuleIsStatedOnce()
    {
        IReadOnlyList<SourceFile> offenders = Matching(all, @"['""]V['""{]");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "VersionNaming.cs" }),
            "The V<n> rule is a contract with the Angular app, which reads and writes the same "
            + "folders. It was stated in the JavaScript that scanned the directory and again in "
            + "two places in C# - in two languages, with nothing keeping them in step.");
    }

    [Test]
    public void WhatAPieceOfLoadingIsWorthIsJudgedInOnePlace()
    {
        IReadOnlyList<SourceFile> offenders = Matching(all, @"MillisecondsPer");

        Assert.That(NamesOf(offenders), Is.EquivalentTo(new[] { "LoadPlan.cs" }),
            "The browser knows how many bytes a map is and counts the tiles it draws; what "
            + "that is worth against the rest of the wait is a judgement, and it belongs "
            + "where a test can reach it. A second copy in JavaScript would be a progress bar "
            + "that disagrees with itself about how far along it is.");
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
            + "takes the rest of the teardown with it. That rule was copied into four files.");
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
