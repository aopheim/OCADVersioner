using OcadViewer.Merge.Format;

namespace OcadViewer.Merge.Writing;

/// <summary>
/// Writes a merge plan into a copy of base's file, byte-faithfully.
/// </summary>
/// <remarks>
/// <para>Base's bytes are copied unchanged. New and replaced records are appended
/// at the end of the file and their index entries pointed at them; deleted objects
/// get index status 3, as OCAD does itself. Nothing else is touched, so whatever
/// this engine does not understand - layouts, database links, settings it never
/// looks at - comes out exactly as it went in.</para>
/// <para>An empty plan returns base's bytes as they were.</para>
/// </remarks>
public static class OcadSplicer
{
    public static byte[] Write(OcadFile @base, MergePlan plan, DateTime now)
    {
        Buffer file = new(@base.Bytes.Span);
        if (plan.IsEmpty)
            return file.ToArray();

        WriteObjects(file, @base, plan.Objects, now);
        WriteSymbols(file, @base, plan.Symbols);
        WriteSettings(file, @base, plan.Settings);
        return file.ToArray();
    }

    private static void WriteObjects(Buffer file, OcadFile @base, IReadOnlyList<Operation<OcadObject>> operations, DateTime now)
    {
        Allocator index = new(file, @base.ObjectIndex, entry => Int(entry, FileLayout.IndexPosOffset) == 0
                                                                && Int(entry, FileLayout.IndexSymbolOffset) == 0);
        DateTime nextIdentity = now;

        foreach (Operation<OcadObject> op in operations) {
            switch (op.Kind) {
                case OperationKind.Delete:
                    file.Bytes[EntryOf(@base, op.Target) + FileLayout.IndexStatusOffset] = FileLayout.StatusDeleted;
                    break;

                case OperationKind.Replace:
                    PlaceObject(file, EntryOf(@base, op.Target), op.Source, op.Source.Record.Span);
                    break;

                case OperationKind.Add:
                    byte[] record = op.Source.Record.ToArray();
                    if (op.NewIdentity) {
                        BitConverter.TryWriteBytes(record.AsSpan(op.Source.Layout.ObjectCreatedOffset), OcadDate.FromDateTime(nextIdentity));
                        nextIdentity = nextIdentity.AddMilliseconds(1);
                    }
                    PlaceObject(file, index.Allocate(), op.Source, record);
                    break;
            }
        }
    }

    private static void PlaceObject(Buffer file, int entryOffset, OcadObject source, ReadOnlySpan<byte> record)
    {
        int pos = file.Append(record);
        source.IndexEntry.Span.CopyTo(file.Bytes.AsSpan(entryOffset, FileLayout.ObjectIndexEntrySize));
        file.WriteInt(entryOffset + FileLayout.IndexPosOffset, pos);
        file.WriteInt(entryOffset + FileLayout.IndexLenOffset, record.Length);
    }

    private static void WriteSymbols(Buffer file, OcadFile @base, IReadOnlyList<Operation<OcadSymbol>> operations)
    {
        Allocator index = new(file, @base.SymbolIndex, entry => Int(entry, 0) == 0);

        foreach (Operation<OcadSymbol> op in operations) {
            int entry = op.Kind == OperationKind.Add ? index.Allocate() : @base.SymbolIndex.EntryOffset(op.Target.Slot);
            if (op.Kind == OperationKind.Delete) {
                file.WriteInt(entry, 0);
                continue;
            }

            int pos = file.Append(op.Source.Record.Span);
            // OCAD keeps each symbol's own position inside it.
            file.WriteInt(pos + FileLayout.SymbolFilePosOffset, pos);
            file.WriteInt(entry, pos);
        }
    }

    private static void WriteSettings(Buffer file, OcadFile @base, IReadOnlyList<Operation<OcadSetting>> operations)
    {
        Allocator index = new(file, @base.StringIndex, entry => Int(entry, FileLayout.StringPosOffset) == 0
                                                                && Int(entry, FileLayout.StringTypeOffset) == 0);

        foreach (Operation<OcadSetting> op in operations) {
            if (op.Kind == OperationKind.Delete) {
                int gone = @base.StringIndex.EntryOffset(op.Target.Slot);
                file.WriteInt(gone + FileLayout.StringPosOffset, 0);
                file.WriteInt(gone + FileLayout.StringLenOffset, 0);
                file.WriteInt(gone + FileLayout.StringTypeOffset, 0);
                continue;
            }

            int entry = op.Kind == OperationKind.Add ? index.Allocate() : @base.StringIndex.EntryOffset(op.Target.Slot);
            byte[] text = [.. op.Source.Content.Span, 0];
            int pos = file.Append(text);
            file.WriteInt(entry + FileLayout.StringPosOffset, pos);
            file.WriteInt(entry + FileLayout.StringLenOffset, text.Length);
            file.WriteInt(entry + FileLayout.StringTypeOffset, op.Source.Type);
            file.WriteInt(entry + FileLayout.StringObjectIndexOffset, 0);
        }
    }

    private static int EntryOf(OcadFile @base, OcadObject target) => @base.ObjectIndex.EntryOffset(target.Slot - 1);

    private static int Int(ReadOnlySpan<byte> bytes, int offset) => BitConverter.ToInt32(bytes[offset..]);

    /// <summary>The file being written: base's bytes, growing as records are appended.</summary>
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
