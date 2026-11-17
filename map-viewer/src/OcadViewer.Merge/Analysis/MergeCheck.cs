using OcadViewer.Merge.Matching;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Analysis;

/// <summary>
/// Whether three files can be merged and look like they belong together: the
/// rules a merge screen shows before the merge starts, and the analyzer enforces.
/// </summary>
public static class MergeCheck
{
    /// <summary>A copy that still has less of base than this probably started from another file.</summary>
    public const int DescendsFromPercent = 50;

    /// <summary>Whether all three are saved in the same OCAD version, which a merge needs.</summary>
    public static bool SameVersion(OcadFile @base, OcadFile a, OcadFile b)
        => a.Version == @base.Version && b.Version == @base.Version;

    /// <summary>How much of base's objects, in percent, <paramref name="copy"/> still has.</summary>
    public static int SharedPercent(OcadFile @base, OcadFile copy)
        => @base.Objects.Count == 0
            ? 100
            : (int)Math.Round(100.0 * ObjectMatcher.Match(@base.Objects, copy.Objects).Pairs.Count / @base.Objects.Count);

    /// <summary>Whether a copy sharing <paramref name="percent"/> of base looks like it started from it.</summary>
    public static bool Descends(int percent) => percent >= DescendsFromPercent;
}
