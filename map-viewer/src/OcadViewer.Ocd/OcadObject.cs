namespace OcadViewer.Ocd;

/// <summary>One live object in an OCAD file.</summary>
/// <remarks>
/// The object's index entry and record are kept as bytes, verbatim, so that an
/// <see cref="OcadEditor"/> writes it back exactly as the file it came from had
/// it. Everything public here is read from those bytes, never stored alongside.
/// </remarks>
public sealed class OcadObject
{
    /// <summary>Bytes the record uses, checked against the record when it is read.</summary>
    private int usedLength;

    /// <summary>The 1-based entry in the object index. A position, not an identity: OCAD reuses entries.</summary>
    public int Entry { get; }

    public IReadOnlyList<OcadCoord> Coords { get; }

    /// <summary>OCAD's symbol number times 1000, e.g. 109000 for 109.0.</summary>
    public int Symbol => BitConverter.ToInt32(Record.Span);

    public ObjectType Type => (ObjectType)Record.Span[4];

    public ObjectStatus Status => (ObjectStatus)IndexEntry.Span[FileLayout.IndexStatusOffset];

    /// <summary>When the object was drawn, to the millisecond, or null if the file has no date for it.</summary>
    /// <remarks>Edits, moves, cut-and-paste and copies all keep it.</remarks>
    public DateTime? CreatedAt => OcadDate.ToDateTime(BitConverter.ToDouble(Record.Span[Layout.ObjectCreatedOffset..]));

    /// <summary>When the object was last edited, or null (OCAD 11 has no such date).</summary>
    public DateTime? ModifiedAt
        => Layout.ObjectModifiedOffset < 0
            ? null
            : OcadDate.ToDateTime(BitConverter.ToDouble(Record.Span[Layout.ObjectModifiedOffset..]));

    /// <summary>The extent OCAD recorded in the index, in hundredths of a millimetre.</summary>
    public Bounds Bounds
    {
        get {
            ReadOnlySpan<byte> entry = IndexEntry.Span;
            OcadCoord lowerLeft = OcadCoord.Read(entry);
            OcadCoord upperRight = OcadCoord.Read(entry[OcadCoord.Size..]);
            return new Bounds(lowerLeft.X, lowerLeft.Y, upperRight.X, upperRight.Y);
        }
    }

    /// <summary>The file the object was read from.</summary>
    internal OcadFile File { get; }

    /// <summary>The object's 40-byte index entry.</summary>
    internal ReadOnlyMemory<byte> IndexEntry { get; }

    /// <summary>The object's record, as long as the index reserves for it.</summary>
    internal ReadOnlyMemory<byte> Record { get; }

    internal FileLayout Layout => File.Layout;

    /// <summary>The bytes the record actually uses, without the index's spare room.</summary>
    internal ReadOnlySpan<byte> UsedRecord => Record.Span[..usedLength];

    internal OcadObject(OcadFile file, int entry, ReadOnlyMemory<byte> indexEntry, ReadOnlyMemory<byte> record)
    {
        File = file;
        Entry = entry;
        IndexEntry = indexEntry;
        Record = record;
        Coords = ReadCoords();
    }

    /// <summary>The same object with another creation date: a new identity for a copy kept beside its original.</summary>
    public OcadObject WithCreatedAt(DateTime value)
    {
        byte[] record = Record.ToArray();
        BitConverter.TryWriteBytes(record.AsSpan(Layout.ObjectCreatedOffset), OcadDate.FromDateTime(value));
        return new OcadObject(File, Entry, IndexEntry, record);
    }

    /// <summary>The same object with the same number of coordinates, moved to <paramref name="coords"/>.</summary>
    /// <remarks>
    /// Internal: the index keeps an extent that includes the symbol's size, which this
    /// cannot recompute, so the result is fit for comparing but not for writing. Tests
    /// use it to nudge coordinates; nothing in the product moves objects.
    /// </remarks>
    /// <exception cref="ArgumentException">The number of coordinates differs.</exception>
    internal OcadObject WithCoords(IReadOnlyList<OcadCoord> coords)
    {
        if (coords.Count != Coords.Count)
            throw new ArgumentException($"The object has {Coords.Count} coordinates, not {coords.Count}.", nameof(coords));

        byte[] record = Record.ToArray();
        for (int i = 0; i < coords.Count; i++)
            coords[i].Write(record.AsSpan(Layout.ObjectHeaderSize + i * OcadCoord.Size));
        return new OcadObject(File, Entry, IndexEntry, record);
    }

    /// <summary>
    /// Whether <paramref name="other"/> has the same content: no field differs but
    /// those in <paramref name="ignoring"/>, with coordinates that may each be up
    /// to <paramref name="coordTolerance"/> apart. Objects from files of different
    /// versions are never the same.
    /// </summary>
    public bool SameContent(OcadObject other, ObjectFields ignoring, int coordTolerance)
        => Differences(other, ignoring, coordTolerance) == ObjectFields.None;

    /// <summary>
    /// The fields in which <paramref name="other"/> differs from this object, less
    /// those in <paramref name="ignoring"/>. Coordinates count as the same while
    /// each is within <paramref name="coordTolerance"/> and their flags agree.
    /// </summary>
    /// <remarks>
    /// Records of the same version are compared byte for byte, so nothing escapes:
    /// a byte no named field covers is <see cref="ObjectFields.Other"/>. Records of
    /// different versions are compared field by field where the layouts agree, and
    /// always differ in <see cref="ObjectFields.Other"/>.
    /// </remarks>
    public ObjectFields Differences(OcadObject other, ObjectFields ignoring, int coordTolerance)
    {
        if (ReferenceEquals(this, other))
            return ObjectFields.None;

        ObjectFields found = ObjectFields.None;
        if (Status != other.Status)
            found |= ObjectFields.Status;
        if (!SameCoords(other, coordTolerance))
            found |= ObjectFields.Coordinates;
        if (!Texts.SequenceEqual(other.Texts))
            found |= ObjectFields.Text;
        if (!ExtraStrings.SequenceEqual(other.ExtraStrings))
            found |= ObjectFields.Other;

        if (Layout.Version == other.Layout.Version) {
            ReadOnlySpan<byte> mine = Record.Span, theirs = other.Record.Span;
            for (int i = 0; i < Layout.ObjectHeaderSize; i++) {
                if (mine[i] != theirs[i])
                    found |= Layout.ObjectFieldAt(i);
            }
        }
        else {
            found |= ObjectFields.Other;
            if (Symbol != other.Symbol)
                found |= ObjectFields.Symbol;
            if (Type != other.Type)
                found |= ObjectFields.Type;
            if (AngleField != other.AngleField)
                found |= ObjectFields.Angle;
        }

        return found & ~ignoring;
    }

    private short AngleField => BitConverter.ToInt16(Record.Span[FileLayout.ObjectAngleOffset..]);

    private int TextLength => BitConverter.ToInt16(Record.Span[Layout.ObjectTextCountOffset..]) * OcadCoord.Size;

    private int TextsStart => Layout.ObjectHeaderSize + Coords.Count * OcadCoord.Size;

    /// <summary>The object's own text, as stored.</summary>
    private ReadOnlySpan<byte> Texts => UsedRecord.Slice(TextsStart, TextLength);

    /// <summary>The object and database strings after the text (OCAD 12 and later).</summary>
    private ReadOnlySpan<byte> ExtraStrings => UsedRecord[(TextsStart + TextLength)..];

    private bool SameCoords(OcadObject other, int tolerance)
    {
        if (Coords.Count != other.Coords.Count)
            return false;

        for (int i = 0; i < Coords.Count; i++) {
            OcadCoord a = Coords[i], b = other.Coords[i];
            if (a.XFlags != b.XFlags || a.YFlags != b.YFlags
                || Math.Abs(a.X - b.X) > tolerance || Math.Abs(a.Y - b.Y) > tolerance)
                return false;
        }
        return true;
    }

    /// <exception cref="OcadFormatException">The record claims more coordinates or text than it holds.</exception>
    private OcadCoord[] ReadCoords()
    {
        ReadOnlySpan<byte> record = Record.Span;
        long used = Layout.ObjectRecordSize(record);
        if (used < 0 || used > record.Length)
            throw new OcadFormatException($"Object {Entry} claims more coordinates or text than its record holds.");

        usedLength = (int)used;
        int count = BitConverter.ToInt32(record[Layout.ObjectCoordCountOffset..]);
        OcadCoord[] coords = new OcadCoord[count];
        for (int i = 0; i < count; i++)
            coords[i] = OcadCoord.Read(record[(Layout.ObjectHeaderSize + i * OcadCoord.Size)..]);
        return coords;
    }

    public override string ToString() => $"#{Entry} {Symbol / 1000.0:0.000} {Type} {CreatedAt:O}";
}
