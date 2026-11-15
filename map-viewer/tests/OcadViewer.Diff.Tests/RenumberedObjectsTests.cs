namespace OcadViewer.Diff.Tests;

/// <summary>
/// Objects that changed index entry between versions without being deleted.
/// </summary>
/// <remarks>
/// <para>
/// The index entry is not always kept. In the Bymarkabasen project two releases
/// out of three renumbered nearly the whole map: of 23 000 objects, 200 kept their
/// entry, the rest turned up unchanged under another number. One of the two also
/// moved every creation and modification date by exactly two hours. Both look like
/// a map rebuilt by copying its objects into another file.
/// </para>
/// <para>
/// Matching by entry alone reported those maps as almost wholly deleted and
/// re-added. These tests build the same situations from the fixtures by patching
/// the index (see <see cref="OcadBytes"/>), so they need no copyrighted map;
/// <see cref="RealMapTests"/> checks the real projects where they are on disk.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RenumberedObjectsTests
{
    private readonly IOcadDiffService diff = new OcadDiffService();

    private static readonly DateTime OneMoment = new(2017, 5, 22, 11, 50, 0, DateTimeKind.Utc);

    private ObjectDiff Compare(byte[] older, byte[] newer)
        => diff.Compare(Fixture.Snapshot("older", older), Fixture.Snapshot("newer", newer));

    private static byte[] Bytes(string fixture, Action<byte[]> patch = null)
    {
        byte[] bytes = Fixture.Bytes(fixture);
        patch?.Invoke(bytes);
        return bytes;
    }

    /* ------------------------------------------------- as OCAD itself does it */

    [Test]
    public void AMapImportedIntoANewFileHasNoChanges()
    {
        // File, Import into a blank map, offset 0: OCAD renumbers 3, 4, 5 to 1, 2, 3,
        // keeps every coordinate and creation date, and stamps a new modification
        // date - Bymarkabasen's rebuilt releases, made by OCAD.
        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), Fixture.Bytes(Fixture.PointLineAreaImported));

        Assert.That(result.IsEmpty, Is.True, Describe(result));
    }

    [Test]
    public void ObjectsPastedIntoANewFileAreEditsNotNewObjects()
    {
        // Copy and paste instead: renumbered the same way, but the paste moved
        // every object by (-1.27, +1.18) mm. The same three objects, moved.
        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), Fixture.Bytes(Fixture.PointLineAreaPasted));

        Assert.Multiple(() => {
            Assert.That(result.Added, Is.Empty, Describe(result));
            Assert.That(result.Deleted, Is.Empty, Describe(result));
            Assert.That(result.Edited.Select(e => (e.Index, e.Symbol.Number, e.PreviousSymbol.Number, e.Changes)),
                        Is.EqualTo(new[] {
                            (1, "101.0", "101.0", ObjectChanges.Geometry),
                            (2, "109.0", "109.0", ObjectChanges.Geometry),
                            (3, "403.0", "403.0", ObjectChanges.Geometry),
                        }));
        });
    }

    [Test]
    public void APastedObjectMeasuresOnlyThePasteShift()
    {
        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), Fixture.Bytes(Fixture.PointLineAreaPasted));

        Assert.Multiple(() => {
            // 1.73 mm on paper at 1:15 000.
            Assert.That(result.Edited.Single(e => e.Kind == MapObjectKind.Point).MovementInMeters,
                        Is.EqualTo(26.0).Within(0.1));
            Assert.That(result.Edited.Single(e => e.Kind == MapObjectKind.Line).LengthChangeInPercent,
                        Is.EqualTo(0).Within(1e-9));
            Assert.That(result.Edited.Single(e => e.Kind == MapObjectKind.Area).AreaChangeInPercent,
                        Is.EqualTo(0).Within(1e-9));
        });
    }

    /* --------------------------------------------- unchanged under a new number */

    [Test]
    public void ObjectsThatOnlySwappedIndexEntriesAreUnchanged()
    {
        // The contour (3) and the area (5) trade numbers.
        byte[] newer = Bytes(Fixture.PointLineArea, b => OcadBytes.SwapIndexEntries(b, 3, 5));

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.That(result.IsEmpty, Is.True, Describe(result));
    }

    [Test]
    public void AnObjectMovedToAnUnusedIndexEntryIsUnchanged()
    {
        // Entry 6 is empty in the fixture.
        byte[] newer = Bytes(Fixture.PointLineArea, b => OcadBytes.SwapIndexEntries(b, 5, 6));

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.That(result.IsEmpty, Is.True, Describe(result));
    }

    [Test]
    public void ObjectsSharingACreationDateAreNotEditsOfEachOther()
    {
        // An import stamps thousands of objects with the same creation date. When
        // two of them trade entries, same entry plus same date used to say "the
        // contour became the area": an edit of symbol and geometry that never
        // happened.
        void sameDate(byte[] b)
        {
            OcadBytes.SetCreationDate(b, 3, OneMoment);
            OcadBytes.SetCreationDate(b, 5, OneMoment);
        }

        byte[] older = Bytes(Fixture.PointLineArea, sameDate);
        byte[] newer = Bytes(Fixture.PointLineArea, b => { sameDate(b); OcadBytes.SwapIndexEntries(b, 3, 5); });

        ObjectDiff result = Compare(older, newer);

        Assert.That(result.IsEmpty, Is.True, Describe(result));
    }

    /* -------------------------------------------------------- dates shifted */

    [Test]
    public void DatesShiftedByWholeHoursAreNotAChange()
    {
        byte[] newer = Bytes(Fixture.PointLineArea, b => OcadBytes.ShiftAllDates(b, TimeSpan.FromHours(-2)));

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.That(result.IsEmpty, Is.True, Describe(result));
    }

    [Test]
    public void ARenumberedMapWithShiftedDatesHasNoChanges()
    {
        // V1 to V2 of Bymarkabasen, in miniature.
        byte[] newer = Bytes(Fixture.PointLineArea, b => {
            OcadBytes.ShiftAllDates(b, TimeSpan.FromHours(-2));
            OcadBytes.SwapIndexEntries(b, 3, 5);
            OcadBytes.SwapIndexEntries(b, 4, 6);
        });

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.That(result.IsEmpty, Is.True, Describe(result));
    }

    /* ---------------------------------------------- edited under a new number */

    [Test]
    public void ObjectsEditedAndRenumberedAreStillEdits()
    {
        byte[] newer = Bytes(Fixture.PointLineAreaMoved, b => {
            OcadBytes.SwapIndexEntries(b, 3, 5);
            OcadBytes.SwapIndexEntries(b, 4, 6);
        });

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.Multiple(() => {
            Assert.That(result.Added, Is.Empty, Describe(result));
            Assert.That(result.Deleted, Is.Empty, Describe(result));
            Assert.That(result.Edited.Select(e => (e.Symbol.Number, e.PreviousSymbol.Number, e.Changes)),
                        Is.EquivalentTo(new[] {
                            ("101.0", "101.0", ObjectChanges.Geometry),
                            ("109.0", "109.0", ObjectChanges.Geometry),
                            ("403.0", "403.0", ObjectChanges.Geometry),
                        }));
        });
    }

    [Test]
    public void ARenumberedEditStillMeasuresTheSameChange()
    {
        byte[] newer = Bytes(Fixture.PointLineAreaMoved, b => OcadBytes.SwapIndexEntries(b, 4, 6));

        ObjectEdited knoll = Compare(Fixture.Bytes(Fixture.PointLineArea), newer)
            .Edited.Single(e => e.Kind == MapObjectKind.Point);

        Assert.Multiple(() => {
            Assert.That(knoll.Index, Is.EqualTo(6), "Reported under the entry it has now.");
            Assert.That(knoll.MovementInMeters, Is.EqualTo(77.7).Within(0.1));
            Assert.That(knoll.Bounds, Is.EqualTo(new PaperBounds(-2.67, 0.95, 2.50, 1.28)));
        });
    }

    [Test]
    public void AmongObjectsSharingACreationDateAnEditIsPairedWithItsOwnSymbol()
    {
        // All three objects stamped with one date, all three edited, two of them
        // renumbered. The date alone cannot tell them apart; the symbol can.
        void sameDate(byte[] b)
        {
            foreach (int entry in new[] { 3, 4, 5 })
                OcadBytes.SetCreationDate(b, entry, OneMoment);
        }

        byte[] older = Bytes(Fixture.PointLineArea, sameDate);
        byte[] newer = Bytes(Fixture.PointLineAreaMoved, b => { sameDate(b); OcadBytes.SwapIndexEntries(b, 3, 5); });

        ObjectDiff result = Compare(older, newer);

        Assert.Multiple(() => {
            Assert.That(result.Added, Is.Empty, Describe(result));
            Assert.That(result.Deleted, Is.Empty, Describe(result));
            Assert.That(result.Edited.Select(e => e.Changes), Has.All.EqualTo(ObjectChanges.Geometry),
                        "Nothing changed symbol, so no pairing may claim it did.");
        });
    }

    /* --------------------------------------------- still a deletion + addition */

    [Test]
    public void ARenumberedObjectWithAnotherCreationDateIsStillANewObject()
    {
        // Renumbering must not turn a genuinely new object into an edit of an old one.
        byte[] newer = Bytes(Fixture.PointLineAreaMoved, b => {
            OcadBytes.SetCreationDate(b, 4, new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));
            OcadBytes.SwapIndexEntries(b, 4, 6);
        });

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.Multiple(() => {
            Assert.That(result.Deleted.Select(d => d.Index), Is.EqualTo(new[] { 4 }));
            Assert.That(result.Added.Select(a => a.Index), Is.EqualTo(new[] { 6 }));
        });
    }

    [Test]
    public void ARenumberedObjectNeverModifiedSinceIsNotAnEditOfAnother()
    {
        // Same creation date, nearby, different content - but the newer object's
        // modification date has not moved, so OCAD did not edit it. In
        // Bymarkabasen these were imported objects, a batch stamped with one date.
        byte[] newer = Bytes(Fixture.PointLineArea, b => {
            OcadBytes.SetSymbol(b, 4, 110000);
            OcadBytes.SwapIndexEntries(b, 4, 6);
        });

        ObjectDiff result = Compare(Fixture.Bytes(Fixture.PointLineArea), newer);

        Assert.Multiple(() => {
            Assert.That(result.Edited, Is.Empty, Describe(result));
            Assert.That(result.Deleted.Select(d => d.Index), Is.EqualTo(new[] { 4 }));
            Assert.That(result.Added.Select(a => a.Index), Is.EqualTo(new[] { 6 }));
        });
    }

    private static string Describe(ObjectDiff result)
        => $"added {string.Join(", ", result.Added.Select(a => $"{a.Index}:{a.Symbol.Number}"))}; " +
           $"edited {string.Join(", ", result.Edited.Select(e => $"{e.Index}:{e.PreviousSymbol.Number}->{e.Symbol.Number} {e.Changes}"))}; " +
           $"deleted {string.Join(", ", result.Deleted.Select(d => $"{d.Index}:{d.Symbol.Number}"))}";
}
