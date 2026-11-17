using OcadViewer.Ocd;
using OcadViewer.Merge.Matching;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class ContentComparerTests
{
    private static readonly ContentComparer Comparer = new(toleranceUnits: 1);

    private static OcadObject Find(OcadFile file, DateTime created)
        => file.Objects.Single(o => o.CreatedAt == created && o.Coords.Count > 0);

    [Test]
    public void AnObjectIsTheSameAsItself()
    {
        OcadObject lake = Find(Fixtures.Base, Fixtures.Created.Lake);

        Assert.That(Comparer.Same(lake, lake), Is.True);
    }

    [Test]
    public void AReshapedObjectHasChanged()
        => Assert.That(Comparer.Same(Find(Fixtures.Base, Fixtures.Created.Lake), Find(Fixtures.Kari, Fixtures.Created.Lake)), Is.False);

    [Test]
    public void AMovedPointHasChanged()
        => Assert.That(Comparer.Same(Find(Fixtures.Base, Fixtures.Created.EastBoulder), Find(Fixtures.Ola, Fixtures.Created.EastBoulder)), Is.False);

    [Test]
    public void ANewModificationDateAloneIsNotAChange()
    {
        // Ola's footpath differs from base only in its modification date.
        OcadObject inBase = Find(Fixtures.Base, Fixtures.Created.Footpath);
        OcadObject inOla = Find(Fixtures.Ola, Fixtures.Created.Footpath);

        Assert.That(inOla.ModifiedAt, Is.Not.EqualTo(inBase.ModifiedAt));
        Assert.That(Comparer.Same(inBase, inOla), Is.True);
    }

    [Test]
    public void MovesWithinTheToleranceAreNotChanges()
    {
        OcadObject knoll = Find(Fixtures.Base, Fixtures.Created.Knoll);
        OcadObject nudged = Fixtures.WithCoords(knoll, c => c with { X = c.X + 1 });
        OcadObject moved = Fixtures.WithCoords(knoll, c => c with { X = c.X + 2 });

        Assert.Multiple(() => {
            Assert.That(Comparer.Same(knoll, nudged), Is.True);
            Assert.That(Comparer.Same(knoll, moved), Is.False);
        });
    }

    [Test]
    public void ToleranceIsMetresOnTheGroundConvertedWithTheMapScale()
    {
        // 0.1 m at 1:10 000 is 0.01 mm on paper: one coordinate unit.
        Assert.Multiple(() => {
            Assert.That(MergeOptions.Default.ToleranceUnits(10000), Is.EqualTo(1));
            Assert.That(MergeOptions.Default.ToleranceUnits(4000), Is.EqualTo(2));
            Assert.That(new MergeOptions { OverlapBufferMeters = 10 }.OverlapBufferUnits(10000), Is.EqualTo(100));
        });
    }

    [Test]
    public void ASymbolWithANewDescriptionHasChanged()
    {
        OcadSymbol inBase = Fixtures.Base.Symbols.Single(s => s.Number == 109000);
        OcadSymbol inKari = Fixtures.Kari.Symbols.Single(s => s.Number == 109000);

        Assert.That(Comparer.Same(inBase, inKari), Is.False);
    }

    [Test]
    public void UntouchedSymbolsAreTheSame()
    {
        OcadSymbol inBase = Fixtures.Base.Symbols.Single(s => s.Number == 401000);
        OcadSymbol inKari = Fixtures.Kari.Symbols.Single(s => s.Number == 401000);

        Assert.That(Comparer.Same(inBase, inKari), Is.True);
    }
    [TestCase(SettingType.Template, "contours.ocd\ts1\tr1\td50", "contours.ocd\ts0\tr1\td50")]
    [TestCase(SettingType.TerrainModel, "map.ocdDem\tidata\tl1\tf0", "map.ocdDem\tidata\tl0\tf0")]
    [TestCase(SettingType.PointCloud, "map.ocdLas\tl1\tf1", "map.ocdLas\tl0\tf1")]
    public void FlagsOcadSetsWhenItOpensTheMapAreNotChanges(int type, string inBase, string opened)
        // Opening the map on another computer flips these: seen on a real map
        // where both mappers' copies differed from base only here.
        => Assert.That(Comparer.Same(Setting(type, inBase), Setting(type, opened)), Is.True);

    [Test]
    public void ABackgroundMapWithANewDimmingHasChanged()
        => Assert.That(Comparer.Same(Setting(SettingType.Template, "contours.ocd\ts1\td50"),
                                     Setting(SettingType.Template, "contours.ocd\ts1\td40")), Is.False);

    [Test]
    public void ColoursAreComparedWhole()
        => Assert.That(Comparer.Same(Setting(SettingType.Color, "Brown\tn5\ts1"),
                                     Setting(SettingType.Color, "Brown\tn5\ts0")), Is.False);

    private static OcadSetting Setting(int type, string text)
        => new(null, 0, type, 0, System.Text.Encoding.Latin1.GetBytes(text));
}
