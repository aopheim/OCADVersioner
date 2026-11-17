using OcadViewer.Ocd;

namespace OcadViewer.Merge.Analysis;

/// <summary>
/// Clean changes from both branches that lie close together in the terrain:
/// both mappers worked here, so the user should look before trusting either.
/// </summary>
public sealed class OverlapGroup(string key, IReadOnlyList<ObjectItem> items)
{
    /// <summary>Derived from the member keys, so it is stable across analyses.</summary>
    public string Key { get; } = key;

    public IReadOnlyList<ObjectItem> Items { get; } = items;

    public Bounds Bounds => Items.Aggregate(Bounds.Empty, (u, i) => u.Union(i.Bounds));

    public IEnumerable<ObjectItem> ItemsFrom(Side side)
        => Items.Where(i => i.Outcome == (side == Side.A ? Outcome.OnlyA : Outcome.OnlyB));
}

/// <summary>Groups clean A changes with clean B changes whose extents come within a buffer.</summary>
/// <remarks>
/// Extents, not exact shapes: a knoll dropped next to a newly drawn contour is the
/// case to catch, and shapes that merely come close never intersect. Changes are
/// joined transitively, so a chain of neighbours is one group.
/// </remarks>
public static class OverlapDetector
{
    public static IReadOnlyList<OverlapGroup> Find(IReadOnlyList<ObjectItem> items, int bufferUnits)
    {
        ObjectItem[] fromA = [.. items.Where(i => i.Outcome == Outcome.OnlyA)];
        ObjectItem[] fromB = [.. items.Where(i => i.Outcome == Outcome.OnlyB)];

        Dictionary<ObjectItem, ObjectItem> parent = new(ReferenceEqualityComparer.Instance);
        ObjectItem Root(ObjectItem item)
        {
            while (parent.TryGetValue(item, out ObjectItem up) && !ReferenceEquals(up, item))
                item = up;
            return item;
        }

        foreach (ObjectItem a in fromA) {
            Bounds near = a.Bounds.Inflate(bufferUnits);
            foreach (ObjectItem b in fromB) {
                if (!near.Intersects(b.Bounds))
                    continue;
                parent.TryAdd(a, a);
                parent.TryAdd(b, b);
                parent[Root(a)] = Root(b);
            }
        }

        return [.. items.Where(parent.ContainsKey)
                        .GroupBy(Root, ReferenceEqualityComparer.Instance)
                        .Select(g => g.Cast<ObjectItem>().ToArray())
                        .Select(members => new OverlapGroup("overlap:" + string.Join('+', members.Select(m => m.Key)), members))];
    }
}
