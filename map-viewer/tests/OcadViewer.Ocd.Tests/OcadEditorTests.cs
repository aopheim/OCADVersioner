namespace OcadViewer.Ocd.Tests;

[TestFixture]
public sealed class OcadEditorTests
{
    private static OcadObject Lake(OcadFile file) => file.Objects.Single(o => o.CreatedAt == Fixtures.Created.Lake);

    private static OcadObject Knoll(OcadFile file) => file.Objects.Single(o => o.CreatedAt == Fixtures.Created.Knoll);

    private static OcadFile Edit(OcadFile file, Action<OcadEditor> edit)
    {
        OcadEditor editor = new(file);
        edit(editor);
        return OcadFile.Read(editor.ToArray());
    }

    [TestCase("base.ocd")]
    [TestCase("base_v12.ocd")]
    [TestCase("base_v11.ocd")]
    public void WithoutChangesTheFileComesOutByteForByte(string name)
        => Assert.That(new OcadEditor(Fixtures.Read(name)).ToArray(), Is.EqualTo(Fixtures.Bytes(name)));

    [Test]
    public void EditingNeverChangesTheFilesItReadsFrom()
    {
        byte[] baseBytes = Fixtures.Bytes("base.ocd"), kariBytes = Fixtures.Bytes("kari.ocd");
        byte[] baseBefore = [.. baseBytes], kariBefore = [.. kariBytes];
        OcadFile @base = OcadFile.Read(baseBytes), kari = OcadFile.Read(kariBytes);

        OcadEditor editor = new(@base);
        editor.Replace(Lake(@base), Lake(kari));
        editor.Add(Knoll(kari).WithCreatedAt(new DateTime(2026, 11, 17, 12, 0, 0)));
        editor.Delete(Knoll(@base));
        editor.Replace(@base.Symbols.Single(s => s.Number == 109000), kari.Symbols.Single(s => s.Number == 109000));
        editor.Delete(@base.Settings.First(s => s.Type == SettingType.Color));
        editor.Add(kari.Settings.First(s => s.Type == SettingType.Color));
        editor.ToArray();

        Assert.Multiple(() => {
            Assert.That(baseBytes, Is.EqualTo(baseBefore));
            Assert.That(kariBytes, Is.EqualTo(kariBefore));
        });
    }

    [Test]
    public void AReplacedObjectReadsBackAsTheReplacement()
    {
        OcadFile @base = Fixtures.Base, kari = Fixtures.Kari;

        OcadFile edited = Edit(@base, e => e.Replace(Lake(@base), Lake(kari)));

        Assert.Multiple(() => {
            Assert.That(Lake(edited).Coords, Is.EqualTo(Lake(kari).Coords));
            Assert.That(Lake(edited).Entry, Is.EqualTo(Lake(@base).Entry), "a replacement keeps its entry");
        });
    }

    [Test]
    public void ADeletedObjectIsGone()
    {
        OcadFile @base = Fixtures.Base;

        OcadFile edited = Edit(@base, e => e.Delete(Knoll(@base)));

        Assert.That(edited.Objects.Select(o => o.CreatedAt), Has.None.EqualTo(Fixtures.Created.Knoll));
        Assert.That(edited.Objects, Has.Count.EqualTo(@base.Objects.Count - 1));
    }

    [Test]
    public void AnAddedCopyWithANewCreationDateSitsBesideItsOriginal()
    {
        OcadFile @base = Fixtures.Base;
        DateTime copied = new(2026, 11, 17, 12, 0, 0, 1);

        OcadFile edited = Edit(@base, e => e.Add(Knoll(@base).WithCreatedAt(copied)));

        OcadObject copy = edited.Objects.Single(o => o.CreatedAt == copied);
        Assert.Multiple(() => {
            Assert.That(Knoll(edited).Coords, Is.EqualTo(Knoll(@base).Coords));
            Assert.That(copy.Coords, Is.EqualTo(Knoll(@base).Coords));
            Assert.That(copy.Entry, Is.Not.EqualTo(Knoll(@base).Entry));
        });
    }

    [Test]
    public void AddingMoreObjectsThanTheIndexHoldsGrowsTheIndex()
    {
        OcadFile @base = Fixtures.Base;
        DateTime start = new(2026, 11, 17, 12, 0, 0);

        OcadFile edited = Edit(@base, e => {
            for (int i = 0; i < 300; i++)
                e.Add(Knoll(@base).WithCreatedAt(start.AddMilliseconds(i)));
        });

        Assert.That(edited.Objects, Has.Count.EqualTo(@base.Objects.Count + 300));
    }

    [Test]
    public void AReplacedSymbolReadsBackAsTheReplacement()
    {
        OcadFile @base = Fixtures.Base, kari = Fixtures.Kari;
        OcadSymbol knoll = @base.Symbols.Single(s => s.Number == 109000);

        OcadFile edited = Edit(@base, e => e.Replace(knoll, kari.Symbols.Single(s => s.Number == 109000)));

        Assert.That(edited.Symbols.Single(s => s.Number == 109000).Description, Is.EqualTo("Small knoll, min 1 m (Kari)"));
    }

    [Test]
    public void SettingsCanBeDeletedAndAdded()
    {
        OcadFile @base = Fixtures.Base;
        OcadSetting colour = @base.Settings.First(s => s.Type == SettingType.Color);

        OcadFile deleted = Edit(@base, e => e.Delete(colour));
        OcadFile restored = Edit(deleted, e => e.Add(colour));

        Assert.Multiple(() => {
            Assert.That(deleted.Settings.Select(s => s.Text), Has.None.EqualTo(colour.Text));
            Assert.That(restored.Settings.Select(s => s.Text), Has.One.EqualTo(colour.Text));
        });
    }

    [Test]
    public void OnlyWhatWasReadFromTheEditedFileCanBeReplacedOrDeleted()
    {
        OcadFile @base = Fixtures.Base, kari = Fixtures.Kari;
        OcadEditor editor = new(@base);

        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(() => editor.Delete(Knoll(kari)));
            Assert.Throws<ArgumentException>(() => editor.Replace(Lake(kari), Lake(kari)));
            Assert.Throws<ArgumentException>(() => editor.Delete(kari.Symbols[0]));
            Assert.Throws<ArgumentException>(() => editor.Delete(kari.Settings[0]));
        });
    }

    [Test]
    public void ObjectsFromAnotherOcadVersionAreRefused()
    {
        OcadFile @base = Fixtures.Base, v12 = Fixtures.Read("base_v12.ocd");

        Assert.Throws<ArgumentException>(() => new OcadEditor(@base).Add(Knoll(v12)));
    }

    [Test]
    public void MovingAnObjectKeepsItsNumberOfCoordinates()
    {
        OcadObject lake = Lake(Fixtures.Base);

        Assert.Multiple(() => {
            Assert.That(lake.WithCoords([.. lake.Coords.Select(c => c with { X = c.X + 5 })]).Coords.Select(c => c.X),
                        Is.EqualTo(lake.Coords.Select(c => c.X + 5)));
            Assert.Throws<ArgumentException>(() => lake.WithCoords([.. lake.Coords.Skip(1)]));
        });
    }
}
