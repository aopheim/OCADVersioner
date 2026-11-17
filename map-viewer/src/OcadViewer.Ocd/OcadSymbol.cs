using System.Text;

namespace OcadViewer.Ocd;

/// <summary>One symbol definition in an OCAD file.</summary>
public sealed class OcadSymbol
{
    /// <summary>The symbol number times 1000, e.g. 109000 for 109.0. The identity of a symbol.</summary>
    public int Number => BitConverter.ToInt32(Record.Span[FileLayout.SymbolNumberOffset..]);

    public ObjectType Type => (ObjectType)Record.Span[FileLayout.SymbolTypeOffset];

    public string Description
    {
        get {
            ReadOnlySpan<byte> bytes = Record.Span.Slice(FileLayout.SymbolDescriptionOffset,
                                                         FileLayout.SymbolDescriptionChars * 2);
            string text = Encoding.Unicode.GetString(bytes);
            int end = text.IndexOf('\0');
            return end < 0 ? text : text[..end];
        }
    }

    /// <summary>The file the symbol was read from.</summary>
    internal OcadFile File { get; }

    /// <summary>The 0-based entry in the symbol index.</summary>
    internal int Entry { get; }

    /// <summary>The whole record, as long as its own size field says.</summary>
    internal ReadOnlyMemory<byte> Record { get; }

    internal OcadSymbol(OcadFile file, int entry, ReadOnlyMemory<byte> record)
    {
        File = file;
        Entry = entry;
        Record = record;
    }

    /// <summary>
    /// Whether <paramref name="other"/> has the same definition: the same record
    /// byte for byte, except for the fields in <paramref name="ignoring"/>.
    /// </summary>
    public bool SameContent(OcadSymbol other, SymbolFields ignoring)
    {
        ReadOnlySpan<byte> mine = Record.Span, theirs = other.Record.Span;
        if (mine.Length != theirs.Length)
            return false;

        for (int i = 0; i < mine.Length; i++) {
            if (mine[i] != theirs[i] && !IsIgnored(i, ignoring))
                return false;
        }
        return true;
    }

    private static bool IsIgnored(int offset, SymbolFields ignoring)
        => (ignoring.HasFlag(SymbolFields.Selected) && offset == FileLayout.SymbolSelectedOffset)
        || (ignoring.HasFlag(SymbolFields.BoxStatus) && offset == FileLayout.SymbolStatusOffset)
        || (ignoring.HasFlag(SymbolFields.FilePosition)
            && offset >= FileLayout.SymbolFilePosOffset && offset < FileLayout.SymbolFilePosOffset + 4);

    public override string ToString() => $"{Number / 1000.0:0.000} {Description}";
}
