namespace OcadViewer.Tests.Shared;

/// <summary>
/// Finds the <c>map-viewer</c> directory from wherever a test assembly runs.
/// </summary>
/// <remarks>
/// Compiled into each test project that needs it, as a linked file, so the
/// suites agree on what "the source tree" is without a project of their own.
/// </remarks>
internal static class SourceTree
{
    /// <summary>The directory holding <c>MapViewer.slnx</c>, found by walking up from the test binaries.</summary>
    public static DirectoryInfo FindRoot()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MapViewer.slnx")))
            directory = directory.Parent;

        return directory
            ?? throw new DirectoryNotFoundException(
                $"No MapViewer.slnx above {AppContext.BaseDirectory}; these tests need the source tree.");
    }
}
