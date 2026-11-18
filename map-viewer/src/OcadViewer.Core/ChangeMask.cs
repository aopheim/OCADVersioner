using System;

namespace OcadViewer.Core
{
    /// <summary>
    /// Decides which pixels changed between two renders of the same rectangle,
    /// and which pixels are map at all.
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
    /// <para>What kind of change a pixel is, is not decided here: the caller says,
    /// through <c>classify</c>. That is the renderer's knowledge, and it stays in
    /// <see cref="MapStore"/>.</para>
    ///
    /// <para>Pure arithmetic on pixel arrays, with no Skia and no map, so it can be
    /// tested on arrays written out by hand.</para>
    ///
    /// <para>It belongs with whatever compares versions. When OcadViewer.Comparison
    /// reaches develop, this and its tests are expected to move there, with
    /// <see cref="MapStore"/> only drawing the two renders.</para>
    /// </remarks>
    public static class ChangeMask
    {
        /// <summary>
        /// How far, in paper millimetres, ink may lie from where it was and still
        /// count as unchanged.
        /// </summary>
        public const double DefaultToleranceMm = 0.25;

        /// <summary>The smallest change, in paper millimetres, worth showing.</summary>
        public const double DefaultMinimumChangeMm = 0.5;

        /// <summary>
        /// Set on every pixel inside the drawn map, changed or not, so the share
        /// of the map that changed can be measured against the map rather than
        /// against the rectangle around it.
        /// </summary>
        /// <remarks>The browser reads this value too; OrthogonalityTests holds the two copies together.</remarks>
        public const byte InMap = 0x80;

        /// <summary>
        /// The bits of a mask byte that say what changed. Zero there means
        /// unchanged; what a non-zero value means is up to <c>classify</c>.
        /// </summary>
        /// <remarks>The browser reads this value too; OrthogonalityTests holds the two copies together.</remarks>
        public const byte ChangeBits = 0x03;

        /// <summary>
        /// Colour only. The fourth byte is alpha in every layout Skia uses, and
        /// in a little-endian <see cref="uint"/> it is the top one.
        /// </summary>
        public const uint ColourBits = 0x00FF_FFFF;

        /// <summary>Opaque white, which is "no ink" on both RGBA and BGRA.</summary>
        public const uint Blank = 0x00FF_FFFF;

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
        /// <param name="classify">
        /// The mask value for a changed pixel, from its colour before and after
        /// (alpha removed). Must be non-zero and fit in <see cref="ChangeBits"/>.
        /// </param>
        /// <returns>
        /// One byte per pixel: <paramref name="classify"/>'s answer in
        /// <see cref="ChangeBits"/>, zero where nothing changed, with
        /// <see cref="InMap"/> set inside the drawn map.
        /// </returns>
        public static byte[] Compare(ReadOnlySpan<uint> before, ReadOnlySpan<uint> after,
                                     int width, int height, int tolerance, int minimumSpeck,
                                     Func<uint, uint, byte> classify)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
            ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
            ArgumentNullException.ThrowIfNull(classify);

            int count = width * height;
            if (before.Length < count || after.Length < count)
                throw new ArgumentException($"Both renders need {count} pixels.");

            var mask = new byte[count];
            var ink = new InkSpans(width, height);

            for (int y = 0, i = 0; y < height; y++) {
                for (int x = 0; x < width; x++, i++) {
                    uint old = before[i] & ColourBits;
                    uint now = after[i] & ColourBits;

                    if (old != Blank || now != Blank)
                        ink.Add(x, y);

                    if (old == now)
                        continue;

                    if (tolerance > 0
                        && HasColourNear(after, width, height, x, y, old, tolerance)
                        && HasColourNear(before, width, height, x, y, now, tolerance))
                        continue;

                    mask[i] = (byte)(classify(old, now) & ChangeBits);
                }
            }

            if (minimumSpeck > 1)
                DropSpecks(mask, width, height, minimumSpeck);

            ink.MarkMap(mask);
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
            double pixelsPerMillimetre,
            double toleranceMm = DefaultToleranceMm,
            double minimumSpeckMm = DefaultMinimumChangeMm)
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
        /// <remarks>
        /// A speck fits within <c>minimumSpeck - 1</c> pixels of any of its pixels,
        /// so each walk is confined to that window around where it starts and gives
        /// up the moment it reaches the window's edge: the cluster is too big to be
        /// a speck. Nothing is marked outside the window, so memory is the window's
        /// size however large a change is, and a large cluster costs each of its
        /// pixels one short, abandoned walk.
        /// </remarks>
        private static void DropSpecks(byte[] mask, int width, int height, int minimumSpeck)
        {
            int reach = minimumSpeck - 1;
            int side = 2 * reach + 1;
            var seen = new bool[side * side];
            var found = new int[side * side];

            for (int y = 0, start = 0; y < height; y++) {
                for (int x = 0; x < width; x++, start++) {
                    if (mask[start] == 0)
                        continue;

                    int size = WalkSpeck(mask, width, height, x, y, reach, seen, found);
                    for (int k = 0; k < size; k++)
                        mask[found[k]] = 0;
                }
            }
        }

        /// <summary>
        /// Walks the cluster holding (<paramref name="startX"/>, <paramref name="startY"/>)
        /// within <paramref name="reach"/> of it.
        /// </summary>
        /// <returns>
        /// How many pixels of <paramref name="found"/> hold the cluster, if it is a
        /// speck; 0 if it is not.
        /// </returns>
        private static int WalkSpeck(byte[] mask, int width, int height, int startX, int startY,
                                     int reach, bool[] seen, int[] found)
        {
            int side = 2 * reach + 1;
            int size = 0, next = 0;
            int left = startX, right = startX, top = startY, bottom = startY;
            bool speck = true;

            found[size++] = startY * width + startX;
            seen[reach * side + reach] = true;

            // The found pixels double as the queue: everything before 'next' has
            // had its neighbours looked at.
            while (speck && next < size) {
                int i = found[next++];
                int x = i % width, y = i / width;

                for (int dy = -1; dy <= 1 && speck; dy++) {
                    for (int dx = -1; dx <= 1; dx++) {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                            continue;

                        int n = ny * width + nx;
                        if (mask[n] == 0)
                            continue;

                        int wx = nx - startX + reach, wy = ny - startY + reach;
                        if (wx < 0 || wy < 0 || wx >= side || wy >= side) {
                            speck = false;
                            break;
                        }

                        if (seen[wy * side + wx])
                            continue;

                        seen[wy * side + wx] = true;
                        found[size++] = n;

                        left = Math.Min(left, nx); right = Math.Max(right, nx);
                        top = Math.Min(top, ny); bottom = Math.Max(bottom, ny);

                        // A speck spans fewer than minimumSpeck = reach + 1 pixels.
                        if (right - left >= reach || bottom - top >= reach) {
                            speck = false;
                            break;
                        }
                    }
                }
            }

            // Leave the window clean for the next walk.
            for (int k = 0; k < size; k++) {
                int x = found[k] % width - startX + reach, y = found[k] / width - startY + reach;
                seen[y * side + x] = false;
            }

            return speck ? size : 0;
        }

        /// <summary>
        /// Where the ink is: for every row its first and last inked pixel, and the
        /// same for every column, on either side.
        /// </summary>
        /// <remarks>
        /// White is ink too on an orienteering map: it is runnable forest. So
        /// "not white" would leave every forest out of the map. Spans fill it in,
        /// because forest lies between the lines around it, while the empty paper
        /// beyond the outermost line on any row or column stays out.
        /// </remarks>
        private sealed class InkSpans
        {
            private readonly int width, height;
            private readonly int[] rowLeft, rowRight, columnTop, columnBottom;

            public InkSpans(int width, int height)
            {
                this.width = width;
                this.height = height;

                rowLeft = new int[height];
                rowRight = new int[height];
                columnTop = new int[width];
                columnBottom = new int[width];

                Array.Fill(rowLeft, int.MaxValue);
                Array.Fill(rowRight, -1);
                Array.Fill(columnTop, int.MaxValue);
                Array.Fill(columnBottom, -1);
            }

            /// <summary>Called in row order, so a row's left edge is its first call.</summary>
            public void Add(int x, int y)
            {
                if (rowLeft[y] == int.MaxValue) rowLeft[y] = x;
                rowRight[y] = x;
                if (columnTop[x] == int.MaxValue) columnTop[x] = y;
                columnBottom[x] = y;
            }

            /// <summary>
            /// Sets <see cref="InMap"/> on every pixel between ink along its row
            /// and down its column.
            /// </summary>
            public void MarkMap(byte[] mask)
            {
                for (int y = 0; y < height; y++) {
                    for (int x = rowLeft[y]; x <= rowRight[y]; x++) {
                        if (y >= columnTop[x] && y <= columnBottom[x])
                            mask[y * width + x] |= InMap;
                    }
                }
            }
        }
    }
}
