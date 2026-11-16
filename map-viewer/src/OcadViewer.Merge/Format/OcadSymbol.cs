using System.Text;

namespace OcadViewer.Merge.Format;

/// <summary>One symbol definition in an OCAD file, as bytes.</summary>
public sealed class OcadSymbol
{
    /// <summary>The 0-based entry in the symbol index.</summary>
    public int Slot { get; }

    /// <summary>The whole record, as long as its own size field says.</summary>
    public ReadOnlyMemory<byte> Record { get; }

    internal OcadSymbol(int slot, ReadOnlyMemory<byte> record)
    {
        Slot = slot;
        Record = record;
    }

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

    public override string ToString() => $"{Number / 1000.0:0.000} {Description}";
}
