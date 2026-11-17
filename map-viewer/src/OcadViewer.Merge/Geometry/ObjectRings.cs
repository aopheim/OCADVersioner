using OcadViewer.Ocd;

namespace OcadViewer.Merge.Geometry;

/// <summary>
/// How an object's coordinates make a shape: what kind of shape, and its rings.
/// The one reading of coordinates that drawing (<see cref="ObjectShape"/>) and
/// measuring (<see cref="ChangedStretch"/>) both build on.
/// </summary>
internal static class ObjectRings
{
    public static ShapeKind KindOf(OcadObject o) => o.Type switch {
        ObjectType.Area or ObjectType.Rectangle => ShapeKind.Area,
        ObjectType.Line or ObjectType.LineText => ShapeKind.Line,
        _ => ShapeKind.Point,
    };

    /// <summary>
    /// A point's position; a line's coordinates; an area's outer ring followed by its
    /// holes, each starting where a coordinate is marked as starting a hole. Control
    /// points are kept; rings are as stored, closed or not.
    /// </summary>
    public static IReadOnlyList<OcadCoord[]> Of(OcadObject o)
    {
        if (KindOf(o) == ShapeKind.Point)
            return o.Coords.Count > 0 ? [[o.Coords[0]]] : [];

        List<OcadCoord[]> rings = [];
        List<OcadCoord> ring = [];
        foreach (OcadCoord c in o.Coords) {
            if (c.StartsHole && ring.Count > 0) {
                rings.Add([.. ring]);
                ring = [];
            }
            ring.Add(c);
        }
        if (ring.Count > 0)
            rings.Add([.. ring]);
        return rings;
    }
}
