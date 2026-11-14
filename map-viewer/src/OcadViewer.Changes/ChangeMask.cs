namespace OcadViewer.Changes;

/// <summary>
/// Where two versions of a map differ, as a raster: one byte per pixel, zero
/// where they agree.
/// </summary>
/// <remarks>
/// The only thing this type asserts about those bytes is that zero means
/// unchanged. What a non-zero byte says about the <em>kind</em> of change is the
/// renderer's business and no use here: a region is somewhere to go and look,
/// not a verdict.
///
/// <para>It also owns the one awkward fact about a mask, which is that its rows
/// run downwards from the top of the sheet while paper Y runs up. Everything
/// above it works in millimetres and never needs to know.</para>
/// </remarks>
public sealed class ChangeMask
{
    /// <param name="pixels">One byte per pixel, row by row from the top.</param>
    /// <param name="width">Mask width in pixels.</param>
    /// <param name="height">Mask height in pixels.</param>
    /// <param name="area">The rectangle of the sheet these pixels cover, exactly.</param>
    public ChangeMask(byte[] pixels, int width, int height, PaperRectangle area)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(area);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (pixels.Length != (long)width * height) {
            throw new ArgumentException(
                $"A {width} by {height} mask needs {(long)width * height} bytes, not {pixels.Length}.",
                nameof(pixels));
        }

        if (area.Width <= 0 || area.Height <= 0)
            throw new ArgumentException($"A mask cannot cover an empty rectangle ({area}).", nameof(area));

        Pixels = pixels;
        Width = width;
        Height = height;
        Area = area;
    }

    public byte[] Pixels { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The part of the sheet the mask covers.</summary>
    public PaperRectangle Area { get; }

    /// <summary>
    /// The mask's resolution.
    /// </summary>
    /// <remarks>
    /// Read off the horizontal axis. Masks are square-pixelled - both edges are
    /// scaled by one factor when they are planned - so the vertical figure is
    /// the same to within a rounded pixel.
    /// </remarks>
    public double PixelsPerMillimetre => Width / Area.Width;

    /// <summary>The sheet rectangle covered by a block of pixels, ends included.</summary>
    public PaperRectangle PaperFor(int left, int top, int right, int bottom)
        => new(X(left), Y(bottom + 1), X(right + 1), Y(top));

    private double X(int column) => Area.MinX + (double)column / Width * Area.Width;

    // Downwards: row zero is the top of the sheet, which is the largest Y.
    private double Y(int row) => Area.MaxY - (double)row / Height * Area.Height;
}
