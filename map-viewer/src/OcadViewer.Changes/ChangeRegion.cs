namespace OcadViewer.Changes;

/// <summary>
/// One place on the sheet where something changed.
/// </summary>
/// <param name="Rank">1 for the largest region, counting down by pixel count.</param>
/// <param name="Area">Somewhere to go and look, in paper millimetres.</param>
/// <param name="Pixels">How many mask pixels changed inside it.</param>
/// <remarks>
/// A region is deliberately not "a changed object". Nothing here has read the
/// map's symbols; this is a cluster of changed pixels, and two edits a
/// millimetre apart are one region however many objects they touched. That is
/// the right answer for what it is for - a short list of places to visit - and
/// it is the only answer available without an object-level diff.
/// </remarks>
public sealed record ChangeRegion(int Rank, PaperRectangle Area, int Pixels);

/// <summary>
/// What the grouper found.
/// </summary>
/// <param name="Regions">
/// Largest first, and only the ones worth a trip: see
/// <see cref="ChangeGroupingOptions.MinimumShare"/>.
/// </param>
/// <param name="SmallerRegions">
/// How many places were left out of the list for being too small a share of
/// the change. Reported rather than silently dropped: a list that quietly
/// stopped short would be worse than a long one.
/// </param>
/// <param name="ChangedPixels">Every non-zero pixel in the mask, listed or not.</param>
/// <param name="TotalPixels">The whole mask, for a share of the sheet.</param>
public sealed record ChangeGrouping(
    IReadOnlyList<ChangeRegion> Regions, int SmallerRegions,
    int ChangedPixels, int TotalPixels)
{
    /// <summary>
    /// Everything worth looking at, as one rectangle, or <c>null</c> when there
    /// is nothing.
    /// </summary>
    /// <remarks>
    /// The regions, not every changed pixel. One stray speck in a far corner
    /// used to pull "zoom to the changes" back out to the whole sheet, which is
    /// the one thing that view is for avoiding.
    /// </remarks>
    public PaperRectangle Bounds => PaperRectangle.Covering(Regions.Select(region => region.Area));
}
