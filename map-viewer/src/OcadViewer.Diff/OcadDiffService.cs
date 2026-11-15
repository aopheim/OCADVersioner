namespace OcadViewer.Diff;

/// <summary>
/// Compares the objects of two versions, pairing each object with itself by its
/// entry in OCAD's object index.
/// </summary>
/// <remarks>
/// <para>
/// OCAD keeps an object in its index entry for as long as the object exists, edits
/// included, so the entry is the object's identity. Unlike the Angular app, which
/// matched GeoJSON ids that its converter regenerated on every read, this survives
/// any edit to the object itself, including a change of symbol.
/// </para>
/// <para>
/// The one way an entry can mislead is being handed to a different object. The
/// creation date guards against that: same entry, different creation date, and the
/// old object is reported deleted and the new one added.
/// </para>
/// </remarks>
public sealed class OcadDiffService : IOcadDiffService
{
    public ObjectDiff Compare(OcadMapSnapshot older, OcadMapSnapshot newer)
    {
        ArgumentNullException.ThrowIfNull(newer);

        IReadOnlyDictionary<int, MapObject> before = older?.Objects ?? new Dictionary<int, MapObject>();

        List<ObjectAdded> added = [];
        List<ObjectEdited> edited = [];
        List<ObjectDeleted> deleted = [];

        foreach (MapObject now in newer.Objects.Values.OrderBy(o => o.Index)) {
            if (!before.TryGetValue(now.Index, out MapObject then)) {
                added.Add(Added(now, newer));
                continue;
            }

            if (!IsSameObject(then, now)) {
                deleted.Add(Deleted(then, older));
                added.Add(Added(now, newer));
                continue;
            }

            ObjectChanges changes = ChangesBetween(then, now);
            if (changes != ObjectChanges.None)
                edited.Add(Edited(then, older, now, newer, changes));
        }

        foreach (MapObject then in before.Values.OrderBy(o => o.Index)) {
            if (!newer.Objects.ContainsKey(then.Index))
                deleted.Add(Deleted(then, older));
        }

        deleted.Sort((a, b) => a.Index.CompareTo(b.Index));
        return new ObjectDiff(added, edited, deleted);
    }

    /// <summary>
    /// Whether an entry still holds the object it held before. Without a creation
    /// date on both sides (OCAD 9 has none) the entry is all there is to go on.
    /// </summary>
    private static bool IsSameObject(MapObject then, MapObject now)
        => then.CreatedUtc is null || now.CreatedUtc is null || then.CreatedUtc == now.CreatedUtc;

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
