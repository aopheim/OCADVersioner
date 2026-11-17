using OcadViewer.Merge.Analysis;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

/// <summary>The fixtures' README table, row by row.</summary>
[TestFixture]
public sealed class MergeAnalyzerTests
{
    private static MergeAnalysis analysis;

    [OneTimeSetUp]
    public void Analyse() => analysis = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola);

    private static ObjectItem ItemFor(DateTime created)
        => analysis.Objects.Single(i => i.Base?.CreatedAt == created);

    [Test]
    public void OnlyOlaChangedTheOpenLand()
    {
        ObjectItem item = ItemFor(Fixtures.Created.OpenLand);

        Assert.Multiple(() => {
            Assert.That(item.Outcome, Is.EqualTo(Outcome.OnlyB));
            Assert.That(item.ChangeB, Is.EqualTo(ChangeKind.Changed));
            Assert.That(item.ChangeA, Is.EqualTo(ChangeKind.None));
        });
    }

    [Test]
    public void OnlyKariChangedTheLake()
        => Assert.That(ItemFor(Fixtures.Created.Lake).Outcome, Is.EqualTo(Outcome.OnlyA));

    [Test]
    public void BothEditedTheForest()
    {
        ObjectItem item = ItemFor(Fixtures.Created.Forest);

        Assert.Multiple(() => {
            Assert.That(item.Outcome, Is.EqualTo(Outcome.Conflict));
            Assert.That(item.Conflict, Is.EqualTo(ConflictKind.BothEdited));
        });
    }

    [Test]
    public void TheFootpathIsUnchangedDespiteItsNewModificationDate()
        => Assert.That(analysis.Objects.Where(i => i.Base?.CreatedAt == Fixtures.Created.Footpath), Is.Empty);

    [Test]
    public void OlaDeletedTheMiddleBoulder()
    {
        ObjectItem item = ItemFor(Fixtures.Created.MiddleBoulder);

        Assert.Multiple(() => {
            Assert.That(item.Outcome, Is.EqualTo(Outcome.OnlyB));
            Assert.That(item.ChangeB, Is.EqualTo(ChangeKind.Deleted));
        });
    }

    [Test]
    public void KariDeletedTheEastBoulderThatOlaMoved()
    {
        ObjectItem item = ItemFor(Fixtures.Created.EastBoulder);

        Assert.Multiple(() => {
            Assert.That(item.Conflict, Is.EqualTo(ConflictKind.EditDelete));
            Assert.That(item.ChangeA, Is.EqualTo(ChangeKind.Deleted));
            Assert.That(item.ChangeB, Is.EqualTo(ChangeKind.Changed));
        });
    }

    [Test]
    public void KariAddedARideAndAContour()
        => Assert.That(analysis.Objects.Where(i => i.ChangeA == ChangeKind.Added).Select(i => i.A.Symbol),
                       Is.EquivalentTo(new[] { 508000, 101000 }));

    [Test]
    public void OlaAddedTwoKnolls()
        => Assert.That(analysis.Objects.Where(i => i.ChangeB == ChangeKind.Added).Select(i => i.B.Symbol),
                       Is.EqualTo(new[] { 109000, 109000 }));

    [Test]
    public void AddedObjectsAreCleanChanges()
        => Assert.That(analysis.Objects.Where(i => i.Base is null).Select(i => i.Outcome),
                       Is.All.AnyOf(Outcome.OnlyA, Outcome.OnlyB));

    [Test]
    public void BothChangedTheKnollSymbol()
    {
        SymbolItem item = analysis.Symbols.Single();

        Assert.Multiple(() => {
            Assert.That(item.Base.Number, Is.EqualTo(109000));
            Assert.That(item.Conflict, Is.EqualTo(ConflictKind.BothEdited));
        });
    }

    [Test]
    public void NoSettingChanged() => Assert.That(analysis.Settings, Is.Empty);

    [Test]
    public void KarisContourAndOlasKnollsOverlapOnTheHill()
    {
        OverlapGroup hill = analysis.Overlaps.Single(g => g.Items.Any(i => i.A?.Symbol == 101000));

        Assert.That(hill.Items.Where(i => i.B is not null).Select(i => i.B.Symbol), Is.EqualTo(new[] { 109000, 109000 }));
    }

    [Test]
    public void InTheWestOnlyOlasOpenLandAndKarisRideAreClose()
    {
        // Their boxes all touch, but what changed does not: Kari moved the lake's NE
        // corner and Ola the open land's, more than 10 m apart, and the middle boulder
        // Ola deleted is more than 10 m from Kari's new ride. The ride does pass within
        // 10 m of the open land's moved corner.
        OverlapGroup west = analysis.Overlaps.Single(g => g.Items.Any(i => i.Symbol == 508000));

        Assert.Multiple(() => {
            Assert.That(analysis.Overlaps, Has.Count.EqualTo(2));
            Assert.That(west.Items.Select(i => i.Symbol), Is.EquivalentTo(new[] { 401000, 508000 }));
        });
    }

    [Test]
    public void ItemsInAConflictAreNotAlsoInAnOverlap()
        => Assert.That(analysis.Overlaps.SelectMany(g => g.Items).Select(i => i.Outcome), Is.All.Not.EqualTo(Outcome.Conflict));

    [Test]
    public void OverlapsOnlyJoinKariChangesWithOlaChanges()
        => Assert.That(analysis.Overlaps, Has.All.Matches<OverlapGroup>(
            g => g.Items.Any(i => i.Outcome == Outcome.OnlyA) && g.Items.Any(i => i.Outcome == Outcome.OnlyB)));

    [Test]
    public void ItemKeysAreUniqueAndStable()
    {
        MergeAnalysis again = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola);

        Assert.Multiple(() => {
            Assert.That(analysis.AllItems.Select(i => i.Key), Is.Unique);
            Assert.That(again.AllItems.Select(i => i.Key), Is.EqualTo(analysis.AllItems.Select(i => i.Key)));
            Assert.That(again.Overlaps.Select(g => g.Key), Is.EqualTo(analysis.Overlaps.Select(g => g.Key)));
        });
    }

    [Test]
    public void MergingAFileWithItselfFindsNothing()
    {
        MergeAnalysis nothing = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Base, Fixtures.Base);

        Assert.That(nothing.AllItems, Is.Empty);
    }

    [Test]
    public void IdenticalChangesOnBothSidesAreNotConflicts()
    {
        MergeAnalysis same = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Kari);

        Assert.Multiple(() => {
            Assert.That(same.AllItems.Select(i => i.Outcome), Is.All.EqualTo(Outcome.BothSame));
            Assert.That(same.Overlaps, Is.Empty);
        });
    }

    [Test]
    public void FilesMustBeTheSameOcadVersion()
        => Assert.Throws<MergeException>(
            () => MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Read("base_v12.ocd"), Fixtures.Ola));
}
