using System;
using System.Drawing;
using System.Linq;

namespace OcadViewer.Core
{
    /// <summary>
    /// The geometry of a screen canvas on the ground: which paper it shows, through
    /// what transform, and where a change mask drawn on the paper lands on it.
    /// </summary>
    /// <remarks>
    /// A canvas is a square of <c>pixels</c> on a side whose top-left corner is at
    /// <c>(minX, maxY)</c> in Web Mercator metres, each pixel
    /// <c>metresPerPixel</c> across - exactly what the page's tile layers hand
    /// over. Canvas rows run down, ground and paper Y run up; the transforms here
    /// take care of that, so nothing on the page has to.
    /// </remarks>
    public static class GroundView
    {
        /// <summary>
        /// Canvas pixels to paper millimetres, fitted at three corners of the canvas
        /// through the exact placement.
        /// </summary>
        public static Affine CanvasToPaper(GroundPlacement placement, double minX, double maxY,
                                           double metresPerPixel, int pixels)
        {
            double side = pixels * metresPerPixel;
            return Affine.FromCorners(pixels,
                                      placement.ToPaper(minX, maxY),
                                      placement.ToPaper(minX + side, maxY),
                                      placement.ToPaper(minX, maxY - side));
        }

        /// <summary>The paper a canvas shows: the rectangle round its four corners.</summary>
        public static RectangleF PaperUnder(Affine canvasToPaper, int pixels)
        {
            (double X, double Y)[] corners = [
                canvasToPaper.Apply(0, 0), canvasToPaper.Apply(pixels, 0),
                canvasToPaper.Apply(0, pixels), canvasToPaper.Apply(pixels, pixels)
            ];
            return Bounds(corners);
        }

        /// <summary>
        /// Where a rectangle of paper lies on the ground, in Web Mercator metres: the
        /// rectangle round its corners and the middles of its sides. A rotated map
        /// covers its rectangle's corners; the middles catch most of a projection's bend.
        /// </summary>
        public static (double MinX, double MinY, double MaxX, double MaxY) GroundExtent(GroundPlacement placement,
                                                                                         RectangleF paper)
        {
            double x0 = paper.Left, x1 = paper.Right, y0 = paper.Top, y1 = paper.Bottom;
            double xm = (x0 + x1) / 2, ym = (y0 + y1) / 2;
            (double X, double Y)[] points = new[] {
                (x0, y0), (xm, y0), (x1, y0), (x1, ym), (x1, y1), (xm, y1), (x0, y1), (x0, ym)
            }.Select(p => placement.ToGround(p.Item1, p.Item2)).ToArray();

            return (points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }

        /// <summary>
        /// Change-mask pixels to canvas pixels: the transform that puts a mask,
        /// drawn over <paramref name="maskArea"/> of the paper at
        /// <paramref name="maskWidth"/> by <paramref name="maskHeight"/>, where it
        /// belongs on a canvas.
        /// </summary>
        /// <remarks>Mask rows run down from the top of the paper rectangle, like any image.</remarks>
        public static Affine MaskToCanvas(GroundPlacement placement, RectangleF maskArea, int maskWidth, int maskHeight,
                                          double minX, double maxY, double metresPerPixel, int pixels)
        {
            double sx = maskWidth / (double)maskArea.Width;
            double sy = maskHeight / (double)maskArea.Height;
            var paperToMask = new Affine(sx, 0, 0, -sy, -maskArea.Left * sx, maskArea.Bottom * sy);

            return CanvasToPaper(placement, minX, maxY, metresPerPixel, pixels).Then(paperToMask).Invert();
        }

        private static RectangleF Bounds((double X, double Y)[] points)
        {
            double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
            return RectangleF.FromLTRB((float)minX, (float)minY, (float)maxX, (float)maxY);
        }
    }
}
