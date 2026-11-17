using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class MergeResolutionTests
{
    private static MergeAnalysis analysis;

    [OneTimeSetUp]
    public void Analyse() => analysis = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola);

    private static ObjectItem ItemFor(DateTime created) => analysis.Objects.Single(i => i.Base?.CreatedAt == created);

    [Test]
    public void CleanChangesAreAcceptedByDefault()
    {
        MergeResolution resolution = new(analysis, new MergeDecisions());

        Assert.Multiple(() => {
            Assert.That(resolution.ChoiceFor(ItemFor(Fixtures.Created.Lake)), Is.EqualTo(Choice.A));
            Assert.That(resolution.ChoiceFor(ItemFor(Fixtures.Created.OpenLand)), Is.EqualTo(Choice.B));
        });
    }

    [Test]
    public void ConflictsHaveNoChoiceUntilTheUserMakesOne()
    {
        MergeResolution resolution = new(analysis, new MergeDecisions());

        Assert.That(resolution.ChoiceFor(ItemFor(Fixtures.Created.Forest)), Is.Null);
    }

    [Test]
    public void ACleanChangeCanBeRejected()
    {
        MergeDecisions decisions = new();
        decisions.Decide(ItemFor(Fixtures.Created.Lake).Key, Choice.Base);

        Assert.That(new MergeResolution(analysis, decisions).ChoiceFor(ItemFor(Fixtures.Created.Lake)), Is.EqualTo(Choice.Base));
    }

    [Test]
    public void TheMergeIsCompleteOnlyWhenEveryConflictAndOverlapIsDecided()
    {
        MergeDecisions decisions = new();
        Assert.That(new MergeResolution(analysis, decisions).IsComplete, Is.False);

        Decide(decisions);

        MergeResolution resolution = new(analysis, decisions);
        Assert.Multiple(() => {
            Assert.That(resolution.Unresolved, Is.Empty);
            Assert.That(resolution.IsComplete, Is.True);
        });
    }

    [Test]
    public void CountsWhatIsLeft()
    {
        // Forest, east boulder, symbol 109, and the two overlap groups.
        Assert.That(new MergeResolution(analysis, new MergeDecisions()).Unresolved, Has.Count.EqualTo(5));
    }

    [Test]
    public void TheDecisionsNeededStayTheSameAsTheyAreMade()
    {
        MergeDecisions decisions = new();
        Decide(decisions);
        MergeResolution decided = new(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(new MergeResolution(analysis, new MergeDecisions()).DecisionsNeeded, Is.EqualTo(5));
            Assert.That(decided.DecisionsNeeded, Is.EqualTo(5));
            Assert.That(decided.Unresolved, Is.Empty);
        });
    }

    [Test]
    public void TakingASideInAnOverlapRejectsTheOtherSidesChangesThere()
    {
        OverlapGroup hill = analysis.Overlaps.Single(g => g.Items.Any(i => i.A?.Symbol == 101000));
        MergeDecisions decisions = new();
        decisions.DecideOverlap(hill.Key, Choice.A);

        MergeResolution resolution = new(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(hill.ItemsFrom(Side.A).Select(resolution.ChoiceFor), Is.All.EqualTo(Choice.A));
            Assert.That(hill.ItemsFrom(Side.B).Select(resolution.ChoiceFor), Is.All.EqualTo(Choice.Base));
        });
    }

    [Test]
    public void KeepingBothInAnOverlapAcceptsEverything()
    {
        OverlapGroup hill = analysis.Overlaps.Single(g => g.Items.Any(i => i.A?.Symbol == 101000));
        MergeDecisions decisions = new();
        decisions.DecideOverlap(hill.Key, Choice.Both);

        MergeResolution resolution = new(analysis, decisions);

        Assert.That(hill.Items.Select(resolution.ChoiceFor), Is.EqualTo(hill.Items.Select(i => i.Outcome == Outcome.OnlyA ? Choice.A : Choice.B)));
    }

    [Test]
    public void AnItemDecisionBeatsItsOverlapDecision()
    {
        OverlapGroup hill = analysis.Overlaps.Single(g => g.Items.Any(i => i.A?.Symbol == 101000));
        ObjectItem knoll = hill.ItemsFrom(Side.B).First();
        MergeDecisions decisions = new();
        decisions.DecideOverlap(hill.Key, Choice.A);
        decisions.Decide(knoll.Key, Choice.B);

        Assert.That(new MergeResolution(analysis, decisions).ChoiceFor(knoll), Is.EqualTo(Choice.B));
    }

    [Test]
    public void AnOverlapIsResolvedWhenEveryMemberIsDecidedOneByOne()
    {
        OverlapGroup hill = analysis.Overlaps.Single(g => g.Items.Any(i => i.A?.Symbol == 101000));
        MergeDecisions decisions = new();
        foreach (ObjectItem item in hill.Items)
            decisions.Decide(item.Key, Choice.Base);

        Assert.That(new MergeResolution(analysis, decisions).IsResolved(hill), Is.True);
    }

    [Test]
    public void SymbolsCannotBeKeptTwice()
    {
        MergeDecisions decisions = new();

        Assert.Throws<ArgumentException>(() => decisions.Decide(analysis.Symbols.Single(), Choice.Both));
    }

    [Test]
    public void DecisionsRoundTripThroughTheirSnapshot()
    {
        MergeDecisions decisions = new();
        Decide(decisions);

        MergeDecisions restored = MergeDecisions.FromSnapshot(decisions.ToSnapshot());

        Assert.That(new MergeResolution(analysis, restored).IsComplete, Is.True);
    }

    [Test]
    public void AResolutionIsASnapshotOfItsDecisions()
    {
        MergeDecisions decisions = new();
        Decide(decisions);
        MergeResolution resolution = new(analysis, decisions);
        ObjectItem forest = ItemFor(Fixtures.Created.Forest);

        decisions.Forget(forest.Key);

        Assert.Multiple(() => {
            Assert.That(resolution.ChoiceFor(forest), Is.EqualTo(Choice.A));
            Assert.That(resolution.IsComplete, Is.True);
            Assert.That(new MergeResolution(analysis, decisions).IsComplete, Is.False);
        });
    }

    /// <summary>A full set of decisions for the fixtures.</summary>
    public static void Decide(MergeDecisions decisions)
    {
        decisions.Decide(ItemFor(Fixtures.Created.Forest).Key, Choice.A);
        decisions.Decide(ItemFor(Fixtures.Created.EastBoulder).Key, Choice.B);
        decisions.Decide(analysis.Symbols.Single(), Choice.A);
        foreach (OverlapGroup group in analysis.Overlaps)
            decisions.DecideOverlap(group.Key, Choice.Both);
    }
}
