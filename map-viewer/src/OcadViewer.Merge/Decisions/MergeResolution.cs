using OcadViewer.Merge.Analysis;
using OcadViewer.Ocd;
using static OcadViewer.Merge.Decisions.ItemVersions;

namespace OcadViewer.Merge.Decisions;

/// <summary>
/// The decisions applied to an analysis: what each item becomes, and what still
/// needs the user. The single place the rules of precedence live.
/// </summary>
/// <remarks>
/// <para>For an item, in order: the user's decision for that item; then the decision
/// for the overlap group it is in; then the default - a clean change is accepted, a
/// conflict has no choice until the user makes one. A group choice, and "keep both"
/// on a change only one branch made, mean for each item what <see cref="GroupChoice"/>
/// says.</para>
/// <para>Symbols then decide for the objects that use them, and objects for symbols;
/// those rules are <see cref="SymbolDependencies"/>'s.</para>
/// <para>A resolution is a snapshot: it copies the decisions it is given, so changing
/// them afterwards changes nothing here. Make a new one when they change. What it
/// works out is computed once, on first use.</para>
/// </remarks>
public sealed class MergeResolution
{
    private readonly Dictionary<MergeItem, OverlapGroup> groupOf = new(ReferenceEqualityComparer.Instance);
    private readonly SymbolDependencies symbols;
    private readonly Lazy<IReadOnlyList<object>> unresolved;

    public MergeAnalysis Analysis { get; }

    /// <summary>The decisions as they were when this resolution was made.</summary>
    public MergeDecisions Decisions { get; }

    public MergeResolution(MergeAnalysis analysis, MergeDecisions decisions)
    {
        Analysis = analysis;
        Decisions = MergeDecisions.FromSnapshot(decisions.ToSnapshot());
        foreach (OverlapGroup group in analysis.Overlaps) {
            foreach (ObjectItem item in group.Items)
                groupOf[item] = group;
        }

        symbols = new SymbolDependencies(analysis, Chosen, item => Decisions.Items.ContainsKey(item.Key));
        unresolved = new(() => [.. analysis.AllItems.Where(i => !IsResolved(i)).Cast<object>()
                                           .Concat(analysis.Overlaps.Where(g => !IsResolved(g)))]);
    }

    /// <summary>What <paramref name="item"/> becomes, or null while it awaits the user.</summary>
    public Choice? ChoiceFor(MergeItem item) => item is SymbolItem symbol ? symbols.ChoiceFor(symbol) : Chosen(item);

    /// <summary>
    /// Why <paramref name="item"/> needs the user, or null if it does not: the
    /// analysis's conflict, or a deletion of a symbol objects in the merge still use.
    /// Stays set once decided, like any conflict.
    /// </summary>
    public ConflictKind? ConflictOf(MergeItem item)
        => item.Conflict ?? (item is SymbolItem symbol && symbols.IsDeletionInUse(symbol) ? ConflictKind.SymbolInUse : null);

    /// <summary>Whether <paramref name="item"/> is one the user decides: a conflict, now.</summary>
    public bool NeedsDecision(MergeItem item) => ConflictOf(item) is not null;

    /// <summary>
    /// Whether the user's decision for <paramref name="item"/> cannot be carried out:
    /// leaving out a symbol both branches added while objects use it. Such an item
    /// stays open, and a restored decision like it should be dropped.
    /// </summary>
    public bool IsOverruled(MergeItem item) => Decisions.Items.ContainsKey(item.Key) && ChoiceFor(item) is null;

    /// <summary>
    /// The versions of <paramref name="item"/> the merged map holds: none, one, or for
    /// "keep both" two, the second to be written as a new object. Undecided conflicts
    /// count as base. An object whose symbol the merge deletes is not held.
    /// </summary>
    public IReadOnlyList<T> ResultOf<T>(MergeItem<T> item) where T : class
        => [.. Versions(item, ChoiceFor(item) ?? Choice.Base).Where(v => v is not OcadObject o || !symbols.IsRemoved(o))];

    /// <summary>
    /// The objects in the merged map that use symbol <paramref name="number"/>, as the
    /// objects are decided now and before the symbol's own fate is applied: what keeps
    /// an added symbol, and what goes if the symbol is deleted.
    /// </summary>
    public IReadOnlyList<OcadObject> UsersOf(int number) => symbols.UsersOf(number);

    /// <summary>Whether <paramref name="symbol"/> is in the merge only because objects in it use it.</summary>
    public bool IsKeptForObjects(SymbolItem symbol) => symbols.IsKeptForObjects(symbol);

    /// <summary>Base's objects no branch changed that go because the merge deletes their symbol.</summary>
    public IEnumerable<OcadObject> RemovedWithSymbols => symbols.RemovedWithSymbols;

    public OverlapGroup GroupOf(MergeItem item) => groupOf.GetValueOrDefault(item);

    public bool IsResolved(MergeItem item) => ChoiceFor(item) is not null;

    public bool IsResolved(OverlapGroup group)
        => Decisions.Overlaps.ContainsKey(group.Key) || group.Items.All(i => Decisions.Items.ContainsKey(i.Key));

    /// <summary>Conflicting items and overlap groups still waiting for a decision.</summary>
    public IReadOnlyList<object> Unresolved => unresolved.Value;

    /// <summary>
    /// How many decisions the merge needs in all, decided or not: every item that
    /// needs one and every overlap group. Progress is this less <see cref="Unresolved"/>.
    /// </summary>
    public int DecisionsNeeded => Analysis.AllItems.Count(NeedsDecision) + Analysis.Overlaps.Count;

    /// <summary>Whether the merged map can be written.</summary>
    public bool IsComplete => Unresolved.Count == 0;

    /// <summary>The user's choice, the group's, or the default; before symbols have their say.</summary>
    private Choice? Chosen(MergeItem item)
    {
        if (Decisions.Items.TryGetValue(item.Key, out Choice decided))
            return decided == Choice.Both ? GroupChoice.For(decided, item) : decided;

        if (groupOf.TryGetValue(item, out OverlapGroup group) && Decisions.Overlaps.TryGetValue(group.Key, out Choice forGroup))
            return GroupChoice.For(forGroup, item);

        return Own(item);
    }
}
