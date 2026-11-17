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
/// conflict has no choice until the user makes one. "Keep both" on a change only
/// one branch made means taking that branch.</para>
/// <para>Symbols then decide for the objects that use them, so the merged map never
/// has an object without its symbol: a symbol a branch added is kept while objects
/// in the merge use it, and a symbol the merge deletes takes every object that uses
/// it along.</para>
/// <para>A resolution is a snapshot of one set of decisions; make a new one when they
/// change. What it works out about symbols is computed once, on first use.</para>
/// </remarks>
public sealed class MergeResolution
{
    private readonly Dictionary<MergeItem, OverlapGroup> groupOf = new(ReferenceEqualityComparer.Instance);
    private readonly Lazy<IReadOnlyList<OcadObject>> untouched;
    private readonly Lazy<ILookup<int, OcadObject>> users;
    private readonly Lazy<IReadOnlySet<int>> removedSymbols;

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
    }

    /// <summary>What <paramref name="item"/> becomes, or null while it awaits the user.</summary>
    public Choice? ChoiceFor(MergeItem item)
    {
        Choice? chosen = Chosen(item);
        return item is SymbolItem symbol ? KeptWhileUsed(symbol, chosen) : chosen;
    }

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
        => Chosen(symbol) is Choice chosen && symbol.Base is null && Version(symbol, chosen) is null && users.Value[symbol.Number].Any();

    /// <summary>Base's objects no branch changed that go because the merge deletes their symbol.</summary>
    public IEnumerable<OcadObject> RemovedWithSymbols => untouched.Value.Where(o => removedSymbols.Value.Contains(o.Symbol));

    public OverlapGroup GroupOf(MergeItem item) => groupOf.GetValueOrDefault(item);

    public bool IsResolved(MergeItem item) => ChoiceFor(item) is not null;

    public bool IsResolved(OverlapGroup group)
        => Decisions.Overlaps.ContainsKey(group.Key) || group.Items.All(i => Decisions.Items.ContainsKey(i.Key));

    /// <summary>Conflicting items and overlap groups still waiting for a decision.</summary>
    public IReadOnlyList<object> Unresolved
        => [.. Analysis.AllItems.Where(i => !IsResolved(i)).Cast<object>()
                                .Concat(Analysis.Overlaps.Where(g => !IsResolved(g)))];

    /// <summary>Whether the merged map can be written.</summary>
    public bool IsComplete => Unresolved.Count == 0;

    /// <summary>The user's choice, the group's, or the default; before symbols have their say.</summary>
    private Choice? Chosen(MergeItem item)
    {
        if (Decisions.Items.TryGetValue(item.Key, out Choice decided))
            return decided == Choice.Both && item.Outcome != Outcome.Conflict ? Default(item) : decided;

        if (groupOf.TryGetValue(item, out OverlapGroup group) && Decisions.Overlaps.TryGetValue(group.Key, out Choice forGroup))
            return Apply(forGroup, item);

        return Default(item);
    }

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
