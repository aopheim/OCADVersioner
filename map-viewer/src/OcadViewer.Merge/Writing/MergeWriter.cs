using OcadViewer.Merge.Decisions;

namespace OcadViewer.Merge.Writing;

/// <summary>Writes the merged map: plan the resolved choices, splice them into base.</summary>
public static class MergeWriter
{
    /// <param name="now">The creation date for objects kept twice. Passed in so tests can pin it.</param>
    /// <exception cref="InvalidOperationException">A conflict or overlap is still undecided.</exception>
    public static byte[] Write(MergeResolution resolution, DateTime now)
        => OcadSplicer.Write(resolution.Analysis.Base, MergePlanner.Plan(resolution), now);

    /// <summary>The map as decided so far, undecided conflicts left as base. For showing only.</summary>
    public static byte[] Preview(MergeResolution resolution, DateTime now)
        => OcadSplicer.Write(resolution.Analysis.Base, MergePlanner.Plan(resolution, preview: true), now);
}
