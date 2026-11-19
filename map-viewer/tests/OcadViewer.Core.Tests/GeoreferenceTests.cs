using System.Drawing;

using NUnit.Framework;

using Matrix = PurplePen.Graphics2D.Matrix;
using MatrixOrder = PurplePen.Graphics2D.MatrixOrder;

namespace OcadViewer.Core.Tests;

[TestFixture]
public sealed class GeoreferenceTests
{
    /// <summary>UTM zone 32 on ETRS89, which is what OCAD's grid 63005 means.</summary>
    private const string Utm32 = "+proj=utm +zone=32 +ellps=GRS80 +towgs84=0,0,0,0,0,0,0 +units=m +no_defs";

    [Test]
    public void PaperToGridAgreesWithPurplePen()
    {
        // Purple Pen's own MapToRealWorldTransform, without the offset: its float
        // matrix cannot hold seven-digit coordinates, which is why ours is in doubles.
        const float metresPerMm = 4f, angle = 7.5f;
        var purplePen = new Matrix();
        purplePen.Scale(metresPerMm, metresPerMm, MatrixOrder.Append);
        purplePen.Rotate(-angle, MatrixOrder.Append);

        foreach (PointF paper in new[] { new PointF(10, 0), new PointF(0, 10), new PointF(-35.5f, 120.25f) }) {
            PointF[] point = [paper];
            purplePen.TransformPoints(point);
            PointF expected = point[0];
            (double x, double y) = Georeference.PaperToGrid(paper.X, paper.Y, metresPerMm, angle, 0, 0);

            Assert.Multiple(() => {
                Assert.That(x, Is.EqualTo(expected.X).Within(1e-3), $"x of {paper}");
                Assert.That(y, Is.EqualTo(expected.Y).Within(1e-3), $"y of {paper}");
            });
        }
    }

    [Test]
    public void PaperToGridScalesThenMovesToTheOffset()
    {
        (double x, double y) = Georeference.PaperToGrid(25, -10, 4, 0, 564000, 7031000);

        Assert.Multiple(() => {
            Assert.That(x, Is.EqualTo(564100).Within(1e-9));
            Assert.That(y, Is.EqualTo(7030960).Within(1e-9));
        });
    }

    [Test]
    public void MercatorPutsTheEquatorAtZeroAndTheDateLineAtTheEdge()
    {
        (double x0, double y0) = Georeference.LonLatToMercator(0, 0);
        (double x180, _) = Georeference.LonLatToMercator(180, 0);

        Assert.Multiple(() => {
            Assert.That(x0, Is.EqualTo(0).Within(1e-6));
            Assert.That(y0, Is.EqualTo(0).Within(1e-6));
            Assert.That(x180, Is.EqualTo(20037508.342789244).Within(1e-3));
        });
    }

    [Test]
    public void FitReproducesAnAffineProjectionExactly()
    {
        static (double, double) Affine(double x, double y) => (3 * x - 2 * y + 1000, 0.5 * x + 4 * y - 7);

        PaperToMercator fitted = Georeference.Fit(RectangleF.FromLTRB(-50, -20, 300, 180), Affine);

        foreach ((double x, double y) in new[] { (0.0, 0.0), (123.4, -56.7), (-50.0, 180.0) }) {
            (double ex, double ey) = Affine(x, y);
            (double ax, double ay) = fitted.Apply(x, y);
            Assert.Multiple(() => {
                Assert.That(ax, Is.EqualTo(ex).Within(1e-6));
                Assert.That(ay, Is.EqualTo(ey).Within(1e-6));
            });
        }
    }

    [Test]
    public void AFittedSprintMapIsUsable()
    {
        PaperToMercator fitted = Georeference.Fit(RectangleF.FromLTRB(-250, -250, 250, 250),
            (px, py) => {
                (double gx, double gy) = Georeference.PaperToGrid(px, py, 4, 7.5, 564000, 7031000);
                return Georeference.GridToMercator(Utm32, gx, gy);
            });

        Assert.That(fitted.IsUsable, Is.True);
    }

    [Test]
    public void AGridScaleFactorOfZeroIsNotUsable()
    {
        // Every point of paper lands on the offset: nothing to invert.
        PaperToMercator fitted = Georeference.Fit(RectangleF.FromLTRB(0, 0, 100, 100),
            (px, py) => Georeference.PaperToGrid(px, py, 0, 0, 564000, 7031000));

        Assert.That(fitted.IsUsable, Is.False);
    }

    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void ATransformWithANonFiniteNumberIsNotUsable(double bad)
    {
        // What a projection gives for a point past the poles; JSON cannot carry NaN.
        Assert.Multiple(() => {
            Assert.That(new PaperToMercator(bad, 0, 0, 0, 1, 0).IsUsable, Is.False, "scale");
            Assert.That(new PaperToMercator(1, 0, bad, 0, 1, 0).IsUsable, Is.False, "offset");
        });
    }

    [Test]
    public void AUtmPointInTrondheimLandsInTrondheim()
    {
        // The origin of a real sprint map at Byåsen, Trondheim: 63.4°N 10.3°E.
        (double x, double y) = Georeference.GridToMercator(Utm32, 564000, 7031000);
        (double minX, double minY) = Georeference.LonLatToMercator(10.1, 63.3);
        (double maxX, double maxY) = Georeference.LonLatToMercator(10.5, 63.5);

        Assert.Multiple(() => {
            Assert.That(x, Is.InRange(minX, maxX), "easting");
            Assert.That(y, Is.InRange(minY, maxY), "northing");
        });
    }

    [Test]
    public void OverASprintMapTheFittedTransformIsWithinACentimetreOfTheProjection()
    {
        // 1:4000, half a metre of paper: two kilometres of ground.
        RectangleF paper = RectangleF.FromLTRB(-250, -250, 250, 250);
        (double, double) Exact(double px, double py)
        {
            (double gx, double gy) = Georeference.PaperToGrid(px, py, 4, 0, 564000, 7031000);
            return Georeference.GridToMercator(Utm32, gx, gy);
        }

        PaperToMercator fitted = Georeference.Fit(paper, Exact);

        // Mercator metres are stretched by 1/cos(latitude), about 2.2 at 63°N, so a
        // centimetre on the ground is about 2.2 cm here.
        foreach ((double px, double py) in new[] { (250.0, 250.0), (0.0, 0.0), (125.0, -60.0) }) {
            (double ex, double ey) = Exact(px, py);
            (double ax, double ay) = fitted.Apply(px, py);
            Assert.That(Math.Sqrt((ax - ex) * (ax - ex) + (ay - ey) * (ay - ey)), Is.LessThan(0.03),
                $"at paper ({px}, {py})");
        }
    }
}
