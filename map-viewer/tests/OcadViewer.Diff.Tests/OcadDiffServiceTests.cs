namespace OcadViewer.Diff.Tests;

/// <summary>
/// The object diff, version against version, using the fixtures as OCAD saved them.
/// </summary>
/// <remarks>
/// What the fixtures hold, as read from their object index:
/// <list type="bullet">
///   <item><c>1knoll</c>: entry 1 a knoll (109.0). Entry 2 is a second knoll OCAD
///   keeps for undo, which does not count.</item>
///   <item><c>point_line_area</c>: drawn from <c>1knoll</c>. Entry 1's knoll is now
///   deleted (kept for undo); entry 3 a contour (101.0), entry 4 a knoll (109.0),
///   entry 5 an area (403.0).</item>
///   <item><c>_moved</c>: the same three entries and creation dates, each edited.</item>
///   <item><c>_deleted</c>: all five entries kept for undo, none counting.</item>
///   <item><c>_406</c>: entry 5's area changed to 406.0 in OCAD.</item>
///   <item><c>1knoll_rotated</c>: entry 1's knoll rotated 45° in OCAD.</item>
///   <item><c>blank</c>: a new map with the same symbols, and no index entries.</item>
/// </list>
/// </remarks>
[TestFixture]
public sealed class OcadDiffServiceTests
{
    private readonly IOcadDiffService diff = new OcadDiffService();

    private ObjectDiff Compare(string older, string newer)
        => diff.Compare(older is null ? null : Fixture.Snapshot(older), Fixture.Snapshot(newer));

    /* ----------------------------------------------------------- nothing changed */

    [Test]
    public void AVersionComparedWithItselfHasNoChanges()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineArea);

        Assert.That(result.IsEmpty, Is.True);
    }

    /* ------------------------------------------------------------------- added */

    [Test]
    public void WithNoOlderVersionEveryObjectIsAdded()
    {
        ObjectDiff result = Compare(null, Fixture.OneKnoll);

        Assert.Multiple(() => {
            Assert.That(result.Added, Has.Count.EqualTo(1));
            Assert.That(result.Edited, Is.Empty);
            Assert.That(result.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AnAddedObjectCarriesItsSymbolAndDates()
    {
        ObjectAdded knoll = Compare(null, Fixture.OneKnoll).Added.Single();

        Assert.Multiple(() => {
            Assert.That(knoll.Index, Is.EqualTo(1));
            Assert.That(knoll.Symbol.Number, Is.EqualTo("109.0"));
            Assert.That(knoll.Kind, Is.EqualTo(MapObjectKind.Point));
            Assert.That(knoll.CreatedUtc, Is.Not.Null);
        });
    }

    [Test]
    public void ObjectsThatOnlyExistedAsDeletedBeforeAreAdded()
    {
        // _deleted keeps all three objects for undo; nothing there counts, so
        // getting them back is three additions, not three edits.
        ObjectDiff result = Compare(Fixture.PointLineAreaDeleted, Fixture.PointLineArea);

        Assert.Multiple(() => {
            Assert.That(result.Added.Select(a => a.Symbol.Number),
                        Is.EquivalentTo(new[] { "101.0", "109.0", "403.0" }));
            Assert.That(result.Edited, Is.Empty);
            Assert.That(result.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AnObjectNeverEditedSinceItWasCreatedHasNoLastEditedDate()
    {
        // Same rule as the Angular app: a last-edited date equal to the creation
        // date says nothing, so it is left blank rather than repeated.
        ObjectAdded contour = Compare(Fixture.PointLineAreaDeleted, Fixture.PointLineArea)
            .Added.Single(a => a.Symbol.Number == "101.0");

        Assert.That(contour.LastEditedUtc, Is.Null);
    }

    [Test]
    public void AnAddedLineHasALengthAndNoArea()
    {
        ObjectAdded contour = Compare(null, Fixture.PointLineArea).Added.Single(a => a.Kind == MapObjectKind.Line);

        Assert.Multiple(() => {
            Assert.That(contour.LengthInMeters, Is.GreaterThan(0));
            Assert.That(contour.AreaInSquareMeters, Is.Null);
        });
    }

    [Test]
    public void AnAddedAreaHasAnAreaAndNoLength()
    {
        ObjectAdded area = Compare(null, Fixture.PointLineArea).Added.Single(a => a.Kind == MapObjectKind.Area);

        Assert.Multiple(() => {
            Assert.That(area.AreaInSquareMeters, Is.GreaterThan(0));
            Assert.That(area.LengthInMeters, Is.Null);
        });
    }

    [Test]
    public void AnAddedPointHasNeitherLengthNorArea()
    {
        ObjectAdded knoll = Compare(null, Fixture.OneKnoll).Added.Single();

        Assert.Multiple(() => {
            Assert.That(knoll.LengthInMeters, Is.Null);
            Assert.That(knoll.AreaInSquareMeters, Is.Null);
        });
    }

    /* ----------------------------------------------------------------- deleted */

    [Test]
    public void DeletingEverythingGivesThreeDeletedObjects()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaDeleted);

        Assert.Multiple(() => {
            Assert.That(result.Deleted.Select(d => d.Symbol.Number),
                        Is.EquivalentTo(new[] { "101.0", "109.0", "403.0" }));
            Assert.That(result.Added, Is.Empty);
            Assert.That(result.Edited, Is.Empty);
        });
    }

    [Test]
    public void ADeletedLineAndAreaKeepTheirSize()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaDeleted);

        Assert.Multiple(() => {
            Assert.That(result.Deleted.Single(d => d.Kind == MapObjectKind.Line).LengthInMeters, Is.GreaterThan(0));
            Assert.That(result.Deleted.Single(d => d.Kind == MapObjectKind.Area).AreaInSquareMeters, Is.GreaterThan(0));
        });
    }

    [Test]
    public void AnObjectOcadMarksDeletedCountsAsDeleted()
    {
        // 1knoll -> point_line_area: the knoll in entry 1 stays in the file, marked
        // deleted, while three new objects are drawn.
        ObjectDiff result = Compare(Fixture.OneKnoll, Fixture.PointLineArea);

        Assert.Multiple(() => {
            Assert.That(result.Deleted.Select(d => d.Index), Is.EqualTo(new[] { 1 }));
            Assert.That(result.Added.Select(a => a.Index), Is.EquivalentTo(new[] { 3, 4, 5 }));
            Assert.That(result.Edited, Is.Empty);
        });
    }

    /* ------------------------------------------------------------------ edited */

    [Test]
    public void MovingAllThreeObjectsGivesThreeEdits()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved);

        Assert.Multiple(() => {
            Assert.That(result.Edited.Select(e => e.Index), Is.EquivalentTo(new[] { 3, 4, 5 }));
            Assert.That(result.Added, Is.Empty);
            Assert.That(result.Deleted, Is.Empty);
        });
    }

    [Test]
    public void AMoveIsReportedAsAGeometryChangeOnly()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved);

        Assert.That(result.Edited.Select(e => e.Changes), Has.All.EqualTo(ObjectChanges.Geometry));
    }

    [Test]
    public void AMovedPointReportsHowFarItMovedInMetres()
    {
        // The knoll went from (-267, 128) to (250, 95) in hundredths of a paper
        // millimetre: 5.1805 mm on paper, which at 1:15 000 is 77.7 m.
        ObjectEdited knoll = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved)
            .Edited.Single(e => e.Kind == MapObjectKind.Point);

        Assert.That(knoll.MovementInMeters, Is.EqualTo(77.7).Within(0.1));
    }

    [Test]
    public void AnEditedLineReportsItsLengthChangeAndNothingElse()
    {
        ObjectEdited contour = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved)
            .Edited.Single(e => e.Kind == MapObjectKind.Line);

        Assert.Multiple(() => {
            Assert.That(contour.LengthChangeInPercent, Is.Not.Null);
            Assert.That(contour.AreaChangeInPercent, Is.Null);
            Assert.That(contour.MovementInMeters, Is.Null);
        });
    }

    [Test]
    public void AnEditedAreaReportsItsAreaChangeAndNothingElse()
    {
        ObjectEdited area = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved)
            .Edited.Single(e => e.Kind == MapObjectKind.Area);

        Assert.Multiple(() => {
            Assert.That(area.AreaChangeInPercent, Is.Not.Null);
            Assert.That(area.LengthChangeInPercent, Is.Null);
            Assert.That(area.MovementInMeters, Is.Null);
        });
    }

    [Test]
    public void AnEditedObjectCarriesItsLastEditedDate()
    {
        ObjectEdited knoll = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved)
            .Edited.Single(e => e.Kind == MapObjectKind.Point);

        Assert.Multiple(() => {
            Assert.That(knoll.CreatedUtc, Is.Not.Null);
            Assert.That(knoll.LastEditedUtc, Is.GreaterThan(knoll.CreatedUtc));
        });
    }

    /* ------------------------------------------------- where on the map it is */

    [Test]
    public void AnAddedObjectReportsWhereItIsOnPaper()
    {
        // The knoll sits at (375, 284) in OCAD units: 3.75, 2.84 paper millimetres,
        // the same coordinates the renderer and the viewer use.
        PaperBounds where = Compare(null, Fixture.OneKnoll).Added.Single().Bounds;

        Assert.That(where, Is.EqualTo(new PaperBounds(3.75, 2.84, 3.75, 2.84)));
    }

    [Test]
    public void AnEditedObjectCoversBothWhereItWasAndWhereItIsNow()
    {
        // Moved from (-267, 128) to (250, 95): zooming to it should show both.
        PaperBounds where = Compare(Fixture.PointLineArea, Fixture.PointLineAreaMoved)
            .Edited.Single(e => e.Kind == MapObjectKind.Point).Bounds;

        Assert.That(where, Is.EqualTo(new PaperBounds(-2.67, 0.95, 2.50, 1.28)));
    }

    [Test]
    public void ADeletedObjectReportsWhereItWas()
    {
        PaperBounds where = Compare(Fixture.PointLineArea, Fixture.PointLineAreaDeleted)
            .Deleted.Single(d => d.Kind == MapObjectKind.Point).Bounds;

        Assert.That(where, Is.EqualTo(new PaperBounds(-2.67, 1.28, -2.67, 1.28)));
    }

    [Test]
    public void ALineIsBoundedByAllItsPointsCurveHandlesIncluded()
    {
        // A Bezier curve never leaves the hull of its control points, so including
        // the handles can only make the box a little generous, never too small.
        PaperBounds where = Compare(null, Fixture.PointLineArea).Added.Single(a => a.Kind == MapObjectKind.Line).Bounds;

        Assert.Multiple(() => {
            Assert.That(where.MinX, Is.LessThan(where.MaxX));
            Assert.That(where.MinY, Is.LessThan(where.MaxY));
            Assert.That(where.MinX, Is.EqualTo(-6.09).Within(0.001).Or.LessThan(-6.09),
                        "The contour's first point, (-609, 198), is inside the box.");
        });
    }

    /* -------------------------------------------------- pairing by index entry */

    [Test]
    public void AnIndexEntryReusedForANewObjectIsADeletionAndAnAddition()
    {
        // Same entry, different creation date: OCAD gave the entry to a different
        // object. No fixture shows OCAD doing that, so the newer file is patched.
        byte[] newer = Fixture.Bytes(Fixture.PointLineAreaMoved);
        OcadBytes.SetCreationDate(newer, entry: 4, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));

        ObjectDiff result = diff.Compare(Fixture.Snapshot(Fixture.PointLineArea),
                                         Fixture.Snapshot(Fixture.PointLineAreaMoved, newer));

        Assert.Multiple(() => {
            Assert.That(result.Deleted.Select(d => d.Index), Is.EqualTo(new[] { 4 }));
            Assert.That(result.Added.Select(a => a.Index), Is.EqualTo(new[] { 4 }));
            Assert.That(result.Edited.Select(e => e.Index), Is.EquivalentTo(new[] { 3, 5 }));
        });
    }

    [Test]
    public void ASymbolChangeInTheSameIndexEntryIsAnEdit()
    {
        // 403.0 (rough open land) to 406.0 (slow running), with OCAD's Change
        // Symbol: same object, same entry, new symbol.
        ObjectEdited area = Compare(Fixture.PointLineArea, Fixture.PointLineArea406).Edited.Single();

        Assert.Multiple(() => {
            Assert.That(area.Index, Is.EqualTo(5));
            Assert.That(area.Changes, Is.EqualTo(ObjectChanges.Symbol));
            Assert.That(area.Symbol.Number, Is.EqualTo("406.0"));
            Assert.That(area.PreviousSymbol.Number, Is.EqualTo("403.0"));
        });
    }

    [Test]
    public void ARotatedObjectIsAnAngleChange()
    {
        // Rotated 45° about its own point, so the coordinates do not move.
        ObjectEdited knoll = Compare(Fixture.OneKnoll, Fixture.OneKnollRotated).Edited.Single();

        Assert.That(knoll.Changes, Is.EqualTo(ObjectChanges.Angle));
    }

    /* ------------------------------------------------------------ a blank map */

    [Test]
    public void ABlankMapHasNoObjects()
    {
        ObjectDiff result = Compare(null, Fixture.Blank);

        Assert.That(result.IsEmpty, Is.True);
    }

    [Test]
    public void EverythingDrawnOnABlankMapIsAdded()
    {
        ObjectDiff result = Compare(Fixture.Blank, Fixture.PointLineArea);

        Assert.Multiple(() => {
            Assert.That(result.Added.Select(a => a.Symbol.Number), Is.EquivalentTo(new[] { "101.0", "109.0", "403.0" }));
            Assert.That(result.Edited, Is.Empty);
            Assert.That(result.Deleted, Is.Empty);
        });
    }

    [Test]
    public void EverythingErasedToABlankMapIsDeleted()
    {
        ObjectDiff result = Compare(Fixture.PointLineArea, Fixture.Blank);

        Assert.Multiple(() => {
            Assert.That(result.Deleted.Select(d => d.Index), Is.EquivalentTo(new[] { 3, 4, 5 }));
            Assert.That(result.Added, Is.Empty);
            Assert.That(result.Edited, Is.Empty);
        });
    }
}
