namespace OcadViewer.Merge.Format;

/// <summary>
/// Where things are in an OCAD file of a given version: the one place that knows
/// byte offsets, so the reader and the writer cannot disagree about them.
/// </summary>
/// <remarks>
/// Supported: OCAD 11, 12 and 2018. OCAD 2019 and 2020 save in the 2018 format
/// (their Save As offers 2018, 12, 11 and 10), so "2018" covers every current
/// release. Offsets come from PurplePen's OcadFileFormat.cs and were checked
/// against files saved by OCAD 2020.
/// </remarks>
public sealed class FileLayout
{
    public const short OcadMark = 0x0CAD;

    // File header: shared by every supported version.
    public const int VersionOffset = 4;
    public const int SubversionOffset = 6;
    public const int FirstSymbolBlockOffset = 8;
    public const int FirstObjectBlockOffset = 12;
    public const int FirstStringBlockOffset = 32;
    public const int HeaderSize = 48;

    // Index blocks: a next-block pointer followed by 256 entries.
    public const int EntriesPerBlock = 256;
    public const int ObjectIndexEntrySize = 40;
    public const int SymbolIndexEntrySize = 4;
    public const int StringIndexEntrySize = 16;

    // Object index entry (40 bytes).
    public const int IndexBoundsOffset = 0;   // lower-left, upper-right: two OcadCoords
    public const int IndexPosOffset = 16;
    public const int IndexLenOffset = 20;
    public const int IndexSymbolOffset = 24;
    public const int IndexTypeOffset = 28;
    public const int IndexStatusOffset = 30;

    // String index entry (16 bytes).
    public const int StringPosOffset = 0;
    public const int StringLenOffset = 4;
    public const int StringTypeOffset = 8;
    public const int StringObjectIndexOffset = 12;

    // Symbol record (version 9 and later).
    public const int SymbolSizeOffset = 0;
    public const int SymbolNumberOffset = 4;
    public const int SymbolTypeOffset = 8;
    public const int SymbolSelectedOffset = 10;
    public const int SymbolStatusOffset = 11;
    public const int SymbolFilePosOffset = 20;
    public const int SymbolDescriptionOffset = 56;
    public const int SymbolDescriptionChars = 64;

    /// <summary>Index status of an object OCAD shows.</summary>
    public const byte StatusNormal = 1;
    /// <summary>Index status of an object the user hid.</summary>
    public const byte StatusHidden = 2;
    /// <summary>Index status OCAD leaves on the record of a deleted object.</summary>
    public const byte StatusDeleted = 3;

    public int Version { get; }

    /// <summary>Size of the fixed part of an object record, before its coordinates.</summary>
    public int ObjectHeaderSize { get; }
    public int ObjectCoordCountOffset { get; }
    public int ObjectTextCountOffset { get; }
    public int ObjectCreatedOffset { get; }

    /// <summary>The colour field, which only graphic objects (no symbol) use.</summary>
    public int ObjectColorOffset { get; }

    /// <summary>Where the modification date is, or -1 for versions that have none.</summary>
    public int ObjectModifiedOffset { get; }

    /// <summary>Where the object-string and database-string counts are, or -1.</summary>
    public int ObjectExtraStringCountsOffset { get; }

    private FileLayout(int version, int headerSize, int coordCount, int textCount,
                       int color, int created, int modified, int extraStrings)
    {
        Version = version;
        ObjectHeaderSize = headerSize;
        ObjectCoordCountOffset = coordCount;
        ObjectTextCountOffset = textCount;
        ObjectColorOffset = color;
        ObjectCreatedOffset = created;
        ObjectModifiedOffset = modified;
        ObjectExtraStringCountsOffset = extraStrings;
    }

    /// <summary>The layout of <paramref name="version"/>.</summary>
    /// <exception cref="OcadFormatException">The version is not supported.</exception>
    public static FileLayout For(int version) => version switch {
        11 => new(11, headerSize: 40, coordCount: 8, textCount: 12, color: 16, created: 32, modified: -1, extraStrings: -1),
        12 or >= 2018 => new(version, headerSize: 56, coordCount: 44, textCount: 48, color: 8, created: 24, modified: 36, extraStrings: 50),
        _ => throw new OcadFormatException(
            $"This is an OCAD {version} file. Only OCAD 11, 12 and 2018 (and later) files can be merged."),
    };

    /// <summary>
    /// The size an object record actually uses: its header, coordinates and texts.
    /// The index may reserve more than this.
    /// </summary>
    public int ObjectRecordSize(ReadOnlySpan<byte> record)
    {
        int coords = BitConverter.ToInt32(record[ObjectCoordCountOffset..]);
        int texts = BitConverter.ToInt16(record[ObjectTextCountOffset..]);

        if (ObjectExtraStringCountsOffset >= 0) {
            texts += BitConverter.ToInt16(record[ObjectExtraStringCountsOffset..]);
            texts += BitConverter.ToInt16(record[(ObjectExtraStringCountsOffset + 2)..]);
        }

        // Texts are stored in units of one coordinate: 8 bytes, four UTF-16 characters.
        return ObjectHeaderSize + (coords + texts) * OcadCoord.Size;
    }
}
