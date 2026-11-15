namespace OcadViewer.Diff;

/// <summary>A symbol as the diff reports it.</summary>
/// <param name="Number">OCAD's symbol number as OCAD shows it, e.g. "109.0".</param>
/// <param name="Name">
/// The name in the map's own symbol table, in whatever language the map was made
/// in. Empty when the map has no symbol with that number.
/// </param>
public sealed record DiffSymbol(string Number, string Name);

/// <summary>Which properties of an object differ between two versions.</summary>
[Flags]
public enum ObjectChanges
{
    None = 0,
    Symbol = 1,
    Geometry = 2,
    Angle = 4,
    Text = 8,
}

/// <summary>An object in the newer version only.</summary>
/// <param name="LastEditedUtc">Null when the object has not been edited since it was created.</param>
/// <param name="LengthInMeters">Lines only.</param>
/// <param name="AreaInSquareMeters">Areas only.</param>
public sealed record ObjectAdded(
    int Index,
    DiffSymbol Symbol,
    MapObjectKind Kind,
    DateTime? CreatedUtc,
    DateTime? LastEditedUtc,
    double? LengthInMeters,
    double? AreaInSquareMeters);

/// <summary>An object in the older version only.</summary>
/// <param name="LastEditedUtc">Null when the object was never edited after it was created.</param>
/// <param name="LengthInMeters">Lines only.</param>
/// <param name="AreaInSquareMeters">Areas only.</param>
public sealed record ObjectDeleted(
    int Index,
    DiffSymbol Symbol,
    MapObjectKind Kind,
    DateTime? CreatedUtc,
    DateTime? LastEditedUtc,
    double? LengthInMeters,
    double? AreaInSquareMeters);

/// <summary>An object in both versions that differs between them.</summary>
/// <param name="Symbol">The symbol in the newer version.</param>
/// <param name="PreviousSymbol">The symbol in the older version; the same as <paramref name="Symbol"/> unless it changed.</param>
/// <param name="MovementInMeters">Points only: how far the point moved.</param>
/// <param name="LengthChangeInPercent">Lines only.</param>
/// <param name="AreaChangeInPercent">Areas only.</param>
public sealed record ObjectEdited(
    int Index,
    DiffSymbol Symbol,
    DiffSymbol PreviousSymbol,
    MapObjectKind Kind,
    ObjectChanges Changes,
    DateTime? CreatedUtc,
    DateTime? LastEditedUtc,
    double? MovementInMeters,
    double? LengthChangeInPercent,
    double? AreaChangeInPercent);

/// <summary>How the objects of one version differ from those of another.</summary>
public sealed record ObjectDiff(
    IReadOnlyList<ObjectAdded> Added,
    IReadOnlyList<ObjectEdited> Edited,
    IReadOnlyList<ObjectDeleted> Deleted)
{
    public static readonly ObjectDiff Empty = new([], [], []);

    public bool IsEmpty => Added.Count == 0 && Edited.Count == 0 && Deleted.Count == 0;
}
