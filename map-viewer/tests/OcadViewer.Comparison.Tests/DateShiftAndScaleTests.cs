using System.Diagnostics;

using OcadViewer.Ocd;

namespace OcadViewer.Comparison.Tests;

/// <summary>
/// Matching when every date moved by whole hours, and when thousands of objects
/// share one creation date - both built in memory from base.ocd's objects.
/// </summary>
[TestFixture]
public sealed class DateShiftAndScaleTests
{
    /* ------------------------------------------------------------ date shift */

    [Test]
    public void AnEditInAVersionWhoseDatesAllShiftedIsStillAnEdit()
    {
        // As when a map is rebuilt by copying its objects into a new file: every
        // date two hours earlier. The lake was also reshaped, so only the shift,
        // allowed for, can pair it with its old self.
        IReadOnlyList<OcadObject> before = Fixtures.Base.Objects;
        OcadObject lake = before.Single(o => o.CreatedAt == Fixtures.Created.Lake);
        IReadOnlyList<OcadObject> after = [.. before.Select(o => {
            OcadObject edited = o == lake ? Fixtures.WithCoords(o, c => c with { X = c.X + 500 }) : o;
            return edited.WithCreatedAt(o.CreatedAt.Value.AddHours(-2));
        })];

        ObjectPairing pairing = ObjectMatcher.Match(before, after);

        Assert.Multiple(() => {
            Assert.That(pairing.DateShift, Is.EqualTo(TimeSpan.FromHours(-2)));
            Assert.That(pairing.Pairs, Has.Count.EqualTo(before.Count));
            Assert.That(pairing.OnlyInBase, Is.Empty);
            Assert.That(pairing.OnlyInBranch, Is.Empty);
        });
    }

    [Test]
    public void FilesWithTheSameDatesHaveNoShift()
        => Assert.That(ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Kari.Objects).DateShift, Is.EqualTo(TimeSpan.Zero));

    [Test]
    public void AShiftThatIsNotWholeHoursIsNotAllowedFor()
    {
        IReadOnlyList<OcadObject> before = Fixtures.Base.Objects;
        IReadOnlyList<OcadObject> after = [.. before.Select(o => o.WithCreatedAt(o.CreatedAt.Value.AddMinutes(-90)))];

        Assert.That(ObjectMatcher.Match(before, after).DateShift, Is.EqualTo(TimeSpan.Zero));
    }

    /* ----------------------------------------- one creation date, many objects */

    /// <summary>
    /// <paramref name="count"/> copies of base.ocd's knoll on a grid 5 mm apart,
    /// all with one creation date - an import - each shifted by <paramref name="shift"/> units.
    /// </summary>
    private static List<OcadObject> Import(int count, int shift)
    {
        OcadObject knoll = Fixtures.Base.Objects.Single(o => o.CreatedAt == Fixtures.Created.Knoll);
        int side = (int)Math.Ceiling(Math.Sqrt(count));

        return [.. Enumerable.Range(0, count).Select(i =>
            Fixtures.WithCoords(knoll, c => c with { X = (i % side) * 500 + shift, Y = (i / side) * 500 }))];
    }

    [Test]
    public void AnImportMovedAsAWholePairsEachObjectWithItself()
    {
        // 10 000 objects with one date: past the exhaustive limit, so through the grid.
        List<OcadObject> before = Import(10_000, shift: 0);
        List<OcadObject> after = Import(10_000, shift: 40);

        ObjectPairing pairing = ObjectMatcher.Match(before, after);

        Assert.Multiple(() => {
            Assert.That(pairing.OnlyInBase, Is.Empty);
            Assert.That(pairing.OnlyInBranch, Is.Empty);
            Assert.That(pairing.Pairs.All(p => p.Branch.Coords[0].X - p.Base.Coords[0].X == 40
                                               && p.Branch.Coords[0].Y == p.Base.Coords[0].Y), Is.True,
                        "each object paired with its own moved self");
        });
    }

    [Test]
    public void AnObjectPastedFarAwayInsideALargeImportKeepsItsIdentity()
    {
        List<OcadObject> before = Import(10_000, shift: 0);
        List<OcadObject> after = Import(10_000, shift: 0);
        after[0] = Fixtures.WithCoords(after[0], c => c with { X = c.X + 2_000_000 });   // 20 m on paper away

        ObjectPairing pairing = ObjectMatcher.Match(before, after);

        Assert.Multiple(() => {
            Assert.That(pairing.OnlyInBase, Is.Empty);
            Assert.That(pairing.OnlyInBranch, Is.Empty);
        });
    }

    [Test]
    public void MatchingALargeImportStaysWithinItsBudget()
    {
        // The diff runs on the browser's only thread, so this is a budget, not a
        // benchmark: 20 000 objects with one date, every one of them moved.
        List<OcadObject> before = Import(20_000, shift: 0);
        List<OcadObject> after = Import(20_000, shift: 40);
        ObjectMatcher.Match(before.Take(100).ToList(), after.Take(100).ToList());   // warm up

        Stopwatch watch = Stopwatch.StartNew();
        ObjectPairing pairing = ObjectMatcher.Match(before, after);
        watch.Stop();

        Assert.Multiple(() => {
            Assert.That(pairing.Pairs, Has.Count.EqualTo(20_000));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)),
                        "Matching one import date must not grow with the square of the import.");
        });
    }
}
