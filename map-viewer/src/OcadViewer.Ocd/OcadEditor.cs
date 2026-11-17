namespace OcadViewer.Ocd;

/// <summary>
/// Changes a copy of an OCAD file, byte-faithfully.
/// </summary>
/// <remarks>
/// <para>The file's bytes are copied unchanged. New and replacing records are
/// appended at the end and their index entries pointed at them; deleted objects
/// get index status 3, as OCAD does itself. Nothing else is touched, so whatever
/// the caller does not change - layouts, database links, strings it never looks
/// at - comes out exactly as it went in. The file the editor was made from is
/// never modified.</para>
/// <para>Objects, symbols and strings being replaced or deleted must come from that
/// file. What is added or put in their place may come from any file of the same
/// OCAD version, and is written exactly as that file had it.</para>
/// <para>With no changes, <see cref="ToArray"/> returns the file's bytes as they were.</para>
/// </remarks>
public sealed class OcadEditor
{
    private readonly OcadFile basis;
    private readonly Buffer file;
    private readonly Allocator objectEntries, symbolEntries, stringEntries;

    public OcadEditor(OcadFile basis)
    {
        this.basis = basis;
        file = new Buffer(basis.Bytes.Span);
        objectEntries = new Allocator(file, basis.ObjectIndex, entry => Int(entry, FileLayout.IndexPosOffset) == 0
                                                                     && Int(entry, FileLayout.IndexSymbolOffset) == 0);
        symbolEntries = new Allocator(file, basis.SymbolIndex, entry => Int(entry, 0) == 0);
        stringEntries = new Allocator(file, basis.StringIndex, entry => Int(entry, FileLayout.StringPosOffset) == 0
                                                                     && Int(entry, FileLayout.StringTypeOffset) == 0);
    }

    /// <summary>The edited file.</summary>
    public byte[] ToArray() => file.ToArray();

    public void Replace(OcadObject target, OcadObject with) => PlaceObject(EntryOf(target), Compatible(with));

    public void Add(OcadObject obj) => PlaceObject(objectEntries.Allocate(), Compatible(obj));

    public void Delete(OcadObject target)
        => file.Bytes[EntryOf(target) + FileLayout.IndexStatusOffset] = (byte)ObjectStatus.Deleted;

    public void Replace(OcadSymbol target, OcadSymbol with) => PlaceSymbol(EntryOf(target), with);

    public void Add(OcadSymbol symbol) => PlaceSymbol(symbolEntries.Allocate(), symbol);

    public void Delete(OcadSymbol target) => file.WriteInt(EntryOf(target), 0);

    public void Replace(OcadSetting target, OcadSetting with) => PlaceSetting(EntryOf(target), with);

    public void Add(OcadSetting setting) => PlaceSetting(stringEntries.Allocate(), setting);

    public void Delete(OcadSetting target)
    {
        int entry = EntryOf(target);
        file.WriteInt(entry + FileLayout.StringPosOffset, 0);
        file.WriteInt(entry + FileLayout.StringLenOffset, 0);
        file.WriteInt(entry + FileLayout.StringTypeOffset, 0);
    }

    private void PlaceObject(int entry, OcadObject source)
    {
        int pos = file.Append(source.Record.Span);
        source.IndexEntry.Span.CopyTo(file.Bytes.AsSpan(entry, FileLayout.ObjectIndexEntrySize));
        file.WriteInt(entry + FileLayout.IndexPosOffset, pos);
        file.WriteInt(entry + FileLayout.IndexLenOffset, source.Record.Length);
    }

    private void PlaceSymbol(int entry, OcadSymbol source)
    {
        int pos = file.Append(source.Record.Span);
        // OCAD keeps each symbol's own position inside it.
        file.WriteInt(pos + FileLayout.SymbolFilePosOffset, pos);
        file.WriteInt(entry, pos);
    }

    private void PlaceSetting(int entry, OcadSetting source)
    {
        byte[] text = [.. source.Content.Span, 0];
        int pos = file.Append(text);
        file.WriteInt(entry + FileLayout.StringPosOffset, pos);
        file.WriteInt(entry + FileLayout.StringLenOffset, text.Length);
        file.WriteInt(entry + FileLayout.StringTypeOffset, source.Type);
        file.WriteInt(entry + FileLayout.StringObjectIndexOffset, 0);
    }

    private int EntryOf(OcadObject target) => basis.ObjectIndex.EntryOffset(Own(target.File, target.Entry - 1));

    private int EntryOf(OcadSymbol target) => basis.SymbolIndex.EntryOffset(Own(target.File, target.Entry));

    private int EntryOf(OcadSetting target) => basis.StringIndex.EntryOffset(Own(target.File, target.Entry));

    private int Own(OcadFile from, int entry)
        => ReferenceEquals(from, basis)
            ? entry
            : throw new ArgumentException("Only what was read from the file being edited can be replaced or deleted.");

    private OcadObject Compatible(OcadObject source)
        => source.Layout.Version == basis.Version
            ? source
            : throw new ArgumentException(
                $"An object from an OCAD {source.Layout.Version} file cannot be written into an OCAD {basis.Version} file.");

    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BitConverter.ToInt32(bytes[offset..]);

    /// <summary>The file being written: the basis's bytes, growing as records are appended.</summary>
    private sealed class Buffer(ReadOnlySpan<byte> start)
    {
        private const int Alignment = 8;

        public byte[] Bytes { get; private set; } = start.ToArray();
        public int Length { get; private set; } = start.Length;

        /// <summary>Appends <paramref name="data"/> at the next aligned offset and returns that offset.</summary>
        public int Append(ReadOnlySpan<byte> data)
        {
            int offset = (Length + Alignment - 1) / Alignment * Alignment;
            Ensure(offset + data.Length);
            data.CopyTo(Bytes.AsSpan(offset));
            Length = offset + data.Length;
            return offset;
        }

        public void WriteInt(int offset, int value) => BitConverter.TryWriteBytes(Bytes.AsSpan(offset), value);

        public byte[] ToArray() => Bytes.AsSpan(0, Length).ToArray();

        private void Ensure(int size)
        {
            if (size > Bytes.Length) {
                byte[] bigger = new byte[Math.Max(size, Bytes.Length * 2)];
                Bytes.AsSpan(0, Length).CopyTo(bigger);
                Bytes = bigger;
            }
        }
    }

    /// <summary>Finds free entries in an index chain, adding a block to the chain when it is full.</summary>
    private sealed class Allocator(Buffer file, IndexChain chain, Allocator.FreeTest isFree)
    {
        public delegate bool FreeTest(ReadOnlySpan<byte> entry);

        private readonly List<int> blocks = [.. chain.Blocks];
        private int next;

        public int Allocate()
        {
            for (; next < blocks.Count * FileLayout.EntriesPerBlock; next++) {
                int offset = Offset(next);
                if (isFree(file.Bytes.AsSpan(offset, chain.EntrySize))) {
                    next++;
                    return offset;
                }
            }

            int block = file.Append(new byte[4 + FileLayout.EntriesPerBlock * chain.EntrySize]);
            file.WriteInt(blocks.Count == 0 ? chain.HeaderPointerOffset : blocks[^1], block);
            blocks.Add(block);
            return Allocate();
        }

        private int Offset(int index)
            => blocks[index / FileLayout.EntriesPerBlock] + 4 + index % FileLayout.EntriesPerBlock * chain.EntrySize;
    }
}
