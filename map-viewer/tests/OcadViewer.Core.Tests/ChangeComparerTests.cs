using NUnit.Framework;

namespace OcadViewer.Core.Tests;

/// <summary>
/// The tolerant comparison on hand-drawn pixels, where every case is exact.
/// </summary>
public sealed class ChangeComparerTests
{
    private const int Size = 40;

    private const uint White = 0xFFFFFFFF;
    private const uint Brown = 0xFF1E5ABE;
    private const uint Black = 0xFF000000;
    private const uint Yellow = 0xFF00C8FF;

    private static uint[] Blank()
    {
        var pixels = new uint[Size * Size];
        Array.Fill(pixels, White);
        return pixels;
    }

    private static uint[] With(uint[] pixels, int left, int top, int width, int height, uint colour)
    {
        for (int y = top; y < top + height; y++)
            for (int x = left; x < left + width; x++)
                pixels[y * Size + x] = colour;
        return pixels;
    }

    private static byte[] Compare(uint[] before, uint[] after, int radius = 1, int minSpeck = 1)
        => ChangeComparer.Compare(before, after, Size, Size, radius, minSpeck);

    private static int Changed(byte[] mask)
        => mask.Count(v => v is (byte)PixelChange.Appeared or (byte)PixelChange.Vanished or (byte)PixelChange.Altered);

    [Test]
    public void IdenticalRendersHaveNoChangesAndMarkTheInk()
    {
        byte[] mask = Compare(With(Blank(), 5, 10, 30, 2, Brown), With(Blank(), 5, 10, 30, 2, Brown));

        Assert.That(Changed(mask), Is.Zero);
        Assert.That(mask.Count(v => v == (byte)PixelChange.UnchangedInk), Is.EqualTo(60));
        Assert.That(mask.Count(v => v == (byte)PixelChange.Unchanged), Is.EqualTo(Size * Size - 60));
    }

    [Test]
    public void ALineNudgedWithinTheToleranceIsUnchanged()
    {
        // The re-save jitter: a contour one pixel lower.
        byte[] mask = Compare(With(Blank(), 5, 10, 30, 2, Brown), With(Blank(), 5, 11, 30, 2, Brown));

        Assert.That(Changed(mask), Is.Zero);
    }

    [Test]
    public void ALineMovedBeyondTheToleranceIsChangedOnBothSides()
    {
        byte[] mask = Compare(With(Blank(), 5, 10, 30, 2, Brown), With(Blank(), 5, 20, 30, 2, Brown));

        Assert.That(mask[10 * Size + 20], Is.EqualTo((byte)PixelChange.Vanished));
        Assert.That(mask[20 * Size + 20], Is.EqualTo((byte)PixelChange.Appeared));
        Assert.That(Changed(mask), Is.EqualTo(120));
    }

    [Test]
    public void ADeletedLineIsChanged()
    {
        byte[] mask = Compare(With(Blank(), 5, 10, 30, 2, Brown), Blank());

        Assert.That(Changed(mask), Is.EqualTo(60));
        Assert.That(mask[10 * Size + 5], Is.EqualTo((byte)PixelChange.Vanished));
    }

    [Test]
    public void ASymbolChangeIsChangedEvenWhereTheShapeIsTheSame()
    {
        // Same geometry, other symbol: a path that became a road.
        byte[] mask = Compare(With(Blank(), 5, 10, 30, 2, Black), With(Blank(), 5, 10, 30, 2, Yellow));

        Assert.That(Changed(mask), Is.EqualTo(60));
        Assert.That(mask[10 * Size + 5], Is.EqualTo((byte)PixelChange.Altered));
    }

    [Test]
    public void OnlyTheChangedSectionOfALongLineIsLit()
    {
        uint[] before = With(Blank(), 2, 10, 36, 2, Brown);

        // The same line, with one bend pushed well away.
        uint[] after = With(With(With(Blank(), 2, 10, 14, 2, Brown), 24, 10, 14, 2, Brown), 16, 20, 8, 2, Brown);

        byte[] mask = Compare(before, after);

        Assert.That(mask[10 * Size + 4], Is.EqualTo((byte)PixelChange.UnchangedInk), "start of the line");
        Assert.That(mask[10 * Size + 35], Is.EqualTo((byte)PixelChange.UnchangedInk), "end of the line");
        Assert.That(mask[10 * Size + 20], Is.EqualTo((byte)PixelChange.Vanished), "where the bend was");
        Assert.That(mask[20 * Size + 20], Is.EqualTo((byte)PixelChange.Appeared), "where the bend is");
    }

    [Test]
    public void SpecksSmallerThanTheMinimumAreDropped()
    {
        uint[] before = Blank();
        uint[] after = With(With(Blank(), 3, 3, 1, 1, Black), 20, 20, 3, 3, Black);

        byte[] mask = Compare(before, after, radius: 1, minSpeck: 4);

        Assert.That(mask[3 * Size + 3], Is.EqualTo((byte)PixelChange.UnchangedInk), "a one-pixel speck is noise");
        Assert.That(mask[21 * Size + 21], Is.EqualTo((byte)PixelChange.Appeared), "a 3 x 3 patch is an edit");
        Assert.That(Changed(mask), Is.EqualTo(9));
    }

    [Test]
    public void ToleranceAndSpeckScaleWithResolution()
    {
        Assert.That(ChangeComparer.RadiusFor(4f), Is.EqualTo(1), "0.25 mm at 4 px/mm");
        Assert.That(ChangeComparer.RadiusFor(16f), Is.EqualTo(4));
        Assert.That(ChangeComparer.RadiusFor(0.5f), Is.EqualTo(1), "never less than a pixel");
        Assert.That(ChangeComparer.MinSpeckFor(4f), Is.EqualTo(4), "0.5 mm square at 4 px/mm");
    }
}
