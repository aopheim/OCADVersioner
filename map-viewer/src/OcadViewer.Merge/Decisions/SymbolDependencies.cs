using OcadViewer.Merge.Analysis;
using OcadViewer.Ocd;
using static OcadViewer.Merge.Decisions.ItemVersions;

namespace OcadViewer.Merge.Decisions;

/// <summary>
/// How symbols and the objects that use them decide for each other, so the merged
/// map never has an object without its symbol. The one place these rules live.
/// </summary>
/// <remarks>
/// <para>A symbol a branch added is kept while objects in the merge use it; when both
/// added it differently, the user has to take one. A base symbol deleted cleanly -
/// by one branch or both - that objects in the merge still use waits for the user.
/// A symbol the merge deletes takes every object that uses it along.</para>
/// <para>Evaluation runs one way only: objects as chosen (<c>chosen</c>, before any
/// symbol rule) decide which objects use a symbol; that decides what happens to the
/// symbol; that decides which objects are removed. Nothing here asks for a symbol's
/// final choice while working out the objects, so there is no cycle.</para>
/// </remarks>
internal sealed class SymbolDependencies
{
    private readonly MergeAnalysis analysis;
    private readonly Func<MergeItem, Choice?> chosen;
    private readonly Func<MergeItem, bool> decided;
    private readonly Lazy<IReadOnlyList<OcadObject>> untouched;
    private readonly Lazy<ILookup<int, OcadObject>> users;
    private readonly Lazy<IReadOnlySet<int>> removed;

    /// <param name="chosen">Each item's choice before symbols have their say.</param>
    /// <param name="decided">Whether the user decided an item themselves.</param>
    public SymbolDependencies(MergeAnalysis analysis, Func<MergeItem, Choice?> chosen, Func<MergeItem, bool> decided)
    {
        this.analysis = analysis;
        this.chosen = chosen;
        this.decided = decided;

        untouched = new(() => {
            HashSet<OcadObject> changed = new(analysis.Objects.Select(i => i.Base).Where(o => o is not null), ReferenceEqualityComparer.Instance);
            return [.. analysis.Base.Objects.Where(o => !changed.Contains(o))];
        });
        users = new(() => analysis.Objects.SelectMany(i => Versions(i, chosen(i) ?? Choice.Base))
                                          .Concat(untouched.Value).ToLookup(o => o.Symbol));
        removed = new(() => analysis.Symbols
            .Where(s => s.Base is not null && Version(s, ChoiceFor(s) ?? Choice.Base) is null)
            .Select(s => s.Number).ToHashSet());
    }

    /// <summary>What a symbol becomes once objects have had their say, or null while it awaits the user.</summary>
    public Choice? ChoiceFor(SymbolItem symbol)
        => IsDeletionInUse(symbol) && !decided(symbol) ? null : KeptWhileUsed(symbol, chosen(symbol));

    /// <summary>A clean deletion of a base symbol - by one branch or both - that objects in the merge still use.</summary>
    public bool IsDeletionInUse(SymbolItem symbol)
        => symbol.Base is not null && Own(symbol) is Choice taken && Version(symbol, taken) is null && IsUsed(symbol.Number);

    /// <summary>Whether a symbol is in the merge only because objects in it use it.</summary>
    public bool IsKeptForObjects(SymbolItem symbol)
        => chosen(symbol) is Choice c && symbol.Base is null && Version(symbol, c) is null
        && ChoiceFor(symbol) is not null && IsUsed(symbol.Number);

    /// <summary>The objects in the merge that use a symbol, before the symbol's own fate is applied.</summary>
    public IReadOnlyList<OcadObject> UsersOf(int number) => [.. users.Value[number]];

    /// <summary>Whether an object goes because the merge deletes its symbol.</summary>
    public bool IsRemoved(OcadObject obj) => removed.Value.Contains(obj.Symbol);

    /// <summary>Base's objects no branch changed that go because the merge deletes their symbol.</summary>
    public IEnumerable<OcadObject> RemovedWithSymbols => untouched.Value.Where(IsRemoved);

    private bool IsUsed(int number) => users.Value[number].Any();

    private Choice? KeptWhileUsed(SymbolItem symbol, Choice? choice)
    {
        if (choice is not Choice c || symbol.Base is not null || Version(symbol, c) is not null || !IsUsed(symbol.Number))
            return choice;
        if (symbol.A is null || symbol.B is null)
            return symbol.A is null ? Choice.B : Choice.A;
        return symbol.Outcome == Outcome.BothSame ? Choice.A : null;
    }
}
