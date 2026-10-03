using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices.JavaScript;

using OcadViewer.Core;

using PurplePen.MapModel;

using SkiaSharp;

namespace OcadViewer.Wasm;

/// <summary>
/// What the Leaflet tile layer calls. One map is loaded at a time per slot, and
/// tiles are rendered on demand for a world rectangle.
/// </summary>
/// <remarks>
/// These are <c>[JSExport]</c> rather than <c>[JSInvokable]</c> on purpose. A tile
/// is 256 KB of raw RGBA; <c>[JSExport]</c> hands that to JavaScript as a typed
/// array, whereas <c>DotNetObjectReference.invokeMethodAsync</c> would base64 it
/// inside JSON and make every tile pay for a string and a parse.
///
/// Two slots exist because the app shows two versions side by side. Keeping both
/// parsed avoids re-reading a file every time the user switches which pair is
/// being compared.
/// </remarks>
public static partial class TileInterop
{
    private const int SlotCount = 2;

    private static readonly LoadedMap[] Slots = new LoadedMap[SlotCount];

    private sealed class LoadedMap(Map map, RectangleF bounds, int version, long parseMs)
    {
        public Map Map { get; } = map;
        public RectangleF Bounds { get; } = bounds;
        public int Version { get; } = version;
        public long ParseMs { get; } = parseMs;

        /// <summary>Set by JS to abandon a render that is no longer wanted.</summary>
        public bool CancelRequested { get; set; }
    }

    /// <summary>Writes a font into the in-memory filesystem, ready for registration.</summary>
    [JSExport]
    internal static void StageFont(string fileName, byte[] data)
    {
        Directory.CreateDirectory("/fonts");
        File.WriteAllBytes($"/fonts/{fileName}", data);
    }

    /// <summary>Registers every staged font. Must happen before any map is parsed.</summary>
    [JSExport]
    internal static int InstallFonts() => FontPack.InstallFromDirectory("/fonts");

    /// <summary>
    /// Parses a map into <paramref name="slot"/>. Returns a JSON summary, because
    /// the caller only needs numbers back and a struct would mean more marshalling
    /// machinery than it is worth.
    /// </summary>
    [JSExport]
    internal static string LoadMap(int slot, string fileName, byte[] data)
    {
        if (slot is < 0 or >= SlotCount)
            return Error($"slot must be 0..{SlotCount - 1}");

        try {
            Slots[slot]?.Map.Dispose();
            Slots[slot] = null;

            Directory.CreateDirectory("/maps");
            string path = $"/maps/{slot}_{fileName}";
            File.WriteAllBytes(path, data);

            OcadMapLoadResult loaded = OcadMapLoader.Load(data, path, "/maps");
            RectangleF b = loaded.Bounds;

            if (b.Width <= 0 || b.Height <= 0) {
                loaded.Map.Dispose();
                return Error("the map parsed but its bounds are empty");
            }

            Slots[slot] = new LoadedMap(loaded.Map, b, loaded.Format.version,
                                        (long)loaded.ParseTime.TotalMilliseconds);

            return "{\"ok\":true"
                 + $",\"version\":{loaded.Format.version}"
                 + $",\"minX\":{N(b.Left)},\"minY\":{N(b.Top)}"
                 + $",\"maxX\":{N(b.Right)},\"maxY\":{N(b.Bottom)}"
                 + $",\"parseMs\":{(long)loaded.ParseTime.TotalMilliseconds}}}";
        }
        catch (Exception ex) {
            return Error($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Releases a slot's map.</summary>
    [JSExport]
    internal static void UnloadMap(int slot)
    {
        if (slot is < 0 or >= SlotCount)
            return;
        Slots[slot]?.Map.Dispose();
        Slots[slot] = null;
    }

    /// <summary>
    /// Renders one tile of <paramref name="slot"/>'s map as raw RGBA, sized
    /// <paramref name="pixels"/> square.
    /// </summary>
    /// <returns>RGBA bytes, or <c>null</c> if the slot is empty or the render was cancelled.</returns>
    [JSExport]
    internal static byte[] RenderTile(int slot, double minX, double minY, double width, double height, int pixels)
    {
        if (slot is < 0 or >= SlotCount)
            return null;

        LoadedMap loaded = Slots[slot];
        if (loaded is null)
            return null;

        loaded.CancelRequested = false;
        var rect = new RectangleF((float)minX, (float)minY, (float)width, (float)height);

        var stopwatch = Stopwatch.StartNew();
        MapRenderResult result;
        try {
            result = MapRenderer.Render(
                loaded.Map, rect, pixels, pixels, settings: null,
                throwOnCancel: () => {
                    if (loaded.CancelRequested)
                        throw new OperationCanceledException();
                });
        }
        catch (OperationCanceledException) {
            LastRenderMs = stopwatch.Elapsed.TotalMilliseconds;
            return null;
        }
        stopwatch.Stop();
        LastRenderMs = stopwatch.Elapsed.TotalMilliseconds;

        using SKBitmap bitmap = result.Bitmap;

        // ImageData wants straight RGBA. Skia's native layout varies, so convert
        // rather than assume - but skip the copy when it already matches.
        if (bitmap.ColorType == SKColorType.Rgba8888)
            return bitmap.Bytes;

        using var rgba = new SKBitmap(new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        return bitmap.CopyTo(rgba, SKColorType.Rgba8888) ? rgba.Bytes : bitmap.Bytes;
    }

    /// <summary>
    /// Asks the in-flight render for <paramref name="slot"/> to stop. Checked after
    /// every colour layer, so it takes effect within one layer rather than at once.
    /// </summary>
    [JSExport]
    internal static void CancelRender(int slot)
    {
        if (slot is >= 0 and < SlotCount && Slots[slot] is { } loaded)
            loaded.CancelRequested = true;
    }

    /// <summary>Milliseconds the last <see cref="RenderTile"/> call spent drawing.</summary>
    [JSExport]
    internal static double GetLastRenderMs() => LastRenderMs;

    private static double LastRenderMs;

    /// <summary>
    /// What the parser worked out about real-world coordinates. Doubles as evidence
    /// that the embedded EPSG table survives trimming.
    /// </summary>
    [JSExport]
    internal static string Georeference(int slot)
    {
        if (slot is < 0 or >= SlotCount || Slots[slot] is not { } loaded)
            return "no map loaded";

        using (loaded.Map.Read()) {
            RealWorldCoords coords = loaded.Map.RealWorldCoords;
            if (!coords.RealWorldOn)
                return "not georeferenced";
            return string.IsNullOrWhiteSpace(coords.Proj4String)
                ? $"grid {coords.RealWorldGridAndZone}, no proj4"
                : coords.Proj4String;
        }
    }

    private static string Error(string message)
        => $"{{\"ok\":false,\"error\":\"{message.Replace("\\", " ").Replace("\"", "'")}\"}}";

    private static string N(float value)
        => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
