namespace OcadViewer.Diff;

/// <summary>
/// Compares the objects of two versions: which were added, edited and deleted.
/// </summary>
/// <remarks>
/// Which old object each new one is gets decided by <see cref="ObjectMatcher"/>:
/// by content, by OCAD's object index entry and by creation date, in that order of
/// certainty. Unlike the Angular app, which matched GeoJSON ids its converter
/// regenerated on every read, this survives any edit to an object, a change of
/// symbol included, and a map whose objects were all renumbered. Edits are reported
/// under the index entry the object has now.
/// </remarks>
public sealed class OcadDiffService : IOcadDiffService
{
    public ObjectDiff Compare(OcadMapSnapshot older, OcadMapSnapshot newer)
    {
        ArgumentNullException.ThrowIfNull(newer);

        ObjectMatcher.Result match = ObjectMatcher.Match(
            older?.Objects ?? new Dictionary<int, MapObject>(), newer.Objects);

        List<ObjectEdited> edited = [];
        foreach ((MapObject then, MapObject now) in match.Pairs) {
            ObjectChanges changes = ChangesBetween(then, now);
            if (changes != ObjectChanges.None)
                edited.Add(Edited(then, older, now, newer, changes));
        }
        edited.Sort((a, b) => a.Index.CompareTo(b.Index));

        return new ObjectDiff(match.Added.Select(o => Added(o, newer)).ToList(),
                              edited,
                              match.Deleted.Select(o => Deleted(o, older)).ToList());
    }

    private static ObjectChanges ChangesBetween(MapObject then, MapObject now)
    {
        ObjectChanges changes = ObjectChanges.None;

        if (then.SymbolNumber != now.SymbolNumber)
            changes |= ObjectChanges.Symbol;
        if (then.Kind != now.Kind || !then.Points.SequenceEqual(now.Points))
            changes |= ObjectChanges.Geometry;
        if (then.AngleInDegrees != now.AngleInDegrees)
            changes |= ObjectChanges.Angle;
        if (!string.Equals(then.Text ?? "", now.Text ?? "", StringComparison.Ordinal))
            changes |= ObjectChanges.Text;

        return changes;
    }

    private static ObjectAdded Added(MapObject o, OcadMapSnapshot version)
        => new(o.Index, version.SymbolFor(o.SymbolNumber), o.Kind, o.CreatedUtc, LastEdited(o),
               LengthInMeters(o, version), AreaInSquareMeters(o, version), Measure.Bounds(o.Points));

    private static ObjectDeleted Deleted(MapObject o, OcadMapSnapshot version)
        => new(o.Index, version.SymbolFor(o.SymbolNumber), o.Kind, o.CreatedUtc, LastEdited(o),
               LengthInMeters(o, version), AreaInSquareMeters(o, version), Measure.Bounds(o.Points));

    private static ObjectEdited Edited(MapObject then, OcadMapSnapshot older,
                                       MapObject now, OcadMapSnapshot newer, ObjectChanges changes)
    {
        bool bothAre(MapObjectKind kind) => then.Kind == kind && now.Kind == kind;

        double? movement = bothAre(MapObjectKind.Point) && then.Points.Count > 0 && now.Points.Count > 0
            ? Measure.Distance(then.Points[0], now.Points[0]) * newer.MetresPerUnit
            : null;

        double? lengthChange = bothAre(MapObjectKind.Line)
            ? PercentChange(LengthInMeters(then, older), LengthInMeters(now, newer))
            : null;

        double? areaChange = bothAre(MapObjectKind.Area)
            ? PercentChange(AreaInSquareMeters(then, older), AreaInSquareMeters(now, newer))
            : null;

        return new ObjectEdited(now.Index, newer.SymbolFor(now.SymbolNumber), older.SymbolFor(then.SymbolNumber),
                                now.Kind, changes, now.CreatedUtc, now.ModifiedUtc,
                                movement, lengthChange, areaChange,
                                Measure.Bounds(now.Points)?.Union(Measure.Bounds(then.Points))
                                    ?? Measure.Bounds(then.Points));
    }

    /// <summary>A last-edited date equal to the creation date says nothing, so it is left out.</summary>
    private static DateTime? LastEdited(MapObject o)
        => o.ModifiedUtc == o.CreatedUtc ? null : o.ModifiedUtc;

    private static double? LengthInMeters(MapObject o, OcadMapSnapshot version)
        => o.Kind == MapObjectKind.Line ? Measure.Length(o.Points) * version.MetresPerUnit : null;

    private static double? AreaInSquareMeters(MapObject o, OcadMapSnapshot version)
        => o.Kind == MapObjectKind.Area
            ? Measure.Area(o.Points) * version.MetresPerUnit * version.MetresPerUnit
            : null;

    private static double? PercentChange(double? before, double? after)
        => before is > 0 && after is not null ? (after.Value - before.Value) / before.Value * 100 : null;
}
