using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

using System.IO;

using OcadViewer.Diff;

using PurplePen.MapModel;

using SkiaSharp;

namespace OcadViewer.Core
{
    /// <summary>A parsed map held by <see cref="MapStore"/>, and what parsing found.</summary>
    public sealed class StoredMap : IDisposable
    {
        internal StoredMap(string key, Map map, RectangleF bounds, int formatVersion,
                           TimeSpan parseTime, string georeference, OcadMapSnapshot snapshot)
        {
            Key = key;
            Bounds = bounds;
            FormatVersion = formatVersion;
            ParseTime = parseTime;
            Georeference = georeference;
            Snapshot = snapshot;
            Map = map;
        }

        /// <summary>
        /// What the diff needs from this version, taken while the file's bytes were
        /// still in hand. <c>null</c> for a map the diff cannot read - an
        /// OpenOrienteering map, or OCAD 8 and older - which is shown but not compared.
        /// </summary>
        public OcadMapSnapshot Snapshot { get; }

        /// <summary>What the caller knows this map as - a version name, not a position.</summary>
        public string Key { get; }

        /// <summary>Bounding rectangle of all symbols, in paper millimetres.</summary>
        public RectangleF Bounds { get; }

        /// <summary>The OCAD format version the file was written in.</summary>
        public int FormatVersion { get; }

        public TimeSpan ParseTime { get; }

        /// <summary>
        /// The map's projection as a proj4 string, a grid description when it has
        /// no proj4, or <c>null</c> when the map is not georeferenced at all.
        /// </summary>
        public string Georeference { get; }

        internal Map Map { get; }

        public void Dispose() => Map.Dispose();
    }

    /// <summary>One rendered tile.</summary>
    /// <param name="Rgba">Straight RGBA, four bytes per pixel, row by row.</param>
    public sealed record RenderedTile(byte[] Rgba, int Pixels, TimeSpan RenderTime);

    /// <summary>What happened at one pixel between two versions.</summary>
    /// <remarks>
    /// Both versions are drawn onto opaque white, so "no ink here" is exactly
    /// white and the direction of a change is readable wherever one side is
    /// blank. Where both sides have ink the mask can see that it changed but not
    /// which way, which is <see cref="Altered"/> - common wherever a line was
    /// redrawn across a coloured area rather than across open ground.
    /// </remarks>
    public enum PixelChange : byte
    {
        Unchanged = 0,

        /// <summary>Blank before, ink after.</summary>
        Appeared = 1,

        /// <summary>Ink before, blank after.</summary>
        Vanished = 2,

        /// <summary>Ink both times, and not the same ink.</summary>
        Altered = 3
    }

    /// <summary>The size and extent a change mask will be computed at.</summary>
    public sealed record ChangeMaskPlan(int Width, int Height, RectangleF Area);

    /// <summary>
    /// Keeps a small number of parsed maps, each under a name the caller chooses.
    /// </summary>
    /// <remarks>
    /// Maps are addressed <em>by name</em> - "V3", "current" - and never by which
    /// pane they happen to be showing in. The previous design handed out slot 0 and
    /// slot 1, and that integer then had to mean the same thing in four layers
    /// across two languages: the page, the component, the Leaflet module and the
    /// interop. Worse, it tied a parsed map to a screen position, so selecting a
    /// different version re-read and re-parsed <em>both</em> sides even when one of
    /// them was already in memory and unchanged.
    ///
    /// Naming them instead means showing V3 beside V2, and then V2 beside V1,
    /// parses V1 only. The cost of being wrong about this is not small: a parse is
    /// 175-290 ms per map with AOT and several seconds without it, on top of a
    /// multi-megabyte read.
    ///
    /// <para>Capacity is a cache bound, not a pane count. Exceeding it evicts the
    /// map that has gone longest without being touched.</para>
    ///
    /// <para>Not thread safe, and does not need to be: WebAssembly runs this on one
    /// thread. The console harness uses it from one thread too.</para>
    /// </remarks>
    public sealed class MapStore : IDisposable
    {
        /// <summary>
        /// Both sides of a comparison, plus the one being swapped in.
        /// </summary>
        /// <remarks>
        /// Two is not enough, and the third is not slack. Changing the comparison
        /// shows one side at a time, so there is a moment when the incoming map has
        /// been parsed and the outgoing one is still on screen. At a capacity of
        /// two, loading the first new side evicted a map the other side was still
        /// drawing, and those tiles came back empty - 8 of them in a four-step
        /// walk through the versions. The number to keep is "everything on screen,
        /// plus what is arriving".
        /// </remarks>
        public const int DefaultCapacity = 3;

        private readonly Dictionary<string, StoredMap> maps = new(StringComparer.Ordinal);

        /// <summary>Keys in touch order, least recently used first.</summary>
        private readonly List<string> recent = [];

        private readonly int capacity;
        private readonly string templateDirectory;

        /// <param name="capacity">How many parsed maps to keep. At least one.</param>
        /// <param name="settings">
        /// How tiles are drawn. The default renders the map only: see the remarks
        /// on <see cref="MapStore"/> callers in a browser, where there is no
        /// directory for a template to be found in.
        /// </param>
        /// <param name="templateDirectory">
        /// Where the parser looks for templates and bitmaps a map references.
        /// </param>
        public MapStore(int capacity = DefaultCapacity,
                        MapRenderSettings settings = null,
                        string templateDirectory = null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

            this.capacity = capacity;
            this.templateDirectory = templateDirectory;
            Settings = settings ?? new MapRenderSettings();
        }

        /// <summary>How tiles from this store are drawn.</summary>
        public MapRenderSettings Settings { get; }

        /// <summary>Names currently held, in no particular order.</summary>
        public IReadOnlyCollection<string> Keys => maps.Keys;

        /// <summary>Whether <paramref name="key"/> is parsed and ready to render.</summary>
        public bool Contains(string key) => key is not null && maps.ContainsKey(key);

        /// <summary>
        /// Parses <paramref name="data"/> and keeps it under <paramref name="key"/>,
        /// replacing anything already held under that name.
        /// </summary>
        /// <param name="key">What to call it, e.g. a version name.</param>
        /// <param name="fileName">
        /// Name the bytes came from. Used to resolve templates relative to the map
        /// and reported in parse errors.
        /// </param>
        /// <remarks>
        /// Whether to call this at all is the caller's decision: a release never
        /// changes once written, while the working file changes under the app's
        /// feet. <see cref="Contains"/> is how a caller avoids re-reading a file it
        /// already has.
        /// </remarks>
        public StoredMap Load(string key, string fileName, byte[] data)
        {
            ArgumentException.ThrowIfNullOrEmpty(key);
            ArgumentException.ThrowIfNullOrEmpty(fileName);
            ArgumentNullException.ThrowIfNull(data);

            Unload(key);

            OcadMapLoadResult loaded = OcadMapLoader.Load(data, fileName, templateDirectory);
            RectangleF bounds = loaded.Bounds;

            if (bounds.Width <= 0 || bounds.Height <= 0) {
                loaded.Map.Dispose();
                throw new InvalidOperationException(
                    $"{fileName} parsed but its bounds are empty, so there is nothing to show.");
            }

            var stored = new StoredMap(key, loaded.Map, bounds, loaded.Format.version,
                                       loaded.ParseTime, GeoreferenceOf(loaded.Map),
                                       SnapshotOf(loaded.Map, data));

            maps.Add(key, stored);
            Touch(key);
            EvictDownToCapacity();

            return stored;
        }

        /// <summary>
        /// The diff's view of a freshly parsed map. A map the diff cannot read is
        /// still a map worth showing, so that is a missing snapshot, not a failed load.
        /// </summary>
        private static OcadMapSnapshot SnapshotOf(Map map, byte[] data)
        {
            try {
                return OcadMapSnapshot.Create(map, data);
            }
            catch (InvalidDataException) {
                return null;
            }
        }

        /// <summary>The map held under <paramref name="key"/>, or <c>null</c>.</summary>
        public StoredMap Get(string key)
        {
            if (key is null || !maps.TryGetValue(key, out StoredMap stored))
                return null;

            Touch(key);
            return stored;
        }

        /// <summary>Forgets <paramref name="key"/>. Returns whether it was there.</summary>
        public bool Unload(string key)
        {
            if (key is null || !maps.TryGetValue(key, out StoredMap stored))
                return false;

            maps.Remove(key);
            recent.Remove(key);
            stored.Dispose();
            return true;
        }

        /// <summary>
        /// Draws <paramref name="area"/> of <paramref name="key"/>'s map as a square
        /// tile <paramref name="pixels"/> on a side.
        /// </summary>
        /// <returns><c>null</c> when nothing is held under that name.</returns>
        public RenderedTile Render(string key, RectangleF area, int pixels)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 1);

            StoredMap stored = Get(key);
            if (stored is null)
                return null;

            var stopwatch = Stopwatch.StartNew();
            MapRenderResult result = MapRenderer.Render(stored.Map, area, pixels, pixels, Settings);
            stopwatch.Stop();

            using SKBitmap bitmap = result.Bitmap;
            return new RenderedTile(ToRgba(bitmap, pixels), pixels, stopwatch.Elapsed);
        }

        /// <summary>
        /// Where the two versions differ, pixel by pixel.
        /// </summary>
        /// <returns>
        /// One <see cref="PixelChange"/> per pixel, row by row, sized by
        /// <see cref="PlanChangeMask"/>. <c>null</c> if either version is missing.
        /// </returns>
        /// <remarks>
        /// Both versions are drawn over the same rectangle at the same size and
        /// compared. That is the whole trick: it needs no object identity, no
        /// georeferencing and no diff engine, and it reports what a person would
        /// actually see as different. What it cannot do is explain itself - a
        /// moved object shows as vanished at the old position and appeared at the
        /// new one, with nothing tying the two together.
        ///
        /// The comparison is exact. Identical geometry renders to identical
        /// pixels here, down to the phase of area patterns, because a pattern is
        /// anchored to the map rather than to the tile - so there is no noise to
        /// filter out, and anything lit really is different. The corollary is
        /// that redefining a symbol lights up every object using it.
        /// </remarks>
        public byte[] RenderChangeMask(string olderKey, string newerKey)
        {
            StoredMap older = Get(olderKey);
            StoredMap newer = Get(newerKey);

            if (older is null || newer is null)
                return null;

            ChangeMaskPlan plan = PlanFor(newer);

            MapRenderResult before = MapRenderer.Render(
                older.Map, plan.Area, plan.Width, plan.Height, Settings);

            using SKBitmap beforeBitmap = before.Bitmap;

            MapRenderResult after = MapRenderer.Render(
                newer.Map, plan.Area, plan.Width, plan.Height, Settings);

            using SKBitmap afterBitmap = after.Bitmap;

            return Classify(beforeBitmap, afterBitmap, plan.Width * plan.Height);
        }

        /// <summary>
        /// The size and extent <see cref="RenderChangeMask"/> will use for a
        /// comparison whose newer side is <paramref name="newerKey"/>.
        /// </summary>
        /// <returns><c>null</c> if that version is not loaded.</returns>
        /// <remarks>
        /// Separate from the render so a caller can lay out the overlay, and show
        /// how big the job is, before paying for it. It is a pure function of the
        /// map's bounds, so both calls agree without anything being remembered
        /// between them.
        /// </remarks>
        public ChangeMaskPlan PlanChangeMask(string newerKey)
        {
            StoredMap newer = Get(newerKey);
            return newer is null ? null : PlanFor(newer);
        }

        /// <summary>
        /// Detail is fixed per millimetre of paper rather than per map, so a small
        /// map is quick and a large one stays sharp.
        /// </summary>
        /// <remarks>
        /// Resolution is close to free here: the same area at 128, 256 and 512
        /// pixels measured 389, 389 and 435 ms, because the cost is managed
        /// geometry work rather than rasterising. The cap is about memory, not
        /// time - two bitmaps of this size exist at once while the mask is built.
        /// </remarks>
        private static ChangeMaskPlan PlanFor(StoredMap map)
        {
            const float PixelsPerMillimetre = 4f;
            const int MaxEdge = 4096;

            RectangleF area = map.Bounds;

            float scale = PixelsPerMillimetre;
            float longest = Math.Max(area.Width, area.Height) * scale;
            if (longest > MaxEdge)
                scale *= MaxEdge / longest;

            int width = Math.Max(1, (int)Math.Round(area.Width * scale));
            int height = Math.Max(1, (int)Math.Round(area.Height * scale));

            return new ChangeMaskPlan(width, height, area);
        }

        /// <summary>
        /// Compares two renders of the same rectangle, pixel by pixel.
        /// </summary>
        /// <remarks>
        /// Reads the bitmaps' own memory rather than <c>SKBitmap.Bytes</c>, which
        /// copies: at this size that would be tens of megabytes of copying for
        /// data that is read once. The two bitmaps share a layout, whatever Skia
        /// chose, and in every layout Skia uses the three colour bytes come first
        /// - so "all three at 255" means white in both RGBA and BGRA without
        /// having to know which one this is.
        /// </remarks>
        private static byte[] Classify(SKBitmap before, SKBitmap after, int pixelCount)
        {
            ReadOnlySpan<byte> oldPixels = before.GetPixelSpan();
            ReadOnlySpan<byte> newPixels = after.GetPixelSpan();

            var mask = new byte[pixelCount];

            for (int i = 0, at = 0; i < pixelCount; i++, at += 4) {
                byte oldR = oldPixels[at], oldG = oldPixels[at + 1], oldB = oldPixels[at + 2];
                byte newR = newPixels[at], newG = newPixels[at + 1], newB = newPixels[at + 2];

                if (oldR == newR && oldG == newG && oldB == newB)
                    continue;

                bool wasBlank = oldR == 255 && oldG == 255 && oldB == 255;
                bool isBlank = newR == 255 && newG == 255 && newB == 255;

                mask[i] = (byte)(wasBlank ? PixelChange.Appeared
                               : isBlank ? PixelChange.Vanished
                               : PixelChange.Altered);
            }

            return mask;
        }

        /// <summary>
        /// ImageData wants straight RGBA. Skia's native layout varies by platform,
        /// so convert rather than assume - but skip the copy when it already matches.
        /// </summary>
        private static byte[] ToRgba(SKBitmap bitmap, int pixels)
        {
            if (bitmap.ColorType == SKColorType.Rgba8888)
                return bitmap.Bytes;

            using var rgba = new SKBitmap(
                new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Unpremul));

            return bitmap.CopyTo(rgba, SKColorType.Rgba8888) ? rgba.Bytes : bitmap.Bytes;
        }

        private static string GeoreferenceOf(Map map)
        {
            using (map.Read()) {
                RealWorldCoords coords = map.RealWorldCoords;
                if (!coords.RealWorldOn)
                    return null;

                return string.IsNullOrWhiteSpace(coords.Proj4String)
                    ? $"grid {coords.RealWorldGridAndZone}"
                    : coords.Proj4String;
            }
        }

        private void Touch(string key)
        {
            recent.Remove(key);
            recent.Add(key);
        }

        private void EvictDownToCapacity()
        {
            while (recent.Count > capacity)
                Unload(recent[0]);
        }

        public void Dispose()
        {
            foreach (StoredMap stored in maps.Values)
                stored.Dispose();

            maps.Clear();
            recent.Clear();
        }
    }
}
