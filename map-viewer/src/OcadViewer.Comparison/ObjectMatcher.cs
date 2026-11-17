using OcadViewer.Ocd;

namespace OcadViewer.Comparison;

/// <summary>Which object in one version is which object in another.</summary>
/// <param name="DateShift">
/// How far every creation date moved between the two, a whole number of hours, or
/// zero. Non-zero when a version was rebuilt by copying its objects into another
/// file; see <see cref="ObjectMatcher"/>.
/// </param>
public sealed record ObjectPairing(
    IReadOnlyList<(OcadObject Base, OcadObject Branch)> Pairs,
    IReadOnlyList<OcadObject> OnlyInBase,
    IReadOnlyList<OcadObject> OnlyInBranch,
    TimeSpan DateShift);

/// <summary>
/// Pairs objects across two files: the identity half of every comparison.
/// </summary>
/// <remarks>
/// <para>OCAD has no object ID, but every object carries a creation date to the
/// millisecond that survives editing, moving and cut-and-paste. It is not unique,
/// though: copy-and-paste and Duplicate give the copy the original's date, and an
/// import stamps thousands of objects with one. So objects are first grouped by
/// date, and inside a group the closest pairs win - an untouched original pairs
/// with itself, and the copy is left over as new.</para>
/// <para>A version rebuilt by copying its objects into another file can have every
/// date shifted by the hours between local time and UTC. When most objects that
/// are identical in both files show the same whole-hour shift, dates are compared
/// with that shift allowed for.</para>
/// <para>Objects without a usable date (OCAD 11 files written by OCAD 2020 have
/// none) or whose date was changed by hand fall back to identical geometry: same
/// symbol, same type, same coordinates. Nothing is paired on mere nearness, so a
/// doubt becomes a delete plus an add, which is safe.</para>
/// <para>Entry numbers are never used: a new object takes the next free entry, so
/// the same entry holds unrelated objects in two branches, and a map rebuilt from
/// another file renumbers every object.</para>
/// </remarks>
public static class ObjectMatcher
{
    private const double OtherSymbolPenalty = 1e12;

    /// <summary>
    /// A date group with at most this many possible pairings is paired by trying
    /// every one. Larger groups - imports - go through a spatial grid instead, so
    /// the work does not grow with the square of the group.
    /// </summary>
    internal const int ExhaustiveGroupLimit = 4096;

    /// <summary>
    /// The most pairings one ring of the grid search may consider. A group whose
    /// objects are all far from their partners stops here, and what is left falls
    /// back to geometry, then to delete plus add.
    /// </summary>
    internal const int GridPairingBudget = 2_000_000;

    /// <summary>Shifts larger than this are not a time zone, so they are not allowed for.</summary>
    private static readonly TimeSpan LargestShift = TimeSpan.FromHours(26);

    public static ObjectPairing Match(IReadOnlyList<OcadObject> baseObjects, IReadOnlyList<OcadObject> branchObjects)
    {
        Dictionary<OcadObject, OcadObject> pairs = new(ReferenceEqualityComparer.Instance);
        HashSet<OcadObject> used = new(ReferenceEqualityComparer.Instance);

        TimeSpan shift = DateShift(baseObjects, branchObjects);
        PairByCreationDate(baseObjects, branchObjects, shift, pairs, used);
        PairByGeometry(baseObjects.Where(o => !pairs.ContainsKey(o)), branchObjects.Where(o => !used.Contains(o)), pairs, used);

        return new ObjectPairing(
            [.. baseObjects.Where(pairs.ContainsKey).Select(o => (o, pairs[o]))],
            [.. baseObjects.Where(o => !pairs.ContainsKey(o))],
            [.. branchObjects.Where(o => !used.Contains(o))],
            shift);
    }

    /// <summary>
    /// The whole number of hours every date moved by, judged from objects that are
    /// identical in both files: the shift most of them share, or zero.
    /// </summary>
    internal static TimeSpan DateShift(IReadOnlyList<OcadObject> baseObjects, IReadOnlyList<OcadObject> branchObjects)
    {
        Dictionary<string, Queue<OcadObject>> branchBySignature = BySignature(branchObjects.Where(HasDate));

        Dictionary<long, int> votes = [];
        int identical = 0;
        foreach (OcadObject b in baseObjects.Where(HasDate)) {
            if (!branchBySignature.TryGetValue(Signature(b), out Queue<OcadObject> twins) || twins.Count == 0)
                continue;

            long seconds = (long)Math.Round((twins.Dequeue().CreatedAt.Value - b.CreatedAt.Value).TotalSeconds);
            votes[seconds] = votes.GetValueOrDefault(seconds) + 1;
            identical++;
        }

        if (identical == 0)
            return TimeSpan.Zero;

        KeyValuePair<long, int> commonest = votes.MaxBy(v => v.Value);
        TimeSpan shift = TimeSpan.FromSeconds(commonest.Key);
        bool wholeHours = commonest.Key % 3600 == 0 && shift.Duration() <= LargestShift;

        return wholeHours && commonest.Value * 2 > identical ? shift : TimeSpan.Zero;
    }

    private static void PairByCreationDate(IReadOnlyList<OcadObject> baseObjects, IReadOnlyList<OcadObject> branchObjects,
                                           TimeSpan shift, Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        ILookup<DateTime, OcadObject> branchByDate = branchObjects.Where(HasDate).ToLookup(DateKey);

        foreach (IGrouping<DateTime, OcadObject> group in baseObjects.Where(HasDate).GroupBy(o => DateKey(o) + shift)) {
            OcadObject[] candidates = [.. branchByDate[group.Key]];
            if (candidates.Length == 0)
                continue;

            OcadObject[] bases = [.. group];
            if ((long)bases.Length * candidates.Length <= ExhaustiveGroupLimit)
                PairExhaustively(bases, candidates, pairs, used);
            else
                PairThroughGrid(bases, candidates, pairs, used);
        }
    }

    /// <summary>Every possible pairing, cheapest first.</summary>
    private static void PairExhaustively(OcadObject[] bases, OcadObject[] candidates,
                                         Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        var byCost = from b in bases
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

    /// <summary>
    /// The same pairing as <see cref="PairExhaustively"/>, without considering every
    /// pair: identical objects first, then same-symbol pairs and then any pairs in
    /// rings of growing radius.
    /// </summary>
    /// <remarks>
    /// Every pairing inside one ring is closer than any in the next, so taking the
    /// rings in order, cheapest first within each, picks what the exhaustive search
    /// would pick - until a ring exceeds <see cref="GridPairingBudget"/>.
    /// </remarks>
    private static void PairThroughGrid(OcadObject[] bases, OcadObject[] candidates,
                                        Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        Dictionary<string, Queue<OcadObject>> identical = [];
        foreach (OcadObject r in candidates) {
            string key = ShapeKey(r);
            if (!identical.TryGetValue(key, out Queue<OcadObject> queue))
                identical[key] = queue = new Queue<OcadObject>();
            queue.Enqueue(r);
        }
        foreach (OcadObject b in bases) {
            if (identical.TryGetValue(ShapeKey(b), out Queue<OcadObject> queue) && queue.Count > 0) {
                OcadObject r = queue.Dequeue();
                pairs[b] = r;
                used.Add(r);
            }
        }

        PairByRings(bases, candidates, sameSymbolOnly: true, pairs, used);
        PairByRings(bases, candidates, sameSymbolOnly: false, pairs, used);
    }

    private static void PairByRings(OcadObject[] bases, OcadObject[] candidates, bool sameSymbolOnly,
                                    Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        Dictionary<OcadObject, int> order = new(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < bases.Length; i++) order[bases[i]] = i;
        for (int i = 0; i < candidates.Length; i++) order[candidates[i]] = i;

        double extent = Extent([.. bases, .. candidates]);

        for (double radius = 1000; ; radius *= 2) {
            OcadObject[] openBases = [.. bases.Where(b => !pairs.ContainsKey(b))];
            OcadObject[] openCandidates = [.. candidates.Where(r => !used.Contains(r))];
            if (openBases.Length == 0 || openCandidates.Length == 0)
                return;

            Dictionary<(long, long), List<OcadObject>> grid = [];
            foreach (OcadObject r in openCandidates) {
                (double x, double y) = Centre(r.Coords);
                (long, long) cell = ((long)Math.Floor(x / radius), (long)Math.Floor(y / radius));
                if (!grid.TryGetValue(cell, out List<OcadObject> inCell))
                    grid[cell] = inCell = [];
                inCell.Add(r);
            }

            List<(double Cost, OcadObject Base, OcadObject Branch)> ring = [];
            foreach (OcadObject b in openBases) {
                (double x, double y) = Centre(b.Coords);
                long cx = (long)Math.Floor(x / radius), cy = (long)Math.Floor(y / radius);
                for (long dx = -1; dx <= 1; dx++)
                for (long dy = -1; dy <= 1; dy++) {
                    if (!grid.TryGetValue((cx + dx, cy + dy), out List<OcadObject> inCell))
                        continue;
                    foreach (OcadObject r in inCell) {
                        if (sameSymbolOnly && r.Symbol != b.Symbol)
                            continue;
                        double cost = Cost(b, r);
                        if (cost - (b.Symbol == r.Symbol ? 0 : OtherSymbolPenalty) - 1 <= radius)
                            ring.Add((cost, b, r));
                    }
                }
                if (ring.Count > GridPairingBudget)
                    return;
            }

            ring.Sort((x, y) => x.Cost.CompareTo(y.Cost) is var c and not 0 ? c
                              : order[x.Base].CompareTo(order[y.Base]) is var bo and not 0 ? bo
                              : order[x.Branch].CompareTo(order[y.Branch]));

            foreach ((_, OcadObject b, OcadObject r) in ring) {
                if (!pairs.ContainsKey(b) && !used.Contains(r)) {
                    pairs[b] = r;
                    used.Add(r);
                }
            }

            if (radius > extent)
                return;
        }
    }

    private static void PairByGeometry(IEnumerable<OcadObject> baseObjects, IEnumerable<OcadObject> branchObjects,
                                       Dictionary<OcadObject, OcadObject> pairs, HashSet<OcadObject> used)
    {
        Dictionary<string, Queue<OcadObject>> branchBySignature = BySignature(branchObjects);

        foreach (OcadObject b in baseObjects) {
            if (branchBySignature.TryGetValue(Signature(b), out Queue<OcadObject> queue) && queue.Count > 0) {
                OcadObject r = queue.Dequeue();
                pairs[b] = r;
                used.Add(r);
            }
        }
    }

    private static Dictionary<string, Queue<OcadObject>> BySignature(IEnumerable<OcadObject> objects)
    {
        Dictionary<string, Queue<OcadObject>> bySignature = [];
        foreach (OcadObject o in objects) {
            string signature = Signature(o);
            if (!bySignature.TryGetValue(signature, out Queue<OcadObject> queue))
                bySignature[signature] = queue = new Queue<OcadObject>();
            queue.Enqueue(o);
        }
        return bySignature;
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

    /// <summary>The diagonal of the box around every object's centre: no pairing is further apart than this.</summary>
    private static double Extent(IEnumerable<OcadObject> objects)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (OcadObject o in objects) {
            (double x, double y) = Centre(o.Coords);
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return minX > maxX ? 0 : Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
    }

    /// <summary>What <see cref="Cost"/> treats as identical: symbol and coordinates.</summary>
    private static string ShapeKey(OcadObject o)
        => $"{o.Symbol}|{string.Join(';', o.Coords.Select(c => $"{c.X},{c.Y},{c.XFlags},{c.YFlags}"))}";

    private static string Signature(OcadObject o)
        => $"{o.Symbol}|{(int)o.Type}|{string.Join(';', o.Coords.Select(c => $"{c.X},{c.Y},{c.XFlags},{c.YFlags}"))}";
}
