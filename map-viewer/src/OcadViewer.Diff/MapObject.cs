namespace OcadViewer.Diff;

/// <summary>What kind of geometry an object has, from OCAD's object type.</summary>
public enum MapObjectKind
{
    Point = 1,
    Line = 2,
    Area = 3,
    Text = 4,
    /// <summary>Formatted text, line text and rectangles: drawn, but not measured.</summary>
    Other = 0,
}

/// <summary>OCAD's per-object status, from the object index.</summary>
public enum MapObjectStatus : byte
{
    Deleted = 0,
    Normal = 1,
    Hidden = 2,
    /// <summary>Deleted, but kept so the edit can be undone.</summary>
    DeletedForUndo = 3,
}

/// <summary>
/// One coordinate, in OCAD's own units: hundredths of a paper millimetre.
/// </summary>
/// <param name="IsCurveControl">
/// A Bezier control point rather than a point on the object. Measurements skip
/// these and measure the chord between on-curve points instead, which is close
/// for gentle curves and makes no claim to be exact.
/// </param>
/// <param name="StartsHole">The first point of a hole in an area.</param>
public readonly record struct MapPoint(int X, int Y, bool IsCurveControl, bool StartsHole);

/// <summary>One object as OCAD's object index and object block describe it.</summary>
/// <param name="Index">
/// The object's entry in OCAD's index, counted from 1. OCAD keeps an object in its
/// entry when it is edited, which is what lets the diff pair it with itself.
/// </param>
/// <param name="SymbolNumber">OCAD's symbol number, e.g. 109000 for symbol 109.0.</param>
/// <param name="AngleInDegrees">Rotation, for symbols that have one.</param>
/// <param name="CreatedUtc">
/// Null when the file format has no timestamps (before OCAD 10) or the stored
/// value is not a usable date. OCAD writes no time zone; like the Angular app,
/// the value is taken as UTC.
/// </param>
public sealed record MapObject(
    int Index,
    MapObjectStatus Status,
    int SymbolNumber,
    MapObjectKind Kind,
    double AngleInDegrees,
    string Text,
    DateTime? CreatedUtc,
    DateTime? ModifiedUtc,
    IReadOnlyList<MapPoint> Points);
