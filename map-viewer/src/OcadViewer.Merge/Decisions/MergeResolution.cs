using OcadViewer.Merge.Analysis;

namespace OcadViewer.Merge.Decisions;

/// <summary>
/// The decisions applied to an analysis: what each item becomes, and what still
/// needs the user. The single place the rules of precedence live.
/// </summary>
/// <remarks>
/// For an item, in order: the user's decision for that item; then the decision for
/// the overlap group it is in; then the default - a clean change is accepted, a
/// conflict has no choice until the user makes one.
/// </remarks>
public sealed class MergeResolution
{
    private readonly Dictionary<MergeItem, OverlapGroup> groupOf = new(ReferenceEqualityComparer.Instance);

    public MergeAnalysis Analysis { get; }
    public MergeDecisions Decisions { get; }

    public MergeResolution(MergeAnalysis analysis, MergeDecisions decisions)
    {
        Analysis = analysis;
        Decisions = decisions;
        foreach (OverlapGroup group in analysis.Overlaps) {
            foreach (ObjectItem item in group.Items)
                groupOf[item] = group;
        }
    }

    /// <summary>What <paramref name="item"/> becomes, or null while it awaits the user.</summary>
    public Choice? ChoiceFor(MergeItem item)
    {
        if (Decisions.Items.TryGetValue(item.Key, out Choice decided))
            return decided;

        if (groupOf.TryGetValue(item, out OverlapGroup group) && Decisions.Overlaps.TryGetValue(group.Key, out Choice forGroup))
            return Apply(forGroup, item);

        return Default(item);
    }

    public OverlapGroup GroupOf(MergeItem item) => groupOf.GetValueOrDefault(item);

    public bool IsResolved(MergeItem item) => item.Outcome != Outcome.Conflict || Decisions.Items.ContainsKey(item.Key);

    public bool IsResolved(OverlapGroup group)
        => Decisions.Overlaps.ContainsKey(group.Key) || group.Items.All(i => Decisions.Items.ContainsKey(i.Key));

    /// <summary>Conflicting items and overlap groups still waiting for a decision.</summary>
    public IReadOnlyList<object> Unresolved
        => [.. Analysis.AllItems.Where(i => !IsResolved(i)).Cast<object>()
                                .Concat(Analysis.Overlaps.Where(g => !IsResolved(g)))];

    /// <summary>Whether the merged map can be written.</summary>
    public bool IsComplete => Unresolved.Count == 0;

    private static Choice? Default(MergeItem item) => item.Outcome switch {
        Outcome.OnlyA or Outcome.BothSame => Choice.A,
        Outcome.OnlyB => Choice.B,
        _ => null,
    };

    /// <summary>A group choice, as it applies to one clean change inside the group.</summary>
    private static Choice Apply(Choice forGroup, MergeItem item)
    {
        Choice own = item.Outcome == Outcome.OnlyA ? Choice.A : Choice.B;
        return forGroup switch {
            Choice.Both => own,
            Choice.Base => Choice.Base,
            _ => forGroup == own ? own : Choice.Base,
        };
    }
}
