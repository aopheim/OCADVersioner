using OcadViewer.Merge.Format;

namespace OcadViewer.Merge.Matching;

/// <summary>
/// Decides whether two versions of the same thing are the same: the definition
/// of "changed" for objects, symbols and parameter strings.
/// </summary>
/// <remarks>
/// Bytes are compared, except for what OCAD rewrites without the user changing
/// anything: an object's dates; the colour field of an object that has a symbol
/// (only graphic objects are drawn in it, yet OCAD sets it on symbol objects too);
/// and a symbol's selection flag, symbol-box status and own file position.
/// Coordinates may differ by the tolerance.
/// </remarks>
public sealed class ContentComparer(int toleranceUnits)
{
    public int ToleranceUnits { get; } = toleranceUnits;

    public bool Same(OcadObject a, OcadObject b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a.Symbol != b.Symbol || a.Type != b.Type || a.Status != b.Status || a.Coords.Count != b.Coords.Count)
            return false;

        FileLayout layout = a.Layout;
        if (layout.ObjectHeaderSize != b.Layout.ObjectHeaderSize)
            return false;

        ReadOnlySpan<byte> ra = a.UsedRecord, rb = b.UsedRecord;
        if (ra.Length != rb.Length)
            return false;

        bool hasSymbol = a.Symbol > 0;
        for (int i = 0; i < layout.ObjectHeaderSize; i++) {
            if (ra[i] != rb[i] && !IsDate(layout, i) && !(hasSymbol && InRange(i, layout.ObjectColorOffset, 4)))
                return false;
        }

        for (int i = 0; i < a.Coords.Count; i++) {
            OcadCoord ca = a.Coords[i], cb = b.Coords[i];
            if (ca.XFlags != cb.XFlags || ca.YFlags != cb.YFlags
                || Math.Abs(ca.X - cb.X) > ToleranceUnits || Math.Abs(ca.Y - cb.Y) > ToleranceUnits)
                return false;
        }

        int texts = layout.ObjectHeaderSize + a.Coords.Count * OcadCoord.Size;
        return ra[texts..].SequenceEqual(rb[texts..]);
    }

    public bool Same(OcadSymbol a, OcadSymbol b)
    {
        ReadOnlySpan<byte> ra = a.Record.Span, rb = b.Record.Span;
        if (ra.Length != rb.Length)
            return false;

        for (int i = 0; i < ra.Length; i++) {
            if (ra[i] != rb[i] && !IsVolatileSymbolByte(i))
                return false;
        }
        return true;
    }

    public bool Same(OcadSetting a, OcadSetting b)
        => a.Type == b.Type && a.Content.Span.SequenceEqual(b.Content.Span);

    private static bool IsDate(FileLayout layout, int offset)
        => InRange(offset, layout.ObjectCreatedOffset, 8) || InRange(offset, layout.ObjectModifiedOffset, 8);

    private static bool IsVolatileSymbolByte(int offset)
        => offset == FileLayout.SymbolSelectedOffset
        || offset == FileLayout.SymbolStatusOffset
        || InRange(offset, FileLayout.SymbolFilePosOffset, 4);

    private static bool InRange(int offset, int start, int length) => start >= 0 && offset >= start && offset < start + length;
}
