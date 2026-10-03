using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices.JavaScript;

using OcadViewer.Core;

using PurplePen.MapModel;

using SkiaSharp;

namespace OcadViewer.TileSpike;

/// <summary>
/// The surface JavaScript sees. Everything behind it is the vendored Purple Pen
/// parser and renderer.
/// </summary>
/// <remarks>
/// Deliberately small and procedural: the point of the spike is to find out what
/// the JS-to-WebAssembly boundary costs for a tile, not to design an API.
/// </remarks>
public static partial class Interop
{
    private static Map currentMap;
    private static RectangleF currentBounds;

    public static void Main()
    {
        // Nothing to do at startup. JS drives everything from here.
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
    /// Parses a map and keeps it loaded. Returns a small JSON summary rather than a
    /// marshalled object, because the spike only needs numbers back.
    /// </summary>
    [JSExport]
    internal static string LoadMap(string fileName, byte[] data)
    {
        try {
            currentMap?.Dispose();
            currentMap = null;

            Directory.CreateDirectory("/maps");
            string path = $"/maps/{fileName}";
            File.WriteAllBytes(path, data);

            OcadMapLoadResult loaded = OcadMapLoader.Load(data, path, "/maps");
            currentMap = loaded.Map;
            currentBounds = loaded.Bounds;

            return $$"""
                {"ok":true,"version":{{loaded.Format.version}},
                 "minX":{{F(currentBounds.Left)}},"minY":{{F(currentBounds.Top)}},
                 "maxX":{{F(currentBounds.Right)}},"maxY":{{F(currentBounds.Bottom)}},
                 "parseMs":{{(long)loaded.ParseTime.TotalMilliseconds}}}
                """.Replace("\n", "").Replace(" ", "");
        }
        catch (Exception ex) {
            string message = ex.Message.Replace("\\", " ").Replace("\"", "'");
            return $"{{\"ok\":false,\"error\":\"{ex.GetType().Name}: {message}\"}}";
        }
    }

    /// <summary>
    /// Renders one tile and returns it as raw RGBA, ready for ImageData.
    /// </summary>
    /// <remarks>
    /// Raw pixels rather than PNG on purpose: it keeps PNG encode and decode out of
    /// the measurement, and lets JS do a synchronous putImageData with no await.
    /// </remarks>
    [JSExport]
    internal static byte[] RenderTile(double minX, double minY, double width, double height, int pixels)
    {
        if (currentMap is null)
            return null;

        var rect = new RectangleF((float)minX, (float)minY, (float)width, (float)height);

        var stopwatch = Stopwatch.StartNew();
        MapRenderResult result = MapRenderer.Render(currentMap, rect, pixels, pixels);
        stopwatch.Stop();
        LastRenderMs = stopwatch.Elapsed.TotalMilliseconds;

        using SKBitmap bitmap = result.Bitmap;

        // ImageData wants straight RGBA. Skia's native layout varies by platform, so
        // convert rather than assume.
        if (bitmap.ColorType == SKColorType.Rgba8888)
            return bitmap.Bytes;

        using var rgba = new SKBitmap(new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        return bitmap.CopyTo(rgba, SKColorType.Rgba8888) ? rgba.Bytes : bitmap.Bytes;
    }

    /// <summary>Milliseconds the last <see cref="RenderTile"/> call spent drawing.</summary>
    [JSExport]
    internal static double GetLastRenderMs() => LastRenderMs;

    private static double LastRenderMs;

    /// <summary>
    /// Whether the loaded map is georeferenced, and what projection it resolved to.
    /// Doubles as evidence that the embedded EPSG table survives trimming.
    /// </summary>
    [JSExport]
    internal static string Georeference()
    {
        if (currentMap is null)
            return "no map loaded";

        using (currentMap.Read()) {
            RealWorldCoords coords = currentMap.RealWorldCoords;
            if (!coords.RealWorldOn)
                return "not georeferenced";
            return string.IsNullOrWhiteSpace(coords.Proj4String)
                ? $"grid {coords.RealWorldGridAndZone}, no proj4"
                : coords.Proj4String;
        }
    }

    private static string F(float value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}
