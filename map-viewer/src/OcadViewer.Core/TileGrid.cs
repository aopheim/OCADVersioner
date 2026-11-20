using System;
using System.Collections.Generic;

using DotSpatial.Projections;

namespace OcadViewer.Core
{
    /// <summary>
    /// A web map's tile pyramid: where tile (0, 0) starts, how big a tile is, how
    /// many metres a pixel covers at each zoom, and how to get there from the
    /// map's own grid. Rows count down from the top, columns to the right.
    /// </summary>
    public sealed class TileGrid
    {
        /// <summary>Web Mercator's half-width, in metres: the world runs from -R to R both ways.</summary>
        private const double MercatorHalfWidth = 20037508.342789244;

        /// <summary>
        /// Web Mercator (EPSG:3857), the grid of OpenStreetMap and nearly every web
        /// map: 256-pixel tiles, the whole world in one tile at zoom 0, halving each zoom.
        /// </summary>
        public static readonly TileGrid WebMercator = new(
            "EPSG:3857",
            originX: -MercatorHalfWidth,
            originY: MercatorHalfWidth,
            tileSize: 256,
            metresPerPixelAtZero: 2 * MercatorHalfWidth / 256,
            columns: z => 1 << z,
            rows: z => 1 << z,
            project: Georeference.ToWebMercator);

        private static readonly int[] MapAntColumns = [3, 3, 5, 12, 25, 50, 100, 200, 400, 800, 1600];
        private static readonly int[] MapAntRows = [5, 8, 15, 30, 60, 120, 240, 480, 960, 1920, 3840];

        /// <summary>
        /// UTM zone 33 on WGS84 (EPSG:32633) as mapant.no tiles it, from
        /// https://mapant.no/wmts/wmts.xml. Its scale denominators work out to
        /// 1000 m per pixel at zoom 0, halving each zoom, so a 1024-pixel tile is
        /// exactly 1 km at zoom 10, its finest.
        /// </summary>
        public static readonly TileGrid MapAntUtm33 = new(
            "EPSG:32633",
            originX: -150000,
            originY: 10000000,
            tileSize: 1024,
            metresPerPixelAtZero: 1000,
            columns: z => z < MapAntColumns.Length ? MapAntColumns[z] : 0,
            rows: z => z < MapAntRows.Length ? MapAntRows[z] : 0,
            project: Georeference.ToProjection("+proj=utm +zone=33 +datum=WGS84 +units=m +no_defs"));

        /// <summary>Every grid a background map is drawn on; the paper is placed on each.</summary>
        public static readonly IReadOnlyList<TileGrid> All = [WebMercator, MapAntUtm33];

        private readonly Func<int, int> columns;
        private readonly Func<int, int> rows;
        private readonly Func<ProjectionInfo, double, double, (double X, double Y)> project;

        private TileGrid(string name, double originX, double originY, int tileSize, double metresPerPixelAtZero,
                         Func<int, int> columns, Func<int, int> rows,
                         Func<ProjectionInfo, double, double, (double X, double Y)> project)
        {
            Name = name;
            OriginX = originX;
            OriginY = originY;
            TileSize = tileSize;
            MetresPerPixelAtZero = metresPerPixelAtZero;
            this.columns = columns;
            this.rows = rows;
            this.project = project;
        }

        /// <summary>The grid's EPSG name, used as its key.</summary>
        public string Name { get; }

        /// <summary>Grid metres east of tile (0, 0)'s top-left corner.</summary>
        public double OriginX { get; }

        /// <summary>Grid metres north of tile (0, 0)'s top-left corner.</summary>
        public double OriginY { get; }

        /// <summary>Pixels along a tile's side.</summary>
        public int TileSize { get; }

        /// <summary>Metres one pixel covers at zoom 0.</summary>
        public double MetresPerPixelAtZero { get; }

        /// <summary>Metres one pixel covers at zoom <paramref name="z"/>.</summary>
        public double MetresPerPixel(int z) => MetresPerPixelAtZero / Math.Pow(2, z);

        /// <summary>How many tiles wide the grid is at zoom <paramref name="z"/>.</summary>
        public int Columns(int z) => columns(z);

        /// <summary>How many tiles tall the grid is at zoom <paramref name="z"/>.</summary>
        public int Rows(int z) => rows(z);

        /// <summary>A point on a map's own grid, in metres, to this grid's metres.</summary>
        public (double X, double Y) Project(ProjectionInfo source, double x, double y) => project(source, x, y);

        public override string ToString() => Name;
    }
}
