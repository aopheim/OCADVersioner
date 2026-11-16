using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Merge.Regions;

/// <summary>
/// One choice applied to a region: a decision for every changed object wholly
/// inside it, and the objects that cross its border, which it leaves alone.
/// </summary>
/// <param name="Choices">Ready for <see cref="MergeSession.DecideAll"/>: one undo step.</param>
/// <param name="Crossing">Objects only partly inside; the user decides them one by one.</param>
public sealed record RegionDecision(
    IReadOnlyList<(MergeItem Item, Choice Choice)> Choices,
    IReadOnlyList<ObjectItem> Crossing)
{
    public static RegionDecision For(MergeAnalysis analysis, Region region, Choice choice)
    {
        List<(MergeItem, Choice)> choices = [];
        List<ObjectItem> crossing = [];

        foreach (ObjectItem item in analysis.Objects) {
            switch (region.Classify(item)) {
                case Placement.Inside:
                    choices.Add((item, GroupChoice.For(choice, item)));
                    break;
                case Placement.Crossing:
                    crossing.Add(item);
                    break;
            }
        }
        return new RegionDecision(choices, crossing);
    }

    /// <summary>Which of <paramref name="analysis"/>'s changes lie wholly inside and across <paramref name="region"/>.</summary>
    public static (IReadOnlyList<ObjectItem> Inside, IReadOnlyList<ObjectItem> Crossing) Contents(MergeAnalysis analysis, Region region)
    {
        RegionDecision any = For(analysis, region, Choice.Base);
        return ([.. any.Choices.Select(c => (ObjectItem)c.Item)], any.Crossing);
    }
}
