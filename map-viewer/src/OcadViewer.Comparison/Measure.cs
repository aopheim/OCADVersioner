using OcadViewer.Ocd;

namespace OcadViewer.Comparison;

/// <summary>Lengths, areas and distances of objects, on the ground.</summary>
/// <remarks>
/// Bézier control points are skipped and the chord between points on the curve is
/// measured instead. That under-reads tight curves a little and is otherwise close;
/// flattening curves properly is a separate piece of work.
/// </remarks>
public static class Measure
{
    /// <summary>The length of a line in metres, or null for anything that is not a line.</summary>
    public static double? LengthInMeters(OcadObject o, double scale)
        => o.Type == ObjectType.Line ? LengthInUnits(o.Coords) * Ground.MetresPerUnit(scale) : null;

    /// <summary>The area of an area object less its holes, in square metres, or null for anything else.</summary>
    public static double? AreaInSquareMeters(OcadObject o, double scale)
    {
        if (o.Type != ObjectType.Area)
            return null;

        double perUnit = Ground.MetresPerUnit(scale);
        return AreaInUnits(o.Coords) * perUnit * perUnit;
    }

    /// <summary>
    /// How far a point object moved, in metres on the ground at the newer version's
    /// scale, or null unless both versions are point objects.
    /// </summary>
    public static double? MovementInMeters(OcadObject older, OcadObject newer, double scale)
        => older.Type == ObjectType.Point && newer.Type == ObjectType.Point
           && older.Coords.Count > 0 && newer.Coords.Count > 0
            ? Distance(older.Coords[0], newer.Coords[0]) * Ground.MetresPerUnit(scale)
            : null;

    /// <summary>
    /// The change from <paramref name="before"/> to <paramref name="after"/> in
    /// percent, or null when there is nothing to divide by.
    /// </summary>
    public static double? PercentChange(double? before, double? after)
        => before is > 0 && after is not null ? (after.Value - before.Value) / before.Value * 100 : null;

    /// <summary>The box around every coordinate, Bézier handles included, so a curve is never outside it.</summary>
    public static Bounds BoundsOf(OcadObject o) => Bounds.Of(o.Coords);

    internal static double LengthInUnits(IReadOnlyList<OcadCoord> coords)
    {
        double length = 0;
        OcadCoord? previous = null;

        foreach (OcadCoord c in coords) {
            if (c.IsControlPoint)
                continue;
            if (previous is { } p)
                length += Distance(p, c);
            previous = c;
        }

        return length;
    }

    internal static double AreaInUnits(IReadOnlyList<OcadCoord> coords)
    {
        double outline = 0, holes = 0;
        List<OcadCoord> ring = [];
        bool inHole = false;

        foreach (OcadCoord c in coords) {
            if (c.StartsHole) {
                Close();
                inHole = true;
            }
            if (!c.IsControlPoint)
                ring.Add(c);
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

    private static double Distance(OcadCoord a, OcadCoord b)
    {
        double dx = (double)b.X - a.X, dy = (double)b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Shoelace(List<OcadCoord> ring)
    {
        double twiceArea = 0;
        for (int i = 0; i < ring.Count; i++) {
            OcadCoord a = ring[i], b = ring[(i + 1) % ring.Count];
            twiceArea += (double)a.X * b.Y - (double)b.X * a.Y;
        }
        return twiceArea / 2;
    }
}
