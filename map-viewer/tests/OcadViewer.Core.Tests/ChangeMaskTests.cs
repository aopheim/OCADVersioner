using NUnit.Framework;

using OcadViewer.Core;

namespace OcadViewer.Core.Tests;

/// <summary>
/// The change mask on renders drawn by hand: one character per pixel, '.' for
/// blank paper, a letter for a symbol colour.
/// </summary>
[TestFixture]
public sealed class ChangeMaskTests
{
    private const uint White = 0xFFFF_FFFF;

    /// <summary>Opaque colours, as Skia stores them: alpha in the top byte.</summary>
    private static readonly Dictionary<char, uint> Colours = new() {
        ['.'] = White,
        ['b'] = 0xFF22_44AA,   // brown
        ['g'] = 0xFF00_CC66,   // green
        ['k'] = 0xFF00_0000,   // black
    };

    [Test]
    public void IdenticalRendersHaveNoChanges()
    {
        string[] map = [
            "..b...",
            "..b...",
            "..b...",
        ];

        Assert.That(Changed(map, map, tolerance: 1, minimumSpeck: 1), Is.Empty);
    }

    [Test]
    public void ALineOnePixelOverIsTheSameLine()
    {
        // A contour nobody touched, landing a pixel to the right after a save.
        string[] before = [
            "..b....",
            "..b....",
            "..b....",
            "..b....",
        ];
        string[] after = [
            "...b...",
            "...b...",
            "...b...",
            "...b...",
        ];

        Assert.Multiple(() => {
            Assert.That(Changed(before, after, tolerance: 1, minimumSpeck: 1), Is.Empty,
                "Within the tolerance it is the same ink.");
            Assert.That(Changed(before, after, tolerance: 0, minimumSpeck: 1), Has.Length.EqualTo(8),
                "Compared exactly, both columns light up - which is what the overlay used to do.");
        });
    }

    [Test]
    public void ALineMovedFurtherThanTheToleranceIsAChange()
    {
        string[] before = [
            "b......",
            "b......",
            "b......",
        ];
        string[] after = [
            "...b...",
            "...b...",
            "...b...",
        ];

        int[] changed = Changed(before, after, tolerance: 1, minimumSpeck: 1);

        Assert.That(changed, Is.EquivalentTo(new[] { 0, 7, 14, 3, 10, 17 }),
            "Gone from the left column, arrived in the fourth.");
    }

    [Test]
    public void ANewObjectIsReportedAsAppeared()
    {
        string[] before = [
            ".....",
            ".....",
            ".....",
        ];
        string[] after = [
            ".....",
            ".kkk.",
            ".....",
        ];

        byte[] mask = Compare(before, after, tolerance: 1, minimumSpeck: 1);

        Assert.That(ChangesAt(mask, 6, 7, 8), Is.All.EqualTo(PixelChange.Appeared));
    }

    [Test]
    public void ARecolouredAreaIsAChangeEvenWhenItsEdgeStays()
    {
        // Green turned black: the colour is new here, and nowhere nearby before.
        string[] before = [
            "ggggg",
            "ggggg",
            "ggggg",
        ];
        string[] after = [
            "kkkkk",
            "kkkkk",
            "kkkkk",
        ];

        byte[] mask = Compare(before, after, tolerance: 1, minimumSpeck: 1);

        Assert.That(ChangesAt(mask, Enumerable.Range(0, 15).ToArray()), Is.All.EqualTo(PixelChange.Altered));
    }

    [Test]
    public void AChangeSmallerThanTheMinimumIsDropped()
    {
        string[] before = [
            ".......",
            ".......",
            ".......",
        ];
        string[] stray = [
            ".......",
            "...k...",
            ".......",
        ];

        Assert.That(Changed(before, stray, tolerance: 1, minimumSpeck: 2), Is.Empty);
    }

    [Test]
    public void AThinLongChangeIsKeptAlthoughItIsOnlyOnePixelWide()
    {
        // A new path: one pixel wide, which is under the minimum, but long.
        string[] before = [
            ".......",
            ".......",
            ".......",
        ];
        string[] path = [
            ".......",
            ".kkkkk.",
            ".......",
        ];

        Assert.That(Changed(before, path, tolerance: 1, minimumSpeck: 2), Has.Length.EqualTo(5));
    }

    [Test]
    public void SpeckRemovalLooksAtTheClusterNotThePixel()
    {
        // Two pixels touching diagonally make one cluster two pixels across.
        string[] before = [
            ".....",
            ".....",
            ".....",
        ];
        string[] after = [
            ".k...",
            "..k..",
            ".....",
        ];

        Assert.That(Changed(before, after, tolerance: 0, minimumSpeck: 2), Has.Length.EqualTo(2));
    }

    [Test, MaxTime(10_000)]
    public void ALargeChangeIsWalkedOnce()
    {
        // A whole megapixel changed is one cluster. Walking it again from each of
        // its pixels would never finish.
        const int Side = 1000;
        uint[] before = [.. Enumerable.Repeat(White, Side * Side)];
        uint[] after = [.. Enumerable.Repeat(Colours['k'], Side * Side)];

        byte[] mask = ChangeMask.Compare(before, after, Side, Side, tolerance: 1, minimumSpeck: 2);

        Assert.That(mask.Count(m => (m & ChangeMask.ChangeBits) == (byte)PixelChange.Appeared),
                    Is.EqualTo(Side * Side));
    }

    [Test]
    public void TheMapIsWhatLiesBetweenInkIncludingWhiteForest()
    {
        // A white forest inside a black boundary, blank paper outside it.
        string[] map = [
            ".......",
            ".kkkkk.",
            ".k...k.",
            ".kkkkk.",
            ".......",
        ];

        byte[] mask = Compare(map, map, tolerance: 1, minimumSpeck: 1);
        bool[] inMap = [.. mask.Select(m => (m & ChangeMask.InMap) != 0)];

        Assert.Multiple(() => {
            Assert.That(inMap.Count(m => m), Is.EqualTo(15), "The boundary and the forest inside it.");
            Assert.That(inMap[2 * 7 + 3], Is.True, "White forest is map.");
            Assert.That(inMap[0], Is.False, "Blank paper outside is not.");
        });
    }

    [Test]
    public void AnObjectDeletedOnTheEdgeStillCountsAsMap()
    {
        string[] before = [
            "kk...",
            ".....",
        ];
        string[] after = [
            ".....",
            ".....",
        ];

        byte[] mask = Compare(before, after, tolerance: 1, minimumSpeck: 1);

        Assert.Multiple(() => {
            Assert.That(ChangesAt(mask, 0, 1), Is.All.EqualTo(PixelChange.Vanished));
            Assert.That((mask[0] & ChangeMask.InMap) != 0, Is.True,
                "Map on either side is map, or deleting the edge would shrink the total.");
        });
    }

    [TestCase(4.0, 1, 2, TestName = "At the usual 4 px/mm, a pixel of tolerance and specks under 2 px")]
    [TestCase(8.0, 2, 4)]
    [TestCase(1.0, 1, 1, TestName = "A coarse mask keeps a pixel of tolerance")]
    public void TheRulesAreInMillimetres(double pixelsPerMm, int tolerance, int speck)
        => Assert.That(ChangeMask.PixelsFor(pixelsPerMm, 0.25, 0.5), Is.EqualTo((tolerance, speck)));

    [Test]
    public void TheAlphaByteIsIgnored()
    {
        uint[] before = [0xFF00_0000, White];
        uint[] after = [0x0000_0000, White];

        Assert.That(ChangeMask.Compare(before, after, 2, 1, 0, 1).Select(m => m & ChangeMask.ChangeBits),
                    Is.All.Zero);
    }

    private static int[] Changed(string[] before, string[] after, int tolerance, int minimumSpeck)
        => [.. Compare(before, after, tolerance, minimumSpeck)
               .Select((m, i) => (m, i))
               .Where(p => (p.m & ChangeMask.ChangeBits) != 0)
               .Select(p => p.i)];

    private static PixelChange[] ChangesAt(byte[] mask, params int[] indices)
        => [.. indices.Select(i => (PixelChange)(mask[i] & ChangeMask.ChangeBits))];

    private static byte[] Compare(string[] before, string[] after, int tolerance, int minimumSpeck)
        => ChangeMask.Compare(Pixels(before), Pixels(after),
                              before[0].Length, before.Length, tolerance, minimumSpeck);

    private static uint[] Pixels(string[] rows) => [.. rows.SelectMany(row => row).Select(c => Colours[c])];
}
