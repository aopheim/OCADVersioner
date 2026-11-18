using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Geometry;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// What counts as close when grouping both branches' changes: the parts that
/// changed, measured as shapes. Coordinates are paper units (0.01 mm); at the
/// fixtures' 1:10 000 one unit is 0.1 m, so 100 units is the 10 m default.
/// </summary>
[TestFixture]
public sealed class ChangedStretchTests
{
    private const int FiveMetres = 50;
    private const int TenMetres = 100;
    private const int HundredMetres = 1000;

    private static readonly OcadObject Contour = Fixtures.Base.Objects.Where(o => o.Type == ObjectType.Line).MaxBy(o => o.Coords.Count);
    private static readonly OcadObject Knoll = Fixtures.Base.Objects.First(o => o.Type == ObjectType.Point);
    private static readonly OcadObject Lake = Fixtures.Base.Objects.Where(o => o.Type == ObjectType.Area).MaxBy(o => o.Coords.Count);

    /// <summary>A 1 km contour along y = 0, a point every 100 m.</summary>
    private static readonly OcadObject LongContour = Shape(Contour, [.. Enumerable.Range(0, 11).Select(i => P(i * 1000, 0))]);

    /// <summary>The same contour with its last point pulled 50 m north.</summary>
    private static readonly OcadObject NudgedAtTheEastEnd = Shape(Contour, [.. Enumerable.Range(0, 11).Select(i => P(i * 1000, i == 10 ? 500 : 0))]);

    /// <summary>
    /// A fixture object reshaped. WithCoords keeps the number of coordinates, so a
    /// shorter shape repeats its last point, which adds nothing to the outline.
    /// </summary>
    private static OcadObject Shape(OcadObject like, OcadCoord[] coords)
        => like.WithCoords([.. coords, .. Enumerable.Repeat(coords[^1] with { XFlags = 0, YFlags = 0 }, like.Coords.Count - coords.Length)]);

    private static OcadCoord P(int x, int y, byte xFlags = 0, byte yFlags = 0) => new(x, y, xFlags, yFlags);

    private static ChangedStretch KnollAddedAt(int x, int y) => ChangedStretch.Between(null, Shape(Knoll, [P(x, y)]), HundredMetres);

    [Test]
    public void AContourEditedAtOneEndIsChangedOnlyThere()
    {
        Bounds changed = ChangedStretch.Between(LongContour, NudgedAtTheEastEnd, HundredMetres).Bounds;

        Assert.That(changed, Is.EqualTo(new Bounds(9000, 0, 10000, 500)));
    }

    [Test]
    public void AKnollByTheEditedEndIsNearButOneByTheOtherEndIsNot()
    {
        ChangedStretch contour = ChangedStretch.Between(LongContour, NudgedAtTheEastEnd, HundredMetres);

        Assert.Multiple(() => {
            Assert.That(contour.IsNear(KnollAddedAt(9500, 300), TenMetres), Is.True);
            Assert.That(contour.IsNear(KnollAddedAt(500, 50), TenMetres), Is.False);
        });
    }

    [Test]
    public void TheOldLineCountsWhereItUsedToRun()
        // The contour no longer runs through (10000, 0), but a knoll the other mapper
        // dropped there sits on what this one changed.
        => Assert.That(ChangedStretch.Between(LongContour, NudgedAtTheEastEnd, HundredMetres).IsNear(KnollAddedAt(10000, 20), TenMetres), Is.True);

    [Test]
    public void ShapesAreMeasuredNotTheirBoxes()
    {
        // An added diagonal line's box covers (5000, 0), but the line is 350 m away from it.
        ChangedStretch diagonal = ChangedStretch.Between(null, Shape(Contour, [P(0, 0), P(10000, 10000)]), HundredMetres);

        Assert.Multiple(() => {
            Assert.That(diagonal.IsNear(KnollAddedAt(10000, 0), TenMetres), Is.False);
            Assert.That(diagonal.IsNear(KnollAddedAt(5050, 5000), TenMetres), Is.True);
        });
    }

    [Test]
    public void ACrossingIsNearWithNoPointClose()
    {
        ChangedStretch northSouth = ChangedStretch.Between(null, Shape(Contour, [P(5000, -5000), P(5000, 5000)]), HundredMetres);

        Assert.That(ChangedStretch.Between(null, LongContour, HundredMetres).IsNear(northSouth, TenMetres), Is.True);
    }

    [Test]
    public void SomethingInsideASmallAddedAreaIsNearButNotInsideItsHole()
    {
        // A 90 m square with a 30 m hole; both knolls are 15 m from every edge.
        OcadCoord[] square = [P(0, 0), P(900, 0), P(900, 900), P(0, 900), P(0, 0),
                              P(300, 300, yFlags: 0b10), P(600, 300), P(600, 600), P(300, 600), P(300, 300)];
        ChangedStretch area = ChangedStretch.Between(null, Shape(Lake, square), HundredMetres);

        Assert.Multiple(() => {
            Assert.That(area.IsNear(KnollAddedAt(150, 450), FiveMetres), Is.True);
            Assert.That(area.IsNear(KnollAddedAt(450, 450), FiveMetres), Is.False);
        });
    }

    [Test]
    public void TheInsideOfABigAreaDoesNotCount()
    {
        // A 500 m square: what lies in its middle is not near it, or one big polygon
        // would join everything in it into one overlap.
        ChangedStretch area = ChangedStretch.Between(null, Shape(Lake, [P(0, 0), P(5000, 0), P(5000, 5000), P(0, 5000), P(0, 0)]), HundredMetres);

        Assert.Multiple(() => {
            Assert.That(area.IsNear(KnollAddedAt(2500, 2500), TenMetres), Is.False);
            Assert.That(area.IsNear(KnollAddedAt(2500, 50), TenMetres), Is.True);
        });
    }

    [Test]
    public void AnAreaIsClosedThoughOcadDidNotRepeatItsFirstPoint()
    {
        // The ring stops at (0, 5000); the edge back to (0, 0) still counts.
        ChangedStretch area = ChangedStretch.Between(null, Shape(Lake, [P(0, 0), P(5000, 0), P(5000, 5000), P(0, 5000)]), HundredMetres);

        Assert.That(area.IsNear(KnollAddedAt(-30, 2500), TenMetres), Is.True);
    }

    [Test]
    public void AnObjectChangedWithoutMovingCountsWhole()
        // Same coordinates, so the change was to something else, a symbol say.
        => Assert.That(ChangedStretch.Between(LongContour, LongContour, HundredMetres).Bounds, Is.EqualTo(new Bounds(0, 0, 10000, 0)));

    [Test]
    public void CurvesAreMeasuredAlongTheCurve()
    {
        // A Bézier arch from (0, 0) to (1000, 0) whose control points are at height
        // 1000; its top is at (500, 750). A knoll 5 m above the top is near, though
        // 80 m from the straight line between the ends.
        ChangedStretch arch = ChangedStretch.Between(null, Shape(Contour, [P(0, 0), P(0, 1000, xFlags: 1), P(1000, 1000, xFlags: 2), P(1000, 0)]), HundredMetres);

        Assert.Multiple(() => {
            Assert.That(arch.IsNear(KnollAddedAt(500, 800), 60), Is.True);
            Assert.That(arch.IsNear(KnollAddedAt(500, 800), 40), Is.False);
        });
    }

    [Test]
    public void ChangesOnALongContourDoNotChainAcrossTheMap()
    {
        // Kari nudged the contour's east end; Ola added a knoll near each end. Only
        // the east knoll is near what Kari changed.
        ObjectItem contour = new("obj:contour", LongContour, NudgedAtTheEastEnd, LongContour,
                                 new ThreeWay.Verdict(Outcome.OnlyA, ChangeKind.Changed, ChangeKind.None, null));
        ObjectItem west = Added("obj:west", Shape(Knoll, [P(500, 50)]));
        ObjectItem east = Added("obj:east", Shape(Knoll, [P(9500, 300)]));

        OverlapGroup group = OverlapDetector.Find([contour, west, east], TenMetres, HundredMetres).Single();

        Assert.Multiple(() => {
            Assert.That(group.Items, Is.EqualTo(new[] { contour, east }));
            Assert.That(group.Bounds, Is.EqualTo(new Bounds(9000, 0, 10000, 500)));
        });
    }

    private static ObjectItem Added(string key, OcadObject inB)
        => new(key, null, null, inB, new ThreeWay.Verdict(Outcome.OnlyB, ChangeKind.None, ChangeKind.Added, null));
}
