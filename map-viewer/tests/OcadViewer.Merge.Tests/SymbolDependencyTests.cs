using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Writing;
using OcadViewer.Ocd;
using static OcadViewer.Merge.Tests.Fixtures.Symbols;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// A merged map never has objects whose symbol is missing.
/// </summary>
/// <remarks>
/// <para>kari_symbols.ocd duplicates the boulder symbol into 204.002 and draws two
/// objects with it, and draws a new boulder (204.000). ola_symbols.ocd deletes
/// the boulder symbol and, answering Yes in OCAD, the boulders with it.
/// ola_symbols_kept.ocd deletes it answering No, so its boulders stay without a
/// symbol (OCAD draws them as red crosses).</para>
/// <para>The rules, as decided with the product owner: a symbol added by a branch
/// is kept while kept objects use it; a deleted symbol still used in the merge is
/// a conflict, and taking the deletion removes every object that uses it.</para>
/// </remarks>
[TestFixture]
public sealed class SymbolDependencyTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0);

    private static MergeAnalysis Analyse(string a, string b)
        => MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Read(a), Fixtures.Read(b));

    private static OcadFile Write(MergeAnalysis analysis, MergeDecisions decisions)
        => OcadFile.Read(MergeWriter.Write(new MergeResolution(analysis, decisions), Now));

    private static SymbolItem Symbol(MergeAnalysis analysis, int number) => analysis.Symbols.Single(s => s.Number == number);

    /// <summary>Decides every open conflict and overlap other than the ones a test is about.</summary>
    private static void DecideTheRest(MergeAnalysis analysis, MergeDecisions decisions)
    {
        foreach (MergeItem item in analysis.AllItems.Where(i => i.Outcome == Outcome.Conflict && !decisions.Items.ContainsKey(i.Key)))
            decisions.Decide(item, Choice.A);
        foreach (OverlapGroup group in analysis.Overlaps.Where(g => !decisions.Overlaps.ContainsKey(g.Key)))
            decisions.DecideOverlap(group.Key, Choice.Both);
    }

    private static void AssertEveryObjectHasItsSymbol(OcadFile merged)
    {
        HashSet<int> symbols = [.. merged.Symbols.Select(s => s.Number)];
        Assert.That(merged.Objects.Where(o => !symbols.Contains(o.Symbol)).Select(o => o.Symbol), Is.Empty,
                    "objects without a symbol");
    }

    [Test]
    public void ASymbolABranchAddedIsKeptWhileItsObjectsAreKept()
    {
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "base.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, KarisBoulder), Choice.Base);

        MergeResolution resolution = new(analysis, decisions);
        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(resolution.ChoiceFor(Symbol(analysis, KarisBoulder)), Is.EqualTo(Choice.A));
            Assert.That(resolution.IsKeptForObjects(Symbol(analysis, KarisBoulder)), Is.True);
            Assert.That(resolution.UsersOf(KarisBoulder), Has.Count.EqualTo(2));
            Assert.That(merged.Symbols.Select(s => s.Number), Does.Contain(KarisBoulder));
            Assert.That(merged.Objects.Count(o => o.Symbol == KarisBoulder), Is.EqualTo(2));
        });
    }

    [Test]
    public void RejectingTheObjectsLetsTheSymbolGo()
    {
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "base.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, KarisBoulder), Choice.Base);
        foreach (ObjectItem item in analysis.Objects.Where(i => i.Symbol == KarisBoulder))
            decisions.Decide(item, Choice.Base);

        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(new MergeResolution(analysis, decisions).IsKeptForObjects(Symbol(analysis, KarisBoulder)), Is.False);
            Assert.That(merged.Symbols.Select(s => s.Number), Does.Not.Contain(KarisBoulder));
            Assert.That(merged.Objects.Select(o => o.Symbol), Does.Not.Contain(KarisBoulder));
        });
    }

    [Test]
    public void DeletingASymbolTheOtherBranchStillUsesIsAConflict()
    {
        SymbolItem boulder = Symbol(Analyse("kari_symbols.ocd", "ola_symbols.ocd"), Boulder);

        Assert.Multiple(() => {
            Assert.That(boulder.Outcome, Is.EqualTo(Outcome.Conflict));
            Assert.That(boulder.Conflict, Is.EqualTo(ConflictKind.SymbolInUse));
            Assert.That(boulder.ChangeB, Is.EqualTo(ChangeKind.Deleted));
        });
    }

    [Test]
    public void DeletingASymbolNobodyElseUsesIsClean()
    {
        // Ola deleted the boulders with their symbol; Kari did not touch them.
        MergeAnalysis analysis = Analyse("base.ocd", "ola_symbols.ocd");
        OcadFile merged = Write(analysis, new MergeDecisions());

        Assert.Multiple(() => {
            Assert.That(Symbol(analysis, Boulder).Outcome, Is.EqualTo(Outcome.OnlyB));
            Assert.That(merged.Symbols.Select(s => s.Number), Does.Not.Contain(Boulder));
            Assert.That(merged.Objects.Select(o => o.Symbol), Does.Not.Contain(Boulder));
        });
    }

    [Test]
    public void ThePanelCanSayWhatGoesWithADeletedSymbolBeforeItIsDecided()
    {
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "ola_symbols.ocd");
        MergeResolution resolution = new(analysis, new MergeDecisions());

        // Base's boulders are deleted cleanly by Ola; only Kari's new one is still in the merge.
        Assert.That(resolution.UsersOf(Boulder).Select(o => o.CreatedAt), Is.EqualTo(new DateTime?[] { KarisNewBoulder }));
    }

    [Test]
    public void TakingTheDeletionRemovesEveryObjectThatUsesTheSymbol()
    {
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "ola_symbols.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, Boulder), Choice.B);
        DecideTheRest(analysis, decisions);

        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(merged.Symbols.Select(s => s.Number), Does.Not.Contain(Boulder));
            Assert.That(merged.Objects.Select(o => o.Symbol), Does.Not.Contain(Boulder));
            AssertEveryObjectHasItsSymbol(merged);
        });
    }

    [Test]
    public void KeepingTheSymbolKeepsTheObjectsThatUseIt()
    {
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "ola_symbols.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, Boulder), Choice.A);
        DecideTheRest(analysis, decisions);

        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(merged.Symbols.Select(s => s.Number), Does.Contain(Boulder));
            Assert.That(merged.Objects.Where(o => o.Symbol == Boulder).Select(o => o.CreatedAt),
                        Is.EqualTo(new DateTime?[] { KarisNewBoulder }), "base's boulders were deleted by Ola, cleanly");
            AssertEveryObjectHasItsSymbol(merged);
        });
    }

    [Test]
    public void ASymbolDeletedWhileItsObjectsWereKeptIsAConflictToo()
    {
        // OCAD asked "delete the assigned objects too?" and Ola answered No.
        MergeAnalysis analysis = Analyse("base.ocd", "ola_symbols_kept.ocd");
        SymbolItem boulder = Symbol(analysis, Boulder);

        Assert.Multiple(() => {
            Assert.That(boulder.Conflict, Is.EqualTo(ConflictKind.SymbolInUse));
            Assert.That(new MergeResolution(analysis, new MergeDecisions()).UsersOf(Boulder), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void TakingSuchADeletionRemovesTheObjectsOlaKept()
    {
        MergeAnalysis analysis = Analyse("base.ocd", "ola_symbols_kept.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, Boulder), Choice.B);

        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(merged.Objects.Select(o => o.Symbol), Does.Not.Contain(Boulder));
            AssertEveryObjectHasItsSymbol(merged);
        });
    }

    [TestCase(Choice.A)]
    [TestCase(Choice.Base)]
    public void KeepingSuchASymbolKeepsTheObjects(Choice choice)
    {
        MergeAnalysis analysis = Analyse("base.ocd", "ola_symbols_kept.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, Boulder), choice);

        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(merged.Objects.Count(o => o.Symbol == Boulder), Is.EqualTo(2));
            AssertEveryObjectHasItsSymbol(merged);
        });
    }

    [Test]
    public void ADeletedSymbolDecidesForObjectsOtherwiseKept()
    {
        // Rejecting Ola's deletion of a boulder does not save it once the symbol is gone.
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "ola_symbols.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(Symbol(analysis, Boulder), Choice.B);
        foreach (ObjectItem item in analysis.Objects.Where(i => i.Base?.Symbol == Boulder))
            decisions.Decide(item, Choice.Base);
        DecideTheRest(analysis, decisions);

        Assert.That(Write(analysis, decisions).Objects.Select(o => o.Symbol), Does.Not.Contain(Boulder));
    }
}
