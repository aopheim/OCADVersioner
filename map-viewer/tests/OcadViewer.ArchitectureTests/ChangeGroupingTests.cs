using System.Diagnostics;

using NUnit.Framework;

using OcadViewer.Changes;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// What grouping a change mask has to answer, whoever answers it.
/// </summary>
/// <remarks>
/// Written against <see cref="IChangeGrouper"/> and not against the Dbscan
/// adapter, so a different grouping - connected components, a quadtree, an
/// object-level diff - has to satisfy the same list to take its place. That is
/// what the interface is for; a suite that named the implementation would make
/// it the specification.
/// </remarks>
[TestFixture]
public sealed class ChangeGroupingTests
{
    /// <summary>The grouping under test. One line to point this somewhere else.</summary>
    private static IChangeGrouper Grouper() => new DbscanChangeGrouper();

    [Test]
    public void AnUnchangedMapHasNoRegions()
    {
        ChangeGrouping found = Grouper().Group(Mask(100, 100));

        Assert.Multiple(() => {
            Assert.That(found.Regions, Is.Empty);
            Assert.That(found.ChangedPixels, Is.Zero);
            Assert.That(found.TotalPixels, Is.EqualTo(10_000),
                "The share of the sheet has to be reportable even when nothing changed.");
        });
    }

    [Test]
    public void ChangesFarApartAreSeparatePlaces()
    {
        ChangeMask mask = Mask(100, 100, Block(10, 10, 4, 4), Block(60, 60, 6, 6));

        ChangeGrouping found = Grouper().Group(mask);

        Assert.That(found.Regions, Has.Count.EqualTo(2));
        Assert.That(found.ChangedPixels, Is.EqualTo(16 + 36));
    }

    [Test]
    public void ChangesWithinTheGapAreOnePlace()
    {
        // Ink does not have to overlap to be one edit. A symbol redrawn a
        // fraction to one side leaves two marks that meet only at a corner,
        // and listing those as two places to visit would be a lie about the
        // map. The default gap is deliberately tight - a wider one chained a
        // whole storigarden revision into a single entry - so "within the gap"
        // here means within a millimetre.
        ChangeMask mask = Mask(100, 100, Block(10, 10, 3, 3), Block(13, 13, 3, 3));

        ChangeGrouping found = Grouper().Group(mask);

        Assert.That(found.Regions, Has.Count.EqualTo(1));
        Assert.That(found.Regions[0].Pixels, Is.EqualTo(18));
    }

    [Test]
    public void ChangesFurtherApartThanTheGapAreTwoPlaces()
    {
        // The same two marks with three millimetres of clear paper between
        // them. Both are listed: the floor on the list is a share of the
        // change, and half of it is not a speck.
        ChangeMask mask = Mask(100, 100, Block(10, 10, 3, 3), Block(16, 10, 3, 3));

        Assert.That(Grouper().Group(mask).Regions, Has.Count.EqualTo(2));
    }

    [Test]
    public void TheGapIsMeasuredInMillimetresNotPixels()
    {
        // The same pair of marks on a mask computed at four times the
        // resolution. A gap counted in pixels would group one and split the
        // other, and which you got would depend on the size of the map.
        ChangeMask coarse = Mask(100, 100, Block(10, 10, 3, 3), Block(13, 13, 3, 3));
        ChangeMask fine = Mask(400, 400, Block(40, 40, 12, 12), Block(52, 52, 12, 12));

        Assert.Multiple(() => {
            Assert.That(Grouper().Group(coarse).Regions, Has.Count.EqualTo(1));
            Assert.That(Grouper().Group(fine).Regions, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void ARegionIsWhereTheChangeIsOnThePaper()
    {
        // Columns 10 to 13 and rows 20 to 23 of a 100 by 100 mask covering a
        // 100 mm square. Rows run down the sheet and paper Y runs up it, so row
        // 20 is 80 mm up, not 20.
        ChangeMask mask = Mask(100, 100, Block(10, 20, 4, 4));

        PaperRectangle area = Grouper().Group(mask).Regions[0].Area;

        Assert.Multiple(() => {
            Assert.That(area.MinX, Is.EqualTo(10).Within(0.001));
            Assert.That(area.MaxX, Is.EqualTo(14).Within(0.001));
            Assert.That(area.MinY, Is.EqualTo(76).Within(0.001));
            Assert.That(area.MaxY, Is.EqualTo(80).Within(0.001));
        });
    }

    [Test]
    public void ARegionIsNoBiggerThanTheChangeInsideIt()
    {
        // Grouping works on cells, which are coarser than pixels. A region that
        // reported its cells rather than its pixels would frame a change four
        // times too wide, and a zoom to it would land beside it.
        ChangeMask mask = Mask(400, 400, Block(41, 41, 2, 2));

        PaperRectangle area = Grouper().Group(mask).Regions[0].Area;

        Assert.That(area.Width, Is.EqualTo(0.5).Within(0.001));
        Assert.That(area.Height, Is.EqualTo(0.5).Within(0.001));
    }

    [Test]
    public void TheBiggestChangeIsFirst()
    {
        ChangeMask mask = Mask(100, 100,
            Block(10, 10, 3, 3), Block(50, 50, 8, 8), Block(10, 80, 5, 5));

        IReadOnlyList<ChangeRegion> regions = Grouper().Group(mask).Regions;

        Assert.Multiple(() => {
            Assert.That(regions.Select(r => r.Pixels), Is.EqualTo(new[] { 64, 25, 9 }),
                "A list of places to visit is only useful in the order worth visiting them.");
            Assert.That(regions.Select(r => r.Rank), Is.EqualTo(new[] { 1, 2, 3 }),
                "Ranks are what the page shows and what it asks to zoom to, so they have to "
                + "be the positions in this list and not something the page counts itself.");
        });
    }

    [Test]
    public void SpecksStayOutOfTheListAndInTheCount()
    {
        // A four-pixel mark beside a redrawn clearing is a quarter of one
        // percent of what changed - a line that shifted inside its own width.
        // It did change, so the overlay and the percentage keep it; offering to
        // fly somewhere with nothing to see is the part that is no use.
        ChangeMask mask = Mask(100, 100, Block(10, 10, 40, 40), Block(90, 90, 2, 2));

        ChangeGrouping found = Grouper().Group(mask);

        Assert.Multiple(() => {
            Assert.That(found.Regions, Has.Count.EqualTo(1));
            Assert.That(found.SmallerRegions, Is.EqualTo(1),
                "The list has to say what it is not showing, or a mapper reads it as "
                + "everything that happened.");
            Assert.That(found.ChangedPixels, Is.EqualTo(1600 + 4),
                "Nothing is dropped from what changed - only from what is worth a trip.");
        });
    }

    [Test]
    public void ASmallChangeOnItsOwnIsStillAPlace()
    {
        // The same four-pixel mark, with only another small mark for company.
        // The floor is a share and not a size on purpose: when nothing much
        // happened, what did happen is the whole story.
        ChangeMask mask = Mask(100, 100, Block(10, 10, 2, 2), Block(90, 90, 2, 2));

        ChangeGrouping found = Grouper().Group(mask);

        Assert.That(found.Regions, Has.Count.EqualTo(2));
        Assert.That(found.SmallerRegions, Is.Zero);
    }

    [Test]
    public void NothingIsHiddenWhenTheCallerAsksForEverything()
    {
        ChangeMask mask = Mask(100, 100, Block(10, 10, 40, 40), Block(90, 90, 2, 2));

        ChangeGrouping found = Grouper().Group(
            mask, new ChangeGroupingOptions { MinimumShare = 0 });

        Assert.That(found.Regions, Has.Count.EqualTo(2));
        Assert.That(found.SmallerRegions, Is.Zero);
    }

    [Test]
    public void TheSameMaskAlwaysGivesTheSameList()
    {
        // Equal-sized regions are the awkward case: any order satisfies "biggest
        // first", and a list that reshuffles between two identical runs makes
        // region 3 a different place each time it is clicked.
        ChangeMask mask = Mask(100, 100,
            Block(10, 10, 4, 4), Block(50, 10, 4, 4), Block(10, 50, 4, 4), Block(50, 50, 4, 4));

        IReadOnlyList<ChangeRegion> first = Grouper().Group(mask).Regions;
        IReadOnlyList<ChangeRegion> again = Grouper().Group(mask).Regions;

        Assert.That(again, Is.EqualTo(first));
    }

    [Test]
    public void AMaskHasToDescribeARaster()
    {
        Assert.Multiple(() => {
            Assert.Throws<ArgumentException>(
                () => new ChangeMask(new byte[99], 10, 10, Sheet(100, 100)),
                "A mask whose bytes do not match its size reads neighbouring rows as one, "
                + "and every region it reports is then in the wrong place.");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new ChangeMask([], 0, 10, Sheet(100, 100)));
            Assert.Throws<ArgumentException>(
                () => new ChangeMask(new byte[100], 10, 10, new PaperRectangle(5, 5, 5, 5)));
        });
    }

    [Test]
    public void AWholeMapOfChangesIsStillGroupedInReasonableTime()
    {
        // The case a user reaches by accident: comparing a map with an unrelated
        // one lights up most of the sheet. The clustering package ships a second
        // index for exactly this, because its default one searches every point
        // against every other - and nothing in the overlay would have said why
        // the app had stopped.
        ChangeMask mask = Noise(1200, 1200, 300, oneIn: 5, seed: 20260114);

        Stopwatch clock = Stopwatch.StartNew();
        ChangeGrouping found = Grouper().Group(mask);
        clock.Stop();

        Assert.That(found.Regions, Is.Not.Empty);
        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)),
            $"Grouping {found.ChangedPixels} changed pixels took {clock.ElapsedMilliseconds} ms. "
            + "Whatever is behind the clustering has stopped being a spatial index.");
    }

    /// <summary>A square of the sheet, in millimetres, starting at the origin.</summary>
    private static PaperRectangle Sheet(double width, double height)
        => new(0, 0, width, height);

    /// <summary>
    /// A mask over a 100 mm square of paper, so a mask pixel is a millimetre at
    /// 100 by 100 and a quarter of one at 400 by 400.
    /// </summary>
    private static ChangeMask Mask(
        int width, int height, params (int Left, int Top, int Width, int Height)[] blocks)
    {
        byte[] pixels = new byte[width * height];

        foreach ((int left, int top, int blockWidth, int blockHeight) in blocks) {
            for (int row = top; row < top + blockHeight; row++)
                for (int column = left; column < left + blockWidth; column++)
                    pixels[row * width + column] = 1;
        }

        return new ChangeMask(pixels, width, height, Sheet(100, 100));
    }

    private static (int, int, int, int) Block(int left, int top, int width, int height)
        => (left, top, width, height);

    /// <summary>Changes scattered over the whole sheet, for the degenerate case.</summary>
    private static ChangeMask Noise(int width, int height, double sheetMm, int oneIn, int seed)
    {
        byte[] pixels = new byte[width * height];
        Random random = new(seed);

        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = random.Next(oneIn) == 0 ? (byte)1 : (byte)0;

        return new ChangeMask(pixels, width, height, Sheet(sheetMm, sheetMm));
    }
}
