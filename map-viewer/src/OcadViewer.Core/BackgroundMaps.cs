using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OcadViewer.Core
{
    /// <summary>A web map that can be drawn under an OCAD map.</summary>
    /// <param name="Id">What the page calls it.</param>
    /// <param name="Name">What the menu shows. A proper name, so not translated.</param>
    /// <param name="Grid">The tile pyramid its tiles are cut on.</param>
    /// <param name="UrlTemplate">A tile's address, with <c>{z}</c>, <c>{x}</c> (column) and <c>{y}</c> (row).</param>
    /// <param name="MaxZoom">The finest zoom the server has.</param>
    /// <param name="Attribution">The credit line, as HTML.</param>
    public sealed record BackgroundSource(string Id, string Name, TileGrid Grid, string UrlTemplate,
                                          int MaxZoom, string Attribution)
    {
        public string Url(int z, int x, int y) => UrlTemplate
            .Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
            .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
            .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// One web tile to draw on a background canvas: where to fetch it, how big it
    /// is, and the transform that puts its pixels in place.
    /// </summary>
    /// <param name="Transform">
    /// Web tile pixels to canvas pixels as <c>[a, b, c, d, e, f]</c>, in the order
    /// a canvas's <c>setTransform</c> takes them.
    /// </param>
    public sealed record BackgroundTile(string Url, int Size, double[] Transform);

    /// <summary>
    /// The background maps on offer, and which of their tiles go where under the
    /// paper. Everything the page needs to draw a background is worked out here,
    /// so the page only fetches images and draws them where it is told.
    /// </summary>
    public static class BackgroundMaps
    {
        /// <summary>How many web tiles one background canvas may draw before a coarser zoom is used.</summary>
        public const int MaxTilesPerCanvas = 36;

        /// <summary>
        /// The web maps, in menu order.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Kartverket's topographic map covers Norway only; outside it the tiles are
        /// blank. Its cache takes row before column and stops at zoom 18, about
        /// 0.6 m per pixel at 63°N. Free, no key, CC BY 4.0.
        /// </para>
        /// <para>
        /// mapant.no is an orienteering map drawn by a program from Kartverket's
        /// laser scans, Norway only and not yet all of it. Its tiles are 1024 pixels
        /// and about half a megabyte each, down to 1 m per pixel. Its WMTS says
        /// CC BY 4.0; its web page says the licence is not settled but allows
        /// non-commercial orienteering use.
        /// </para>
        /// </remarks>
        public static readonly IReadOnlyList<BackgroundSource> Sources = [
            new("osm", "OpenStreetMap", TileGrid.WebMercator,
                "https://tile.openstreetmap.org/{z}/{x}/{y}.png", 19,
                "&copy; <a href=\"https://www.openstreetmap.org/copyright\" target=\"_blank\" rel=\"noopener\">OpenStreetMap</a> contributors"),
            new("kartverketTopo", "Kartverket topo", TileGrid.WebMercator,
                "https://cache.kartverket.no/v1/wmts/1.0.0/topo/default/webmercator/{z}/{y}/{x}.png", 18,
                "&copy; <a href=\"https://www.kartverket.no/\" target=\"_blank\" rel=\"noopener\">Kartverket</a> (CC BY 4.0)"),
            new("mapant", "mapant.no", TileGrid.MapAntUtm33,
                "https://mapant.no/tiles/{z}/{y}/{x}.png", 10,
                "&copy; <a href=\"https://mapant.no/\" target=\"_blank\" rel=\"noopener\">MapAnt.no</a>, "
                + "data &copy; Kartverket, Geovekst and the municipalities"),
        ];

        /// <summary>The source with this id, or <c>null</c>.</summary>
        public static BackgroundSource Find(string id) => Sources.FirstOrDefault(s => s.Id == id);

        /// <summary>
        /// The ids of the sources a map can be laid on: those whose grid it has a
        /// transform to. <c>null</c> when it has none, which is a map with no
        /// usable georeference.
        /// </summary>
        public static string[] AvailableFor(IReadOnlyDictionary<string, PaperTransform> paperToGrid)
            => paperToGrid is null
                ? null
                : Sources.Where(s => paperToGrid.ContainsKey(s.Grid.Name)).Select(s => s.Id).ToArray();

        /// <summary>
        /// The web tiles to draw on one square background canvas, and where.
        /// </summary>
        /// <param name="toGrid">The map's paper-to-grid transform for <paramref name="source"/>'s grid.</param>
        /// <param name="minX">The canvas's left edge, in paper millimetres.</param>
        /// <param name="maxY">The canvas's top edge, in paper millimetres (paper Y runs up).</param>
        /// <param name="mmPerPixel">Paper millimetres per canvas pixel.</param>
        /// <param name="pixels">Canvas pixels along a side.</param>
        public static IReadOnlyList<BackgroundTile> TilesUnder(BackgroundSource source, PaperTransform toGrid,
                                                               double minX, double maxY, double mmPerPixel, int pixels)
        {
            TileGrid grid = source.Grid;
            (int z, List<(int X, int Y)> tiles) = ChooseTiles(grid, toGrid, minX, maxY, mmPerPixel, pixels, source.MaxZoom);
            return tiles
                .Select(t => new BackgroundTile(source.Url(z, t.X, t.Y), grid.TileSize,
                                                TileToCanvas(grid, toGrid, minX, maxY, mmPerPixel, z, t.X, t.Y)))
                .ToArray();
        }

        /// <summary>
        /// The web tiles to draw on one square canvas of the ground, and where: a
        /// canvas whose top-left corner is at <paramref name="minX"/>,
        /// <paramref name="maxY"/> in Web Mercator metres.
        /// </summary>
        /// <remarks>
        /// A Web Mercator source lies on the canvas as it is - only moved and
        /// scaled. Any other grid is fitted to the canvas through its projection,
        /// over this one canvas, so it bends no more than a screen tile's worth.
        /// </remarks>
        public static IReadOnlyList<BackgroundTile> TilesOnGround(BackgroundSource source, double minX, double maxY,
                                                                  double metresPerPixel, int pixels)
        {
            double side = pixels * metresPerPixel;
            PaperTransform toGrid = source.Grid == TileGrid.WebMercator
                ? new PaperTransform(1, 0, 0, 0, 1, 0)
                : Georeference.Fit(minX, maxY - side, side, side, (x, y) => {
                    (double lon, double lat) = GroundPlacement.MercatorToLonLat(x, y);
                    return source.Grid.Project(Georeference.Wgs84.Value, lon, lat);
                });

            return toGrid.IsUsable ? TilesUnder(source, toGrid, minX, maxY, metresPerPixel, pixels) : [];
        }

        /// <summary>
        /// Which tiles of a grid lie under a canvas, and at what zoom: the coarsest
        /// zoom whose pixels are no larger than the canvas's, so the background is
        /// as sharp as the screen without fetching more than it shows; coarser still
        /// if that would be more than <see cref="MaxTilesPerCanvas"/> tiles.
        /// </summary>
        public static (int Z, List<(int X, int Y)> Tiles) ChooseTiles(TileGrid grid, PaperTransform toGrid,
                                                                     double minX, double maxY, double mmPerPixel,
                                                                     int pixels, int maxZoom)
        {
            double side = pixels * mmPerPixel;
            (double X, double Y)[] corners = [
                toGrid.Apply(minX, maxY), toGrid.Apply(minX + side, maxY),
                toGrid.Apply(minX, maxY - side), toGrid.Apply(minX + side, maxY - side)
            ];
            double minGx = corners.Min(p => p.X), maxGx = corners.Max(p => p.X);
            double minGy = corners.Min(p => p.Y), maxGy = corners.Max(p => p.Y);

            double metresPerPixel = mmPerPixel * Math.Sqrt(Math.Abs(toGrid.A * toGrid.E - toGrid.B * toGrid.D));
            int z = (int)Math.Ceiling(Math.Log2(grid.MetresPerPixelAtZero / metresPerPixel));
            z = Math.Clamp(z, 0, maxZoom);

            for (; z >= 0; z--) {
                double span = grid.TileSize * grid.MetresPerPixel(z);
                int x0 = Math.Max(0, (int)Math.Floor((minGx - grid.OriginX) / span));
                int x1 = Math.Min(grid.Columns(z) - 1, (int)Math.Floor((maxGx - grid.OriginX) / span));
                int y0 = Math.Max(0, (int)Math.Floor((grid.OriginY - maxGy) / span));
                int y1 = Math.Min(grid.Rows(z) - 1, (int)Math.Floor((grid.OriginY - minGy) / span));

                // Entirely off the grid: nothing to draw at any zoom.
                if (x0 > x1 || y0 > y1)
                    return (z, []);

                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > MaxTilesPerCanvas && z > 0)
                    continue;

                var tiles = new List<(int X, int Y)>();
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        tiles.Add((x, y));
                return (z, tiles);
            }

            return (0, []);
        }

        /// <summary>
        /// The 2D affine transform from one web tile's pixels to a canvas's pixels:
        /// tile pixel to grid metres, back through the paper-to-grid transform to
        /// paper millimetres, then to canvas pixels.
        /// </summary>
        /// <returns><c>[a, b, c, d, e, f]</c> in the order a canvas's <c>setTransform</c> takes them.</returns>
        public static double[] TileToCanvas(TileGrid grid, PaperTransform toGrid, double minX, double maxY,
                                            double mmPerPixel, int z, int column, int row)
        {
            double det = toGrid.A * toGrid.E - toGrid.B * toGrid.D;
            double res = grid.MetresPerPixel(z);

            (double U, double V) ToCanvas(double i, double j)
            {
                double gx = grid.OriginX + (column * grid.TileSize + i) * res - toGrid.C;
                double gy = grid.OriginY - (row * grid.TileSize + j) * res - toGrid.F;
                double px = (toGrid.E * gx - toGrid.B * gy) / det;
                double py = (-toGrid.D * gx + toGrid.A * gy) / det;
                return ((px - minX) / mmPerPixel, (maxY - py) / mmPerPixel);
            }

            (double u0, double v0) = ToCanvas(0, 0);
            (double ui, double vi) = ToCanvas(1, 0);
            (double uj, double vj) = ToCanvas(0, 1);
            return [ui - u0, vi - v0, uj - u0, vj - v0, u0, v0];
        }
    }
}
