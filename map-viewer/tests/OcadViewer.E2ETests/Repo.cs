using System.Reflection;

namespace OcadViewer.E2ETests;

/// <summary>Paths into the source tree the tests run from.</summary>
internal static class Repo
{
    /// <summary>The <c>map-viewer</c> directory, found by walking up from the test binaries.</summary>
    public static string Root { get; } = OcadViewer.Tests.Shared.SourceTree.FindRoot().FullName;

    /// <summary>A sample map from <c>map-viewer/testdata</c>.</summary>
    public static string TestMap(string fileName)
    {
        string path = Path.Combine(Root, "testdata", fileName);
        return File.Exists(path) ? path : throw new FileNotFoundException("No such test map.", path);
    }

    /// <summary>A script embedded from the <c>Scripts</c> folder.</summary>
    public static string Script(string fileName)
    {
        Assembly assembly = typeof(Repo).Assembly;
        string name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(name)!;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
