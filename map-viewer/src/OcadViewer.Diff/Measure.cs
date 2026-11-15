namespace OcadViewer.Diff;

/// <summary>Lengths and areas of objects, in OCAD units.</summary>
/// <remarks>
/// Bezier control points are skipped and the chord between on-curve points is
/// measured instead. That under-reads tight curves a little and is otherwise
/// close; flattening curves properly is a separate piece of work (the Angular
/// app's issue #23) and nothing here pretends to have done it.
/// </remarks>
internal static class Measure
{
    public static double Length(IReadOnlyList<MapPoint> points)
    {
        double length = 0;
        MapPoint? previous = null;

        foreach (MapPoint point in points) {
            if (point.IsCurveControl)
                continue;
            if (previous is { } p)
                length += Distance(p, point);
            previous = point;
        }

        return length;
    }

    /// <summary>The area of the outline less its holes.</summary>
    public static double Area(IReadOnlyList<MapPoint> points)
    {
        double outline = 0, holes = 0;
        List<MapPoint> ring = [];
        bool inHole = false;

        foreach (MapPoint point in points) {
            if (point.StartsHole) {
                Close();
                inHole = true;
            }
            if (!point.IsCurveControl)
                ring.Add(point);
        }
        Close();

        return Math.Max(0, outline - holes);

        void Close()
        {
            double area = Math.Abs(Shoelace(ring));
            if (inHole) holes += area; else outline += area;
            ring.Clear();
        }
    }

    /// <summary>
    /// The box around every point, curve handles included, in paper millimetres.
    /// A Bezier curve stays inside its control points, so this is never too small.
    /// </summary>
    /// <returns><c>null</c> for an object with no points.</returns>
    public static PaperBounds Bounds(IReadOnlyList<MapPoint> points)
    {
        if (points.Count == 0)
            return null;

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (MapPoint p in points) {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }

        // OCAD units are hundredths of a millimetre, as in PurplePen's importer.
        return new PaperBounds(minX / 100.0, minY / 100.0, maxX / 100.0, maxY / 100.0);
    }

    public static double Distance(MapPoint a, MapPoint b)
    {
        double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Shoelace(List<MapPoint> ring)
    {
        double twiceArea = 0;
        for (int i = 0; i < ring.Count; i++) {
            MapPoint a = ring[i], b = ring[(i + 1) % ring.Count];
            twiceArea += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return twiceArea / 2;
    }
}
