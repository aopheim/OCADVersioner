using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

using PurplePen.MapModel;

using SkiaSharp;

namespace OcadViewer.Core
{
    /// <summary>A parsed map held by <see cref="MapStore"/>, and what parsing found.</summary>
    public sealed class StoredMap : IDisposable
    {
        internal StoredMap(string key, int loadId, Map map, RectangleF bounds, int formatVersion,
                           TimeSpan parseTime, string georeference,
                           PaperToMercator paperToMercator)
        {
            Key = key;
            LoadId = loadId;
            Bounds = bounds;
            FormatVersion = formatVersion;
            ParseTime = parseTime;
            Georeference = georeference;
            PaperToMercator = paperToMercator;
            Map = map;
        }

        /// <summary>What the caller knows this map as - a version name, not a position.</summary>
        public string Key { get; }

        /// <summary>
        /// Which load this is, unique within its store. Reading a key again gives
        /// it a new one, so anything computed from a map - a change mask - can
        /// tell whether it still describes the parse that is held now.
        /// </summary>
        public int LoadId { get; }

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

        /// <summary>
        /// Where the paper is on a web map, read from the file's georeference, or
        /// <c>null</c> when it has none that can be used.
        /// </summary>
        public PaperToMercator PaperToMercator { get; }

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
    /// pane they are showing in, so showing V3 beside V2 and then V2 beside V1
    /// parses V1 only. A parse is 175-290 ms per map with AOT and several seconds
    /// without it, on top of a multi-megabyte read.
    ///
    /// <para>A name means something only within one project: every project has a
    /// "V1". Use one store per project and <see cref="Clear"/> it when the
    /// project changes.</para>
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
        /// Changing the comparison shows one side at a time, so there is a moment
        /// when the incoming map has been parsed and the outgoing one is still on
        /// screen. At a capacity of two, loading the first new side evicts a map
        /// the other side is still drawing, and those tiles come back empty. The
        /// number to keep is "everything on screen, plus what is arriving".
        /// </remarks>
        public const int DefaultCapacity = 3;

        /// <summary>
        /// The longest edge a change mask may have. The cap is about memory: two
        /// bitmaps of this size exist at once while a mask is built.
        /// </summary>
        public const int MaxMaskEdge = 4096;

        private readonly Dictionary<string, StoredMap> maps = new(StringComparer.Ordinal);

        /// <summary>The last <see cref="StoredMap.LoadId"/> handed out.</summary>
        private int loads;

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

            // Parse before letting go of what is held under this name. The
            // working file is re-read while OCAD may be half way through saving
            // it, and a failed parse must leave the last good map in place - the
            // pane showing it still draws its tiles from this key.
            OcadMapLoadResult loaded = OcadMapLoader.Load(data, fileName, templateDirectory);
            RectangleF bounds = loaded.Bounds;

            if (bounds.Width <= 0 || bounds.Height <= 0) {
                loaded.Map.Dispose();
                throw new InvalidOperationException(
                    $"{fileName} parsed but its bounds are empty, so there is nothing to show.");
            }

            var stored = new StoredMap(key, ++loads, loaded.Map, bounds, loaded.Format.version,
                                       loaded.ParseTime, GeoreferenceOf(loaded.Map),
                                       Georeference.Of(loaded.Map, bounds));

            Unload(key);
            maps.Add(key, stored);
            Touch(key);
            EvictDownToCapacity();

            return stored;
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
        /// Where the two versions differ, pixel by pixel, over the rectangle and
        /// at the size <paramref name="plan"/> gives.
        /// </summary>
        /// <param name="plan">
        /// Normally what <see cref="PlanChangeMask"/> answered for the same two
        /// versions. The render does what the plan says rather than working the
        /// plan out again, so the caller's layout and the mask cannot disagree.
        /// </param>
        /// <returns>
        /// One byte per pixel, row by row, as <see cref="ChangeMask.Compare"/>
        /// describes. <c>null</c> if either version is missing.
        /// </returns>
        /// <remarks>
        /// Both versions are drawn over the same rectangle at the same size and
        /// compared. That is the whole trick: it needs no object identity, no
        /// georeferencing and no diff engine, and it reports what a person would
        /// actually see as different. What it cannot do is explain itself - a
        /// moved object shows as vanished at the old position and appeared at the
        /// new one, with nothing tying the two together.
        ///
        /// The comparison is not exact, on purpose. Untouched contours lit up
        /// when it was: the same line can land a pixel over from one save to the
        /// next, and an anti-aliased edge changes with whatever is under it. So
        /// both are drawn without anti-aliasing, ink may move
        /// <see cref="ChangeMask.DefaultToleranceMm"/> and still be the same ink,
        /// and changes smaller than <see cref="ChangeMask.DefaultMinimumChangeMm"/>
        /// are dropped (<see cref="ChangeMask"/>). Redefining a symbol still lights
        /// up every object using it.
        /// </remarks>
        public byte[] RenderChangeMask(string olderKey, string newerKey, ChangeMaskPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentOutOfRangeException.ThrowIfLessThan(plan.Width, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(plan.Height, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plan.Width, MaxMaskEdge);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(plan.Height, MaxMaskEdge);

            StoredMap older = Get(olderKey);
            StoredMap newer = Get(newerKey);

            if (older is null || newer is null)
                return null;

            MapRenderSettings crisp = WithoutAntiAliasing(Settings);

            MapRenderResult before = MapRenderer.Render(
                older.Map, plan.Area, plan.Width, plan.Height, crisp);

            using SKBitmap beforeBitmap = before.Bitmap;

            MapRenderResult after = MapRenderer.Render(
                newer.Map, plan.Area, plan.Width, plan.Height, crisp);

            using SKBitmap afterBitmap = after.Bitmap;

            double pixelsPerMillimetre = plan.Width / (double)plan.Area.Width;
            (int tolerance, int minimumSpeck) = ChangeMask.PixelsFor(pixelsPerMillimetre);

            // The bitmaps' own memory rather than SKBitmap.Bytes, which copies:
            // at this size that is tens of megabytes copied to be read once.
            return ChangeMask.Compare(
                MemoryMarshal.Cast<byte, uint>(beforeBitmap.GetPixelSpan()),
                MemoryMarshal.Cast<byte, uint>(afterBitmap.GetPixelSpan()),
                plan.Width, plan.Height, tolerance, minimumSpeck, Classify);
        }

        /// <summary>What kind of change a pixel is, from its colour before and after.</summary>
        /// <remarks>
        /// Both versions are drawn onto opaque white, so blank on one side tells
        /// the direction. Only the renderer knows that, so the answer is given here.
        /// </remarks>
        private static byte Classify(uint before, uint after)
            => (byte)(before == ChangeMask.Blank ? PixelChange.Appeared
                    : after == ChangeMask.Blank ? PixelChange.Vanished
                    : PixelChange.Altered);

        /// <summary>
        /// The store's settings with anti-aliasing off, so each pixel of a
        /// comparison render is a symbol colour rather than a blend of neighbours.
        /// </summary>
        private static MapRenderSettings WithoutAntiAliasing(MapRenderSettings settings) => new() {
            Templates = settings.Templates,
            BlendOverprintedColors = settings.BlendOverprintedColors,
            UsePatternBitmaps = settings.UsePatternBitmaps,
            Background = settings.Background,
            AntiAlias = false
        };

        /// <summary>
        /// The size and extent to compare two versions at, for
        /// <see cref="RenderChangeMask"/>.
        /// </summary>
        /// <returns><c>null</c> if either version is not loaded.</returns>
        /// <remarks>
        /// Separate from the render so a caller can lay out the overlay, and show
        /// how big the job is, before paying for it.
        /// </remarks>
        public ChangeMaskPlan PlanChangeMask(string olderKey, string newerKey)
        {
            StoredMap older = Get(olderKey);
            StoredMap newer = Get(newerKey);
            return older is null || newer is null ? null : PlanFor(older, newer);
        }

        /// <summary>
        /// Detail is fixed per millimetre of paper rather than per map, so a small
        /// map is quick and a large one stays sharp.
        /// </summary>
        /// <remarks>
        /// Resolution is close to free here: the same area at 128, 256 and 512
        /// pixels measured 389, 389 and 435 ms, because the cost is managed
        /// geometry work rather than rasterising, so the cap is
        /// <see cref="MaxMaskEdge"/>, which is about memory.
        ///
        /// The area covers both versions, not just the newer one. Deleting the
        /// outermost object shrinks the newer map's bounds, and a mask clipped to
        /// them would never see that the object went.
        /// </remarks>
        private static ChangeMaskPlan PlanFor(StoredMap older, StoredMap newer)
        {
            const float PixelsPerMillimetre = 4f;

            RectangleF area = RectangleF.Union(older.Bounds, newer.Bounds);

            float scale = PixelsPerMillimetre;
            float longest = Math.Max(area.Width, area.Height) * scale;
            if (longest > MaxMaskEdge)
                scale *= MaxMaskEdge / longest;

            int width = Math.Clamp((int)Math.Round(area.Width * scale), 1, MaxMaskEdge);
            int height = Math.Clamp((int)Math.Round(area.Height * scale), 1, MaxMaskEdge);

            return new ChangeMaskPlan(width, height, area);
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

        /// <summary>Forgets every map, for instance because a different project was opened.</summary>
        public void Clear()
        {
            foreach (StoredMap stored in maps.Values)
                stored.Dispose();

            maps.Clear();
            recent.Clear();
        }

        public void Dispose() => Clear();
    }
}
