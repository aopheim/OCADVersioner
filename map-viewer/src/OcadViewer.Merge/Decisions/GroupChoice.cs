using OcadViewer.Merge.Analysis;

namespace OcadViewer.Merge.Decisions;

/// <summary>
/// What one choice made for many items at once - an overlap group, a region -
/// means for each item in it. The single rule, so groups and regions agree.
/// </summary>
public static class GroupChoice
{
    /// <remarks>
    /// A conflict takes the choice as it is. A clean change is kept when the choice
    /// is its own side or "both", and rejected (base) when the choice is base or the
    /// other side. A change both branches made identically is kept unless the choice
    /// is base.
    /// </remarks>
    public static Choice For(Choice forGroup, MergeItem item)
    {
        if (item.Outcome == Outcome.Conflict)
            return forGroup == Choice.Both && item.Kind != ItemKind.Object ? Choice.Base : forGroup;
        if (item.Outcome == Outcome.BothSame)
            return forGroup == Choice.Base ? Choice.Base : Choice.A;

        Choice own = item.Outcome == Outcome.OnlyB ? Choice.B : Choice.A;
        return forGroup == Choice.Both || forGroup == own ? own : Choice.Base;
    }
}
