using System;
using System.Collections.Generic;

namespace OcadViewer.Core
{
    /// <summary>
    /// Compares two renders of the same rectangle and says, pixel by pixel,
    /// what changed - allowing for a small positional tolerance.
    /// </summary>
    /// <remarks>
    /// <para>Why a tolerance at all: OCAD nudges coordinates when it re-saves a
    /// map. Between two saves of the same map almost every object moves by one
    /// or two hundredths of a millimetre, and an exact pixel comparison lights
    /// up every edge of every line. A change has to be bigger than the
    /// tolerance to count.</para>
    ///
    /// <para>The test is symmetric. A pixel is unchanged when its newer colour
    /// is found within the tolerance in the older render <em>and</em> its older
    /// colour is found within the tolerance in the newer render. So a line that
    /// moved a little is unchanged, while a line that was deleted (its colour is
    /// nowhere near any more), added, or given another symbol (another colour)
    /// is changed.</para>
    ///
    /// <para>The renders must be drawn without anti-aliasing, so every pixel is
    /// a pure map colour. Blended edge colours shift with every sub-pixel move
    /// and would never find their twin.</para>
    ///
    /// <para>Changed clusters smaller than <c>minSpeck</c> pixels are dropped:
    /// stray pixels where two symbols meet, rather than edits.</para>
    /// </remarks>
    internal static class ChangeComparer
    {
        /// <summary>
        /// How far an edge may move, in paper millimetres, and still count as
        /// unchanged. About half a contour line width.
        /// </summary>
        public const float ToleranceMillimetres = 0.25f;

        /// <summary>
        /// Changed clusters covering less than this square, in paper
        /// millimetres, are dropped as noise.
        /// </summary>
        public const float SpeckMillimetres = 0.5f;

        /// <summary>
        /// Both renders are opaque, so the alpha byte says nothing. In every
        /// layout Skia uses the three colour bytes come first, which on a
        /// little-endian machine (x86, ARM, WebAssembly) are the low three bytes
        /// of the pixel read as a <see cref="uint"/>.
        /// </summary>
        private const uint ColourBits = 0x00FFFFFF;

        /// <summary>White in both RGBA and BGRA: no ink.</summary>
        private const uint White = 0x00FFFFFF;

        /// <summary>Marks a pixel already visited while finding clusters.</summary>
        private const byte Visited = 0x80;

        /// <summary>The tolerance in pixels, at least one.</summary>
        public static int RadiusFor(float pixelsPerMillimetre)
            => Math.Max(1, (int)Math.Round(ToleranceMillimetres * pixelsPerMillimetre));

        /// <summary>The smallest cluster kept, in pixels, at least one.</summary>
        public static int MinSpeckFor(float pixelsPerMillimetre)
        {
            float side = SpeckMillimetres * pixelsPerMillimetre;
            return Math.Max(1, (int)Math.Round(side * side));
        }

        /// <summary>
        /// One <see cref="PixelChange"/> per pixel, row by row.
        /// </summary>
        /// <param name="before">The older render, one pixel per element.</param>
        /// <param name="after">The newer render, same size and layout.</param>
        /// <param name="radius">
        /// How far, in pixels horizontally and vertically, a colour may have
        /// moved and still count as the same.
        /// </param>
        /// <param name="minSpeck">Changed clusters smaller than this many pixels are dropped.</param>
        public static byte[] Compare(ReadOnlySpan<uint> before, ReadOnlySpan<uint> after,
                                     int width, int height, int radius, int minSpeck)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(radius, 0);

            int count = checked(width * height);
            if (before.Length < count || after.Length < count)
                throw new ArgumentException("Both renders must hold width x height pixels.");

            var mask = new byte[count];

            for (int y = 0, i = 0; y < height; y++) {
                for (int x = 0; x < width; x++, i++) {
                    uint older = before[i] & ColourBits;
                    uint newer = after[i] & ColourBits;

                    if (older == newer) {
                        if (older != White)
                            mask[i] = (byte)PixelChange.UnchangedInk;
                        continue;
                    }

                    bool moved = Near(before, width, height, x, y, radius, newer)
                              && Near(after, width, height, x, y, radius, older);

                    // The two colours differ, so at least one of them is ink.
                    mask[i] = (byte)(moved ? PixelChange.UnchangedInk
                                   : older == White ? PixelChange.Appeared
                                   : newer == White ? PixelChange.Vanished
                                   : PixelChange.Altered);
                }
            }

            if (minSpeck > 1)
                DropSpecks(mask, width, height, minSpeck);

            return mask;
        }

        /// <summary>Whether <paramref name="colour"/> occurs within the radius of (x, y).</summary>
        private static bool Near(ReadOnlySpan<uint> pixels, int width, int height,
                                 int x, int y, int radius, uint colour)
        {
            int top = Math.Max(0, y - radius), bottom = Math.Min(height - 1, y + radius);
            int left = Math.Max(0, x - radius), right = Math.Min(width - 1, x + radius);

            for (int row = top; row <= bottom; row++) {
                int at = row * width;
                for (int column = left; column <= right; column++)
                    if ((pixels[at + column] & ColourBits) == colour)
                        return true;
            }

            return false;
        }

        private static bool IsChange(byte value)
            => value is (byte)PixelChange.Appeared or (byte)PixelChange.Vanished or (byte)PixelChange.Altered;

        /// <summary>
        /// Turns every changed cluster (8-connected) smaller than
        /// <paramref name="minSpeck"/> pixels back into unchanged ink.
        /// </summary>
        /// <remarks>
        /// Marks visited pixels in the mask itself rather than in a label array:
        /// at the largest mask size a label per pixel would be 64 MB.
        /// </remarks>
        private static void DropSpecks(byte[] mask, int width, int height, int minSpeck)
        {
            var cluster = new List<int>();
            var pending = new Stack<int>();

            for (int start = 0; start < mask.Length; start++) {
                if (!IsChange(mask[start]))
                    continue;

                cluster.Clear();
                mask[start] |= Visited;
                pending.Push(start);

                while (pending.Count > 0) {
                    int at = pending.Pop();
                    cluster.Add(at);

                    int x = at % width, y = at / width;

                    for (int dy = -1; dy <= 1; dy++) {
                        int row = y + dy;
                        if (row < 0 || row >= height)
                            continue;

                        for (int dx = -1; dx <= 1; dx++) {
                            int column = x + dx;
                            if (column < 0 || column >= width)
                                continue;

                            int next = row * width + column;
                            if (!IsChange(mask[next]))
                                continue;

                            mask[next] |= Visited;
                            pending.Push(next);
                        }
                    }
                }

                // Clusters that stay keep their mark until the end, so the scan
                // does not walk them again from each of their pixels.
                if (cluster.Count < minSpeck)
                    foreach (int at in cluster)
                        mask[at] = (byte)PixelChange.UnchangedInk;
            }

            for (int i = 0; i < mask.Length; i++)
                mask[i] &= unchecked((byte)~Visited);
        }
    }
}
