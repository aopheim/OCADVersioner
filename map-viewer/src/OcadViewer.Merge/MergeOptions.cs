namespace OcadViewer.Merge;

/// <summary>The merge's adjustable thresholds, in metres on the ground.</summary>
/// <remarks>
/// Mappers think in metres, files store hundredths of a millimetre on paper; the
/// map scale converts one into the other. Conversions round down, so a threshold
/// is never looser than asked for.
/// </remarks>
public sealed record MergeOptions
{
    public static readonly MergeOptions Default = new();

    /// <summary>Vertex moves up to this far are not changes. Absorbs rounding when OCAD re-saves.</summary>
    public double ToleranceMeters { get; init; } = 0.1;

    /// <summary>A Kari change and an Ola change closer than this are a spatial-overlap conflict.</summary>
    public double OverlapBufferMeters { get; init; } = 10;

    /// <summary>
    /// An area added, deleted or restyled is near what lies inside it, if it is no larger
    /// across than this. Larger areas count by their outline only, so one big polygon
    /// does not join everything in it into one overlap.
    /// </summary>
    public double OverlapInsideAreaMeters { get; init; } = 100;

    public int ToleranceUnits(double scale) => ToUnits(ToleranceMeters, scale);

    public int OverlapBufferUnits(double scale) => ToUnits(OverlapBufferMeters, scale);

    public int OverlapInsideAreaUnits(double scale) => ToUnits(OverlapInsideAreaMeters, scale);

    /// <summary>Metres on the ground to hundredths of a millimetre on paper.</summary>
    public static int ToUnits(double meters, double scale)
        => (int)Math.Floor(meters * 100_000 / scale + 1e-9);
}
