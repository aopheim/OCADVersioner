using System.Buffers.Binary;

namespace OcadViewer.Diff.Tests;

/// <summary>
/// Edits a field of one object in an OCAD 2018 file, in place.
/// </summary>
/// <remarks>
/// For the cases no fixture shows: an entry handed to a new object, a symbol swap,
/// a rotation. Only the first index block is searched, which is all the fixtures
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

    public static void SetAngle(byte[] file, int entry, short tenthsOfADegree)
        => BinaryPrimitives.WriteInt16LittleEndian(file.AsSpan(ObjectAt(file, entry) + 6), tenthsOfADegree);

    private static int IndexEntryAt(byte[] file, int entry)
    {
        Assert.That(entry, Is.InRange(1, 256), "Only the first index block is supported.");
        int firstIndexBlock = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(12));
        return firstIndexBlock + 4 + (entry - 1) * 40;
    }

    private static int ObjectAt(byte[] file, int entry)
        => BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(IndexEntryAt(file, entry) + 16));
}
