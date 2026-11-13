using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;

using PurplePen.MapModel;

using SkiaSharp;

namespace OcadViewer.Core
{
    /// <summary>A parsed map held by <see cref="MapStore"/>, and what parsing found.</summary>
    public sealed class StoredMap : IDisposable
    {
        internal StoredMap(string key, Map map, RectangleF bounds, int formatVersion,
                           TimeSpan parseTime, string georeference)
        {
            Key = key;
            Bounds = bounds;
            FormatVersion = formatVersion;
            ParseTime = parseTime;
            Georeference = georeference;
            Map = map;
        }

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
                                       loaded.ParseTime, GeoreferenceOf(loaded.Map));

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
