using System;

namespace OcadViewer.Core
{
    /// <summary>
    /// A 2D affine transform in the order a canvas's <c>setTransform</c> takes it:
    /// <c>x' = A·x + C·y + E</c> and <c>y' = B·x + D·y + F</c>.
    /// </summary>
    /// <remarks>
    /// Also the order of Purple Pen's <c>Matrix(m11, m12, m21, m22, dx, dy)</c>,
    /// so one value serves both the page's canvases and the renderer.
    /// </remarks>
    public readonly record struct Affine(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Affine Identity = new(1, 0, 0, 1, 0, 0);

        public (double X, double Y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);

        /// <summary>How much the transform scales areas; negative when it mirrors.</summary>
        public double Determinant => A * D - B * C;

        /// <summary>How far one unit goes, on average: the square root of the area scale.</summary>
        public double Scale => Math.Sqrt(Math.Abs(Determinant));

        /// <summary>Whether every number is finite and nothing is squashed flat, so it can be inverted.</summary>
        public bool IsUsable
            => double.IsFinite(A) && double.IsFinite(B) && double.IsFinite(C) && double.IsFinite(D)
               && double.IsFinite(E) && double.IsFinite(F) && double.IsFinite(Determinant) && Determinant != 0;

        /// <summary>The transform that undoes this one.</summary>
        public Affine Invert()
        {
            double det = Determinant;
            double a = D / det, b = -B / det, c = -C / det, d = A / det;
            return new Affine(a, b, c, d, -(a * E + c * F), -(b * E + d * F));
        }

        /// <summary>
        /// The transform of a square <paramref name="side"/> units across whose
        /// top-left, top-right and bottom-left corners land on <paramref name="origin"/>,
        /// <paramref name="right"/> and <paramref name="down"/>.
        /// </summary>
        /// <remarks>
        /// How every local fit here is made: work out exactly where three corners
        /// of a canvas go, and let the straight lines between them stand for the
        /// rest. Over one screen tile a projection bends far less than a pixel.
        /// </remarks>
        public static Affine FromCorners(double side, (double X, double Y) origin,
                                         (double X, double Y) right, (double X, double Y) down)
            => new((right.X - origin.X) / side, (right.Y - origin.Y) / side,
                   (down.X - origin.X) / side, (down.Y - origin.Y) / side,
                   origin.X, origin.Y);

        /// <summary>This transform followed by <paramref name="next"/>.</summary>
        public Affine Then(Affine next)
            => new(next.A * A + next.C * B, next.B * A + next.D * B,
                   next.A * C + next.C * D, next.B * C + next.D * D,
                   next.A * E + next.C * F + next.E, next.B * E + next.D * F + next.F);

        /// <summary>The six numbers, for the page.</summary>
        public double[] ToArray() => [A, B, C, D, E, F];
    }
}
