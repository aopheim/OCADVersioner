using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Geometry;
using OcadViewer.Merge.Regions;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class RegionTests
{
    private static MergeAnalysis analysis;

    [OneTimeSetUp]
    public void Analyse() => analysis = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola);

    private static ObjectItem ItemFor(DateTime created) => analysis.Objects.Single(i => i.Base?.CreatedAt == created);

    /// <summary>A rectangle in paper millimetres.</summary>
    private static Region Box(double minX, double minY, double maxX, double maxY)
        => new([(minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)]);

    /// <summary>A rectangle around an item's extent, grown by a millimetre.</summary>
    private static Region Around(ObjectItem item)
    {
        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(item.Bounds);
        return Box(minX - 1, minY - 1, maxX + 1, maxY + 1);
    }

    [Test]
    public void AnItemWhollyInsideIsInside()
        => Assert.That(Around(ItemFor(Fixtures.Created.Lake)).Classify(ItemFor(Fixtures.Created.Lake)), Is.EqualTo(Placement.Inside));

    [Test]
    public void AnItemFarAwayIsOutside()
        => Assert.That(Box(100, 100, 110, 110).Classify(ItemFor(Fixtures.Created.Lake)), Is.EqualTo(Placement.Outside));

    [Test]
    public void AnItemCutByTheBorderIsCrossing()
    {
        ObjectItem lake = ItemFor(Fixtures.Created.Lake);
        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(lake.Bounds);

        Region leftHalf = Box(minX - 1, minY - 1, (minX + maxX) / 2, maxY + 1);

        Assert.That(leftHalf.Classify(lake), Is.EqualTo(Placement.Crossing));
    }

    [Test]
    public void ALineThatPassesThroughWithNoVertexInsideIsCrossing()
    {
        // The footpath runs west to east across the whole map; a thin box across its
        // middle holds none of its vertices.
        OcadViewer.Merge.Format.OcadObject path = Fixtures.Base.Objects.Single(o => o.CreatedAt == Fixtures.Created.Footpath);
        (double x, double y) = ObjectShape.Of(path).Parts[0][0];
        (double nx, double ny) = ObjectShape.Of(path).Parts[0][1];
        double midX = (x + nx) / 2, midY = (y + ny) / 2;

        Assert.That(Box(midX - 0.05, midY - 3, midX + 0.05, midY + 3).Classify(path), Is.EqualTo(Placement.Crossing));
    }

    [Test]
    public void TakingASideForARegionAcceptsThatSidesChangesAndRejectsTheOthers()
    {
        Region everything = Box(-100, -100, 100, 100);

        Dictionary<string, Choice> choices = RegionDecision.For(analysis, everything, Choice.A).Choices
            .ToDictionary(c => c.Item.Key, c => c.Choice);

        Assert.Multiple(() => {
            Assert.That(choices[ItemFor(Fixtures.Created.Lake).Key], Is.EqualTo(Choice.A));
            Assert.That(choices[ItemFor(Fixtures.Created.OpenLand).Key], Is.EqualTo(Choice.Base));
            Assert.That(choices[ItemFor(Fixtures.Created.Forest).Key], Is.EqualTo(Choice.A));
        });
    }

    [Test]
    public void KeepingBothForARegionAcceptsEveryCleanChange()
    {
        RegionDecision decision = RegionDecision.For(analysis, Box(-100, -100, 100, 100), Choice.Both);

        Assert.That(decision.Choices.Where(c => c.Item.Outcome == Outcome.OnlyB).Select(c => c.Choice), Is.All.EqualTo(Choice.B));
    }

    [Test]
    public void ItemsCrossingTheBorderAreLeftForTheUser()
    {
        ObjectItem lake = ItemFor(Fixtures.Created.Lake);
        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(lake.Bounds);

        RegionDecision decision = RegionDecision.For(analysis, Box(minX - 1, minY - 1, (minX + maxX) / 2, maxY + 1), Choice.A);

        Assert.Multiple(() => {
            Assert.That(decision.Crossing, Does.Contain(lake));
            Assert.That(decision.Choices.Select(c => c.Item), Does.Not.Contain(lake));
        });
    }

    [Test]
    public void ARegionDecisionIsOneUndoStep()
    {
        MergeSession session = new(analysis);

        session.DecideAll(RegionDecision.For(analysis, Box(-100, -100, 100, 100), Choice.B).Choices);
        session.Undo();

        Assert.That(session.Decisions.Items, Is.Empty);
    }

    [Test]
    public void OverlapGroupsAndRegionsShareOneRule()
    {
        // Choosing A for a region and for an overlap group must give each clean change
        // in it the same fate.
        OverlapGroup hill = analysis.Overlaps.Single(g => g.Items.Any(i => i.A?.Symbol == 101000));
        MergeDecisions decisions = new();
        decisions.DecideOverlap(hill.Key, Choice.A);
        MergeResolution resolution = new(analysis, decisions);

        Dictionary<string, Choice> region = RegionDecision.For(analysis, Box(-100, -100, 100, 100), Choice.A).Choices
            .ToDictionary(c => c.Item.Key, c => c.Choice);

        Assert.That(hill.Items.Select(i => region[i.Key]), Is.EqualTo(hill.Items.Select(i => resolution.ChoiceFor(i).Value)));
    }

    [Test]
    public void SuggestionsCoverEveryCleanObjectChange()
    {
        IReadOnlyList<SuggestedRegion> suggestions = RegionSuggester.Suggest(analysis);

        int[] covered = [.. analysis.Objects.Where(i => i.Outcome is Outcome.OnlyA or Outcome.OnlyB)
                                            .Select(i => suggestions.Count(s => s.Region.Classify(i) == Placement.Inside))];
        Assert.That(covered, Is.All.GreaterThan(0));
    }

    [Test]
    public void ASuggestionHoldingBothSidesSuggestsDecidingPerObject()
    {
        foreach (SuggestedRegion s in RegionSuggester.Suggest(analysis)) {
            bool a = s.Items.Any(i => i.Outcome == Outcome.OnlyA);
            bool b = s.Items.Any(i => i.Outcome == Outcome.OnlyB);
            Assert.That(s.Suggested, Is.EqualTo(a && b ? null : a ? Side.A : Side.B));
        }
    }

    [Test]
    public void ChangesFurtherApartThanTheJoinDistanceAreSeparateSuggestions()
    {
        IReadOnlyList<SuggestedRegion> coarse = RegionSuggester.Suggest(analysis);
        IReadOnlyList<SuggestedRegion> fine = RegionSuggester.Suggest(analysis, joinMeters: 1, marginMeters: 0.5);

        Assert.That(fine, Has.Count.GreaterThan(coarse.Count));
    }
}
