namespace OcadViewer.Ocd;

/// <summary>
/// An OCAD map file (OCAD 11, 12 or 2018; 2019 and 2020 save as 2018): its
/// objects, symbols and parameter strings. Read-only; to change a file, make an
/// <see cref="OcadEditor"/> from it.
/// </summary>
public sealed class OcadFile
{
    /// <summary>The map scale assumed when a file does not state one.</summary>
    private const double DefaultScale = 10000;

    /// <summary>The OCAD version the file was saved as: 11, 12 or 2018.</summary>
    public int Version => Layout.Version;

    /// <summary>The map scale denominator, e.g. 10000 for 1:10 000.</summary>
    public double Scale { get; }

    /// <summary>Objects OCAD shows or hides; deleted records are left out.</summary>
    public IReadOnlyList<OcadObject> Objects { get; }

    public IReadOnlyList<OcadSymbol> Symbols { get; }

    public IReadOnlyList<OcadSetting> Settings { get; }

    /// <summary>The file as it was read. Never modified.</summary>
    internal ReadOnlyMemory<byte> Bytes { get; }

    internal FileLayout Layout { get; }

    internal IndexChain ObjectIndex { get; }
    internal IndexChain SymbolIndex { get; }
    internal IndexChain StringIndex { get; }

    private OcadFile(ReadOnlyMemory<byte> bytes, FileLayout layout,
                     IndexChain objectIndex, IndexChain symbolIndex, IndexChain stringIndex)
    {
        Bytes = bytes;
        Layout = layout;
        ObjectIndex = objectIndex;
        SymbolIndex = symbolIndex;
        StringIndex = stringIndex;
        Objects = ReadObjects();
        Symbols = ReadSymbols();
        Settings = ReadSettings();
        Scale = Settings.FirstOrDefault(s => s.Type == SettingType.Scale)?.NumberField('m') is double scale and > 0
            ? scale
            : DefaultScale;
    }

    /// <summary>Reads <paramref name="bytes"/>, which are kept, not copied: do not change them afterwards.</summary>
    /// <exception cref="OcadFormatException">Not an OCAD file, damaged, or an unsupported version.</exception>
    public static OcadFile Read(ReadOnlyMemory<byte> bytes)
    {
        ReadOnlySpan<byte> span = bytes.Span;
        if (span.Length < FileLayout.HeaderSize || BitConverter.ToInt16(span) != FileLayout.OcadMark)
            throw new OcadFormatException("This is not an OCAD file.");

        FileLayout layout = FileLayout.For(BitConverter.ToInt16(span[FileLayout.VersionOffset..]));

        return new OcadFile(bytes, layout,
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
            var status = (ObjectStatus)e[FileLayout.IndexStatusOffset];
            int symbol = BitConverter.ToInt32(e[FileLayout.IndexSymbolOffset..]);

            if (symbol == 0 || (status != ObjectStatus.Normal && status != ObjectStatus.Hidden))
                continue;

            int pos = BitConverter.ToInt32(e[FileLayout.IndexPosOffset..]);
            int len = BitConverter.ToInt32(e[FileLayout.IndexLenOffset..]);
            objects.Add(new OcadObject(this, i + 1, entry.ToArray(), Slice(pos, len, Layout.ObjectHeaderSize, $"object {i + 1}")));
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
            symbols.Add(new OcadSymbol(this, i, Slice(pos, size, FileLayout.SymbolDescriptionOffset + FileLayout.SymbolDescriptionChars * 2, $"symbol {i}")));
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
            settings.Add(new OcadSetting(this, i, type, objectIndex, Slice(pos, len, 0, $"string {i}")));
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
