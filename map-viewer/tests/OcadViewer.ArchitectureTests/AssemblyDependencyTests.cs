using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using NUnit.Framework;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// The dependency graph as the compiler actually emitted it.
/// </summary>
/// <remarks>
/// <see cref="ProjectDependencyTests"/> reads what the project files declare,
/// which is the intent. This reads what came out, which is the fact - it would
/// catch a dependency arriving through a transitive reference nobody meant to
/// expose.
///
/// The assemblies are read as metadata, never loaded. Loading them would run the
/// vendored code's module initialisers, and would also make this suite fail
/// outright on a machine whose policy refuses unsigned binaries.
/// </remarks>
[TestFixture]
public sealed class AssemblyDependencyTests
{
    private static readonly string[] BrowserAndUi = [
        "Microsoft.AspNetCore.",
        "Microsoft.JSInterop",
        "System.Runtime.InteropServices.JavaScript",
    ];

    [Test]
    public void TheRenderGlueCarriesNoBrowserOrUiDependency()
    {
        IReadOnlyList<string> references = ReferencesOf("OcadViewer.Core");

        string[] offending = [.. references.Where(
            reference => BrowserAndUi.Any(prefix => reference.StartsWith(prefix, StringComparison.Ordinal)))];

        Assert.That(offending, Is.Empty,
            "OcadViewer.Core is the only thing between the app and the vendored renderer, and it "
            + "has to stay usable from a console. Its references were: "
            + string.Join(", ", references));
    }

    [Test]
    public void TheDomainProjectReferencesOnlyTheFramework()
    {
        IReadOnlyList<string> references = ReferencesOf("OcadViewer.Versioning");

        string[] offending = [.. references.Where(reference =>
            !reference.StartsWith("System", StringComparison.Ordinal)
            && reference is not ("netstandard" or "mscorlib"))];

        Assert.That(offending, Is.Empty,
            "The project-folder contract must stay free of everything, so its rules can be tested "
            + "directly. References found: " + string.Join(", ", references));
    }

    [TestCase("MapModel")]
    [TestCase("Graphics2D")]
    [TestCase("Map_SkiaStd")]
    [TestCase("DotSpatial.Projections")]
    public void TheVendoredAssembliesDoNotReferenceOurs(string assembly)
    {
        string[] offending = [.. ReferencesOf(assembly)
            .Where(reference => reference.StartsWith("OcadViewer.", StringComparison.Ordinal))];

        Assert.That(offending, Is.Empty,
            $"{assembly} is vendored and re-synced from upstream PurplePen. A dependency on our "
            + "code would survive exactly until the next sync.");
    }

    private static IReadOnlyList<string> ReferencesOf(string assemblyName)
    {
        string path = Locate(assemblyName);

        using FileStream stream = File.OpenRead(path);
        using PEReader pe = new(stream);

        MetadataReader metadata = pe.GetMetadataReader();

        return [.. metadata.AssemblyReferences
                           .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
                           .Distinct(StringComparer.Ordinal)
                           .Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// The newest build of an assembly, looked for under its own project only -
    /// several projects copy each other's output, and a stale copy would make
    /// this test lie.
    /// </summary>
    private static string Locate(string assemblyName)
    {
        string projectDirectory = Path.Combine(Repo.Src, assemblyName);

        if (!Directory.Exists(projectDirectory))
            Assert.Fail($"No project directory for {assemblyName} at {projectDirectory}.");

        string[] candidates = [.. Directory
            .EnumerateFiles(projectDirectory, $"{assemblyName}.dll", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                                         StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)];

        if (candidates.Length == 0)
            Assert.Fail($"{assemblyName}.dll has not been built. Run `dotnet build` on MapViewer.slnx first.");

        return candidates[0];
    }
}
