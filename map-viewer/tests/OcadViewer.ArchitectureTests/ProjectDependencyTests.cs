using System.Xml.Linq;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// The declared dependency graph: which project is allowed to know about which.
/// </summary>
/// <remarks>
/// The intended shape is a single chain pointing inward, from the hosts to the
/// vendored renderer, with a domain project hanging off the side that depends on
/// nothing:
///
/// <code>
///   Graphics2D ← MapModel ← Map_SkiaStd ← OcadViewer.Core ← RenderCli
///                   ↑                          ↑    ↑
///                   └──── OcadViewer.Diff ─────┘    │
///                      OcadViewer.Versioning ← OcadViewer.Wasm
/// </code>
///
/// OcadViewer.Diff sits beside the renderer rather than on top of it: it needs
/// the parsed Map, never the pixels.
///
/// That was true when these tests were written, and the only thing keeping it
/// true was a comment in a .csproj saying it should be. One <c>using</c> of
/// Microsoft.AspNetCore in the wrong project would have compiled.
/// </remarks>
[TestFixture]
public sealed class ProjectDependencyTests
{
    /// <summary>
    /// What each project may reference. A reference that is not listed here fails
    /// the build of the test suite, which is the point: adding one is a decision
    /// about the architecture and should have to be written down.
    /// </summary>
    private static readonly Dictionary<string, string[]> Allowed = new(StringComparer.Ordinal) {
        ["Graphics2D"] = [],
        ["DotSpatial.Projections"] = [],
        ["MapModel"] = ["Graphics2D", "DotSpatial.Projections"],
        ["Map_SkiaStd"] = ["Graphics2D", "MapModel"],
        // The diff reads the parsed Map and nothing above it: no renderer, no host.
        ["OcadViewer.Diff"] = ["MapModel"],
        // Core keeps a diff snapshot beside every parsed map, because Core is where
        // the file bytes and the Map are both in hand, once.
        ["OcadViewer.Core"] = ["Map_SkiaStd", "OcadViewer.Diff"],
        ["OcadViewer.Versioning"] = [],
        ["OcadViewer.RenderCli"] = ["OcadViewer.Core"],
        ["OcadViewer.Wasm"] = ["OcadViewer.Core", "OcadViewer.Diff", "OcadViewer.Versioning"],
        ["OcadViewer.ArchitectureTests"] = ["OcadViewer.Versioning"],
        ["OcadViewer.Diff.Tests"] = ["OcadViewer.Core", "OcadViewer.Diff"],
    };

    /// <summary>Packages that only a UI host has any business referencing.</summary>
    private static readonly string[] HostOnlyPackages = [
        "Microsoft.AspNetCore.",
        "Microsoft.JSInterop",
        "SkiaSharp.Views",
    ];

    private static IReadOnlyList<Project> projects;

    [OneTimeSetUp]
    public void ReadProjects() => projects = Project.ReadAll();

    [Test]
    public void EveryProjectIsAccountedFor()
    {
        string[] unknown = [.. projects.Select(p => p.Name).Where(name => !Allowed.ContainsKey(name))];

        Assert.That(unknown, Is.Empty,
            "A new project exists that the dependency rules say nothing about. Add it to "
            + $"{nameof(ProjectDependencyTests)}.{nameof(Allowed)} with the references it is "
            + "allowed to make, so the shape of the solution stays something we decided rather "
            + "than something that happened.");
    }

    [Test]
    public void NoProjectReferencesSomethingItMayNot()
    {
        List<string> violations = [];

        foreach (Project project in projects) {
            if (!Allowed.TryGetValue(project.Name, out string[] allowed))
                continue;

            foreach (string reference in project.ProjectReferences) {
                if (!allowed.Contains(reference, StringComparer.Ordinal))
                    violations.Add($"{project.Name} -> {reference}");
            }
        }

        Assert.That(violations, Is.Empty,
            "These references point somewhere they should not. Dependencies run inward: a host "
            + "may know about the library, never the other way round.");
    }

    [Test]
    public void TheGraphHasNoCycles()
    {
        Dictionary<string, string[]> edges = projects.ToDictionary(
            p => p.Name, p => p.ProjectReferences.ToArray(), StringComparer.Ordinal);

        List<string> cycles = [];
        HashSet<string> finished = new(StringComparer.Ordinal);

        foreach (string start in edges.Keys)
            Walk(start, [], finished, edges, cycles);

        Assert.That(cycles, Is.Empty, "A dependency cycle makes the layering meaningless.");

        static void Walk(string node, List<string> path, HashSet<string> finished,
                         Dictionary<string, string[]> edges, List<string> cycles)
        {
            if (finished.Contains(node))
                return;

            if (path.Contains(node, StringComparer.Ordinal)) {
                cycles.Add(string.Join(" -> ", path.Append(node)));
                return;
            }

            path.Add(node);
            foreach (string next in edges.TryGetValue(node, out string[] e) ? e : [])
                Walk(next, path, finished, edges, cycles);
            path.RemoveAt(path.Count - 1);

            finished.Add(node);
        }
    }

    [Test]
    public void TheDomainProjectDependsOnNothingAtAll()
    {
        Project versioning = Find("OcadViewer.Versioning");

        Assert.Multiple(() => {
            Assert.That(versioning.ProjectReferences, Is.Empty,
                "OcadViewer.Versioning states the contract this app shares with the Angular app. "
                + "It has to stay testable without a browser, a renderer or a vendored assembly.");
            Assert.That(versioning.PackageReferences, Is.Empty,
                "Not even a package. Its only dependency is the framework.");
        });
    }

    [Test]
    public void OnlyTheBrowserHostKnowsAboutTheBrowser()
    {
        List<string> violations = [];

        foreach (Project project in projects) {
            if (project.Name == "OcadViewer.Wasm")
                continue;

            foreach (string package in project.PackageReferences) {
                if (HostOnlyPackages.Any(prefix => package.StartsWith(prefix, StringComparison.Ordinal)))
                    violations.Add($"{project.Name} references {package}");
            }
        }

        Assert.That(violations, Is.Empty,
            "Hosting and UI packages belong to the host. OcadViewer.Core in particular has to go "
            + "on serving the console harness, which is the only thing proving it is host-agnostic.");
    }

    [Test]
    public void TheVendoredWrappersNeverDependOnOurCode()
    {
        string[] vendored = ["Graphics2D", "MapModel", "Map_SkiaStd", "DotSpatial.Projections"];
        List<string> violations = [];

        foreach (Project project in projects.Where(p => vendored.Contains(p.Name, StringComparer.Ordinal))) {
            foreach (string reference in project.ProjectReferences) {
                if (reference.StartsWith("OcadViewer.", StringComparison.Ordinal))
                    violations.Add($"{project.Name} -> {reference}");
            }
        }

        Assert.That(violations, Is.Empty,
            "The vendored PurplePen sources are compiled unmodified and re-synced from upstream. "
            + "Anything of ours they depended on would have to be vendored too.");
    }

    private static Project Find(string name)
        => projects.SingleOrDefault(p => p.Name == name)
           ?? throw new AssertionException($"No project called {name}.");

    /// <summary>One .csproj, reduced to what it depends on.</summary>
    private sealed record Project(string Name, IReadOnlyList<string> ProjectReferences,
                                  IReadOnlyList<string> PackageReferences)
    {
        public static IReadOnlyList<Project> ReadAll()
            => [.. Repo.ProjectFiles().Select(Read)];

        private static Project Read(FileInfo file)
        {
            XDocument document = XDocument.Load(file.FullName);

            // Some of these project files use no namespace; none use one in
            // practice, but reading by local name costs nothing and cannot break.
            List<string> ReferencesOf(string element, Func<string, string> name) =>
                [.. document.Descendants()
                            .Where(e => e.Name.LocalName == element)
                            .Select(e => (string)e.Attribute("Include"))
                            .Where(include => !string.IsNullOrWhiteSpace(include))
                            .Select(name)
                            .Distinct(StringComparer.Ordinal)
                            .Order(StringComparer.Ordinal)];

            return new Project(
                Path.GetFileNameWithoutExtension(file.Name),
                ReferencesOf("ProjectReference", include => Path.GetFileNameWithoutExtension(include.Replace('\\', '/'))),
                ReferencesOf("PackageReference", include => include));
        }
    }
}
