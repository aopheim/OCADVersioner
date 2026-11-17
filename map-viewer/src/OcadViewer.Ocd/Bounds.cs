namespace OcadViewer.Ocd;

/// <summary>An axis-aligned rectangle in hundredths of a millimetre on paper.</summary>
public readonly record struct Bounds(int MinX, int MinY, int MaxX, int MaxY)
{
    public static readonly Bounds Empty = new(int.MaxValue, int.MaxValue, int.MinValue, int.MinValue);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    public Bounds Union(Bounds other)
        => IsEmpty ? other
         : other.IsEmpty ? this
         : new(Math.Min(MinX, other.MinX), Math.Min(MinY, other.MinY),
               Math.Max(MaxX, other.MaxX), Math.Max(MaxY, other.MaxY));

    public Bounds Inflate(int by)
        => IsEmpty ? this : new(MinX - by, MinY - by, MaxX + by, MaxY + by);

    public bool Intersects(Bounds other)
        => !IsEmpty && !other.IsEmpty
        && MinX <= other.MaxX && other.MinX <= MaxX
        && MinY <= other.MaxY && other.MinY <= MaxY;

    public static Bounds Of(IEnumerable<OcadCoord> coords)
    {
        Bounds bounds = Empty;
        foreach (OcadCoord c in coords)
            bounds = bounds.Union(new Bounds(c.X, c.Y, c.X, c.Y));
        return bounds;
    }
}
