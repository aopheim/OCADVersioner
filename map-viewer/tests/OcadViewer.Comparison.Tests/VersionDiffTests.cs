using OcadViewer.Ocd;

namespace OcadViewer.Comparison.Tests;

/// <summary>
/// The two-way diff on the files in tests/TestData/diff, drawn in OCAD. What each
/// contains is described where it is used.
/// </summary>
[TestFixture]
public sealed class VersionDiffTests
{
    private static OcadFile PointLineArea => Fixtures.Diff("point_line_area.ocd");
    private static OcadFile Moved => Fixtures.Diff("point_line_area_moved.ocd");

    [Test]
    public void AVersionComparedWithItselfHasNoChanges()
    {
        VersionDiff diff = VersionDiff.Compare(PointLineArea, PointLineArea);

        Assert.Multiple(() => {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
            Assert.That(diff.DateShift, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void WithNoOlderVersionEveryObjectIsAdded()
    {
        VersionDiff diff = VersionDiff.Compare(null, PointLineArea);

        Assert.Multiple(() => {
            Assert.That(diff.Added.Select(a => a.Object.Type),
                        Is.EquivalentTo(new[] { ObjectType.Point, ObjectType.Line, ObjectType.Area }));
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AnAddedObjectCarriesItsSymbolName()
    {
        ObjectOnOneSide knoll = VersionDiff.Compare(null, Fixtures.Diff("1knoll.ocd")).Added.Single();

        Assert.Multiple(() => {
            Assert.That(knoll.Object.Symbol, Is.EqualTo(109000));
            Assert.That(knoll.SymbolDescription, Is.Not.Empty);
        });
    }

    [Test]
    public void LinesHaveALengthAreasAnAreaAndPointsNeither()
    {
        IReadOnlyList<ObjectOnOneSide> added = VersionDiff.Compare(null, PointLineArea).Added;
        ObjectOnOneSide line = added.Single(a => a.Object.Type == ObjectType.Line);
        ObjectOnOneSide area = added.Single(a => a.Object.Type == ObjectType.Area);
        ObjectOnOneSide point = added.Single(a => a.Object.Type == ObjectType.Point);

        Assert.Multiple(() => {
            Assert.That(line.LengthInMeters, Is.GreaterThan(0));
            Assert.That(line.AreaInSquareMeters, Is.Null);
            Assert.That(area.AreaInSquareMeters, Is.GreaterThan(0));
            Assert.That(area.LengthInMeters, Is.Null);
            Assert.That(point.LengthInMeters, Is.Null);
            Assert.That(point.AreaInSquareMeters, Is.Null);
        });
    }

    [Test]
    public void DeletingEverythingGivesThreeDeletedObjectsThatKeepTheirSize()
    {
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_deleted.ocd"));

        Assert.Multiple(() => {
            Assert.That(diff.Deleted, Has.Count.EqualTo(3));
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Deleted.Single(d => d.Object.Type == ObjectType.Line).LengthInMeters, Is.GreaterThan(0));
            Assert.That(diff.Deleted.Single(d => d.Object.Type == ObjectType.Area).AreaInSquareMeters, Is.GreaterThan(0));
        });
    }

    [Test]
    public void MovingAllThreeObjectsGivesThreeGeometryChanges()
    {
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Moved);

        Assert.Multiple(() => {
            Assert.That(diff.Changed, Has.Count.EqualTo(3));
            Assert.That(diff.Changed.Select(c => c.Changes.HasFlag(ObjectFields.Coordinates)), Is.All.True);
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AMovedPointReportsHowFarItMovedInMetres()
    {
        // The knoll moved 5.18 mm on paper at 1:15 000.
        ObjectChanged knoll = VersionDiff.Compare(PointLineArea, Moved).Changed.Single(c => c.Newer.Type == ObjectType.Point);

        Assert.Multiple(() => {
            Assert.That(knoll.MovementInMeters, Is.EqualTo(77.7).Within(0.1));
            Assert.That(knoll.LengthChangeInPercent, Is.Null);
            Assert.That(knoll.AreaChangeInPercent, Is.Null);
        });
    }

    [Test]
    public void AnEditedLineAndAreaReportTheirChangeInPercent()
    {
        IReadOnlyList<ObjectChanged> changed = VersionDiff.Compare(PointLineArea, Moved).Changed;
        ObjectChanged line = changed.Single(c => c.Newer.Type == ObjectType.Line);
        ObjectChanged area = changed.Single(c => c.Newer.Type == ObjectType.Area);

        Assert.Multiple(() => {
            Assert.That(line.LengthChangeInPercent, Is.EqualTo(47.6).Within(0.1));
            Assert.That(line.AreaChangeInPercent, Is.Null);
            Assert.That(area.AreaChangeInPercent, Is.EqualTo(20.9).Within(0.1));
            Assert.That(area.LengthChangeInPercent, Is.Null);
        });
    }

    [Test]
    public void AChangedObjectIsBoundedByBothPositions()
    {
        ObjectChanged knoll = VersionDiff.Compare(PointLineArea, Moved).Changed.Single(c => c.Newer.Type == ObjectType.Point);

        Assert.That(knoll.Bounds, Is.EqualTo(new Bounds(-267, 95, 250, 128)));
    }

    [Test]
    public void ASymbolChangeIsReportedAsOne()
    {
        // The area changed from 403.0 to 406.0 in OCAD.
        ObjectChanged area = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_406.ocd")).Changed.Single();

        Assert.Multiple(() => {
            Assert.That(area.Changes.HasFlag(ObjectFields.Symbol), Is.True);
            Assert.That(area.Changes.HasFlag(ObjectFields.Coordinates), Is.False);
            Assert.That(area.Older.Symbol, Is.EqualTo(403000));
            Assert.That(area.Newer.Symbol, Is.EqualTo(406000));
        });
    }

    [Test]
    public void ARotatedObjectIsAnAngleChange()
    {
        ObjectChanged knoll = VersionDiff.Compare(Fixtures.Diff("1knoll.ocd"), Fixtures.Diff("1knoll_rotated.ocd")).Changed.Single();

        Assert.Multiple(() => {
            Assert.That(knoll.Changes.HasFlag(ObjectFields.Angle), Is.True);
            Assert.That(knoll.Changes.HasFlag(ObjectFields.Coordinates), Is.False);
        });
    }

    [Test]
    public void AnEditedTextIsATextChange()
    {
        ObjectChanged text = VersionDiff.Compare(Fixtures.Diff("point_line_area_text.ocd"),
                                                 Fixtures.Diff("point_line_area_text_edited.ocd")).Changed.Single();

        Assert.That(text.Changes.HasFlag(ObjectFields.Text), Is.True);
    }

    [Test]
    public void HidingASymbolInTheSymbolBoxChangesNoObject()
    {
        // Symbol 403.0 was hidden with Symbol, Hide Objects: a view setting.
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_symbol_hidden.ocd"));

        Assert.Multiple(() => {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AMapImportedIntoANewFileHasNoChanges()
    {
        // The same three objects, renumbered, creation dates kept.
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_imported.ocd"));

        Assert.Multiple(() => {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
        });
    }

    [Test]
    public void ObjectsPastedIntoANewFileAreChangesNotNewObjects()
    {
        // Renumbered, creation dates kept, every object shifted by the paste.
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_pasted.ocd"));

        Assert.Multiple(() => {
            Assert.That(diff.Changed, Has.Count.EqualTo(3));
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AMapThatOcadSavedWithEveryDateAnHourEarlierHasNoChanges()
    {
        // OCAD App Data Exchange: renumbered, every date one hour earlier.
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_app_exchange.ocd"));

        Assert.Multiple(() => {
            Assert.That(diff.Added, Is.Empty);
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
            Assert.That(diff.DateShift, Is.EqualTo(TimeSpan.FromHours(-1)));
        });
    }

    [Test]
    public void ADuplicatedObjectIsAddedAndTheOriginalUnchanged()
    {
        // The knoll duplicated: a new entry with the original's creation date.
        VersionDiff diff = VersionDiff.Compare(PointLineArea, Fixtures.Diff("point_line_area_duplicated.ocd"));

        Assert.Multiple(() => {
            Assert.That(diff.Added.Select(a => a.Object.Type), Is.EqualTo(new[] { ObjectType.Point }));
            Assert.That(diff.Changed, Is.Empty);
            Assert.That(diff.Deleted, Is.Empty);
        });
    }

    [Test]
    public void EverythingDrawnOnABlankMapIsAddedAndEverythingErasedIsDeleted()
    {
        OcadFile blank = Fixtures.Diff("blank.ocd");

        Assert.Multiple(() => {
            Assert.That(VersionDiff.Compare(blank, PointLineArea).Added, Has.Count.EqualTo(3));
            Assert.That(VersionDiff.Compare(PointLineArea, blank).Deleted, Has.Count.EqualTo(3));
        });
    }
}
