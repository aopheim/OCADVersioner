using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Regions;

namespace OcadViewer.Wasm.Services;

/// <summary>One button: a choice the user can make about an item or an overlap.</summary>
/// <param name="Choice">What it decides, or null to go back to the default.</param>
/// <param name="Side">"a", "b" or null, for its colour.</param>
/// <param name="Current">Whether this is what is decided now.</param>
public sealed record ChoiceOption(Choice? Choice, string Label, string Side, bool Current);

/// <summary>
/// Which choices an item, overlap or region offers, and which of them is in force:
/// the same buttons wherever a decision can be made - side panel, regions, conflict
/// walker.
/// </summary>
public static class MergeChoices
{
    public static IReadOnlyList<ChoiceOption> For(object source, MergeSession session, string labelA, string labelB, Localiser l)
    {
        MergeDecisions decisions = session.Decisions;
        switch (source) {
            case OverlapGroup group:
                return Conflict(decisions.Overlaps.TryGetValue(group.Key, out Choice g) ? g : null, keepBoth: true);

            case MergeRegion region:
                return Conflict(MergeRegions.Current(session, region), keepBoth: true);

            case MergeItem item when item.Outcome == Outcome.Conflict:
                return Conflict(decisions.Items.TryGetValue(item.Key, out Choice c) ? c : null,
                                keepBoth: item.Kind == ItemKind.Object);

            case MergeItem item:
                return session.Resolution.ChoiceFor(item) == Choice.Base
                    ? [new(null, l["#_mergeAccept"], null, false)]
                    : [new(Choice.Base, l["#_mergeReject"], null, false)];

            default:
                return [];
        }

        IReadOnlyList<ChoiceOption> Conflict(Choice? current, bool keepBoth)
        {
            List<ChoiceOption> options = [
                new(Choice.A, l.Format("#_mergeTake", ("name", labelA)), "a", current == Choice.A),
                new(Choice.B, l.Format("#_mergeTake", ("name", labelB)), "b", current == Choice.B),
            ];
            if (keepBoth)
                options.Add(new(Choice.Both, l["#_mergeKeepBoth"], null, current == Choice.Both));
            options.Add(new(Choice.Base, l["#_mergeKeepBase"], null, current == Choice.Base));
            return options;
        }
    }

    /// <summary>Applies an option to the session: the one place a button's meaning is carried out.</summary>
    public static void Apply(MergeSession session, object source, Choice? choice)
    {
        switch (source, choice) {
            case (OverlapGroup group, Choice c):
                session.DecideOverlap(group, c);
                break;
            case (MergeRegion region, Choice c):
                session.DecideAll(RegionDecision.For(session.Analysis, region.Region, c).Choices);
                break;
            case (OverlapGroup group, null):
                session.Forget(group.Key);
                break;
            case (MergeItem item, Choice c):
                session.Decide(item, c);
                break;
            case (MergeItem item, null):
                session.Forget(item.Key);
                break;
        }
    }
}
