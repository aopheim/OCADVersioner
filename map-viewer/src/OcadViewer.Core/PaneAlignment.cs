using System.Drawing;

namespace OcadViewer.Core
{
    /// <summary>
    /// How far apart two versions sit on the paper, so the split view can show
    /// them side by side over the same area.
    /// </summary>
    /// <remarks>
    /// A mapper moves a map around on the paper between versions - a new frame,
    /// a reorganised sheet - and the two panes, which share paper millimetres,
    /// then look at different places. Lining up the middles of the two maps'
    /// drawn areas brings them back over the same ground.
    ///
    /// The middle of the drawn area, not the georeference: it needs nothing but
    /// the objects, so it works for every map, georeferenced or not. It is not
    /// exact - an object added or removed at the edge moves the middle by half
    /// its overhang - which is fine for putting two panes over the same area,
    /// and is all this is for.
    /// </remarks>
    public static class PaneAlignment
    {
        /// <summary>
        /// What to add to a point on the <paramref name="from"/> map's paper to get
        /// the matching point on the <paramref name="to"/> map's paper, in
        /// millimetres: the difference between the two rectangles' centres.
        /// </summary>
        /// <param name="from">The drawn area of the map the point is on.</param>
        /// <param name="to">The drawn area of the map the point goes to.</param>
        public static (double X, double Y) Offset(RectangleF from, RectangleF to)
            => (Centre(to.Left, to.Width) - Centre(from.Left, from.Width),
                Centre(to.Top, to.Height) - Centre(from.Top, from.Height));

        /// <summary>In doubles: paper offsets run to hundreds of millimetres, and float halves of them lose the last digits.</summary>
        private static double Centre(float start, float length) => (double)start + (double)length / 2;
    }
}
