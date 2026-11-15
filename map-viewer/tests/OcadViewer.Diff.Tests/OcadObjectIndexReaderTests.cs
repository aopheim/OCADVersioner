namespace OcadViewer.Diff.Tests;

[TestFixture]
public sealed class OcadObjectIndexReaderTests
{
    private static IReadOnlyList<MapObject> NormalObjectsIn(string fixture)
        => [.. OcadObjectIndexReader.Read(Fixture.Bytes(fixture)).Where(o => o.Status == MapObjectStatus.Normal)];

    [Test]
    public void AKnollIsOnePointObjectWithTheKnollSymbol()
    {
        IReadOnlyList<MapObject> objects = NormalObjectsIn(Fixture.OneKnoll);

        Assert.That(objects, Has.Count.EqualTo(1));
        Assert.Multiple(() => {
            Assert.That(objects[0].SymbolNumber, Is.EqualTo(109000));
            Assert.That(objects[0].Kind, Is.EqualTo(MapObjectKind.Point));
            Assert.That(objects[0].Points, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ObjectIndexesStartAtOne()
    {
        // OCAD numbers index slots from 1, and the diff pairs objects by slot,
        // so an off-by-one here would silently pair nothing with anything.
        Assert.That(NormalObjectsIn(Fixture.OneKnoll)[0].Index, Is.EqualTo(1));
    }

    [Test]
    public void TimestampsAreReadAsUtcDates()
    {
        MapObject knoll = NormalObjectsIn(Fixture.OneKnoll)[0];

        Assert.Multiple(() => {
            Assert.That(knoll.CreatedUtc, Is.Not.Null);
            Assert.That(knoll.CreatedUtc.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            // The fixtures were drawn in late 2023; anything far off means the
            // OLE-date conversion or the field offset is wrong.
            Assert.That(knoll.CreatedUtc.Value.Year, Is.InRange(2020, 2030));
            Assert.That(knoll.ModifiedUtc, Is.Not.Null);
            Assert.That(knoll.ModifiedUtc, Is.GreaterThanOrEqualTo(knoll.CreatedUtc));
        });
    }

    [Test]
    public void PointLineAreaHasOneObjectOfEachKind()
    {
        IReadOnlyList<MapObject> objects = NormalObjectsIn(Fixture.PointLineArea);

        Assert.That(objects.Select(o => o.Kind),
                    Is.EquivalentTo(new[] { MapObjectKind.Point, MapObjectKind.Line, MapObjectKind.Area }));
    }

    [Test]
    public void MovedObjectsKeepTheirSlotAndCreationDate()
    {
        IReadOnlyList<MapObject> before = NormalObjectsIn(Fixture.PointLineArea);
        IReadOnlyList<MapObject> after = NormalObjectsIn(Fixture.PointLineAreaMoved);

        Assert.That(after.Select(o => (o.Index, o.CreatedUtc)),
                    Is.EquivalentTo(before.Select(o => (o.Index, o.CreatedUtc))),
                    "The diff's whole pairing rule rests on OCAD keeping an object in its slot when it is edited.");
    }

    [Test]
    public void DeletedObjectsAreNotNormal()
    {
        Assert.That(NormalObjectsIn(Fixture.PointLineAreaDeleted), Is.Empty);
    }

    [Test]
    public void SomethingThatIsNotAnOcadFileIsRejected()
    {
        byte[] notOcad = "this is not a map, just some text"u8.ToArray();

        Assert.That(() => OcadObjectIndexReader.Read(notOcad), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ATruncatedFileIsRejectedRatherThanReadPastItsEnd()
    {
        byte[] truncated = Fixture.Bytes(Fixture.PointLineArea)[..64];

        Assert.That(() => OcadObjectIndexReader.Read(truncated), Throws.TypeOf<InvalidDataException>());
    }
}
