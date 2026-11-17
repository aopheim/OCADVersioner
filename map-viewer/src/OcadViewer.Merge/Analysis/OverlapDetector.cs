using OcadViewer.Merge.Geometry;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Analysis;

/// <summary>
/// Clean changes from both branches that lie close together in the terrain:
/// both mappers worked here, so the user should look before trusting either.
/// </summary>
public sealed class OverlapGroup
{
    internal OverlapGroup(string key, IReadOnlyList<ObjectItem> items, Bounds bounds)
    {
        Key = key;
        Items = items;
        Bounds = bounds;
    }

    /// <summary>Derived from the member keys, so it is stable across analyses.</summary>
    public string Key { get; }

    public IReadOnlyList<ObjectItem> Items { get; }

    /// <summary>
    /// The extent of what the members changed - not of the whole objects, so a group
    /// on a long contour covers only the stretch where both mappers worked.
    /// </summary>
    public Bounds Bounds { get; }

    public IEnumerable<ObjectItem> ItemsFrom(Side side)
        => Items.Where(i => i.Outcome == (side == Side.A ? Outcome.OnlyA : Outcome.OnlyB));
}

/// <summary>Groups clean A changes with clean B changes that come within a distance of each other.</summary>
/// <remarks>
/// <para>Distances are measured between what each branch changed (see
/// <see cref="ChangedStretch"/>): a knoll dropped next to a newly drawn contour is
/// the case to catch, and a contour edited at one end is not near a knoll by its
/// other end. A change inside an area the other branch added also counts.</para>
/// <para>Changes are joined transitively, so a chain of neighbours is one group.</para>
/// </remarks>
public static class OverlapDetector
{
    public static IReadOnlyList<OverlapGroup> Find(IReadOnlyList<ObjectItem> items, int distanceUnits)
    {
        Dictionary<ObjectItem, ChangedStretch> stretch = new(ReferenceEqualityComparer.Instance);
        foreach (ObjectItem item in items) {
            if (item.Outcome == Outcome.OnlyA)
                stretch[item] = ChangedStretch.Between(item.Base, item.A);
            else if (item.Outcome == Outcome.OnlyB)
                stretch[item] = ChangedStretch.Between(item.Base, item.B);
        }

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
            foreach (ObjectItem b in fromB) {
                if (!stretch[a].IsNear(stretch[b], distanceUnits))
                    continue;
                parent.TryAdd(a, a);
                parent.TryAdd(b, b);
                parent[Root(a)] = Root(b);
            }
        }

        return [.. items.Where(parent.ContainsKey)
                        .GroupBy(Root, ReferenceEqualityComparer.Instance)
                        .Select(g => g.Cast<ObjectItem>().ToArray())
                        .Select(members => new OverlapGroup(
                            "overlap:" + string.Join('+', members.Select(m => m.Key)), members,
                            members.Aggregate(Bounds.Empty, (u, m) => u.Union(stretch[m].Bounds))))];
    }
}
