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

    /// <summary>
    /// The domain projects reference the framework and, where named, each other -
    /// nothing vendored, nothing from the browser.
    /// </summary>
    [TestCase("OcadViewer.Versioning")]
    [TestCase("OcadViewer.Ocd")]
    [TestCase("OcadViewer.Comparison", "OcadViewer.Ocd")]
    [TestCase("OcadViewer.Merge", "OcadViewer.Comparison", "OcadViewer.Ocd")]
    public void TheDomainProjectsReferenceOnlyTheFrameworkAndEachOther(string assembly, params string[] allowed)
    {
        IReadOnlyList<string> references = ReferencesOf(assembly);

        string[] offending = [.. references.Where(reference =>
            !reference.StartsWith("System", StringComparison.Ordinal)
            && reference is not ("netstandard" or "mscorlib")
            && !allowed.Contains(reference))];

        Assert.That(offending, Is.Empty,
            $"{assembly} must stay free of everything but the framework, so its rules can be tested "
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

    /// <summary>
    /// OCAD files are written by OcadViewer.Ocd's OcadEditor and nothing else.
    /// </summary>
    /// <remarks>
    /// The vendored PurplePen model can write OCAD files too (OcadExport, reached
    /// through InputOutput.WriteFile and WriteToBytes). It is compiled in only
    /// because InputOutput refers to it. It writes the drawing model back out, so
    /// whatever the model does not keep would be lost from a mapper's file. Read
    /// from the compiled code rather than the source, so that no alias or
    /// <c>using static</c> can hide a call.
    /// </remarks>
    [TestCase("OcadViewer.Core")]
    [TestCase("OcadViewer.RenderCli")]
    [TestCase("OcadViewer.Wasm")]
    [TestCase("OcadViewer.Comparison")]
    [TestCase("OcadViewer.Merge")]
    [TestCase("OcadViewer.Ocd")]
    [TestCase("OcadViewer.Versioning")]
    public void OnlyOcadEditorWritesOcadFiles(string assembly)
    {
        using FileStream stream = File.OpenRead(Locate(assembly));
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();

        string[] typesUsed = [.. metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Where(type => metadata.GetString(type.Namespace) == "PurplePen.MapModel")
            .Select(type => metadata.GetString(type.Name))
            .Where(name => name == "OcadExport")];

        string[] writersCalled = [.. metadata.MemberReferences
            .Select(handle => metadata.GetMemberReference(handle))
            .Where(member => member.Parent.Kind == HandleKind.TypeReference
                && metadata.GetTypeReference((TypeReferenceHandle)member.Parent) is var parent
                && metadata.GetString(parent.Namespace) == "PurplePen.MapModel"
                && metadata.GetString(parent.Name) == "InputOutput")
            .Select(member => metadata.GetString(member.Name))
            .Where(name => name.StartsWith("Write", StringComparison.Ordinal))
            .Select(name => $"InputOutput.{name}")];

        Assert.That(typesUsed.Concat(writersCalled), Is.Empty,
            $"{assembly} writes OCAD files through PurplePen. Write them with OcadViewer.Ocd's "
            + "OcadEditor, which copies the file byte for byte and changes only what it is told to.");
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
