namespace OcadViewer.Merge.Format;

/// <summary>
/// An OCAD file, read far enough to merge: its objects, symbols and parameter
/// strings as byte records, plus the index chains that locate them.
/// </summary>
public sealed class OcadFile
{
    /// <summary>The map scale assumed when a file does not state one.</summary>
    public const double DefaultScale = 10000;

    /// <summary>The file as it was read. Never modified.</summary>
    public ReadOnlyMemory<byte> Bytes { get; }

    public FileLayout Layout { get; }

    public int Version => Layout.Version;

    public int Subversion { get; }

    /// <summary>The map scale denominator, e.g. 10000 for 1:10 000.</summary>
    public double Scale { get; }

    /// <summary>Objects OCAD shows or hides; deleted records are left out.</summary>
    public IReadOnlyList<OcadObject> Objects { get; }

    public IReadOnlyList<OcadSymbol> Symbols { get; }

    public IReadOnlyList<OcadSetting> Settings { get; }

    public IndexChain ObjectIndex { get; }
    public IndexChain SymbolIndex { get; }
    public IndexChain StringIndex { get; }

    private OcadFile(ReadOnlyMemory<byte> bytes, FileLayout layout, int subversion,
                     IndexChain objectIndex, IndexChain symbolIndex, IndexChain stringIndex)
    {
        Bytes = bytes;
        Layout = layout;
        Subversion = subversion;
        ObjectIndex = objectIndex;
        SymbolIndex = symbolIndex;
        StringIndex = stringIndex;
        Objects = ReadObjects();
        Symbols = ReadSymbols();
        Settings = ReadSettings();
        Scale = Settings.FirstOrDefault(s => s.Type == OcadSetting.ScaleType)?.NumberField('m') is double scale and > 0
            ? scale
            : DefaultScale;
    }

    /// <exception cref="OcadFormatException">Not an OCAD file, damaged, or an unsupported version.</exception>
    public static OcadFile Read(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> span = bytes.Span;
        if (span.Length < FileLayout.HeaderSize || BitConverter.ToInt16(span) != FileLayout.OcadMark)
            throw new OcadFormatException("This is not an OCAD file.");

        FileLayout layout = FileLayout.For(BitConverter.ToInt16(span[FileLayout.VersionOffset..]));
        int subversion = BitConverter.ToInt16(span[FileLayout.SubversionOffset..]);

        return new OcadFile(bytes, layout, subversion,
            IndexChain.Read(span, FileLayout.FirstObjectBlockOffset, FileLayout.ObjectIndexEntrySize),
            IndexChain.Read(span, FileLayout.FirstSymbolBlockOffset, FileLayout.SymbolIndexEntrySize),
            IndexChain.Read(span, FileLayout.FirstStringBlockOffset, FileLayout.StringIndexEntrySize));
    }

    private List<OcadObject> ReadObjects()
    {
        List<OcadObject> objects = [];
        for (int i = 0; i < ObjectIndex.Count; i++) {
            ReadOnlyMemory<byte> entry = Bytes.Slice(ObjectIndex.EntryOffset(i), FileLayout.ObjectIndexEntrySize);
            ReadOnlySpan<byte> e = entry.Span;
            byte status = e[FileLayout.IndexStatusOffset];
            int symbol = BitConverter.ToInt32(e[FileLayout.IndexSymbolOffset..]);

            if (symbol == 0 || (status != FileLayout.StatusNormal && status != FileLayout.StatusHidden))
                continue;

            int pos = BitConverter.ToInt32(e[FileLayout.IndexPosOffset..]);
            int len = BitConverter.ToInt32(e[FileLayout.IndexLenOffset..]);
            objects.Add(new OcadObject(Layout, i + 1, entry.ToArray(), Slice(pos, len, Layout.ObjectHeaderSize, $"object {i + 1}")));
        }
        return objects;
    }

    private List<OcadSymbol> ReadSymbols()
    {
        List<OcadSymbol> symbols = [];
        for (int i = 0; i < SymbolIndex.Count; i++) {
            int pos = BitConverter.ToInt32(Bytes.Span[SymbolIndex.EntryOffset(i)..]);
            if (pos == 0)
                continue;

            int size = pos >= 0 && pos <= Bytes.Length - 4 ? BitConverter.ToInt32(Bytes.Span[pos..]) : -1;
            symbols.Add(new OcadSymbol(i, Slice(pos, size, FileLayout.SymbolDescriptionOffset + FileLayout.SymbolDescriptionChars * 2, $"symbol {i}")));
        }
        return symbols;
    }

    private List<OcadSetting> ReadSettings()
    {
        List<OcadSetting> settings = [];
        for (int i = 0; i < StringIndex.Count; i++) {
            ReadOnlySpan<byte> e = Bytes.Span.Slice(StringIndex.EntryOffset(i), FileLayout.StringIndexEntrySize);
            int type = BitConverter.ToInt32(e[FileLayout.StringTypeOffset..]);
            if (type == 0)
                continue;

            int pos = BitConverter.ToInt32(e[FileLayout.StringPosOffset..]);
            int len = BitConverter.ToInt32(e[FileLayout.StringLenOffset..]);
            int objectIndex = BitConverter.ToInt32(e[FileLayout.StringObjectIndexOffset..]);
            settings.Add(new OcadSetting(i, type, objectIndex, Slice(pos, len, 0, $"string {i}")));
        }
        return settings;
    }

    private ReadOnlyMemory<byte> Slice(int pos, int len, int minimum, string what)
    {
        if (pos < FileLayout.HeaderSize || len < minimum || (long)pos + len > Bytes.Length)
            throw new OcadFormatException($"The file is damaged: {what} lies outside it.");
        return Bytes.Slice(pos, len);
    }
}
