namespace OcadViewer.Diff;

/// <summary>Compares two versions of an OCAD map.</summary>
/// <remarks>
/// The seam the app depends on. Objects are what it compares today; symbols,
/// colours, georeferencing and templates are to follow behind the same interface.
/// </remarks>
public interface IOcadDiffService
{
    /// <summary>What changed from <paramref name="older"/> to <paramref name="newer"/>.</summary>
    /// <param name="older">
    /// The earlier version, or <c>null</c> when there is none - the first release of
    /// a map - in which case every object is added.
    /// </param>
    ObjectDiff Compare(OcadMapSnapshot older, OcadMapSnapshot newer);
}
