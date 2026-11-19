using NUnit.Framework;

namespace OcadViewer.Core.Tests;

[TestFixture]
public sealed class BackgroundMapsTests
{
    /// <summary>
    /// Paper to grid for a 1:4000 map turned 10°, with its origin at Byåsen in
    /// whichever grid is asked for. Rotated, so a transform that only works for
    /// a north-up map fails.
    /// </summary>
    private static PaperTransform Sprint(TileGrid grid, double metresPerMm = 4)
    {
        (double x0, double y0) = grid == TileGrid.WebMercator ? (1146600.0, 9220000.0) : (270000.0, 7040000.0);
        double theta = -10 * Math.PI / 180, cos = Math.Cos(theta), sin = Math.Sin(theta);
        return new PaperTransform(metresPerMm * cos, -metresPerMm * sin, x0,
                                  metresPerMm * sin, metresPerMm * cos, y0);
    }

    public static IEnumerable<TileGrid> Grids => TileGrid.All;

    [TestCaseSource(nameof(Grids))]
    public void ATilePixelIsDrawnWherePaperProjectsToIt(TileGrid grid)
    {
        PaperTransform toGrid = Sprint(grid);
        const double minX = -40, maxY = 70, mmPerPixel = 0.3;
        const int z = 9;
        double span = grid.TileSize * grid.MetresPerPixel(z);
        (double gx, double gy) = toGrid.Apply(minX, maxY);
        int column = (int)Math.Floor((gx - grid.OriginX) / span);
        int row = (int)Math.Floor((grid.OriginY - gy) / span);

        double[] m = BackgroundMaps.TileToCanvas(grid, toGrid, minX, maxY, mmPerPixel, z, column, row);

        foreach ((double i, double j) in new[] { (0.0, 0.0), (grid.TileSize, 0.0), (17.0, 200.0) }) {
            // Canvas setTransform(a, b, c, d, e, f): u = a·i + c·j + e, v = b·i + d·j + f.
            double u = m[0] * i + m[2] * j + m[4], v = m[1] * i + m[3] * j + m[5];
            (double ax, double ay) = toGrid.Apply(minX + u * mmPerPixel, maxY - v * mmPerPixel);
            double ex = grid.OriginX + (column * grid.TileSize + i) * grid.MetresPerPixel(z);
            double ey = grid.OriginY - (row * grid.TileSize + j) * grid.MetresPerPixel(z);

            Assert.Multiple(() => {
                Assert.That(ax, Is.EqualTo(ex).Within(1e-6), $"x of tile pixel ({i}, {j})");
                Assert.That(ay, Is.EqualTo(ey).Within(1e-6), $"y of tile pixel ({i}, {j})");
            });
        }
    }

    [TestCase(0.25, 10, TestName = "One metre per canvas pixel takes mapant.no's 1 m zoom")]
    [TestCase(1.0, 8, TestName = "Four metres per canvas pixel takes the zoom just finer than that")]
    [TestCase(0.01, 10, TestName = "Finer than the server has stops at its finest zoom")]
    public void TheZoomIsTheCoarsestNoCoarserThanTheCanvas(double mmPerPixel, int expected)
    {
        BackgroundSource mapant = BackgroundMaps.Find("mapant");

        (int z, _) = BackgroundMaps.ChooseTiles(mapant.Grid, Sprint(mapant.Grid), 0, 0, mmPerPixel, 256, mapant.MaxZoom);

        Assert.That(z, Is.EqualTo(expected));
    }

    [Test]
    public void NoCanvasAsksForMoreThanTheCap()
    {
        foreach (BackgroundSource source in BackgroundMaps.Sources)
            foreach (double mmPerPixel in new[] { 0.01, 0.1, 1, 10, 100, 1000 }) {
                (_, List<(int X, int Y)> tiles) = BackgroundMaps.ChooseTiles(
                    source.Grid, Sprint(source.Grid), 0, 0, mmPerPixel, 256, source.MaxZoom);
                Assert.That(tiles, Has.Count.LessThanOrEqualTo(BackgroundMaps.MaxTilesPerCanvas),
                    $"{source.Id} at {mmPerPixel} mm per pixel");
            }
    }

    [Test]
    public void PaperOffTheGridHasNoTiles()
    {
        // Far west of mapant.no's grid, which starts at -150 000 m.
        var westOfTheGrid = new PaperTransform(4, 0, -5000000, 0, 4, 7040000);

        (_, List<(int X, int Y)> tiles) = BackgroundMaps.ChooseTiles(
            TileGrid.MapAntUtm33, westOfTheGrid, 0, 0, 0.25, 256, 10);

        Assert.That(tiles, Is.Empty);
    }

    [Test]
    public void TilesUnderGivesEachTileItsAddressAndSize()
    {
        BackgroundSource mapant = BackgroundMaps.Find("mapant");

        IReadOnlyList<BackgroundTile> tiles = BackgroundMaps.TilesUnder(mapant, Sprint(mapant.Grid), 0, 0, 0.25, 256);

        Assert.That(tiles, Is.Not.Empty);
        Assert.Multiple(() => {
            foreach (BackgroundTile tile in tiles) {
                Assert.That(tile.Url, Does.StartWith("https://mapant.no/tiles/10/"));
                Assert.That(tile.Size, Is.EqualTo(1024));
                Assert.That(tile.Transform, Has.Length.EqualTo(6));
            }
        });
    }

    [TestCase("osm", "https://tile.openstreetmap.org/5/1/2.png")]
    [TestCase("kartverketTopo", "https://cache.kartverket.no/v1/wmts/1.0.0/topo/default/webmercator/5/2/1.png")]
    [TestCase("mapant", "https://mapant.no/tiles/5/2/1.png")]
    public void EachSourceOrdersColumnAndRowItsOwnWay(string id, string expected)
        => Assert.That(BackgroundMaps.Find(id).Url(5, x: 1, y: 2), Is.EqualTo(expected));

    [Test]
    public void EverySourceIsOnAGridThePaperIsPlacedOn()
    {
        Assert.Multiple(() => {
            Assert.That(BackgroundMaps.Sources.Select(s => s.Id), Is.Unique);
            foreach (BackgroundSource source in BackgroundMaps.Sources)
                Assert.That(TileGrid.All, Does.Contain(source.Grid), source.Id);
        });
    }

    [Test]
    public void AMapIsOfferedTheSourcesOnTheGridsItHas()
    {
        var mercatorOnly = new Dictionary<string, PaperTransform> {
            [TileGrid.WebMercator.Name] = new PaperTransform(1, 0, 0, 0, 1, 0)
        };

        Assert.Multiple(() => {
            Assert.That(BackgroundMaps.AvailableFor(null), Is.Null, "no georeference");
            Assert.That(BackgroundMaps.AvailableFor(mercatorOnly), Is.EqualTo(new[] { "osm", "kartverketTopo" }));
        });
    }
}
