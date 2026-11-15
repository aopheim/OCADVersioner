namespace OcadViewer.Diff.Tests;

/// <summary>
/// What OCAD's own commands do to the object index, and what the diff makes of it.
/// </summary>
/// <remarks>
/// <para>
/// Every fixture here was made in OCAD 2020.8 from <c>point_line_area</c> by one
/// command, then saved; <see cref="Fixture"/> says what each one holds. They pin
/// down behaviour found by trying the commands, which the patched fixtures could
/// only guess at:
/// </para>
/// <list type="bullet">
///   <item>Opening File, OCAD App Data Exchange saves the map, and that save
///   renumbers every object and moves every date from local time to UTC. That is
///   how Bymarkabasen's working file was renumbered between V1 and V2, with dates
///   two hours earlier in June.</item>
///   <item>Map, Optimize / Repair renumbers as well, but keeps the dates.</item>
///   <item>Cutting a line and duplicating an object both make a new entry that
///   carries the original's creation date. The date alone is no proof of identity.</item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class OcadCommandTests
{
    private readonly IOcadDiffService diff = new OcadDiffService();

    private ObjectDiff Compare(string older, string newer)
        => diff.Compare(Fixture.Snapshot(older), Fixture.Snapshot(newer));

    /* ------------------------------------------------- renumbered by a save */

    [Test]
    public void TheSaveForTheOcadAppIsNotAChange()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaAppExchange);

        Assert.That(result.IsEmpty, Is.True);
    }

    [Test]
    public void TheSaveForTheOcadAppRenumbersAndShiftsTheDatesToUtc()
    {
        // What the matcher has to see through, as OCAD wrote it.
        ObjectMatcher.Result match = ObjectMatcher.Match(Fixture.Snapshot(Fixture.PointLineArea).Objects,
                                                         Fixture.Snapshot(Fixture.PointLineAreaAppExchange).Objects);

        Assert.Multiple(() => {
            Assert.That(match.Pairs.Select(p => (p.Then.Index, p.Now.Index)),
                        Is.EquivalentTo(new[] { (3, 1), (4, 2), (5, 3) }));
            Assert.That(match.DateShift, Is.EqualTo(TimeSpan.FromHours(-1)), "Made in November, at UTC+1.");
            Assert.That(match.EntriesHeld, Is.False);
        });
    }

    [Test]
    public void OptimizeAndRepairIsNotAChange()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaOptimized);

        Assert.That(result.IsEmpty, Is.True);
    }

    /* ------------------------------------- a new entry with an old creation date */

    [Test]
    public void ALineCutInTwoIsAnEditAndANewLine()
    {
        // Entry 6 has the contour's creation date. Entry 3 is still the contour.
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaCut);

        Assert.Multiple(() => {
            Assert.That(result.Edited.Select(e => (e.Index, e.Symbol.Number, e.Changes)),
                        Is.EqualTo(new[] { (3, "101.0", ObjectChanges.Geometry) }));
            Assert.That(result.Added.Select(a => (a.Index, a.Symbol.Number)), Is.EqualTo(new[] { (6, "101.0") }));
            Assert.That(result.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AModificationDateAloneIsNotAnEdit()
    {
        // A Cut Area that OCAD refused still stamped the area with a new
        // modification date. Its content is the same, so it is not an edit.
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaCut);

        Assert.That(result.Edited.Select(e => e.Index), Does.Not.Contain(5));
    }

    [Test]
    public void ADuplicateIsANewObject()
    {
        // Entry 6 has the original knoll's creation date; the original is untouched.
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaDuplicated);

        Assert.Multiple(() => {
            Assert.That(result.Added.Select(a => (a.Index, a.Symbol.Number)), Is.EqualTo(new[] { (6, "109.0") }));
            Assert.That(result.Edited, Is.Empty);
            Assert.That(result.Deleted, Is.Empty);
        });
    }

    /* ------------------------------------------------------------------ text */

    [Test]
    public void ANewTextIsAdded()
    {
        ObjectAdded text = Compare(Fixture.PointLineArea, Fixture.PointLineAreaText).Added.Single();

        Assert.Multiple(() => {
            Assert.That(text.Kind, Is.EqualTo(MapObjectKind.Text));
            Assert.That(text.Symbol.Number, Is.EqualTo("901.0"));
        });
    }

    [Test]
    public void EditedWordingIsATextChange()
    {
        // OCAD stores a text as its anchor point and the four corners of its box.
        // A longer word widens the box, so the geometry changes with the wording;
        // the anchor stays put.
        ObjectDiff result = Compare(Fixture.PointLineAreaText, Fixture.PointLineAreaTextEdited);
        MapPoint[] before = Fixture.Snapshot(Fixture.PointLineAreaText).Objects[6].Points.ToArray();
        MapPoint[] after = Fixture.Snapshot(Fixture.PointLineAreaTextEdited).Objects[6].Points.ToArray();

        Assert.Multiple(() => {
            Assert.That(result.Edited.Select(e => (e.Index, e.Changes)),
                        Is.EqualTo(new[] { (6, ObjectChanges.Text | ObjectChanges.Geometry) }));
            Assert.That(after[0], Is.EqualTo(before[0]), "The anchor.");
            Assert.That(after.Length, Is.EqualTo(5));
        });
    }

    [Test]
    public void TheTextIsReadAsWritten()
    {
        Assert.Multiple(() => {
            Assert.That(Fixture.Snapshot(Fixture.PointLineAreaText).Objects[6].Text, Is.EqualTo("Knoll"));
            Assert.That(Fixture.Snapshot(Fixture.PointLineAreaTextEdited).Objects[6].Text, Is.EqualTo("Knolls"));
        });
    }

    /* --------------------------------------------------------------- symbols */

    [Test]
    public void HidingASymbolIsNoObjectChange()
    {
        // The area no longer draws, but it is untouched; only symbol 403.0's status
        // changed. That belongs to the symbol diff, which is not written yet.
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaSymbolHidden);

        Assert.That(result.IsEmpty, Is.True);
    }
}
