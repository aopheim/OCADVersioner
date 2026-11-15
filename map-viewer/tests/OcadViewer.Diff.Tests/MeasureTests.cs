namespace OcadViewer.Diff.Tests;

/// <summary>Lengths and areas on shapes whose answers are known.</summary>
[TestFixture]
public sealed class MeasureTests
{
    private static MapPoint P(int x, int y, bool control = false, bool hole = false) => new(x, y, control, hole);

    [Test]
    public void AStraightLineIsAsLongAsTheDistanceBetweenItsEnds()
        => Assert.That(Measure.Length([P(0, 0), P(300, 400)]), Is.EqualTo(500));

    [Test]
    public void ALineAddsUpItsSegments()
        => Assert.That(Measure.Length([P(0, 0), P(100, 0), P(100, 100)]), Is.EqualTo(200));

    [Test]
    public void CurveControlPointsAreSkipped()
    {
        // A Bezier from (0,0) to (100,0) with its handles well off the line: the
        // chord is measured, not the path through the handles.
        double length = Measure.Length([P(0, 0), P(0, 500, control: true), P(100, 500, control: true), P(100, 0)]);

        Assert.That(length, Is.EqualTo(100));
    }

    [Test]
    public void ASquareHasItsSideSquaredAsArea()
        => Assert.That(Measure.Area([P(0, 0), P(100, 0), P(100, 100), P(0, 100), P(0, 0)]), Is.EqualTo(10_000));

    [Test]
    public void TheDirectionAnOutlineIsDrawnInDoesNotMatter()
        => Assert.That(Measure.Area([P(0, 0), P(0, 100), P(100, 100), P(100, 0), P(0, 0)]), Is.EqualTo(10_000));

    [Test]
    public void AHoleIsSubtracted()
    {
        double area = Measure.Area([
            P(0, 0), P(100, 0), P(100, 100), P(0, 100), P(0, 0),
            P(25, 25, hole: true), P(75, 25), P(75, 75), P(25, 75), P(25, 25),
        ]);

        Assert.That(area, Is.EqualTo(10_000 - 2_500));
    }

    [Test]
    public void AtOneToFifteenThousandAKilometreOnTheGroundIsSixAndTwoThirdsCentimetresOnPaper()
    {
        // 1:15 000 -> 0.15 m per OCAD unit (0.01 mm of paper is 15 cm of ground).
        const double metresPerUnit = 15_000 / 100_000.0;

        Assert.That(Measure.Length([P(0, 0), P(6667, 0)]) * metresPerUnit, Is.EqualTo(1000).Within(0.1));
    }
}
