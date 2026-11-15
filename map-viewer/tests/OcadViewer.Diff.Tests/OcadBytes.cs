using System.Buffers.Binary;

namespace OcadViewer.Diff.Tests;

/// <summary>
/// Edits an OCAD 2018 file in place: a field of one object, or the index itself.
/// </summary>
/// <remarks>
/// For the cases no fixture shows: an entry handed to a new object, objects
/// trading entries, dates shifted, a batch sharing one creation date. Only the first index block is searched, which is all the fixtures
/// have. Offsets as in <see cref="OcadObjectIndexReader"/>.
/// </remarks>
internal static class OcadBytes
{
    public static void SetCreationDate(byte[] file, int entry, DateTime utc)
        => BinaryPrimitives.WriteDoubleLittleEndian(file.AsSpan(ObjectAt(file, entry) + 24), utc.ToOADate());

    /// <summary>Changes the symbol in both places OCAD records it: the index entry and the object.</summary>
    public static void SetSymbol(byte[] file, int entry, int symbolNumber)
    {
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(IndexEntryAt(file, entry) + 24), symbolNumber);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(ObjectAt(file, entry)), symbolNumber);
    }

    /// <summary>
    /// Swaps two entries of the object index, so each object is found under the
    /// other's number. The objects themselves stay where they are in the file.
    /// Swapping with an empty entry moves an object to a number nobody had.
    /// </summary>
    /// <remarks>
    /// What happens to a whole map when its objects are copied into another file:
    /// every object survives, under a new index entry.
    /// </remarks>
    public static void SwapIndexEntries(byte[] file, int first, int second)
    {
        Span<byte> a = file.AsSpan(IndexEntryAt(file, first), 40);
        Span<byte> b = file.AsSpan(IndexEntryAt(file, second), 40);
        byte[] held = a.ToArray();
        b.CopyTo(a);
        held.CopyTo(b);
    }

    /// <summary>
    /// Moves every object's creation and modification date by the same amount, the
    /// way they moved in a real map saved again with its times read as local.
    /// </summary>
    public static void ShiftAllDates(byte[] file, TimeSpan by)
    {
        for (int entry = 1; entry <= 256; entry++) {
            int at = ObjectAt(file, entry);
            if (at == 0)
                continue;

            foreach (int offset in new[] { 24, 36 }) {
                Span<byte> field = file.AsSpan(at + offset, 8);
                DateTime when = DateTime.FromOADate(BinaryPrimitives.ReadDoubleLittleEndian(field));
                BinaryPrimitives.WriteDoubleLittleEndian(field, (when + by).ToOADate());
            }
        }
    }

    private static int IndexEntryAt(byte[] file, int entry)
    {
        Assert.That(entry, Is.InRange(1, 256), "Only the first index block is supported.");
        int firstIndexBlock = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(12));
        return firstIndexBlock + 4 + (entry - 1) * 40;
    }

    private static int ObjectAt(byte[] file, int entry)
        => BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(IndexEntryAt(file, entry) + 16));
}
