using System;
using System.Drawing;
using System.Linq;

using NUnit.Framework;

namespace OcadViewer.Core.Tests;

[TestFixture]
public sealed class GroundTests
{
    /// <summary>UTM zone 32 on ETRS89, which is what OCAD's grid 63005 means.</summary>
    private const string Utm32 = "+proj=utm +zone=32 +ellps=GRS80 +towgs84=0,0,0,0,0,0,0 +units=m +no_defs";

    /// <summary>Granåsen.ocd's georeference: 1:4000, 4.1° from grid north, offset 564000 / 7031000.</summary>
    private static GroundPlacement Granasen() => GroundPlacement.Of(Utm32, 4.0, 4.1, 564000, 7031000);

    /// <summary>Byåsens del av Bymarkabasen.ocd's: 1:10000, 4.0°, offset 565000 / 7032000.</summary>
    private static GroundPlacement Byasen() => GroundPlacement.Of(Utm32, 10.0, 4.0, 565000, 7032000);

    /* ----------------------------------------------------------------- Affine */

    [Test]
    public void AnAffineUndoneIsWhereItStarted()
    {
        var turn = new Affine(0.8, 0.6, -1.2, 1.6, 30, -7);

        (double x, double y) = turn.Invert().Apply(turn.Apply(12.5, -4).X, turn.Apply(12.5, -4).Y);

        Assert.Multiple(() => {
            Assert.That(x, Is.EqualTo(12.5).Within(1e-9));
            Assert.That(y, Is.EqualTo(-4).Within(1e-9));
        });
    }

    [Test]
    public void ThenAppliesTheFirstTransformFirst()
    {
        var scale = new Affine(2, 0, 0, 2, 0, 0);
        var move = new Affine(1, 0, 0, 1, 10, 0);

        Assert.Multiple(() => {
            Assert.That(scale.Then(move).Apply(1, 1), Is.EqualTo((12.0, 2.0)));
            Assert.That(move.Then(scale).Apply(1, 1), Is.EqualTo((22.0, 2.0)));
        });
    }

    [Test]
    public void FromCornersPutsTheThreeCornersWhereTheyWereSaidToGo()
    {
        Affine fit = Affine.FromCorners(256, (5, 5), (15, 5), (5, -5));

        Assert.Multiple(() => {
            Assert.That(fit.Apply(0, 0), Is.EqualTo((5.0, 5.0)));
            Assert.That(fit.Apply(256, 0), Is.EqualTo((15.0, 5.0)));
            Assert.That(fit.Apply(0, 256), Is.EqualTo((5.0, -5.0)));
            Assert.That(fit.Apply(256, 256), Is.EqualTo((15.0, -5.0)));
        });
    }

    /* -------------------------------------------------------------- Placement */

    [Test]
    public void TheMapsOriginLandsOnItsGridOffset()
    {
        GroundPlacement granasen = Granasen();
        Assert.That(granasen, Is.Not.Null);

        (double x, double y) = granasen.ToGround(0, 0);
        (double lon, double lat) = GroundPlacement.MercatorToLonLat(x, y);

        // 564000 / 7031000 in UTM 32 is just west of Trondheim, at 63.4°N 10.3°E.
        Assert.Multiple(() => {
            Assert.That(lat, Is.EqualTo(63.39).Within(0.02));
            Assert.That(lon, Is.EqualTo(10.27).Within(0.02));
        });
    }

    [Test]
    public void GroundAndPaperAreEachOthersInverse()
    {
        GroundPlacement granasen = Granasen();

        foreach ((double px, double py) in new[] { (0.0, 0.0), (420.5, -640.25), (-300, 80), (530, -790) }) {
            (double gx, double gy) = granasen.ToGround(px, py);
            (double bx, double by) = granasen.ToPaper(gx, gy);
            Assert.Multiple(() => {
                Assert.That(bx, Is.EqualTo(px).Within(1e-4), $"x of ({px}, {py})");
                Assert.That(by, Is.EqualTo(py).Within(1e-4), $"y of ({px}, {py})");
            });
        }
    }

    [Test]
    public void AMillimetreOfPaperCoversTheMapsScaleOnTheGround()
    {
        // 1:4000 is 4 m of ground per millimetre. Web Mercator stretches that by
        // 1/cos(latitude), about 2.24 at Trondheim.
        GroundPlacement granasen = Granasen();
        (double x0, double y0) = granasen.ToGround(100, 100);
        (double x1, double y1) = granasen.ToGround(110, 100);
        (_, double lat) = GroundPlacement.MercatorToLonLat(x0, y0);

        double metresPerMm = Math.Sqrt(Math.Pow(x1 - x0, 2) + Math.Pow(y1 - y0, 2)) / 10;

        Assert.That(metresPerMm * Math.Cos(lat * Math.PI / 180), Is.EqualTo(4.0).Within(0.01));
    }

    [Test]
    public void TwoMapsAtDifferentScalesMeetAtTheSamePlaceOnTheGround()
    {
        // A point on the ground lies at a different place on each map's paper, and
        // each map brings it back to the same ground: that is what lines the two
        // panes up, background and all.
        GroundPlacement byasen = Byasen(), granasen = Granasen();
        (double gx, double gy) = granasen.ToGround(420, -640);

        (double bx, double by) = byasen.ToPaper(gx, gy);
        (double backX, double backY) = byasen.ToGround(bx, by);

        Assert.Multiple(() => {
            Assert.That((bx, by), Is.Not.EqualTo((420.0, -640.0)));
            Assert.That(backX, Is.EqualTo(gx).Within(1e-3));
            Assert.That(backY, Is.EqualTo(gy).Within(1e-3));
        });
    }

    [Test]
    public void ARotatedMapIsTurnedOnTheGround()
    {
        // Paper "up" on a map turned 4.1° from grid north does not point due north
        // on the ground; the pane shows north up, so the map is drawn tilted.
        GroundPlacement granasen = Granasen();
        (double x0, double y0) = granasen.ToGround(0, 0);
        (double x1, double y1) = granasen.ToGround(0, 100);

        double degreesFromNorth = Math.Atan2(x1 - x0, y1 - y0) * 180 / Math.PI;

        Assert.That(Math.Abs(degreesFromNorth), Is.InRange(2.0, 8.0));
    }

    [Test]
    public void PaperPlacementTakesMillimetresAsMetres()
    {
        Assert.Multiple(() => {
            Assert.That(GroundPlacement.Paper.IsOnGround, Is.False);
            Assert.That(GroundPlacement.Paper.ToGround(12.5, -7), Is.EqualTo((12.5, -7.0)));
            Assert.That(GroundPlacement.Paper.ToPaper(12.5, -7), Is.EqualTo((12.5, -7.0)));
        });
    }

    [Test]
    public void AGeoreferenceThatCannotBeUsedPlacesNothing()
    {
        Assert.Multiple(() => {
            Assert.That(GroundPlacement.Of(Utm32, 0, 0, 564000, 7031000), Is.Null, "no scale");
            Assert.That(GroundPlacement.Of("+proj=nonsense", 4, 0, 0, 0), Is.Null, "unreadable projection");
        });
    }

    /* ------------------------------------------------------------- Canvases */

    [Test]
    public void ACanvasOnPaperShowsTheRectangleItCovers()
    {
        // 256 pixels at half a unit each, top-left at (100, 50): paper runs up, rows down.
        Affine canvasToPaper = GroundView.CanvasToPaper(GroundPlacement.Paper, 100, 50, 0.5, 256);

        Assert.Multiple(() => {
            Assert.That(canvasToPaper.Apply(0, 0), Is.EqualTo((100.0, 50.0)));
            Assert.That(canvasToPaper.Apply(256, 256), Is.EqualTo((228.0, -78.0)));
            Assert.That(GroundView.PaperUnder(canvasToPaper, 256), Is.EqualTo(RectangleF.FromLTRB(100, -78, 228, 50)));
        });
    }

    [Test]
    public void ACanvasOverAGeoreferencedMapGoesBackToItsPaper()
    {
        GroundPlacement granasen = Granasen();
        (double gx, double gy) = granasen.ToGround(420, -640);
        const double metresPerPixel = 0.5;

        Affine canvasToPaper = GroundView.CanvasToPaper(granasen, gx, gy, metresPerPixel, 256);

        // The top-left corner is the point itself; the far corner is fitted, and a
        // projection bends far less than a millimetre over 128 m of ground.
        (double fx, double fy) = granasen.ToPaper(gx + 256 * metresPerPixel, gy - 256 * metresPerPixel);
        Assert.Multiple(() => {
            Assert.That(canvasToPaper.Apply(0, 0).X, Is.EqualTo(420).Within(1e-4));
            Assert.That(canvasToPaper.Apply(0, 0).Y, Is.EqualTo(-640).Within(1e-4));
            Assert.That(canvasToPaper.Apply(256, 256).X, Is.EqualTo(fx).Within(0.01));
            Assert.That(canvasToPaper.Apply(256, 256).Y, Is.EqualTo(fy).Within(0.01));
        });
    }

    [Test]
    public void TheExtentOfAMapOnPaperIsItsBounds()
    {
        var bounds = RectangleF.FromLTRB(-10, -20, 30, 40);

        Assert.That(GroundView.GroundExtent(GroundPlacement.Paper, bounds), Is.EqualTo((-10.0, -20.0, 30.0, 40.0)));
    }

    [Test]
    public void TheExtentOfATurnedMapCoversAllOfIt()
    {
        GroundPlacement granasen = Granasen();
        var bounds = RectangleF.FromLTRB(316, -794, 528, -492);

        (double minX, double minY, double maxX, double maxY) = GroundView.GroundExtent(granasen, bounds);

        foreach ((float px, float py) in new[] { (316f, -794f), (528f, -794f), (316f, -492f), (528f, -492f) }) {
            (double gx, double gy) = granasen.ToGround(px, py);
            Assert.That(gx, Is.InRange(minX, maxX));
            Assert.That(gy, Is.InRange(minY, maxY));
        }
    }

    [Test]
    public void AChangeMaskLandsOnTheCanvasWhereItsPaperIs()
    {
        // A 100 x 50 pixel mask over paper (0, 0) - (200, 100), on paper; a canvas
        // whose top-left is at (0, 100), one unit a pixel.
        var area = RectangleF.FromLTRB(0, 0, 200, 100);

        Affine maskToCanvas = GroundView.MaskToCanvas(GroundPlacement.Paper, area, 100, 50, 0, 100, 1, 256);

        Assert.Multiple(() => {
            Assert.That(maskToCanvas.Apply(0, 0).X, Is.EqualTo(0).Within(1e-9), "top-left x");
            Assert.That(maskToCanvas.Apply(0, 0).Y, Is.EqualTo(0).Within(1e-9), "top-left y");
            Assert.That(maskToCanvas.Apply(100, 50).X, Is.EqualTo(200).Within(1e-9), "bottom-right x");
            Assert.That(maskToCanvas.Apply(100, 50).Y, Is.EqualTo(100).Within(1e-9), "bottom-right y");
        });
    }

    /* ---------------------------------------------------------- Backgrounds */

    [Test]
    public void AWebMercatorTileOnACanvasOfItsOwnSizeLiesStraight()
    {
        BackgroundSource osm = BackgroundMaps.Find("osm");
        const int z = 15, column = 17_330, row = 8_920;
        double metresPerPixel = TileGrid.WebMercator.MetresPerPixel(z);
        double minX = TileGrid.WebMercator.OriginX + column * 256 * metresPerPixel;
        double maxY = TileGrid.WebMercator.OriginY - row * 256 * metresPerPixel;

        BackgroundTile[] tiles = BackgroundMaps.TilesOnGround(osm, minX, maxY, metresPerPixel, 256).ToArray();

        Assert.Multiple(() => {
            Assert.That(tiles.Select(t => t.Url),
                        Does.Contain($"https://tile.openstreetmap.org/{z}/{column}/{row}.png"));
            double[] own = tiles.Single(t => t.Url.EndsWith($"/{z}/{column}/{row}.png")).Transform;
            Assert.That(own, Is.EqualTo(new double[] { 1, 0, 0, 1, 0, 0 }).Within(1e-6));
        });
    }

    [Test]
    public void AUtmBackgroundIsFittedToTheGround()
    {
        GroundPlacement granasen = Granasen();
        (double gx, double gy) = granasen.ToGround(420, -640);

        BackgroundTile[] tiles = BackgroundMaps.TilesOnGround(BackgroundMaps.Find("mapant"), gx, gy, 2.0, 256).ToArray();

        Assert.That(tiles, Is.Not.Empty);
        Assert.That(tiles.All(t => t.Transform.All(double.IsFinite)), Is.True);
    }
}
