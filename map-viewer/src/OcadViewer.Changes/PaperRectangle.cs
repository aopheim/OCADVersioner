namespace OcadViewer.Changes;

/// <summary>
/// A rectangle of the map, in paper millimetres.
/// </summary>
/// <remarks>
/// Paper coordinates, as the map file states them: X to the right, Y upwards.
/// Not pixels and not latitude and longitude - the viewer's business is the
/// sheet, and the one place the two meet is <see cref="ChangeMask"/>.
/// </remarks>
public sealed record PaperRectangle(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;

    /// <summary>The same rectangle with a margin on every side.</summary>
    public PaperRectangle Grown(double margin)
        => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

    /// <summary>
    /// The smallest rectangle holding all of these, or <c>null</c> if there are none.
    /// </summary>
    public static PaperRectangle Covering(IEnumerable<PaperRectangle> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        PaperRectangle all = null;

        foreach (PaperRectangle part in parts) {
            all = all is null
                ? part
                : new PaperRectangle(
                    Math.Min(all.MinX, part.MinX), Math.Min(all.MinY, part.MinY),
                    Math.Max(all.MaxX, part.MaxX), Math.Max(all.MaxY, part.MaxY));
        }

        return all;
    }
}
