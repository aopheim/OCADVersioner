using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Wasm.Services;

/// <summary>One button: a choice the user can make about an item or an overlap.</summary>
/// <param name="Choice">What it decides, or null to go back to the default.</param>
/// <param name="Side">"a", "b" or null, for its colour.</param>
/// <param name="Current">Whether this is what is decided now.</param>
/// <param name="Enabled">False for a choice the merge would overrule, shown so the reason can be read.</param>
public sealed record ChoiceOption(Choice? Choice, string Label, string Side, bool Current, bool Enabled = true);

/// <summary>
/// Which choices an item or overlap offers, and which of them is in force: the
/// same buttons wherever a decision can be made - side panel, map, conflict walker.
/// </summary>
public static class MergeChoices
{
    public static IReadOnlyList<ChoiceOption> For(object source, MergeSession session, string labelA, string labelB, Localiser l)
    {
        MergeDecisions decisions = session.Decisions;
        switch (source) {
            case OverlapGroup group:
                return Conflict(decisions.Overlaps.TryGetValue(group.Key, out Choice g) ? g : null, keepBoth: true);

            case MergeItem item when item.Outcome == Outcome.Conflict:
                return Conflict(decisions.Items.TryGetValue(item.Key, out Choice c) ? c : null,
                                keepBoth: item.Kind == ItemKind.Object, keepBase: !Locked(item));

            case MergeItem item:
                return session.Resolution.ChoiceFor(item) == Choice.Base
                    ? [new(null, l["#_mergeAccept"], null, false)]
                    : [new(Choice.Base, l["#_mergeReject"], null, false, Enabled: !Locked(item))];

            default:
                return [];
        }

        IReadOnlyList<ChoiceOption> Conflict(Choice? current, bool keepBoth, bool keepBase = true)
        {
            List<ChoiceOption> options = [
                new(Choice.A, l.Format("#_mergeTake", ("name", labelA)), "a", current == Choice.A),
                new(Choice.B, l.Format("#_mergeTake", ("name", labelB)), "b", current == Choice.B),
            ];
            if (keepBoth)
                options.Add(new(Choice.Both, l["#_mergeKeepBoth"], null, current == Choice.Both));
            options.Add(new(Choice.Base, l["#_mergeKeepBase"], null, current == Choice.Base, keepBase));
            return options;
        }

        bool Locked(MergeItem item) => item is SymbolItem symbol && LockedByObjects(symbol, session.Resolution);
    }

    /// <summary>
    /// Whether leaving out <paramref name="symbol"/> is not the user's to choose: a
    /// branch added it and objects in the merge use it, so it stays while they do.
    /// </summary>
    public static bool LockedByObjects(SymbolItem symbol, MergeResolution resolution)
        => symbol.Base is null && resolution.UsersOf(symbol.Number).Count > 0;

    /// <summary>Applies an option to the session: the one place a button's meaning is carried out.</summary>
    public static void Apply(MergeSession session, object source, Choice? choice)
    {
        switch (source, choice) {
            case (OverlapGroup group, Choice c):
                session.DecideOverlap(group, c);
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
