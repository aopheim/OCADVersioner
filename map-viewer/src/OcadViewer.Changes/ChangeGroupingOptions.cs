namespace OcadViewer.Changes;

/// <summary>
/// How close counts as the same place, and how small is too small to list.
/// </summary>
/// <remarks>
/// Millimetres on the sheet, not pixels, so the answers do not move when the
/// mask is computed at a different resolution - which it is, since a large map
/// is planned down to fit a maximum edge.
/// </remarks>
public sealed record ChangeGroupingOptions
{
    public static ChangeGroupingOptions Default { get; } = new();

    /// <summary>
    /// How finely the mask is divided before clustering.
    /// </summary>
    /// <remarks>
    /// Clustering runs on cells, not pixels. A whole-map comparison can change
    /// several hundred thousand pixels, and nothing useful is learnt by treating
    /// each one as its own point: a millimetre is already finer than anything a
    /// mapper would call two places.
    /// </remarks>
    public double CellMillimetres { get; init; } = 1.0;

    /// <summary>
    /// How far apart two changes can be and still be one region.
    /// </summary>
    /// <remarks>
    /// Four millimetres, because one edited symbol is not one blot. A moved
    /// contour leaves ink along its old line and its new one; a changed path
    /// leaves a row of dashes. Grouping strictly by touching pixels lists those
    /// as a dozen regions, which is worse than listing none.
    ///
    /// <para>Measured between the millimetre cells the changes fall in, so it
    /// is "how far apart" in the ordinary sense and not a count of clear
    /// pixels, which would mean something different on every map.</para>
    /// </remarks>
    public double GapMillimetres { get; init; } = 2.0;

    /// <summary>
    /// How crowded a cell has to be to pull its neighbours into a region.
    /// </summary>
    /// <remarks>
    /// This is the part of density clustering that plain "join up whatever
    /// touches" does not have, and it is what makes the list readable. A cell
    /// with fewer than this many occupied cells within the gap can join a
    /// region but cannot extend one, so a thread of stray pixels - antialiasing
    /// along a line that barely moved - cannot chain two places together.
    /// Without it, comparing a storigarden release with one three releases
    /// older came back as a single region covering 65 by 34 millimetres; with
    /// it, as the three squares the mapper had actually worked in.
    ///
    /// <para>Cells left over are not discarded. Each becomes a region of its
    /// own and is judged on its share like any other.</para>
    /// </remarks>
    public int MinimumCells { get; init; } = 4;

    /// <summary>
    /// How much of the change a place has to hold to be worth a trip.
    /// </summary>
    /// <remarks>
    /// A share and not a size, because the useful question is relative. On a
    /// map where one clearing was redrawn, the dozens of pixels that shifted
    /// along nearby lines are not places to visit; on a map where three small
    /// symbols moved and nothing else, each of the three is a third of
    /// everything that happened and all three belong in the list. One hundredth
    /// separates those two cases, and comparing a storigarden revision with one
    /// three releases older is where it was measured: without it the list was
    /// one real entry and fifty-two specks of a square millimetre each.
    ///
    /// <para>Nothing is hidden by it. The specks stay in the overlay, their
    /// pixels stay in the percentage, and <see cref="ChangeGrouping"/> says how
    /// many were left out.</para>
    /// </remarks>
    public double MinimumShare { get; init; } = 0.01;
}
