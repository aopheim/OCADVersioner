namespace OcadViewer.Ocd;

/// <summary>
/// One OCAD coordinate: a position in hundredths of a millimetre on paper, plus
/// the flags OCAD keeps in the low byte of each axis.
/// </summary>
/// <remarks>
/// OCAD stores each axis as an int32 whose top 24 bits are the signed value and
/// whose bottom 8 bits are flags: on X, bit 0 and 1 mark Bézier control points;
/// on Y, bit 1 marks the first point of a hole in an area.
/// </remarks>
public readonly record struct OcadCoord(int X, int Y, byte XFlags, byte YFlags)
{
    internal const int Size = 8;

    /// <summary>Whether this is a Bézier control point rather than a point on the line.</summary>
    public bool IsControlPoint => (XFlags & 0b11) != 0;

    /// <summary>Whether this point starts a hole in an area object.</summary>
    public bool StartsHole => (YFlags & 0b10) != 0;

    internal void Write(Span<byte> bytes)
    {
        BitConverter.TryWriteBytes(bytes, (X << 8) | XFlags);
        BitConverter.TryWriteBytes(bytes[4..], (Y << 8) | YFlags);
    }

    internal static OcadCoord Read(ReadOnlySpan<byte> bytes)
    {
        int x = BitConverter.ToInt32(bytes);
        int y = BitConverter.ToInt32(bytes[4..]);
        return new OcadCoord(x >> 8, y >> 8, (byte)(x & 0xFF), (byte)(y & 0xFF));
    }
}
