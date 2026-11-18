using System.Xml.Linq;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// The declared dependency graph: which project is allowed to know about which.
/// </summary>
/// <remarks>
/// The intended shape is a single chain pointing inward, from the hosts to the
/// vendored renderer, with domain projects hanging off the side that depend on
/// nothing:
///
/// <code>
///   Graphics2D ← MapModel ← Map_SkiaStd ← OcadViewer.Core ← RenderCli
///                                              ↑
///                      OcadViewer.Versioning ← OcadViewer.Wasm
///   OcadViewer.Ocd ←       OcadViewer.Merge      ←┘
/// </code>
///
/// OcadViewer.Ocd reads and edits OCAD files at the byte level; OcadViewer.Merge
/// builds on it and nothing else.
///
/// Nothing in the compiler keeps it that way: one <c>using</c> of
/// Microsoft.AspNetCore in the wrong project compiles. These tests do.
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
        ["OcadViewer.Core"] = ["Map_SkiaStd"],
        ["OcadViewer.Versioning"] = [],
        ["OcadViewer.Ocd"] = [],
        ["OcadViewer.Merge"] = ["OcadViewer.Ocd"],
        ["OcadViewer.RenderCli"] = ["OcadViewer.Core"],
        ["OcadViewer.Wasm"] = ["OcadViewer.Core", "OcadViewer.Versioning"],
        ["OcadViewer.ArchitectureTests"] = ["OcadViewer.Versioning"],
        ["OcadViewer.Core.Tests"] = ["OcadViewer.Core"],
        ["OcadViewer.E2ETests"] = ["OcadViewer.Versioning"],
        ["OcadViewer.Merge.Tests"] = ["OcadViewer.Merge", "OcadViewer.Ocd"],
        ["OcadViewer.Ocd.Tests"] = ["OcadViewer.Ocd"],
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

    /// <summary>
    /// The domain projects. Versioning states the contract this app shares with the
    /// Angular app; Ocd reads and edits OCAD files byte for byte; Merge decides what
    /// to merge. All have to stay testable without a browser, a renderer or a
    /// vendored assembly, so they reference nothing but each other.
    /// </summary>
    private static readonly string[] DomainProjects = ["OcadViewer.Versioning", "OcadViewer.Ocd", "OcadViewer.Merge"];

    [TestCaseSource(nameof(DomainProjects))]
    public void TheDomainProjectsDependOnNothingButEachOther(string name)
    {
        Project domain = Find(name);

        Assert.Multiple(() => {
            Assert.That(domain.ProjectReferences, Is.SubsetOf(DomainProjects),
                $"{name} is domain code. It has to stay testable without a browser, a renderer "
                + "or a vendored assembly.");
            Assert.That(domain.PackageReferences, Is.Empty,
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
