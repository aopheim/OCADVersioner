using OcadViewer.Merge.Format;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class OcadFileTests
{
    [Test]
    public void ReadsTheLiveObjectsOfBase()
    {
        int[] symbols = [.. Fixtures.Base.Objects.Select(o => o.Symbol)];

        Assert.That(symbols, Is.EqualTo(new[] { 401000, 301000, 406000, 507000, 101000, 204000, 204000, 109000 }));
    }

    [Test]
    public void SkipsRecordsOcadMarkedDeleted()
    {
        // Entry 2 holds the forest that was deleted while drawing base: the record is
        // still in the file, flagged with index status 3.
        Assert.That(Fixtures.Base.Objects.Select(o => o.Entry), Has.None.EqualTo(2));
    }

    [Test]
    public void ReadsCreationDatesToTheMillisecond()
    {
        OcadObject knoll = Fixtures.Base.Objects.Single(o => o.Symbol == 109000);

        Assert.That(knoll.CreatedAt, Is.EqualTo(Fixtures.Created.Knoll));
    }

    [Test]
    public void ReadsCoordinatesInHundredthsOfAMillimetre()
    {
        OcadObject knoll = Fixtures.Base.Objects.Single(o => o.Symbol == 109000);

        Assert.That(knoll.Coords.Select(c => (c.X, c.Y)), Is.EqualTo(new[] { (1511, -400) }));
    }

    [Test]
    public void ReadsObjectTypes()
    {
        OcadFile file = Fixtures.Base;

        Assert.Multiple(() => {
            Assert.That(file.Objects.Single(o => o.Symbol == 301000).Type, Is.EqualTo(ObjectType.Area));
            Assert.That(file.Objects.Single(o => o.Symbol == 507000).Type, Is.EqualTo(ObjectType.Line));
            Assert.That(file.Objects.Single(o => o.Symbol == 109000).Type, Is.EqualTo(ObjectType.Point));
        });
    }

    [Test]
    public void ReadsTheMapScale() => Assert.That(Fixtures.Base.Scale, Is.EqualTo(10000));

    [Test]
    public void ReadsSymbolDefinitions()
    {
        Assert.Multiple(() => {
            Assert.That(Fixtures.Base.Symbols.Single(s => s.Number == 109000).Description, Is.EqualTo("Small knoll"));
            Assert.That(Fixtures.Kari.Symbols.Single(s => s.Number == 109000).Description,
                        Is.EqualTo("Small knoll, min 1 m (Kari)"));
        });
    }

    [Test]
    public void ReadsParameterStrings()
        => Assert.That(Fixtures.Base.Settings.Select(s => s.Type), Does.Contain(1039));

    [TestCase("base_v12.ocd", 12)]
    [TestCase("base_v11.ocd", 11)]
    [TestCase("base.ocd", 2018)]
    public void ReadsEverySupportedFormatAlike(string name, int version)
    {
        OcadFile file = Fixtures.Read(name);

        Assert.That(file.Version, Is.EqualTo(version));
        Assert.That(Describe(file), Is.EqualTo(Describe(Fixtures.Base)));
    }

    [Test]
    public void Ocad11CopiesWrittenByOcad2020CarryNoCreationDates()
    {
        // OCAD 2020's "Save As OCAD 11" zeroes the dates, so identity in such files
        // can only come from geometry. The matcher has to cope.
        Assert.That(Fixtures.Read("base_v11.ocd").Objects.Select(o => o.CreatedAt), Is.All.Null);
    }

    [Test]
    public void Ocad12CopiesKeepCreationDates()
        => Assert.That(Fixtures.Read("base_v12.ocd").Objects.Select(o => o.CreatedAt),
                       Is.EqualTo(Fixtures.Base.Objects.Select(o => o.CreatedAt)));

    [Test]
    public void RejectsWhatIsNotAnOcadFile()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(new byte[200]));

    [Test]
    public void RejectsATruncatedFile()
        => Assert.Throws<OcadFormatException>(() => OcadFile.Read(Fixtures.Bytes("base.ocd")[..4000]));

    private static string[] Describe(OcadFile file)
        => [.. file.Objects.Select(o => $"{o.Symbol} {o.Type} {string.Join(';', o.Coords.Select(c => $"{c.X},{c.Y}"))}")];
}
