using System;
using System.Diagnostics;
using System.Drawing;

using PurplePen.Graphics2D;
using PurplePen.MapModel;

using Map_SkiaStd;
using SkiaSharp;

namespace OcadViewer.Core
{
    /// <summary>Knobs that change what the renderer draws, as opposed to where.</summary>
    public sealed class MapRenderSettings
    {
        /// <summary>Draw OCAD and bitmap templates underneath the map.</summary>
        public RenderTemplateOption Templates { get; init; } = RenderTemplateOption.MapAndTemplates;

        /// <summary>
        /// Blend colours flagged as overprinting, using Darken.
        /// </summary>
        /// <remarks>
        /// Matches what OCAD and Purple Pen show. This is the setting most likely
        /// to render differently between desktop Skia and the WebAssembly build,
        /// because it pushes an offscreen layer, so it is worth diffing explicitly.
        /// </remarks>
        public bool BlendOverprintedColors { get; init; } = true;

        /// <summary>
        /// Render area patterns by stamping glyphs (<c>false</c>) rather than by
        /// building pattern brushes (<c>true</c>).
        /// </summary>
        /// <remarks>
        /// Stamping avoids IGraphicsTarget.CreatePatternBrush and its offscreen
        /// surfaces entirely, which is the safer default on WebAssembly. The bitmap
        /// path is usually faster on dense areas, so this is worth measuring rather
        /// than assuming.
        /// </remarks>
        public bool UsePatternBitmaps { get; init; }

        public bool AntiAlias { get; init; } = true;

        /// <summary>Colour drawn behind the map. Defaults to opaque white.</summary>
        public CmykColor Background { get; init; } = CmykColor.FromCmyk(0, 0, 0, 0);
    }

    /// <summary>A rendered map image and how long it took to produce.</summary>
    public sealed record MapRenderResult(SKBitmap Bitmap, TimeSpan RenderTime);

    /// <summary>
    /// Draws a <see cref="Map"/> onto a Skia bitmap.
    /// </summary>
    /// <remarks>
    /// The whole renderer is vendored; this is only the adapter that builds a
    /// <see cref="RenderOptions"/>, creates a Skia graphics target and calls
    /// <c>Map.Draw</c>. Purple Pen's own Avalonia front end does the same job in
    /// about fifty lines (<c>AvPurplePen/CacheableMapDisplay.cs</c>).
    /// </remarks>
    public static class MapRenderer
    {
        /// <summary>
        /// Renders <paramref name="mapArea"/> of <paramref name="map"/> into a
        /// bitmap <paramref name="pixelWidth"/> by <paramref name="pixelHeight"/>.
        /// </summary>
        /// <param name="throwOnCancel">
        /// Invoked after each colour layer. Throw from it to abort a render that is
        /// no longer wanted - for instance because the user panned again.
        /// </param>
        public static MapRenderResult Render(
            Map map,
            RectangleF mapArea,
            int pixelWidth,
            int pixelHeight,
            MapRenderSettings settings = null,
            Action throwOnCancel = null)
        {
            ArgumentNullException.ThrowIfNull(map);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixelWidth, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(pixelHeight, 1);

            settings ??= new MapRenderSettings();

            RenderOptions renderOptions = new() {
                minResolution = MinResolutionFor(mapArea, pixelWidth, pixelHeight),
                usePatternBitmaps = settings.UsePatternBitmaps,
                renderTemplates = settings.Templates,
                blendOverprintedColors = settings.BlendOverprintedColors,
            };

            var stopwatch = Stopwatch.StartNew();

            // 'inverted: true' flips the Y axis: OCAD paper coordinates run upwards,
            // screen coordinates run downwards.
            var target = new Skia_BitmapGraphicsTarget(
                pixelWidth, pixelHeight,
                alpha: false,
                initialColor: settings.Background,
                rectangle: mapArea,
                inverted: true);

            SKBitmap bitmap;
            using (target) {
                target.PushAntiAliasing(settings.AntiAlias);

                using (map.Read())
                    map.Draw(target, mapArea, renderOptions, throwOnCancel);

                bitmap = ((Skia_Bitmap)target.FinishBitmap()).Bitmap;
            }

            stopwatch.Stop();
            return new MapRenderResult(bitmap, stopwatch.Elapsed);
        }

        /// <summary>
        /// Paper millimetres per destination pixel, which is what tells the renderer
        /// how much detail to cull.
        /// </summary>
        /// <remarks>
        /// On a high-DPI display the caller must pass physical pixels, not CSS
        /// pixels; using CSS pixels makes this value twice too coarse and drops
        /// features that should be drawn.
        /// </remarks>
        public static float MinResolutionFor(RectangleF mapArea, int pixelWidth, int pixelHeight)
            => Math.Min(mapArea.Width / pixelWidth, mapArea.Height / pixelHeight);

        /// <summary>
        /// Pixel height that preserves the aspect ratio of <paramref name="mapArea"/>
        /// at a given pixel width.
        /// </summary>
        public static int HeightForWidth(RectangleF mapArea, int pixelWidth)
            => Math.Max(1, (int)Math.Ceiling(pixelWidth * mapArea.Height / mapArea.Width));
    }
}
