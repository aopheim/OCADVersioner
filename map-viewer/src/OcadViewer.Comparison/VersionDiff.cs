using OcadViewer.Ocd;

namespace OcadViewer.Comparison;

/// <summary>An object one version has and the other does not.</summary>
/// <param name="Object">The object, from the version that has it.</param>
/// <param name="SymbolDescription">Its symbol's name in that version, or empty.</param>
/// <param name="LengthInMeters">For a line; otherwise null.</param>
/// <param name="AreaInSquareMeters">For an area, less its holes; otherwise null.</param>
public sealed record ObjectOnOneSide(
    OcadObject Object,
    string SymbolDescription,
    double? LengthInMeters,
    double? AreaInSquareMeters,
    Bounds Bounds);

/// <summary>One object that both versions have, and that changed.</summary>
/// <param name="Changes">Which fields differ, as <see cref="ContentComparer"/> sees them.</param>
/// <param name="MovementInMeters">For a point object; otherwise null.</param>
/// <param name="LengthChangeInPercent">For a line; otherwise null.</param>
/// <param name="AreaChangeInPercent">For an area; otherwise null.</param>
/// <param name="Bounds">Around both positions, old and new.</param>
public sealed record ObjectChanged(
    OcadObject Older,
    OcadObject Newer,
    string SymbolDescription,
    ObjectFields Changes,
    double? MovementInMeters,
    double? LengthChangeInPercent,
    double? AreaChangeInPercent,
    Bounds Bounds);

/// <summary>What changed from one version of a map to another.</summary>
/// <param name="DateShift">The whole-hour shift of every creation date the matching allowed for; see <see cref="ObjectMatcher"/>.</param>
public sealed record VersionDiff(
    IReadOnlyList<ObjectOnOneSide> Added,
    IReadOnlyList<ObjectChanged> Changed,
    IReadOnlyList<ObjectOnOneSide> Deleted,
    TimeSpan DateShift)
{
    /// <summary>
    /// The objects added, changed and deleted between <paramref name="older"/> and
    /// <paramref name="newer"/>, by the same identity and the same idea of a change
    /// the merge uses.
    /// </summary>
    /// <param name="older">The earlier version, or null when there is none: then every object is added.</param>
    /// <param name="toleranceMeters">Coordinates closer than this on the ground are not a change.</param>
    public static VersionDiff Compare(OcadFile older, OcadFile newer,
                                      double toleranceMeters = ContentComparer.DefaultToleranceMeters)
    {
        ArgumentNullException.ThrowIfNull(newer);

        ObjectPairing pairing = ObjectMatcher.Match(older?.Objects ?? [], newer.Objects);
        ContentComparer comparer = ContentComparer.ForScale(newer.Scale, toleranceMeters);
        SymbolNames newerNames = new(newer), olderNames = new(older);

        List<ObjectChanged> changed = [];
        foreach ((OcadObject then, OcadObject now) in pairing.Pairs) {
            ObjectFields changes = comparer.Differences(then, now);
            if (changes == ObjectFields.None)
                continue;

            changed.Add(new ObjectChanged(
                then, now, newerNames[now.Symbol], changes,
                Measure.MovementInMeters(then, now, newer.Scale),
                Measure.PercentChange(Measure.LengthInMeters(then, older.Scale), Measure.LengthInMeters(now, newer.Scale)),
                Measure.PercentChange(Measure.AreaInSquareMeters(then, older.Scale), Measure.AreaInSquareMeters(now, newer.Scale)),
                Measure.BoundsOf(now).Union(Measure.BoundsOf(then))));
        }

        return new VersionDiff(
            [.. pairing.OnlyInBranch.Select(o => OneSide(o, newer, newerNames))],
            [.. changed.OrderBy(c => c.Newer.Entry)],
            [.. pairing.OnlyInBase.Select(o => OneSide(o, older, olderNames))],
            pairing.DateShift);
    }

    private static ObjectOnOneSide OneSide(OcadObject o, OcadFile file, SymbolNames names)
        => new(o, names[o.Symbol],
               Measure.LengthInMeters(o, file.Scale),
               Measure.AreaInSquareMeters(o, file.Scale),
               Measure.BoundsOf(o));

    /// <summary>Symbol descriptions by number, for one file.</summary>
    private sealed class SymbolNames(OcadFile file)
    {
        private readonly Dictionary<int, string> names = file?.Symbols
            .GroupBy(s => s.Number)
            .ToDictionary(g => g.Key, g => g.First().Description) ?? [];

        public string this[int number] => names.GetValueOrDefault(number, "");
    }
}
