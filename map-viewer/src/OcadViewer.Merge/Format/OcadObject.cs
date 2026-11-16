namespace OcadViewer.Merge.Format;

/// <summary>One live object in an OCAD file: its index entry and its record, as bytes.</summary>
/// <remarks>
/// The bytes are kept verbatim, because the merge writes objects back exactly as
/// the branch that changed them wrote them. Everything else here is read from
/// those bytes, never stored alongside them.
/// </remarks>
public sealed class OcadObject
{
    private readonly FileLayout layout;

    /// <summary>The 1-based entry in the object index. Not an identity: see MERGE-PLAN.md.</summary>
    public int Entry { get; }

    /// <summary>The object's 40-byte index entry.</summary>
    public ReadOnlyMemory<byte> IndexEntry { get; }

    /// <summary>The object's record, as long as the index reserves for it.</summary>
    public ReadOnlyMemory<byte> Record { get; }

    public IReadOnlyList<OcadCoord> Coords { get; }

    internal OcadObject(FileLayout layout, int entry, ReadOnlyMemory<byte> indexEntry, ReadOnlyMemory<byte> record)
    {
        this.layout = layout;
        Entry = entry;
        IndexEntry = indexEntry;
        Record = record;
        Coords = ReadCoords();
    }

    public FileLayout Layout => layout;

    /// <summary>The same object with a different record, e.g. a new creation date.</summary>
    public OcadObject WithRecord(ReadOnlyMemory<byte> record) => new(layout, Entry, IndexEntry, record);

    /// <summary>OCAD's symbol number times 1000, e.g. 109000 for 109.0.</summary>
    public int Symbol => BitConverter.ToInt32(Record.Span);

    public ObjectType Type => (ObjectType)Record.Span[4];

    public byte Status => IndexEntry.Span[FileLayout.IndexStatusOffset];

    /// <summary>The raw creation date, the object's identity across files.</summary>
    public double CreationDate => BitConverter.ToDouble(Record.Span[layout.ObjectCreatedOffset..]);

    public DateTime? CreatedAt => OcadDate.ToDateTime(CreationDate);

    public DateTime? ModifiedAt
        => layout.ObjectModifiedOffset < 0
            ? null
            : OcadDate.ToDateTime(BitConverter.ToDouble(Record.Span[layout.ObjectModifiedOffset..]));

    /// <summary>The extent OCAD recorded in the index.</summary>
    public Bounds Bounds
    {
        get {
            ReadOnlySpan<byte> entry = IndexEntry.Span;
            OcadCoord lowerLeft = OcadCoord.Read(entry);
            OcadCoord upperRight = OcadCoord.Read(entry[OcadCoord.Size..]);
            return new Bounds(lowerLeft.X, lowerLeft.Y, upperRight.X, upperRight.Y);
        }
    }

    /// <summary>The bytes the record actually uses, without the index's spare room.</summary>
    public ReadOnlySpan<byte> UsedRecord => Record.Span[..layout.ObjectRecordSize(Record.Span)];

    private OcadCoord[] ReadCoords()
    {
        ReadOnlySpan<byte> record = Record.Span;
        int count = BitConverter.ToInt32(record[layout.ObjectCoordCountOffset..]);
        if (count < 0 || layout.ObjectHeaderSize + (long)count * OcadCoord.Size > record.Length)
            throw new OcadFormatException($"Object {Entry} claims more coordinates than its record holds.");

        OcadCoord[] coords = new OcadCoord[count];
        for (int i = 0; i < count; i++)
            coords[i] = OcadCoord.Read(record[(layout.ObjectHeaderSize + i * OcadCoord.Size)..]);
        return coords;
    }

    public override string ToString() => $"#{Entry} {Symbol / 1000.0:0.000} {Type} {CreatedAt:O}";
}
