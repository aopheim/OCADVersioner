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
        MergeResolution open = new(analysis, decisions);
        foreach (MergeItem item in analysis.AllItems.Where(i => open.NeedsDecision(i) && !decisions.Items.ContainsKey(i.Key)))
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
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "ola_symbols.ocd");
        SymbolItem boulder = Symbol(analysis, Boulder);
        MergeResolution resolution = new(analysis, new MergeDecisions());

        Assert.Multiple(() => {
            Assert.That(boulder.ChangeB, Is.EqualTo(ChangeKind.Deleted));
            Assert.That(resolution.ConflictOf(boulder), Is.EqualTo(ConflictKind.SymbolInUse));
            Assert.That(resolution.IsResolved(boulder), Is.False);
            Assert.That(resolution.Unresolved, Does.Contain(boulder));
        });
    }

    [Test]
    public void RejectingTheObjectsThatStillUseADeletedSymbolLetsTheDeletionThrough()
    {
        // The conflict follows the decisions: once Kari's new boulder is rejected,
        // nothing in the merge uses the symbol any more.
        MergeAnalysis analysis = Analyse("kari_symbols.ocd", "ola_symbols.ocd");
        MergeDecisions decisions = new();
        decisions.Decide(analysis.Objects.Single(i => i.A?.CreatedAt == KarisNewBoulder), Choice.Base);
        MergeResolution resolution = new(analysis, decisions);
        SymbolItem boulder = Symbol(analysis, Boulder);

        Assert.Multiple(() => {
            Assert.That(resolution.ConflictOf(boulder), Is.Null);
            Assert.That(resolution.ChoiceFor(boulder), Is.EqualTo(Choice.B), "Ola's deletion, accepted by default");
        });
    }

    [Test]
    public void ASymbolBothDeletedWhileObjectsStillUseItIsAConflict()
    {
        // Both deleted symbol 204 answering No: their boulders stay in both files.
        MergeAnalysis analysis = Analyse("ola_symbols_kept.ocd", "ola_symbols_kept.ocd");
        SymbolItem boulder = Symbol(analysis, Boulder);
        MergeResolution resolution = new(analysis, new MergeDecisions());

        Assert.Multiple(() => {
            Assert.That(boulder.Outcome, Is.EqualTo(Outcome.BothSame));
            Assert.That(resolution.ConflictOf(boulder), Is.EqualTo(ConflictKind.SymbolInUse));
            Assert.That(resolution.UsersOf(Boulder), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void KeepingObjectsAMapperDeletedWithTheSymbolMakesItAConflict()
    {
        // Kari deleted the boulders with their symbol (Yes), Ola kept them (No). By
        // default Kari's deletions go through and nothing uses the symbol; keeping the
        // boulders brings the question back.
        MergeAnalysis analysis = Analyse("ola_symbols.ocd", "ola_symbols_kept.ocd");
        SymbolItem boulder = Symbol(analysis, Boulder);
        MergeDecisions decisions = new();

        Assert.That(new MergeResolution(analysis, decisions).ConflictOf(boulder), Is.Null, "by default");

        foreach (ObjectItem item in analysis.Objects.Where(i => i.Base?.Symbol == Boulder))
            decisions.Decide(item, Choice.Base);

        Assert.That(new MergeResolution(analysis, decisions).ConflictOf(boulder), Is.EqualTo(ConflictKind.SymbolInUse));
    }

    [Test]
    public void LeavingOutASymbolBothAddedWhileObjectsUseItIsOverruled()
    {
        // Both duplicated symbol 101 into 101.004 differently; Kari also drew with hers.
        MergeAnalysis analysis = MergeAnalyzer.Analyze(Fixtures.Base, KariSettingsWithAnObjectUsing(101004), Fixtures.Read("ola_settings.ocd"));
        SymbolItem added = Symbol(analysis, 101004);
        MergeDecisions decisions = new();
        decisions.Decide(added, Choice.Base);
        MergeResolution overruled = new(analysis, decisions);

        decisions.Decide(added, Choice.B);
        MergeResolution decided = new(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(added.Conflict, Is.EqualTo(ConflictKind.BothAdded));
            Assert.That(overruled.IsOverruled(added), Is.True);
            Assert.That(overruled.IsResolved(added), Is.False);
            Assert.That(decided.IsOverruled(added), Is.False);
            Assert.That(decided.ChoiceFor(added), Is.EqualTo(Choice.B));
        });
    }

    /// <summary>
    /// kari_settings.ocd with base's knoll added again as a new object using symbol
    /// <paramref name="symbol"/>: no fixture draws with the duplicated symbol.
    /// </summary>
    private static OcadFile KariSettingsWithAnObjectUsing(int symbol)
    {
        OcadFile kari = Fixtures.Read("kari_settings.ocd");
        OcadEditor editor = new(kari);
        DateTime drawn = new(2026, 11, 17, 13, 0, 0);
        editor.Add(kari.Objects.Single(o => o.CreatedAt == Fixtures.Created.Knoll).WithCreatedAt(drawn));
        byte[] bytes = editor.ToArray();

        OcadFile written = OcadFile.Read(bytes);
        OcadObject copy = written.Objects.Single(o => o.CreatedAt == drawn);
        int entry = written.ObjectIndex.EntryOffset(copy.Entry - 1);
        int record = BitConverter.ToInt32(bytes, entry + FileLayout.IndexPosOffset);
        BitConverter.TryWriteBytes(bytes.AsSpan(entry + FileLayout.IndexSymbolOffset), symbol);
        BitConverter.TryWriteBytes(bytes.AsSpan(record), symbol);
        return OcadFile.Read(bytes);
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

        MergeResolution resolution = new(analysis, new MergeDecisions());

        Assert.Multiple(() => {
            Assert.That(resolution.ConflictOf(boulder), Is.EqualTo(ConflictKind.SymbolInUse));
            Assert.That(resolution.UsersOf(Boulder), Has.Count.EqualTo(2));
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
