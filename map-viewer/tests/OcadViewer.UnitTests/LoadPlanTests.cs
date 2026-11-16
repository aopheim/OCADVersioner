using NUnit.Framework;

using OcadViewer.Loading;

namespace OcadViewer.UnitTests;

/// <summary>
/// What the loading bar means.
/// </summary>
/// <remarks>
/// The bar used to be a stripe that said only "something is happening". Saying
/// how much is left means weighing pieces of work against each other, and that
/// is reasoning rather than plumbing - so it was put where a test can reach it
/// rather than in the module that happens to have the numbers.
///
/// These are written in terms of the two constants rather than the figures they
/// hold today, because tuning them against a newer renderer must not turn this
/// suite red.
/// </remarks>
[TestFixture]
public sealed class LoadPlanTests
{
    private const long Megabyte = 1024 * 1024;

    private static double ParseCost(double megabytes)
        => megabytes * LoadPlan.ParseMillisecondsPerMegabyte;

    private static double RenderCost(int tiles)
        => tiles * LoadPlan.RenderMillisecondsPerTile;

    [Test]
    public void ALoadWithNothingToDoShowsNothing()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(0, 0), new LoadStep(0, 0)]);

        Assert.Multiple(() => {
            Assert.That(plan.HasWork, Is.False,
                "Both maps in memory and no tile to draw is not a wait. An empty bar that "
                + "appears and vanishes is worse than no bar.");
            Assert.That(plan.Fraction, Is.Zero);
        });
    }

    [Test]
    public void AnEmptyPlanIsHarmless()
    {
        LoadPlan plan = LoadPlan.For([]);

        Assert.Multiple(() => {
            Assert.That(plan.Steps, Is.Zero);
            Assert.That(plan.HasWork, Is.False);
            Assert.That(plan.Step, Is.Zero, "Nothing to do means nothing in progress.");
        });
    }

    [Test]
    public void TheBarIsHalfwayWhenTheFirstOfTwoEqualMapsIsDone()
    {
        LoadPlan plan = LoadPlan.For([
            new LoadStep(Megabyte, 10),
            new LoadStep(Megabyte, 10)
        ]);

        plan.Finished(0);

        Assert.That(plan.Fraction, Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void ABiggerMapIsWorthMoreOfTheBar()
    {
        LoadPlan plan = LoadPlan.For([
            new LoadStep(2 * Megabyte, 0),
            new LoadStep(Megabyte, 0)
        ]);

        plan.Finished(0);

        Assert.That(plan.Fraction, Is.EqualTo(2.0 / 3).Within(1e-9),
            "Steps of equal count but unequal size are what made the old bar meaningless. "
            + "Two thirds of the bytes is two thirds of the wait.");
    }

    [Test]
    public void ParsingAMapFillsItsShareBeforeADropIsDrawn()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 10)]);

        plan.Parsed(0);

        double expected = ParseCost(1) / (ParseCost(1) + RenderCost(10));
        Assert.That(plan.Fraction, Is.EqualTo(expected).Within(1e-9));
    }

    [Test]
    public void TilesMoveTheBarAsTheyLand()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 10)]);

        plan.Parsed(0);
        plan.Drew(0, tilesDrawn: 5, tilesExpected: 10);

        double expected = (ParseCost(1) + RenderCost(10) / 2) / (ParseCost(1) + RenderCost(10));
        Assert.That(plan.Fraction, Is.EqualTo(expected).Within(1e-9),
            "Drawing is the half of the wait where the map appears, and the only half that "
            + "can move the bar smoothly, because the browser paints between tiles.");
    }

    [Test]
    public void APaneThatAsksForMoreTilesThanWeGuessedDoesNotOverfillTheBar()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 4)]);

        plan.Parsed(0);
        plan.Drew(0, tilesDrawn: 30, tilesExpected: 30);

        Assert.That(plan.Fraction, Is.EqualTo(1).Within(1e-9),
            "The tile count is an estimate from the size of the pane; the real one arrives "
            + "only once Leaflet has the layer. Being wrong must cost accuracy, not sense.");
    }

    [Test]
    public void APaneThatDrawsNothingStillFinishes()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 6)]);

        plan.Finished(0);

        Assert.That(plan.Fraction, Is.EqualTo(1).Within(1e-9),
            "A pane laid out at zero height asks for no tiles at all, so there is no count "
            + "for reaching the end to be read from. Finishing has to be said outright.");
    }

    [Test]
    public void ALateTileReportDoesNotWalkTheBarBackwards()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 10), new LoadStep(Megabyte, 10)]);

        plan.Finished(0);
        plan.Drew(0, tilesDrawn: 3, tilesExpected: 10);

        Assert.That(plan.Fraction, Is.EqualTo(0.5).Within(1e-9),
            "Tile reports are sent without waiting for an answer, so one can arrive after "
            + "the map it belongs to has finished.");
    }

    [Test]
    public void AMapAlreadyInMemoryCostsOnlyItsDrawing()
    {
        LoadPlan plan = LoadPlan.For([
            new LoadStep(0, 10),            // a release still in memory from last time
            new LoadStep(Megabyte, 10)
        ]);

        plan.Finished(0);

        double expected = RenderCost(10) / (RenderCost(10) + ParseCost(1) + RenderCost(10));
        Assert.That(plan.Fraction, Is.EqualTo(expected).Within(1e-9),
            "Releases stay parsed once read, so showing one again is drawing and nothing "
            + "else - but it is still drawing, and both panes get a fresh layer every time.");
    }

    [Test]
    public void TheStepBeingWorkedOnIsTheFirstUnfinishedOne()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 10), new LoadStep(Megabyte, 10)]);

        Assert.Multiple(() => {
            Assert.That(plan.Step, Is.Zero);
            Assert.That(plan.IsDrawing, Is.False, "Nothing has been read yet.");
        });

        plan.Parsed(0);
        plan.Drew(0, tilesDrawn: 1, tilesExpected: 10);

        Assert.Multiple(() => {
            Assert.That(plan.Step, Is.Zero);
            Assert.That(plan.IsDrawing, Is.True, "Past the parse and on screen.");
        });

        plan.Finished(0);
        Assert.That(plan.Step, Is.EqualTo(1));

        plan.Finished(1);
        Assert.Multiple(() => {
            Assert.That(plan.Step, Is.EqualTo(plan.Steps), "Everything is done.");
            Assert.That(plan.IsDrawing, Is.False);
            Assert.That(plan.Fraction, Is.EqualTo(1).Within(1e-9));
        });
    }

    [Test]
    public void AReportAboutAStepThatIsNotInThisPlanIsIgnored()
    {
        LoadPlan plan = LoadPlan.For([new LoadStep(Megabyte, 10)]);

        Assert.Multiple(() => {
            Assert.DoesNotThrow(() => plan.Finished(7));
            Assert.DoesNotThrow(() => plan.Finished(-1));
            Assert.DoesNotThrow(() => plan.Parsed(7));
            Assert.DoesNotThrow(() => plan.Drew(-1, 1, 1));
            Assert.That(plan.Fraction, Is.Zero,
                "A report can outlive the load it belongs to, because a parse already "
                + "running cannot be interrupted. A late report is not a programming error.");
        });
    }
}
