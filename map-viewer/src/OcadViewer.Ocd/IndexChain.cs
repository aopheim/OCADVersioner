namespace OcadViewer.Ocd;

/// <summary>
/// One of OCAD's three linked lists of index blocks (symbols, objects, strings).
/// </summary>
/// <remarks>
/// Each block is a pointer to the next block followed by 256 fixed-size entries.
/// The reader walks the chain to find records; the writer walks the same chain to
/// update entries and to find a free one, so both go through this type.
/// </remarks>
internal sealed class IndexChain
{
    /// <summary>File offsets of the blocks, in chain order.</summary>
    public IReadOnlyList<int> Blocks { get; }

    public int EntrySize { get; }

    /// <summary>Where the header keeps the offset of the first block.</summary>
    public int HeaderPointerOffset { get; }

    public int Count => Blocks.Count * FileLayout.EntriesPerBlock;

    private IndexChain(IReadOnlyList<int> blocks, int entrySize, int headerPointerOffset)
    {
        Blocks = blocks;
        EntrySize = entrySize;
        HeaderPointerOffset = headerPointerOffset;
    }

    public static IndexChain Read(ReadOnlySpan<byte> file, int headerPointerOffset, int entrySize)
    {
        int blockSize = 4 + FileLayout.EntriesPerBlock * entrySize;
        List<int> blocks = [];
        HashSet<int> seen = [];
        int next = BitConverter.ToInt32(file[headerPointerOffset..]);

        while (next != 0) {
            if (next < FileLayout.HeaderSize || next > file.Length - blockSize)
                throw new OcadFormatException($"An index block points outside the file (offset {next}).");
            if (!seen.Add(next))
                throw new OcadFormatException("The index blocks form a loop.");

            blocks.Add(next);
            next = BitConverter.ToInt32(file[next..]);
        }

        return new IndexChain(blocks, entrySize, headerPointerOffset);
    }

    /// <summary>File offset of entry <paramref name="index"/> (0-based, across blocks).</summary>
    public int EntryOffset(int index)
        => Blocks[index / FileLayout.EntriesPerBlock] + 4 + index % FileLayout.EntriesPerBlock * EntrySize;
}
