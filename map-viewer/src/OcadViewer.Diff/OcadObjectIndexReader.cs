using System.Buffers.Binary;
using System.Text;

namespace OcadViewer.Diff;

/// <summary>
/// Reads every object in an OCAD file's object index, with its slot, status and
/// timestamps.
/// </summary>
/// <remarks>
/// <para>
/// PurplePen's importer reads exactly these fields and then drops them - it builds
/// a map to draw, and a drawing has no use for when an object was created or which
/// slot it lives in. The diff needs both, so this reads the index itself rather
/// than patching the vendored importer. The layouts follow PurplePen's
/// <c>OcadFileFormat.cs</c> (<c>OcadFileHeader</c>, <c>OcadIndex</c>,
/// <c>OcadObject</c>), which is the specification this was checked against.
/// </para>
/// <para>
/// OCAD 9 to 2018 are supported. OCAD 9 has no timestamps, so its objects come
/// back with null dates; OCAD 8 and older use a different index layout altogether
/// and are rejected rather than half-read.
/// </para>
/// </remarks>
public static class OcadObjectIndexReader
{
    private const short OcadMark = 0x0CAD;
    private const int IndexEntriesPerBlock = 256;
    private const int IndexEntrySize = 40;

    /// <summary>Reads all objects with a symbol, whatever their status, in slot order.</summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not an OCAD file, are a version this cannot read, or are cut
    /// short - in which case nothing is returned rather than whatever was read so far.
    /// </exception>
    public static IReadOnlyList<MapObject> Read(byte[] file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var data = new Bytes(file);
        if (file.Length < 16 || data.Int16(0) != OcadMark)
            throw new InvalidDataException("Not an OCAD file: the OCAD mark is missing.");

        int version = data.Int16(4);
        if (version < 9 || (version > 12 && version != 2018))
            throw new InvalidDataException(
                $"OCAD {version} files are not supported. The diff reads OCAD 9, 10, 11, 12 and 2018.");

        int firstIndexBlock = data.Int32(12);

        List<MapObject> objects = [];
        HashSet<int> visitedBlocks = [];
        int slot = 1;   // OCAD counts index slots from 1.

        for (int block = firstIndexBlock; block != 0; ) {
            if (!visitedBlocks.Add(block))
                throw new InvalidDataException("The OCAD object index loops back on itself.");

            int next = data.Int32(block);

            for (int i = 0; i < IndexEntriesPerBlock; i++, slot++) {
                int entry = block + 4 + i * IndexEntrySize;
                int position = data.Int32(entry + 16);
                int symbol = data.Int32(entry + 24);

                if (symbol == 0 || position <= 0)
                    continue;

                var status = (MapObjectStatus)data.Byte(entry + 30);
                objects.Add(ReadObject(data, version, slot, status, position));
            }

            block = next;
        }

        return objects;
    }

    private static MapObject ReadObject(Bytes data, int version, int slot, MapObjectStatus status, int at)
    {
        int symbol = data.Int32(at);
        byte objectType = data.Byte(at + 4);
        short angle = data.Int16(at + 6);

        int pointCount, textLength, pointsAt;
        DateTime? created = null, modified = null;

        if (version >= 12) {
            created = OleDate(data.Double(at + 24));
            modified = OleDate(data.Double(at + 36));
            pointCount = data.Int32(at + 44);
            textLength = data.Int16(at + 48);
            pointsAt = at + 56;
        }
        else {
            pointCount = data.Int32(at + 8);
            textLength = data.Int16(at + 12);
            if (version >= 10)
                created = OleDate(data.Double(at + 32));
            pointsAt = at + 40;
        }

        if (pointCount < 0 || textLength < 0)
            throw new InvalidDataException($"Object {slot} has a negative length.");

        var points = new MapPoint[pointCount];
        for (int i = 0; i < pointCount; i++) {
            int x = data.Int32(pointsAt + i * 8);
            int y = data.Int32(pointsAt + i * 8 + 4);
            points[i] = new MapPoint(
                X: x >> 8,
                Y: y >> 8,
                IsCurveControl: (x & 0b11) != 0,
                StartsHole: (y & 0b10) != 0);
        }

        string text = ReadText(data, pointsAt + pointCount * 8, textLength);

        return new MapObject(slot, status, symbol, KindOf(objectType), angle / 10.0,
                             text, created, modified, points);
    }

    /// <summary>OCAD's object types, reduced to what the diff can measure.</summary>
    private static MapObjectKind KindOf(byte objectType) => objectType switch {
        1 => MapObjectKind.Point,
        2 => MapObjectKind.Line,
        3 => MapObjectKind.Area,
        4 => MapObjectKind.Text,
        _ => MapObjectKind.Other,
    };

    /// <summary>
    /// Text is stored in 8-byte units of UTF-16, zero-terminated within them.
    /// </summary>
    private static string ReadText(Bytes data, int at, int units)
    {
        if (units == 0)
            return null;

        string text = Encoding.Unicode.GetString(data.Slice(at, units * 8));
        int end = text.IndexOf('\0');
        return end < 0 ? text : text[..end];
    }

    /// <summary>
    /// OCAD stores dates as OLE automation dates: days since 30 December 1899.
    /// Zero, which OCAD writes when it has no date, and anything outside what a
    /// <see cref="DateTime"/> can hold, become null rather than a misleading year.
    /// </summary>
    private static DateTime? OleDate(double value)
    {
        if (value <= 0 || double.IsNaN(value) || value > 2958465)
            return null;

        return DateTime.SpecifyKind(DateTime.FromOADate(value), DateTimeKind.Utc);
    }

    /// <summary>Bounds-checked little-endian reads, so a short file fails loudly.</summary>
    private readonly struct Bytes(byte[] file)
    {
        public byte Byte(int at) => Slice(at, 1)[0];
        public short Int16(int at) => BinaryPrimitives.ReadInt16LittleEndian(Slice(at, 2));
        public int Int32(int at) => BinaryPrimitives.ReadInt32LittleEndian(Slice(at, 4));
        public double Double(int at) => BinaryPrimitives.ReadDoubleLittleEndian(Slice(at, 8));

        public ReadOnlySpan<byte> Slice(int at, int length)
        {
            if (at < 0 || length < 0 || at > file.Length - length)
                throw new InvalidDataException(
                    $"The OCAD file is cut short: it ends before byte {at + length}.");

            return file.AsSpan(at, length);
        }
    }
}
