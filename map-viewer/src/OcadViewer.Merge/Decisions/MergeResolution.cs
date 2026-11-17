using OcadViewer.Merge.Analysis;
using OcadViewer.Ocd;

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
/// <para>Symbols then decide for the objects that use them, so the merged map never
/// has an object without its symbol. A symbol a branch added is kept while objects
/// in the merge use it. A symbol a branch deleted - one branch or both - that objects
/// in the merge still use waits for the user (<see cref="ConflictKind.SymbolInUse"/>);
/// whether it does follows the current decisions, so rejecting those objects lets the
/// deletion through. A symbol the merge deletes takes every object that uses it along.</para>
/// <para>A resolution is a snapshot of one set of decisions; make a new one when they
/// change. What it works out about symbols and open items is computed once, on first use.</para>
/// </remarks>
public sealed class MergeResolution
{
    private readonly Dictionary<MergeItem, OverlapGroup> groupOf = new(ReferenceEqualityComparer.Instance);
    private readonly Lazy<IReadOnlyList<OcadObject>> untouched;
    private readonly Lazy<ILookup<int, OcadObject>> users;
    private readonly Lazy<IReadOnlySet<int>> removedSymbols;
    private readonly Lazy<IReadOnlyList<object>> unresolved;

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

        untouched = new(() => {
            HashSet<OcadObject> changed = new(analysis.Objects.Select(i => i.Base).Where(o => o is not null), ReferenceEqualityComparer.Instance);
            return [.. analysis.Base.Objects.Where(o => !changed.Contains(o))];
        });
        users = new(() => analysis.Objects.SelectMany(ChosenVersions).Concat(untouched.Value).ToLookup(o => o.Symbol));
        removedSymbols = new(() => analysis.Symbols
            .Where(s => s.Base is not null && Version(s, ChoiceFor(s) ?? Choice.Base) is null)
            .Select(s => s.Number).ToHashSet());
        unresolved = new(() => [.. analysis.AllItems.Where(i => !IsResolved(i)).Cast<object>()
                                           .Concat(analysis.Overlaps.Where(g => !IsResolved(g)))]);
    }

    /// <summary>What <paramref name="item"/> becomes, or null while it awaits the user.</summary>
    public Choice? ChoiceFor(MergeItem item)
    {
        if (item is not SymbolItem symbol)
            return Chosen(item);
        if (IsDeletionInUse(symbol) && !Decisions.Items.ContainsKey(symbol.Key))
            return null;
        return KeptWhileUsed(symbol, Chosen(symbol));
    }

    /// <summary>
    /// Why <paramref name="item"/> needs the user, or null if it does not: the
    /// analysis's conflict, or a deletion of a symbol objects in the merge still use.
    /// Stays set once decided, like any conflict.
    /// </summary>
    public ConflictKind? ConflictOf(MergeItem item)
        => item.Conflict ?? (item is SymbolItem symbol && IsDeletionInUse(symbol) ? ConflictKind.SymbolInUse : null);

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
        => [.. Versions(item, ChoiceFor(item) ?? Choice.Base)
                .Where(v => v is not OcadObject o || !removedSymbols.Value.Contains(o.Symbol))];

    /// <summary>
    /// The objects in the merged map that use symbol <paramref name="number"/>, as the
    /// objects are decided now and before the symbol's own fate is applied: what keeps
    /// an added symbol, and what goes if the symbol is deleted.
    /// </summary>
    public IReadOnlyList<OcadObject> UsersOf(int number) => [.. users.Value[number]];

    /// <summary>Whether <paramref name="symbol"/> is in the merge only because objects in it use it.</summary>
    public bool IsKeptForObjects(SymbolItem symbol)
        => Chosen(symbol) is Choice chosen && symbol.Base is null && Version(symbol, chosen) is null
        && ChoiceFor(symbol) is not null && users.Value[symbol.Number].Any();

    /// <summary>Base's objects no branch changed that go because the merge deletes their symbol.</summary>
    public IEnumerable<OcadObject> RemovedWithSymbols => untouched.Value.Where(o => removedSymbols.Value.Contains(o.Symbol));

    public OverlapGroup GroupOf(MergeItem item) => groupOf.GetValueOrDefault(item);

    public bool IsResolved(MergeItem item) => ChoiceFor(item) is not null;

    public bool IsResolved(OverlapGroup group)
        => Decisions.Overlaps.ContainsKey(group.Key) || group.Items.All(i => Decisions.Items.ContainsKey(i.Key));

    /// <summary>Conflicting items and overlap groups still waiting for a decision.</summary>
    public IReadOnlyList<object> Unresolved => unresolved.Value;

    /// <summary>Whether the merged map can be written.</summary>
    public bool IsComplete => Unresolved.Count == 0;

    /// <summary>The user's choice, the group's, or the default; before symbols have their say.</summary>
    private Choice? Chosen(MergeItem item)
    {
        if (Decisions.Items.TryGetValue(item.Key, out Choice decided))
            return decided == Choice.Both ? GroupChoice.For(decided, item) : decided;

        if (groupOf.TryGetValue(item, out OverlapGroup group) && Decisions.Overlaps.TryGetValue(group.Key, out Choice forGroup))
            return GroupChoice.For(forGroup, item);

        return Default(item);
    }

    /// <summary>
    /// A clean deletion of a base symbol - by one branch or both - that objects in
    /// the merge, as decided now, still use.
    /// </summary>
    private bool IsDeletionInUse(SymbolItem symbol)
        => symbol.Base is not null && symbol.Outcome != Outcome.Conflict
        && Default(symbol) is Choice taken && Version(symbol, taken) is null
        && users.Value[symbol.Number].Any();

    /// <summary>
    /// A symbol a branch added stays while objects in the merge use it, taken from the
    /// branch that has it. When both added it differently the user has to pick one.
    /// </summary>
    private Choice? KeptWhileUsed(SymbolItem symbol, Choice? chosen)
    {
        if (chosen is not Choice c || symbol.Base is not null || Version(symbol, c) is not null || !users.Value[symbol.Number].Any())
            return chosen;
        if (symbol.A is null || symbol.B is null)
            return symbol.A is null ? Choice.B : Choice.A;
        return symbol.Outcome == Outcome.BothSame ? Choice.A : null;
    }

    private IEnumerable<OcadObject> ChosenVersions(ObjectItem item) => Versions(item, Chosen(item) ?? Choice.Base);

    /// <summary>The versions a choice keeps, before symbols are applied.</summary>
    private static IEnumerable<T> Versions<T>(MergeItem<T> item, Choice choice) where T : class
    {
        IEnumerable<T> versions = choice == Choice.Both && item.Kind == ItemKind.Object
            ? [Version(item, Choice.A), Version(item, Choice.B)]
            : [Version(item, choice == Choice.Both ? Choice.A : choice)];
        return versions.Where(v => v is not null);
    }

    /// <summary>A file's version of an item; base's where that branch left it alone.</summary>
    private static T Version<T>(MergeItem<T> item, Choice choice) where T : class => choice switch {
        Choice.A => item.ChangeA == ChangeKind.None ? item.Base : item.A,
        Choice.B => item.ChangeB == ChangeKind.None ? item.Base : item.B,
        _ => item.Base,
    };

    private static Choice? Default(MergeItem item) => item.Outcome switch {
        Outcome.OnlyA or Outcome.BothSame => Choice.A,
        Outcome.OnlyB => Choice.B,
        _ => null,
    };
}
