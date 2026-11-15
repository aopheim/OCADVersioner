namespace OcadViewer.Diff;

/// <summary>
/// Decides which object in the older version each object in the newer one is.
/// </summary>
/// <remarks>
/// <para>
/// OCAD keeps an object in its index entry while the object lives in that file,
/// so within one line of editing the entry is the object's identity. A map rebuilt
/// by copying its objects into another file gives every object a new entry, and
/// can shift every date by the hours between local time and UTC. Bymarkabasen did
/// both: of 23 000 objects, 200 kept their entry from one release to the next. So
/// the entry is one piece of evidence, not the identity, and objects are matched
/// in three passes, most certain first:
/// </para>
/// <list type="number">
///   <item><b>Identical, same entry.</b> Same symbol, kind, angle, text and every
///   coordinate. Unchanged.</item>
///   <item><b>Identical, another entry.</b> The same object renumbered. Also
///   unchanged.</item>
///   <item><b>Same creation date.</b> What is left are edits, additions and
///   deletions. An edit keeps the creation date, so objects pair by it, to the
///   millisecond, once any whole-hour shift of the dates is allowed for. The same
///   entry pairs as before, when the entries have held. Otherwise the objects must
///   be within <see cref="NearbyMillimetres"/> on paper, and the newer one must have
///   been modified since - an import stamps a batch of objects with one date, and
///   two of them are not an edit of each other. A shared date is common, because an import stamps thousands of
///   objects with one, so among several candidates the same symbol wins, then the
///   same entry, then the nearest.</item>
/// </list>
/// <para>
/// Whatever is left unpaired was deleted, or added. A new object never pairs with
/// an old one by its date alone being close: creation dates are copied, not
/// invented, so "different date" still means "different object".
/// </para>
/// </remarks>
internal static class ObjectMatcher
{
    /// <summary>
    /// How far apart on paper a renumbered object's old and new boxes may be for
    /// the creation date to pair them. Generous for an edit - the fixture's knoll
    /// was moved 5 mm - and far short of the hundreds of millimetres a map spans,
    /// so a shared import date does not pair objects across the map.
    /// </summary>
    internal const double NearbyMillimetres = 10;

    internal sealed record Result(
        IReadOnlyList<(MapObject Then, MapObject Now)> Pairs,
        IReadOnlyList<MapObject> Deleted,
        IReadOnlyList<MapObject> Added,
        TimeSpan DateShift,
        bool EntriesHeld);

    public static Result Match(IReadOnlyDictionary<int, MapObject> before, IReadOnlyDictionary<int, MapObject> after)
    {
        Dictionary<int, MapObject> gone = new(before);
        Dictionary<int, MapObject> fresh = new(after);
        List<(MapObject Then, MapObject Now)> pairs = [];

        void pair(MapObject then, MapObject now)
        {
            gone.Remove(then.Index);
            fresh.Remove(now.Index);
            pairs.Add((then, now));
        }

        // 1. Identical, in the same entry.
        foreach (MapObject now in after.Values.OrderBy(o => o.Index)) {
            if (before.TryGetValue(now.Index, out MapObject then) && SameContent.Instance.Equals(then, now))
                pair(then, now);
        }
        int kept = pairs.Count;

        // 2. Identical, in another entry. Identical twins (they exist: a point
        //    drawn twice on the same spot) are paired in index order.
        Dictionary<MapObject, Queue<MapObject>> byContent = new(SameContent.Instance);
        foreach (MapObject then in gone.Values.OrderBy(o => o.Index)) {
            if (!byContent.TryGetValue(then, out Queue<MapObject> twins))
                byContent[then] = twins = new Queue<MapObject>();
            twins.Enqueue(then);
        }
        foreach (MapObject now in fresh.Values.OrderBy(o => o.Index).ToList()) {
            if (byContent.TryGetValue(now, out Queue<MapObject> twins) && twins.Count > 0)
                pair(twins.Dequeue(), now);
        }
        int renumbered = pairs.Count - kept;

        TimeSpan shift = DateShift(pairs);
        bool entriesHeld = kept >= renumbered;

        // 3. Same creation date, nearby or in the same entry.
        foreach (Candidate c in Candidates(gone, fresh, shift, entriesHeld)) {
            if (gone.ContainsKey(c.Then.Index) && fresh.ContainsKey(c.Now.Index))
                pair(c.Then, c.Now);
        }

        return new Result(pairs,
                          gone.Values.OrderBy(o => o.Index).ToList(),
                          fresh.Values.OrderBy(o => o.Index).ToList(),
                          shift, entriesHeld);
    }

    private sealed record Candidate(MapObject Then, MapObject Now, bool SameSymbol, bool SameEntry, double Gap, double Apart);

    /// <summary>Every plausible pairing of what is left, best first.</summary>
    private static List<Candidate> Candidates(Dictionary<int, MapObject> gone, Dictionary<int, MapObject> fresh,
                                              TimeSpan shift, bool entriesHeld)
    {
        Dictionary<MapObject, PaperBounds> bounds = new(ReferenceEqualityComparer.Instance);
        PaperBounds boundsOf(MapObject o)
            => bounds.TryGetValue(o, out PaperBounds b) ? b : bounds[o] = Measure.Bounds(o.Points);

        Candidate candidate(MapObject then, MapObject now, bool sameEntry)
        {
            PaperBounds a = boundsOf(then), b = boundsOf(now);
            (double gap, double apart) = a is null || b is null
                ? (double.PositiveInfinity, double.PositiveInfinity)
                : (Gap(a, b), Apart(a, b));

            return sameEntry || gap <= NearbyMillimetres
                ? new Candidate(then, now, then.SymbolNumber == now.SymbolNumber, sameEntry, gap, apart)
                : null;
        }

        List<Candidate> found = [];

        Dictionary<long, List<MapObject>> goneByDate = gone.Values
            .Where(o => o.CreatedUtc is not null)
            .GroupBy(o => Millisecond(o.CreatedUtc.Value + shift))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (MapObject now in fresh.Values) {
            if (now.CreatedUtc is null)
                continue;

            // A shifted copy of a date can land a millisecond either side.
            long created = Millisecond(now.CreatedUtc.Value);
            for (long ms = created - 1; ms <= created + 1; ms++) {
                if (!goneByDate.TryGetValue(ms, out List<MapObject> sameDate))
                    continue;

                foreach (MapObject then in sameDate) {
                    bool sameEntry = entriesHeld && then.Index == now.Index;
                    if ((sameEntry || WasTouched(then, now, shift)) && candidate(then, now, sameEntry) is { } c)
                        found.Add(c);
                }
            }
        }

        // Without a date on one side (OCAD 9 has none), the entry is all there is.
        if (entriesHeld) {
            foreach (MapObject now in fresh.Values) {
                if (gone.TryGetValue(now.Index, out MapObject then) && (then.CreatedUtc is null || now.CreatedUtc is null))
                    found.Add(candidate(then, now, sameEntry: true));
            }
        }

        found.Sort((x, y) =>
            y.SameSymbol.CompareTo(x.SameSymbol) is var bySymbol and not 0 ? bySymbol :
            y.SameEntry.CompareTo(x.SameEntry) is var byEntry and not 0 ? byEntry :
            x.Gap.CompareTo(y.Gap) is var byGap and not 0 ? byGap :
            x.Apart.CompareTo(y.Apart) is var byApart and not 0 ? byApart :
            x.Now.Index.CompareTo(y.Now.Index) is var byNow and not 0 ? byNow :
            x.Then.Index.CompareTo(y.Then.Index));

        return found;
    }

    /// <summary>
    /// How far every date moved between the versions, judged from the objects that
    /// are certainly the same: a whole number of hours shared by most of them, or
    /// nothing.
    /// </summary>
    internal static TimeSpan DateShift(IEnumerable<(MapObject Then, MapObject Now)> sameObjects)
    {
        long[] seconds = sameObjects
            .Where(p => p.Then.CreatedUtc is not null && p.Now.CreatedUtc is not null)
            .Select(p => Second(p.Now.CreatedUtc.Value) - Second(p.Then.CreatedUtc.Value))
            .ToArray();

        if (seconds.Length == 0)
            return TimeSpan.Zero;

        var commonest = seconds.GroupBy(s => s).MaxBy(g => g.Count());
        bool wholeHours = commonest.Key % 3600 == 0 && Math.Abs(commonest.Key) <= 26 * 3600;

        return wholeHours && commonest.Count() * 2 > seconds.Length
            ? TimeSpan.FromSeconds(commonest.Key)
            : TimeSpan.Zero;
    }

    /// <summary>
    /// Whether OCAD recorded an edit: the newer object was modified at another time
    /// than the older one. Different content under the same modification date is two
    /// objects - an import stamps a whole batch with one date, created and modified
    /// alike - not an edit. Without both dates there is nothing to go on, so yes.
    /// </summary>
    private static bool WasTouched(MapObject then, MapObject now, TimeSpan shift)
        => then.ModifiedUtc is null || now.ModifiedUtc is null ||
           Math.Abs(Millisecond(now.ModifiedUtc.Value) - Millisecond(then.ModifiedUtc.Value + shift)) > 1;

    /// <summary>A date to the whole second, for judging a shift of whole hours.</summary>
    private static long Second(DateTime utc) => (long)Math.Round(utc.Ticks / (double)TimeSpan.TicksPerSecond);

    /// <summary>A date to the millisecond, which is as fine as OCAD's dates come back.</summary>
    private static long Millisecond(DateTime utc) => utc.Ticks / TimeSpan.TicksPerMillisecond;

    /// <summary>Paper millimetres between two boxes; zero when they touch or overlap.</summary>
    private static double Gap(PaperBounds a, PaperBounds b)
    {
        double dx = Math.Max(0, Math.Max(a.MinX - b.MaxX, b.MinX - a.MaxX));
        double dy = Math.Max(0, Math.Max(a.MinY - b.MaxY, b.MinY - a.MaxY));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Paper millimetres between the centres of two boxes.</summary>
    private static double Apart(PaperBounds a, PaperBounds b)
    {
        double dx = (a.MinX + a.MaxX - b.MinX - b.MaxX) / 2;
        double dy = (a.MinY + a.MaxY - b.MinY - b.MaxY) / 2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Two objects that draw the same: symbol, kind, angle, text and every coordinate.</summary>
    internal sealed class SameContent : IEqualityComparer<MapObject>
    {
        public static readonly SameContent Instance = new();

        public bool Equals(MapObject x, MapObject y)
            => ReferenceEquals(x, y) ||
               x is not null && y is not null &&
               x.SymbolNumber == y.SymbolNumber &&
               x.Kind == y.Kind &&
               x.AngleInDegrees == y.AngleInDegrees &&
               string.Equals(x.Text ?? "", y.Text ?? "", StringComparison.Ordinal) &&
               x.Points.SequenceEqual(y.Points);

        public int GetHashCode(MapObject o)
        {
            HashCode hash = new();
            hash.Add(o.SymbolNumber);
            hash.Add(o.Kind);
            hash.Add(o.Points.Count);
            if (o.Points.Count > 0) {
                hash.Add(o.Points[0]);
                hash.Add(o.Points[^1]);
            }
            return hash.ToHashCode();
        }
    }
}
