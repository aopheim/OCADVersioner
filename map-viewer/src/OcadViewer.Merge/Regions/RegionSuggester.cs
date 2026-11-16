using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Format;
using OcadViewer.Merge.Geometry;

namespace OcadViewer.Merge.Regions;

/// <summary>A region the merge proposes, and which side it proposes taking there.</summary>
/// <param name="Suggested">The side whose changes it holds, or null when it holds both
/// sides' - then the user should decide object by object.</param>
public sealed record SuggestedRegion(Region Region, Side? Suggested, IReadOnlyList<ObjectItem> Items);

/// <summary>
/// Proposes regions from where the changes are: mappers usually split the terrain,
/// so each one's changes cluster, and a region around each cluster lets the user
/// take a whole area with one click.
/// </summary>
public static class RegionSuggester
{
    /// <param name="joinMeters">Changes whose extents come this close join one cluster.</param>
    /// <param name="marginMeters">How far a region reaches beyond its cluster.</param>
    public static IReadOnlyList<SuggestedRegion> Suggest(MergeAnalysis analysis, double joinMeters = 50, double marginMeters = 10)
    {
        double scale = analysis.Base.Scale;
        int join = MergeOptions.ToUnits(joinMeters, scale);
        int margin = MergeOptions.ToUnits(marginMeters, scale);

        List<ObjectItem> changes = [.. analysis.Objects.Where(i => i.Outcome is Outcome.OnlyA or Outcome.OnlyB or Outcome.Conflict)];
        List<List<ObjectItem>> clusters = Cluster(changes, join);

        return [.. clusters
            .Where(c => c.Any(i => i.Outcome is Outcome.OnlyA or Outcome.OnlyB))
            .Select(c => new SuggestedRegion(Rectangle(c, margin), SideOf(c), c))];
    }

    /// <summary>Single-link clustering on extents: a chain of neighbours is one cluster.</summary>
    private static List<List<ObjectItem>> Cluster(List<ObjectItem> items, int join)
    {
        List<List<ObjectItem>> clusters = [];
        foreach (ObjectItem item in items) {
            Bounds near = item.Bounds.Inflate(join);
            List<List<ObjectItem>> touching = [.. clusters.Where(c => c.Any(other => near.Intersects(other.Bounds)))];
            List<ObjectItem> merged = [item, .. touching.SelectMany(c => c)];
            clusters.RemoveAll(touching.Contains);
            clusters.Add(merged);
        }
        return clusters;
    }

    private static Side? SideOf(List<ObjectItem> cluster)
    {
        bool a = cluster.Any(i => i.Outcome == Outcome.OnlyA);
        bool b = cluster.Any(i => i.Outcome == Outcome.OnlyB);
        return a && b ? null : a ? Side.A : Side.B;
    }

    private static Region Rectangle(List<ObjectItem> cluster, int margin)
    {
        Bounds b = cluster.Aggregate(Bounds.Empty, (u, i) => u.Union(i.Bounds)).Inflate(margin);
        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(b);
        return new Region([(minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)]);
    }
}
