using System;
using System.Collections.Generic;
using System.Drawing;

using DotSpatial.Projections;

using PurplePen.MapModel;

namespace OcadViewer.Core
{
    /// <summary>
    /// Where a point on the paper is on a tile grid: an affine transform from paper
    /// millimetres to the grid's metres.
    /// </summary>
    /// <remarks>
    /// <c>gridX = A·x + B·y + C</c> and <c>gridY = D·x + E·y + F</c>, with paper Y
    /// running up and grid Y running north. One affine transform per map is not
    /// exact, and how far out it is depends on the grid:
    /// <list type="bullet">
    /// <item>Web Mercator's scale grows with latitude, by about 0.14% across two
    /// kilometres at 63°N, so over a 2 km sprint map the far corner is about
    /// 1.25 m out on the ground - 0.3 mm of paper at 1:4000.</item>
    /// <item>From one UTM zone to the next both grids are conformal and bend the
    /// same way, so the error is a few centimetres.</item>
    /// </list>
    /// Close enough to lay a background web map under the map, which is all this
    /// is for; it is not a survey.
    /// </remarks>
    public sealed record PaperTransform(double A, double B, double C, double D, double E, double F)
    {
        public (double X, double Y) Apply(double x, double y) => (A * x + B * y + C, D * x + E * y + F);

        /// <summary>
        /// Whether a web map can be laid out by this: every number finite, and the
        /// paper not squashed to a line or a point, so the transform can be inverted.
        /// </summary>
        /// <remarks>
        /// A projection can give NaN or infinity for a point past the poles or in the
        /// wrong zone, and a grid scale factor of 0 gives a transform with no inverse.
        /// Either would draw nonsense, and NaN cannot even be sent to the page as JSON.
        /// </remarks>
        public bool IsUsable
        {
            get
            {
                double det = A * E - B * D;
                return double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C)
                    && double.IsFinite(D) && double.IsFinite(E) && double.IsFinite(F)
                    && double.IsFinite(det) && det != 0;
            }
        }
    }

    /// <summary>
    /// The map's georeference, as read from the file, turned into something a web
    /// map can use. Nothing here needs anyone to type anything in: OCAD stores the
    /// grid, its offset, the rotation and the scale in the file.
    /// </summary>
    /// <remarks>
    /// The background maps come on more than one tile grid, so the paper is placed
    /// on each of them. The grid names are the ones the page's background sources
    /// ask for (<c>backgroundMap.js</c>); a new grid there is a new entry here.
    /// </remarks>
    public static class Georeference
    {
        /// <summary>Web Mercator: OpenStreetMap, Kartverket's cache and most web maps.</summary>
        public const string WebMercator = "EPSG:3857";

        /// <summary>UTM zone 33 on WGS84: mapant.no.</summary>
        public const string Utm33 = "EPSG:32633";

        /// <summary>Every grid the paper is placed on.</summary>
        public static readonly IReadOnlyList<string> Grids = [WebMercator, Utm33];

        /// <summary>The sphere Web Mercator is drawn on.</summary>
        private const double MercatorRadius = 6378137.0;

        private static readonly ProjectionInfo Wgs84 =
            ProjectionInfo.FromProj4String("+proj=longlat +ellps=WGS84 +datum=WGS84 +no_defs");

        private static readonly ProjectionInfo Utm33Wgs84 =
            ProjectionInfo.FromProj4String("+proj=utm +zone=33 +datum=WGS84 +units=m +no_defs");

        /// <summary>
        /// Paper millimetres to each tile grid for a map, keyed by grid name, or
        /// <c>null</c> when the map has no georeference this can use: real-world
        /// coordinates are off, the grid is one with no known projection (a local
        /// grid, say), or it projects to somewhere no web map is.
        /// </summary>
        /// <remarks>
        /// Never throws. The background map is optional; a georeference it cannot
        /// use must not stop the map itself from opening. A grid the map cannot be
        /// placed on is left out, and the others are still given.
        /// </remarks>
        /// <param name="bounds">The drawn map, in paper millimetres. The transforms are fitted over it.</param>
        public static IReadOnlyDictionary<string, PaperTransform> Of(Map map, RectangleF bounds)
        {
            RealWorldCoords coords;
            float mapScale;
            using (map.Read()) {
                coords = map.RealWorldCoords;
                mapScale = map.MapScale;
            }

            if (coords is null || !coords.RealWorldOn || string.IsNullOrWhiteSpace(coords.Proj4String))
                return null;

            ProjectionInfo source;
            try {
                source = ProjectionInfo.FromProj4String(coords.Proj4String);
            }
            catch (Exception) {
                return null;   // A projection DotSpatial cannot read is the same as none.
            }

            double scale = coords.GridScaleFactor * mapScale / 1000.0;
            var transforms = new Dictionary<string, PaperTransform>();

            foreach (string target in Grids) {
                try {
                    PaperTransform fitted = Fit(bounds, (x, y) => {
                        (double gx, double gy) = PaperToGrid(x, y, scale, coords.RealWorldAngle,
                                                             coords.RealWorldOffsetX, coords.RealWorldOffsetY);
                        return GridTo(target, source, gx, gy);
                    });
                    if (fitted.IsUsable)
                        transforms.Add(target, fitted);
                }
                catch (Exception) {
                    // DotSpatial could read the projection but not apply it: leave this grid out.
                }
            }

            return transforms.Count > 0 ? transforms : null;
        }

        /// <summary>A point on the map's grid, in metres, to one of the tile grids.</summary>
        /// <param name="target">One of <see cref="Grids"/>.</param>
        /// <param name="proj4">The map's grid, as a proj4 string.</param>
        public static (double X, double Y) GridTo(string target, string proj4, double x, double y)
            => GridTo(target, ProjectionInfo.FromProj4String(proj4), x, y);

        private static (double X, double Y) GridTo(string target, ProjectionInfo source, double x, double y)
        {
            double[] xy = [x, y];
            switch (target) {
                case WebMercator:
                    Reproject.ReprojectPoints(xy, [0.0], source, Wgs84, 0, 1);
                    return LonLatToMercator(xy[0], xy[1]);
                case Utm33:
                    Reproject.ReprojectPoints(xy, [0.0], source, Utm33Wgs84, 0, 1);
                    return (xy[0], xy[1]);
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, "Not a grid background maps are drawn on.");
            }
        }

        /// <summary>
        /// Paper millimetres to the map's grid in metres, as OCAD and Purple Pen
        /// do it (<c>Map.MapToRealWorldTransform</c>): scale, rotate by minus the
        /// angle, then move to the offset.
        /// </summary>
        /// <remarks>
        /// In doubles here because the offset alone is seven digits of metres, and
        /// Purple Pen's float matrix loses about half a metre at that size.
        /// </remarks>
        /// <param name="metresPerMm">Ground metres per paper millimetre: the scale over 1000, times any grid scale factor.</param>
        /// <param name="angleDegrees">The map's rotation from grid north, as OCAD stores it.</param>
        public static (double X, double Y) PaperToGrid(double x, double y, double metresPerMm,
                                                       double angleDegrees, double offsetX, double offsetY)
        {
            double theta = -angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            double sx = x * metresPerMm, sy = y * metresPerMm;
            return (offsetX + sx * cos - sy * sin, offsetY + sx * sin + sy * cos);
        }

        /// <summary>Longitude and latitude in degrees to Web Mercator metres.</summary>
        public static (double X, double Y) LonLatToMercator(double lon, double lat)
        {
            double x = MercatorRadius * lon * Math.PI / 180.0;
            double y = MercatorRadius * Math.Log(Math.Tan(Math.PI / 4 + lat * Math.PI / 360.0));
            return (x, y);
        }

        /// <summary>
        /// The affine transform that agrees with <paramref name="project"/> at three
        /// corners of <paramref name="bounds"/>.
        /// </summary>
        /// <remarks>
        /// Corners rather than one point and its neighbours, so any curvature the
        /// projection has is spread across the map instead of growing towards one
        /// edge. A map with no extent is widened to a square millimetre so the
        /// three points are never the same.
        /// </remarks>
        public static PaperTransform Fit(RectangleF bounds, Func<double, double, (double X, double Y)> project)
        {
            double x0 = bounds.Left, y0 = bounds.Top;
            double w = Math.Max(bounds.Width, 1.0), h = Math.Max(bounds.Height, 1.0);

            (double X, double Y) p0 = project(x0, y0);
            (double X, double Y) px = project(x0 + w, y0);
            (double X, double Y) py = project(x0, y0 + h);

            double a = (px.X - p0.X) / w, b = (py.X - p0.X) / h;
            double d = (px.Y - p0.Y) / w, e = (py.Y - p0.Y) / h;

            return new PaperTransform(a, b, p0.X - a * x0 - b * y0,
                                      d, e, p0.Y - d * x0 - e * y0);
        }
    }
}
