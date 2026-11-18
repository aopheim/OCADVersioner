using System;
using System.Collections.Generic;

namespace OcadViewer.Core
{
    /// <summary>
    /// Turns two renders of the same rectangle into a change mask: one byte per
    /// pixel saying whether, and how, the map changed there.
    /// </summary>
    /// <remarks>
    /// <para>Comparing the renders exactly was too sensitive. Contours nobody
    /// touched lit up across whole maps, because the same geometry need not land
    /// on the same pixels twice: an anti-aliased edge blends with whatever is
    /// under it, and a coordinate that moved by a hundredth of a millimetre when
    /// OCAD saved can tip a line edge onto the next pixel. Three rules filter that
    /// out while still catching a road that was redrawn:</para>
    ///
    /// <list type="number">
    /// <item>The renders are drawn without anti-aliasing (the caller's job), so
    /// every pixel is a symbol colour rather than a blend.</item>
    /// <item>A pixel counts as changed only if its colour on one side appears
    /// nowhere within <c>tolerance</c> pixels on the other side. An edge one pixel
    /// over finds its colour next door and is let go. A redrawn road does not:
    /// its colour is nowhere near the old location.</item>
    /// <item>A cluster of changed pixels whose bounding box is smaller than
    /// <c>minimumSpeck</c> pixels in both directions is dropped. A lone stray
    /// pixel is noise; a line one pixel wide and a few millimetres long is not.</item>
    /// </list>
    ///
    /// <para>Pure arithmetic on pixel arrays, with no Skia and no map, so it can be
    /// tested on arrays written out by hand.</para>
    /// </remarks>
    public static class ChangeMask
    {
        /// <summary>
        /// Set on every pixel inside the drawn map, changed or not, so the share
        /// of the map that changed can be measured against the map rather than
        /// against the rectangle around it. The low bits are the
        /// <see cref="PixelChange"/>.
        /// </summary>
        public const byte InMap = 0x80;

        /// <summary>Picks the <see cref="PixelChange"/> out of a mask byte.</summary>
        public const byte ChangeBits = 0x03;

        /// <summary>
        /// Colour only. The fourth byte is alpha in every layout Skia uses, and
        /// in a little-endian <see cref="uint"/> it is the top one.
        /// </summary>
        private const uint ColourBits = 0x00FF_FFFF;

        /// <summary>Opaque white, which is "no ink" on both RGBA and BGRA.</summary>
        private const uint Blank = 0x00FF_FFFF;

        /// <summary>Marks a changed pixel already visited while finding specks.</summary>
        private const byte Visited = 0x40;

        /// <summary>
        /// Compares two renders of the same rectangle at the same size.
        /// </summary>
        /// <param name="before">The older map, one packed pixel per element, row by row.</param>
        /// <param name="after">The newer map, the same layout.</param>
        /// <param name="tolerance">
        /// How far, in pixels, a colour may move and still count as the same
        /// ink. 0 compares exactly.
        /// </param>
        /// <param name="minimumSpeck">
        /// Clusters of changed pixels narrower and shorter than this many pixels are
        /// dropped. 1 or less keeps everything.
        /// </param>
        /// <returns>
        /// One byte per pixel: the <see cref="PixelChange"/> in
        /// <see cref="ChangeBits"/>, with <see cref="InMap"/> set inside the drawn map.
        /// </returns>
        public static byte[] Compare(ReadOnlySpan<uint> before, ReadOnlySpan<uint> after,
                                     int width, int height, int tolerance, int minimumSpeck)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

            int count = width * height;
            if (before.Length < count || after.Length < count)
                throw new ArgumentException($"Both renders need {count} pixels.");

            var mask = new byte[count];

            for (int y = 0, i = 0; y < height; y++) {
                for (int x = 0; x < width; x++, i++) {
                    uint old = before[i] & ColourBits;
                    uint now = after[i] & ColourBits;

                    if (old == now)
                        continue;

                    if (tolerance > 0
                        && HasColourNear(after, width, height, x, y, old, tolerance)
                        && HasColourNear(before, width, height, x, y, now, tolerance))
                        continue;

                    mask[i] = (byte)(old == Blank ? PixelChange.Appeared
                                   : now == Blank ? PixelChange.Vanished
                                   : PixelChange.Altered);
                }
            }

            if (minimumSpeck > 1)
                DropSpecks(mask, width, height, minimumSpeck);

            MarkMap(mask, before, after, width, height);
            return mask;
        }

        /// <summary>The tolerance and speck size for a mask drawn at this resolution.</summary>
        /// <param name="pixelsPerMillimetre">Of paper.</param>
        /// <param name="toleranceMm">How far ink may move and still be the same ink.</param>
        /// <param name="minimumSpeckMm">The smallest change worth showing.</param>
        /// <remarks>
        /// The tolerance never drops below one pixel. When a large map is drawn coarse,
        /// a quarter of a millimetre is under a pixel, but an edge can still tip
        /// onto the next pixel, and that is exactly the noise this removes.
        /// </remarks>
        public static (int Tolerance, int MinimumSpeck) PixelsFor(
            double pixelsPerMillimetre, double toleranceMm, double minimumSpeckMm)
            => (Math.Max(1, (int)Math.Round(toleranceMm * pixelsPerMillimetre, MidpointRounding.AwayFromZero)),
                (int)Math.Round(minimumSpeckMm * pixelsPerMillimetre, MidpointRounding.AwayFromZero));

        private static bool HasColourNear(ReadOnlySpan<uint> pixels, int width, int height,
                                          int x, int y, uint colour, int radius)
        {
            int top = Math.Max(0, y - radius), bottom = Math.Min(height - 1, y + radius);
            int left = Math.Max(0, x - radius), right = Math.Min(width - 1, x + radius);

            for (int row = top; row <= bottom; row++) {
                ReadOnlySpan<uint> line = pixels.Slice(row * width + left, right - left + 1);
                foreach (uint pixel in line)
                    if ((pixel & ColourBits) == colour)
                        return true;
            }

            return false;
        }

        /// <summary>
        /// Clears every 8-connected cluster of changed pixels that fits inside a
        /// box smaller than <paramref name="minimumSpeck"/> both ways.
        /// </summary>
        private static void DropSpecks(byte[] mask, int width, int height, int minimumSpeck)
        {
            var cluster = new List<int>();
            var pending = new Stack<int>();

            for (int start = 0; start < mask.Length; start++) {
                if (mask[start] == 0 || (mask[start] & Visited) != 0)
                    continue;

                cluster.Clear();
                pending.Push(start);
                mask[start] |= Visited;

                int left = int.MaxValue, right = -1, top = int.MaxValue, bottom = -1;

                while (pending.Count > 0) {
                    int i = pending.Pop();
                    cluster.Add(i);

                    int x = i % width, y = i / width;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);

                    for (int dy = -1; dy <= 1; dy++) {
                        int ny = y + dy;
                        if (ny < 0 || ny >= height)
                            continue;

                        for (int dx = -1; dx <= 1; dx++) {
                            int nx = x + dx;
                            if (nx < 0 || nx >= width)
                                continue;

                            int n = ny * width + nx;
                            if (mask[n] != 0 && (mask[n] & Visited) == 0) {
                                mask[n] |= Visited;
                                pending.Push(n);
                            }
                        }
                    }
                }

                if (right - left + 1 < minimumSpeck && bottom - top + 1 < minimumSpeck) {
                    foreach (int i in cluster)
                        mask[i] = 0;
                }
            }

            // Only now: a cluster that stays has to stay marked while the scan
            // goes on, or each of its pixels would start the same walk again.
            for (int i = 0; i < mask.Length; i++)
                mask[i] &= unchecked((byte)~Visited);
        }

        /// <summary>
        /// Sets <see cref="InMap"/> on every pixel that lies between ink both
        /// along its row and down its column, on either side.
        /// </summary>
        /// <remarks>
        /// White is ink too on an orienteering map: it is runnable forest. So
        /// "not white" would leave every forest out of the map. Spans fill it in,
        /// because forest lies between the lines around it, while the empty paper
        /// beyond the outermost line on any row or column stays out.
        /// </remarks>
        private static void MarkMap(byte[] mask, ReadOnlySpan<uint> before, ReadOnlySpan<uint> after,
                                    int width, int height)
        {
            var columnTop = new int[width];
            var columnBottom = new int[width];
            Array.Fill(columnTop, int.MaxValue);
            Array.Fill(columnBottom, -1);

            var rowLeft = new int[height];
            var rowRight = new int[height];

            for (int y = 0, i = 0; y < height; y++) {
                rowLeft[y] = int.MaxValue;
                rowRight[y] = -1;

                for (int x = 0; x < width; x++, i++) {
                    if ((before[i] & ColourBits) == Blank && (after[i] & ColourBits) == Blank)
                        continue;

                    if (x < rowLeft[y]) rowLeft[y] = x;
                    rowRight[y] = x;
                    if (y < columnTop[x]) columnTop[x] = y;
                    columnBottom[x] = y;
                }
            }

            for (int y = 0; y < height; y++) {
                for (int x = rowLeft[y]; x <= rowRight[y]; x++) {
                    if (y >= columnTop[x] && y <= columnBottom[x])
                        mask[y * width + x] |= InMap;
                }
            }
        }
    }
}
