using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Format;
using OcadViewer.Merge.Geometry;

namespace OcadViewer.Merge.Regions;

/// <summary>Where an item lies relative to a region.</summary>
public enum Placement { Outside, Crossing, Inside }

/// <summary>
/// A polygon the user drew over the map, in paper millimetres. Regions are a way
/// of deciding many changes at once; they are never written to the map file.
/// </summary>
public sealed class Region(IReadOnlyList<(double X, double Y)> ring)
{
    public IReadOnlyList<(double X, double Y)> Ring { get; } = ring.Count >= 3
        ? ring
        : throw new ArgumentException("A region needs at least three corners.", nameof(ring));

    /// <summary>
    /// Inside when every version of the item lies wholly inside; outside when none
    /// touches it; crossing otherwise - the user decides those one by one.
    /// </summary>
    public Placement Classify(ObjectItem item)
    {
        Placement[] placements = [.. new[] { item.Base, item.A, item.B }.Where(o => o is not null).Select(Classify)];
        return placements.All(p => p == Placement.Inside) ? Placement.Inside
             : placements.All(p => p == Placement.Outside) ? Placement.Outside
             : Placement.Crossing;
    }

    public Placement Classify(OcadObject obj)
    {
        Shape shape = ObjectShape.Of(obj);
        (double X, double Y)[] points = [.. shape.Parts.SelectMany(p => p)];
        if (points.Length == 0)
            return Placement.Outside;

        int inside = points.Count(Contains);
        if (inside > 0 && inside < points.Length)
            return Placement.Crossing;

        bool edgesCross = shape.Kind != ShapeKind.Point && shape.Parts.Any(part => Segments(part, shape.Kind == ShapeKind.Area).Any(CrossesBorder));
        if (edgesCross)
            return Placement.Crossing;

        if (inside == points.Length)
            return Placement.Inside;

        // No vertex inside and no edge crossing: outside - unless the region sits
        // wholly inside an area, which an area's outline would otherwise miss.
        return shape.Kind == ShapeKind.Area && ContainsPoint(shape.Parts[0], Ring[0]) ? Placement.Crossing : Placement.Outside;
    }

    public bool Contains((double X, double Y) p) => ContainsPoint(Ring, p);

    private bool CrossesBorder(((double X, double Y) A, (double X, double Y) B) segment)
        => Segments(Ring, closed: true).Any(edge => Intersect(segment.A, segment.B, edge.A, edge.B));

    private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Segments(IReadOnlyList<(double X, double Y)> points, bool closed)
    {
        for (int i = 0; i + 1 < points.Count; i++)
            yield return (points[i], points[i + 1]);
        if (closed && points.Count > 2)
            yield return (points[^1], points[0]);
    }

    /// <summary>Even-odd ray casting.</summary>
    private static bool ContainsPoint(IReadOnlyList<(double X, double Y)> ring, (double X, double Y) p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) {
            (double xi, double yi) = ring[i];
            (double xj, double yj) = ring[j];
            if ((yi > p.Y) != (yj > p.Y) && p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)
                inside = !inside;
        }
        return inside;
    }

    private static bool Intersect((double X, double Y) p1, (double X, double Y) p2, (double X, double Y) q1, (double X, double Y) q2)
    {
        double d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Cross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
