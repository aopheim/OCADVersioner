namespace OcadViewer.Changes;

/// <summary>
/// Turns a change mask into a list of places that changed.
/// </summary>
/// <remarks>
/// The seam is here because the grouping is a choice, not a fact. Density
/// clustering is one answer; connected-component labelling, a quadtree, or an
/// object-level diff once the viewer can do one would be others, and each would
/// give a different and defensible list for the same mask. Callers are written
/// against the question, so swapping the answer is one registration.
/// </remarks>
public interface IChangeGrouper
{
    /// <summary>Groups the mask's changed pixels.</summary>
    /// <param name="mask">Where the two versions differ.</param>
    /// <param name="options">Null for <see cref="ChangeGroupingOptions.Default"/>.</param>
    ChangeGrouping Group(ChangeMask mask, ChangeGroupingOptions options = null);
}
