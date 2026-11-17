namespace OcadViewer.Ocd.Tests;

/// <summary>
/// Damaged files are refused with <see cref="OcadFormatException"/>, never read
/// half-way or crash the reader. Each test breaks base.ocd in one place.
/// </summary>
[TestFixture]
public sealed class DamagedFileTests
{
    private static byte[] Broken(Action<byte[], OcadFile> breakIt)
    {
        byte[] bytes = Fixtures.Bytes("base.ocd");
        breakIt(bytes, OcadFile.Read(Fixtures.Bytes("base.ocd")));
        return bytes;
    }

    private static void WriteInt(byte[] bytes, int offset, int value) => BitConverter.TryWriteBytes(bytes.AsSpan(offset), value);

    [Test]
    public void AnIndexBlockOutsideTheFile()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Broken((bytes, _)
            => WriteInt(bytes, FileLayout.FirstObjectBlockOffset, bytes.Length + 100))));

    [Test]
    public void IndexBlocksThatFormALoop()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Broken((bytes, file)
            => WriteInt(bytes, file.ObjectIndex.Blocks[0], file.ObjectIndex.Blocks[0]))));

    [Test]
    public void AnObjectRecordOutsideTheFile()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Broken((bytes, file)
            => WriteInt(bytes, file.ObjectIndex.EntryOffset(0) + FileLayout.IndexPosOffset, bytes.Length - 10))));

    [Test]
    public void AnObjectClaimingMoreCoordinatesThanItsRecordHolds()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Broken((bytes, file) => {
            int record = BitConverter.ToInt32(bytes, file.ObjectIndex.EntryOffset(0) + FileLayout.IndexPosOffset);
            WriteInt(bytes, record + file.Layout.ObjectCoordCountOffset, 1_000_000);
        })));

    [Test]
    public void ASymbolRecordOutsideTheFile()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Broken((bytes, file)
            => WriteInt(bytes, file.SymbolIndex.EntryOffset(0), bytes.Length - 4))));

    [Test]
    public void AParameterStringOutsideTheFile()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Broken((bytes, file)
            => WriteInt(bytes, file.StringIndex.EntryOffset(0) + FileLayout.StringLenOffset, bytes.Length))));
}
