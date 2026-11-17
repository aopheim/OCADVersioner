using OcadViewer.Ocd;

namespace OcadViewer.Merge.Matching;

/// <summary>Which object in a branch is which object in base.</summary>
public sealed record ObjectPairing(
    IReadOnlyList<(OcadObject Base, OcadObject Branch)> Pairs,
    IReadOnlyList<OcadObject> OnlyInBase,
    IReadOnlyList<OcadObject> OnlyInBranch);

/// <summary>
/// Pairs objects across two files: the identity half of the merge.
/// </summary>
/// <remarks>
/// <para>OCAD has no object ID, but every object carries a creation date to the
/// millisecond that survives editing, moving and cut-and-paste. It is not unique,
/// though: copy-and-paste and Duplicate give the copy the original's date. So
/// objects are first grouped by date, and inside a group the closest pairs win -
/// an untouched original pairs with itself, and the copy is left over as new.</para>
/// <para>Objects without a usable date (OCAD 11 files written by OCAD 2020 have
/// none) or whose date was changed by hand fall back to identical geometry: same
/// symbol, same type, same coordinates. Nothing is paired on mere nearness, so a
/// doubt becomes a delete plus an add, which is safe.</para>
/// <para>Entry numbers are never used: a new object takes the next free entry, so
/// the same entry holds unrelated objects in two branches.</para>
/// </remarks>
public static class ObjectMatcher
{
    private const double OtherSymbolPenalty = 1e12;

    public static ObjectPairing Match(IReadOnlyList<OcadObject> baseObjects, IReadOnlyList<OcadObject> branchObjects)
    {
        Dictionary<OcadObject, OcadObject> pairs = new(ReferenceEqualityComparer.Instance);
        HashSet<OcadObject> used = new(ReferenceEqualityComparer.Instance);

        PairByCreationDate(baseObjects, branchObjects, pairs, used);
        PairByGeometry(baseObjects.Where(o => !pairs.ContainsKey(o)), branchObjects.Where(o => !used.Contains(o)), pairs, used);

        return new ObjectPairing(
            [.. baseObjects.Where(pairs.ContainsKey).Select(o => (o, pairs[o]))],
            [.. baseObjects.Where(o => !pairs.ContainsKey(o))],
            [.. branchObjects.Where(o => !used.Contains(o))]);
    }

    private static void PairByCreationDate(IReadOnlyList<OcadObject> baseObjects, IReadOnlyList<OcadObject> branchObjects,
                                           Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        ILookup<DateTime, OcadObject> branchByDate = branchObjects.Where(HasDate).ToLookup(DateKey);

        foreach (IGrouping<DateTime, OcadObject> group in baseObjects.Where(HasDate).GroupBy(DateKey)) {
            OcadObject[] candidates = [.. branchByDate[group.Key]];
            if (candidates.Length == 0)
                continue;

            var byCost = from b in @group
                         from r in candidates
                         orderby Cost(b, r)
                         select (b, r);

            foreach ((OcadObject b, OcadObject r) in byCost) {
                if (!pairs.ContainsKey(b) && !used.Contains(r)) {
                    pairs[b] = r;
                    used.Add(r);
                }
            }
        }
    }

    private static void PairByGeometry(IEnumerable<OcadObject> baseObjects, IEnumerable<OcadObject> branchObjects,
                                       Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        Dictionary<string, Queue<OcadObject>> branchBySignature = [];
        foreach (OcadObject r in branchObjects) {
            string signature = Signature(r);
            if (!branchBySignature.TryGetValue(signature, out Queue<OcadObject> queue))
                branchBySignature[signature] = queue = new Queue<OcadObject>();
            queue.Enqueue(r);
        }

        foreach (OcadObject b in baseObjects) {
            if (branchBySignature.TryGetValue(Signature(b), out Queue<OcadObject> queue) && queue.Count > 0) {
                OcadObject r = queue.Dequeue();
                pairs[b] = r;
                used.Add(r);
            }
        }
    }

    private static bool HasDate(OcadObject o) => o.CreatedAt is not null;

    private static DateTime DateKey(OcadObject o) => o.CreatedAt.Value;

    private static double Cost(OcadObject a, OcadObject b)
    {
        if (a.Symbol == b.Symbol && a.Coords.SequenceEqual(b.Coords))
            return 0;

        (double ax, double ay) = Centre(a.Coords);
        (double bx, double by) = Centre(b.Coords);
        double distance = Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));
        return (a.Symbol == b.Symbol ? 0 : OtherSymbolPenalty) + 1 + distance;
    }

    private static (double, double) Centre(IReadOnlyList<OcadCoord> coords)
    {
        Bounds bounds = Bounds.Of(coords);
        return bounds.IsEmpty ? (0, 0) : ((bounds.MinX + bounds.MaxX) / 2.0, (bounds.MinY + bounds.MaxY) / 2.0);
    }

    private static string Signature(OcadObject o)
        => $"{o.Symbol}|{(int)o.Type}|{string.Join(';', o.Coords.Select(c => $"{c.X},{c.Y},{c.XFlags},{c.YFlags}"))}";
}
