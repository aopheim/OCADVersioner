using System;

using DotSpatial.Projections;

using PurplePen.MapModel;

namespace OcadViewer.Core
{
    /// <summary>
    /// Where a map's paper lies on the ground, as Web Mercator metres: the
    /// coordinates the viewer shows every map in, so that two maps - two versions,
    /// or two different maps at different scales - are drawn over the same place
    /// and the same web map lies under both.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exact both ways, point by point: paper to the map's own grid as OCAD does it
    /// (<see cref="Georeference.PaperToGrid"/>), the grid to longitude and latitude
    /// through the map's projection, and Web Mercator's closed form from there. Each
    /// screen tile is then drawn through an affine transform fitted to its own three
    /// corners, so nothing is approximated over more than a tile.
    /// </para>
    /// <para>
    /// <see cref="Paper"/> is the placement for a map that cannot be put on the
    /// ground: one millimetre of paper to one Web Mercator metre, at the origin.
    /// Two maps both placed that way line up on the paper, as they always did.
    /// </para>
    /// </remarks>
    public sealed class GroundPlacement
    {
        /// <summary>The sphere Web Mercator is drawn on.</summary>
        public const double MercatorRadius = 6378137.0;

        /// <summary>Paper millimetres taken as Web Mercator metres, for a map with no usable georeference.</summary>
        public static readonly GroundPlacement Paper = new(null, 1, 0, 0, 0);

        private readonly ProjectionInfo grid;
        private readonly double metresPerMm;
        private readonly double angleDegrees;
        private readonly double offsetX;
        private readonly double offsetY;

        private GroundPlacement(ProjectionInfo grid, double metresPerMm, double angleDegrees,
                                double offsetX, double offsetY)
        {
            this.grid = grid;
            this.metresPerMm = metresPerMm;
            this.angleDegrees = angleDegrees;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
        }

        /// <summary>Whether this places the paper on the real ground, rather than at the origin as paper.</summary>
        public bool IsOnGround => grid is not null;

        /// <summary>
        /// The placement a map's georeference gives, or <c>null</c> when it has none
        /// that can be used: real-world coordinates off, no projection, a projection
        /// DotSpatial cannot read, or one that does not reach the ground near
        /// <paramref name="probeX"/>, <paramref name="probeY"/>.
        /// </summary>
        /// <param name="probeX">A point on the map's paper, in millimetres, to check the placement with.</param>
        /// <param name="probeY">Likewise.</param>
        /// <remarks>Never throws: a map the viewer cannot place still opens, on paper.</remarks>
        public static GroundPlacement Of(Map map, double probeX, double probeY)
        {
            RealWorldCoords coords;
            float mapScale;
            using (map.Read()) {
                coords = map.RealWorldCoords;
                mapScale = map.MapScale;
            }

            if (coords is null || !coords.RealWorldOn || string.IsNullOrWhiteSpace(coords.Proj4String))
                return null;

            return Of(coords.Proj4String, coords.GridScaleFactor * mapScale / 1000.0, coords.RealWorldAngle,
                      coords.RealWorldOffsetX, coords.RealWorldOffsetY, probeX, probeY);
        }

        /// <summary>
        /// A placement from the parts of a georeference, or <c>null</c> when they
        /// do not place the probe point anywhere it can be brought back from.
        /// </summary>
        /// <param name="proj4">The map's grid.</param>
        /// <param name="metresPerMm">Grid metres per paper millimetre: the scale over 1000, times any grid scale factor.</param>
        /// <param name="angleDegrees">The map's rotation from grid north, as OCAD stores it.</param>
        public static GroundPlacement Of(string proj4, double metresPerMm, double angleDegrees,
                                         double offsetX, double offsetY, double probeX = 0, double probeY = 0)
        {
            if (!double.IsFinite(metresPerMm) || metresPerMm <= 0)
                return null;

            try {
                var placement = new GroundPlacement(ProjectionInfo.FromProj4String(proj4), metresPerMm, angleDegrees,
                                                    offsetX, offsetY);

                (double gx, double gy) = placement.ToGround(probeX, probeY);
                (double px, double py) = placement.ToPaper(gx, gy);
                bool roundTrips = double.IsFinite(gx) && double.IsFinite(gy)
                                  && Math.Abs(px - probeX) < 0.01 && Math.Abs(py - probeY) < 0.01;
                return roundTrips ? placement : null;
            }
            catch (Exception) {
                return null;   // A projection DotSpatial cannot read or apply is the same as none.
            }
        }

        /// <summary>A point on the paper, in millimetres, to Web Mercator metres.</summary>
        public (double X, double Y) ToGround(double x, double y)
        {
            if (grid is null)
                return (x, y);

            (double gx, double gy) = Georeference.PaperToGrid(x, y, metresPerMm, angleDegrees, offsetX, offsetY);
            double[] xy = [gx, gy];
            Reproject.ReprojectPoints(xy, [0.0], grid, Georeference.Wgs84.Value, 0, 1);
            return Georeference.LonLatToMercator(xy[0], xy[1]);
        }

        /// <summary>A point in Web Mercator metres to the paper, in millimetres.</summary>
        public (double X, double Y) ToPaper(double x, double y)
        {
            if (grid is null)
                return (x, y);

            (double lon, double lat) = MercatorToLonLat(x, y);
            double[] xy = [lon, lat];
            Reproject.ReprojectPoints(xy, [0.0], Georeference.Wgs84.Value, grid, 0, 1);

            // Georeference.PaperToGrid run backwards: off the offset, turn back, unscale.
            double theta = -angleDegrees * Math.PI / 180.0;
            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            double sx = xy[0] - offsetX, sy = xy[1] - offsetY;
            return ((sx * cos + sy * sin) / metresPerMm, (-sx * sin + sy * cos) / metresPerMm);
        }

        /// <summary>Web Mercator metres to longitude and latitude in degrees.</summary>
        public static (double Lon, double Lat) MercatorToLonLat(double x, double y)
            => (x / MercatorRadius * 180.0 / Math.PI,
                (2 * Math.Atan(Math.Exp(y / MercatorRadius)) - Math.PI / 2) * 180.0 / Math.PI);
    }
}
