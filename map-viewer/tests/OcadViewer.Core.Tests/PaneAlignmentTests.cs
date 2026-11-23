using System.Drawing;

using NUnit.Framework;

namespace OcadViewer.Core.Tests;

[TestFixture]
public sealed class PaneAlignmentTests
{
    [Test]
    public void TheSameAreaNeedsNoOffset()
    {
        var area = new RectangleF(-120, -80, 240, 160);

        Assert.That(PaneAlignment.Offset(area, area), Is.EqualTo((0.0, 0.0)));
    }

    [Test]
    public void AMovedMapIsOffsetByHowFarItMoved()
    {
        var before = new RectangleF(-120, -80, 240, 160);
        var after = new RectangleF(430, -710, 240, 160);

        Assert.That(PaneAlignment.Offset(before, after), Is.EqualTo((550.0, -630.0)));
    }

    [Test]
    public void TheOffsetGoesFromOneMapToTheOther()
    {
        var left = new RectangleF(0, 0, 100, 100);
        var right = new RectangleF(30, -20, 100, 100);

        (double x, double y) there = PaneAlignment.Offset(left, right);
        (double x, double y) back = PaneAlignment.Offset(right, left);

        Assert.Multiple(() => {
            Assert.That(there, Is.EqualTo((30.0, -20.0)));
            Assert.That(back, Is.EqualTo((-30.0, 20.0)));
        });
    }

    [Test]
    public void AreasOfDifferentSizesLineUpTheirMiddles()
    {
        // A frame and legend added round the map grow it on every side, but not
        // equally: the middles are what is lined up, not the corners.
        var bare = new RectangleF(10, 10, 200, 300);
        var framed = new RectangleF(0, -10, 240, 330);

        (double x, double y) = PaneAlignment.Offset(bare, framed);

        Assert.Multiple(() => {
            Assert.That(x, Is.EqualTo(10.0));     // middles at 110 and 120
            Assert.That(y, Is.EqualTo(-5.0));     // middles at 160 and 155
        });
    }

    [Test]
    public void AStoreHasNoOffsetForAMapItDoesNotHold()
    {
        using var store = new MapStore();

        Assert.That(store.PaneOffset("V1", "current"), Is.Null);
    }

    [Test]
    public void FarFromTheOriginTheOffsetKeepsItsFraction()
    {
        // Granåsen's working file sits about 800 mm down the paper; halving a float
        // width there would round off the part of a millimetre that matters.
        var near = new RectangleF(-257.78f, -103.31f, 244.33f, 335.26f);
        var far = new RectangleF(315.86f, -793.59f, 212.59f, 301.21f);

        (double x, double y) = PaneAlignment.Offset(near, far);

        Assert.Multiple(() => {
            Assert.That(x, Is.EqualTo((315.86 + 212.59 / 2) - (-257.78 + 244.33 / 2)).Within(1e-4));
            Assert.That(y, Is.EqualTo((-793.59 + 301.21 / 2) - (-103.31 + 335.26 / 2)).Within(1e-4));
        });
    }
}
