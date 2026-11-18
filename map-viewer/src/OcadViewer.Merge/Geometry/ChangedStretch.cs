using OcadViewer.Ocd;

namespace OcadViewer.Merge.Geometry;

/// <summary>
/// Where a branch changed an object: the parts of its outline that differ from
/// base, old and new, as straight segments in paper units.
/// </summary>
/// <remarks>
/// <para>An outline is a run of pieces - a straight stretch between two points, or
/// a Bézier curve with its two control points. A piece found unchanged in the other
/// version is left out, so a long contour nudged in one place is changed only
/// there. Both versions' changed pieces count: the new line where it now runs, and
/// the old one where it used to.</para>
/// <para>An object added, deleted, or changed without moving (a new symbol, say)
/// counts whole. So does the inside of such an area, if it is no larger across than
/// a given size: a knoll in a new marsh is near it, but a forest polygon covering
/// the map would make everything near everything.</para>
/// <para>An area's rings are closed: OCAD often does not repeat the first point at
/// the end, and the edge back to it counts like any other.</para>
/// <para>Curves are flattened into short straight pieces, close enough for
/// measuring distances of metres.</para>
/// </remarks>
internal sealed class ChangedStretch
{
    /// <summary>The longest straight piece a curve is flattened into, in paper units (0.1 mm).</summary>
    private const double FlatteningStep = 10;
    private const int MaxCurvePieces = 64;

    private readonly Segment[] segments;
    private readonly (double X, double Y)[][] inside;

    /// <summary>The extent of the changed parts.</summary>
    public Bounds Bounds { get; }

    private ChangedStretch(Segment[] segments, (double X, double Y)[][] inside)
    {
        this.segments = segments;
        this.inside = inside;
        Bounds bounds = Bounds.Empty;
        foreach (Segment s in segments)
            bounds = bounds.Union(s.Bounds);
        Bounds = bounds;
    }

    /// <summary>What changed between <paramref name="before"/> and <paramref name="after"/>; either may be null, not both.</summary>
    /// <param name="insideUpTo">The largest extent, in paper units, of an area whose inside counts.</param>
    public static ChangedStretch Between(OcadObject before, OcadObject after, int insideUpTo)
    {
        if (before is null || after is null)
            return Whole(before ?? after, insideUpTo);

        List<OcadCoord[]> old = Pieces(before), now = Pieces(after);
        HashSet<string> inOld = [.. old.Select(Key)], inNow = [.. now.Select(Key)];
        OcadCoord[][] changed = [.. old.Where(p => !inNow.Contains(Key(p)) && !inNow.Contains(ReverseKey(p))),
                                 .. now.Where(p => !inOld.Contains(Key(p)) && !inOld.Contains(ReverseKey(p)))];

        return changed.Length == 0 ? Whole(after, insideUpTo) : new ChangedStretch([.. changed.SelectMany(Flatten)], []);
    }

    /// <summary>Whether any changed part comes within <paramref name="distance"/> of <paramref name="other"/>'s.</summary>
    public bool IsNear(ChangedStretch other, int distance)
    {
        if (!Bounds.Inflate(distance).Intersects(other.Bounds))
            return false;

        foreach (Segment s in segments) {
            Bounds near = s.Bounds.Inflate(distance);
            foreach (Segment t in other.segments) {
                if (near.Intersects(t.Bounds) && s.DistanceTo(t) <= distance)
                    return true;
            }
        }
        return Encloses(other) || other.Encloses(this);
    }

    private bool Encloses(ChangedStretch other)
        => inside.Length > 0 && other.segments.Any(s => Contains(s.X1, s.Y1));

    /// <summary>Even-odd test over all rings, so holes are outside.</summary>
    private bool Contains(double x, double y)
    {
        bool contains = false;
        foreach ((double X, double Y)[] ring in inside) {
            for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++) {
                (double xi, double yi) = ring[i];
                (double xj, double yj) = ring[j];
                if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
                    contains = !contains;
            }
        }
        return contains;
    }

    private static ChangedStretch Whole(OcadObject o, int insideUpTo)
    {
        ChangedStretch outline = new([.. Pieces(o).SelectMany(Flatten)], []);
        Bounds b = outline.Bounds;
        bool insideCounts = IsArea(o) && !b.IsEmpty && Math.Max(b.MaxX - b.MinX, b.MaxY - b.MinY) <= insideUpTo;
        return insideCounts
            ? new ChangedStretch(outline.segments, [.. ObjectRings.Of(o).Select(r => Points(Pieces(r, closed: true)))])
            : outline;
    }

    private static bool IsArea(OcadObject o) => ObjectRings.KindOf(o) == ShapeKind.Area;

    private static List<OcadCoord[]> Pieces(OcadObject o) => [.. ObjectRings.Of(o).SelectMany(r => Pieces(r, IsArea(o)))];

    /// <summary>
    /// A ring as straight pieces (two points) and curves (anchor, two control points,
    /// anchor); a closed ring also gets the edge from its last point back to its first.
    /// </summary>
    private static IEnumerable<OcadCoord[]> Pieces(OcadCoord[] ring, bool closed)
    {
        if (ring.Length == 1)
            yield return ring;

        int i = 0;
        while (i < ring.Length - 1) {
            if (i + 3 < ring.Length && ring[i + 1].IsControlPoint && ring[i + 2].IsControlPoint && !ring[i + 3].IsControlPoint) {
                yield return ring[i..(i + 4)];
                i += 3;
            }
            else {
                yield return ring[i..(i + 2)];
                i += 1;
            }
        }

        if (closed && ring.Length > 2 && (ring[^1].X != ring[0].X || ring[^1].Y != ring[0].Y))
            yield return [ring[^1], ring[0]];
    }

    private static (double X, double Y)[] Points(IEnumerable<OcadCoord[]> pieces)
        => [.. pieces.SelectMany(Flatten).Select(s => (s.X1, s.Y1))];

    private static IEnumerable<Segment> Flatten(OcadCoord[] piece)
    {
        if (piece.Length == 1) {
            yield return new Segment(piece[0].X, piece[0].Y, piece[0].X, piece[0].Y);
            yield break;
        }
        if (piece.Length == 2) {
            yield return new Segment(piece[0].X, piece[0].Y, piece[1].X, piece[1].Y);
            yield break;
        }

        double length = 0;
        for (int k = 1; k < piece.Length; k++)
            length += Math.Sqrt(Square(piece[k].X - piece[k - 1].X) + Square(piece[k].Y - piece[k - 1].Y));
        int count = Math.Clamp((int)Math.Ceiling(length / FlatteningStep), 1, MaxCurvePieces);

        (double X, double Y) from = (piece[0].X, piece[0].Y);
        for (int k = 1; k <= count; k++) {
            (double X, double Y) to = Bezier(piece, (double)k / count);
            yield return new Segment(from.X, from.Y, to.X, to.Y);
            from = to;
        }
    }

    private static (double X, double Y) Bezier(OcadCoord[] p, double t)
    {
        double u = 1 - t;
        double a = u * u * u, b = 3 * u * u * t, c = 3 * u * t * t, d = t * t * t;
        return (a * p[0].X + b * p[1].X + c * p[2].X + d * p[3].X,
                a * p[0].Y + b * p[1].Y + c * p[2].Y + d * p[3].Y);
    }

    private static string Key(OcadCoord[] piece) => string.Join(';', piece.Select(c => $"{c.X},{c.Y}"));

    private static string ReverseKey(OcadCoord[] piece) => string.Join(';', piece.Reverse().Select(c => $"{c.X},{c.Y}"));

    private static double Square(double v) => v * v;

    private readonly record struct Segment(double X1, double Y1, double X2, double Y2)
    {
        public Bounds Bounds => new((int)Math.Floor(Math.Min(X1, X2)), (int)Math.Floor(Math.Min(Y1, Y2)),
                                    (int)Math.Ceiling(Math.Max(X1, X2)), (int)Math.Ceiling(Math.Max(Y1, Y2)));

        public double DistanceTo(Segment other)
            => Crosses(other) ? 0
             : Math.Min(Math.Min(PointDistance(other.X1, other.Y1), PointDistance(other.X2, other.Y2)),
                        Math.Min(other.PointDistance(X1, Y1), other.PointDistance(X2, Y2)));

        private double PointDistance(double x, double y)
        {
            double dx = X2 - X1, dy = Y2 - Y1;
            double lengthSquared = dx * dx + dy * dy;
            double t = lengthSquared == 0 ? 0 : Math.Clamp(((x - X1) * dx + (y - Y1) * dy) / lengthSquared, 0, 1);
            return Math.Sqrt(Square(x - (X1 + t * dx)) + Square(y - (Y1 + t * dy)));
        }

        private bool Crosses(Segment o)
        {
            double d1 = Side(o.X1, o.Y1), d2 = Side(o.X2, o.Y2), d3 = o.Side(X1, Y1), d4 = o.Side(X2, Y2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private double Side(double x, double y) => (X2 - X1) * (y - Y1) - (Y2 - Y1) * (x - X1);
    }
}
