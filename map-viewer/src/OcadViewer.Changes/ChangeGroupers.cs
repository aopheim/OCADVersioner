namespace OcadViewer.Changes;

/// <summary>
/// Where the grouping is chosen.
/// </summary>
/// <remarks>
/// The one place in the solution, outside the adapter itself, that names a
/// clustering library - which is what makes <see cref="IChangeGrouper"/> worth
/// having. Callers ask for <see cref="Default"/> or are handed a grouper;
/// replacing the algorithm is this file and nothing else, and
/// OcadViewer.ArchitectureTests fails if the name leaks past it.
/// </remarks>
public static class ChangeGroupers
{
    /// <summary>
    /// Density clustering over the mask, which is the only grouping there is
    /// so far.
    /// </summary>
    /// <remarks>
    /// Shared, and safe to share: grouping keeps no state between calls.
    /// </remarks>
    public static IChangeGrouper Default { get; } = new DbscanChangeGrouper();
}
