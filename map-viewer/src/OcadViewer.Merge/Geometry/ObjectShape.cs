using OcadViewer.Ocd;

namespace OcadViewer.Merge.Geometry;

public enum ShapeKind { Point, Line, Area }

/// <summary>
/// An object's outline in paper millimetres - the units the map viewer already
/// draws in (Leaflet's L.CRS.Simple, longitude = X, latitude = Y).
/// </summary>
/// <param name="Parts">A point's single position; a line's vertices; an area's
/// outer ring followed by its holes.</param>
public sealed record Shape(ShapeKind Kind, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Parts);

/// <summary>Outlines of objects, for drawing changes over the map and for picking them.</summary>
/// <remarks>
/// Bézier control points are dropped and the curve drawn through its anchor
/// points: close enough to recognise a change, which is all an outline is for.
/// </remarks>
public static class ObjectShape
{
    private const double MillimetresPerUnit = 0.01;

    public static Shape Of(OcadObject o)
    {
        List<List<(double, double)>> parts = [[]];
        foreach (OcadCoord c in o.Coords) {
            if (c.IsControlPoint)
                continue;
            if (c.StartsHole && parts[^1].Count > 0)
                parts.Add([]);
            parts[^1].Add(Millimetres(c));
        }

        ShapeKind kind = o.Type switch {
            ObjectType.Area or ObjectType.Rectangle => ShapeKind.Area,
            ObjectType.Line or ObjectType.LineText => ShapeKind.Line,
            _ => ShapeKind.Point,
        };

        if (kind == ShapeKind.Point)
            parts = [[.. parts[0].Take(1)]];

        return new Shape(kind, parts.Where(p => p.Count > 0).Select(p => (IReadOnlyList<(double, double)>)p).ToArray());
    }

    public static (double X, double Y) Millimetres(OcadCoord c) => (c.X * MillimetresPerUnit, c.Y * MillimetresPerUnit);

    public static (double MinX, double MinY, double MaxX, double MaxY) Millimetres(Bounds b)
        => (b.MinX * MillimetresPerUnit, b.MinY * MillimetresPerUnit, b.MaxX * MillimetresPerUnit, b.MaxY * MillimetresPerUnit);
}
