using OcadViewer.Diff;

namespace OcadViewer.Wasm.Services;

/// <summary>Which slice of the diff a table is showing.</summary>
public enum DiffTableView
{
    Added,
    Updated,
    Deleted
}

/// <summary>A symbol as the diff table shows it.</summary>
/// <param name="SymbolNumber">ISOM code, e.g. "109.0".</param>
/// <param name="Bounds">Where the object is on the map, for zooming to it; null if it has no coordinates.</param>
/// <param name="CreatedAtUtc">
/// Null where OCAD held a date the parser could not make sense of, which happens
/// often enough in real files to be worth tolerating rather than rejecting.
/// </param>
public abstract record DiffSymbol(
    string SymbolNumber,
    string SymbolName,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? LastEditedAtUtc,
    PaperBounds Bounds);

/// <summary>A symbol present in the newer version only.</summary>
public sealed record AddedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? LengthInMeters, double? AreaInSquareMeters, PaperBounds Bounds)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc, Bounds);

/// <summary>A symbol present in the older version only.</summary>
public sealed record DeletedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? LengthInMeters, double? AreaInSquareMeters, PaperBounds Bounds)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc, Bounds);

/// <summary>
/// A symbol in both versions that differs between them.
/// </summary>
/// <remarks>
/// Only one of the three measures is normally meaningful, depending on whether the
/// symbol is a point, a line or an area, which is why each is nullable rather than
/// defaulted to zero.
/// </remarks>
/// <param name="Changes">Which properties differ: symbol, geometry, angle, text.</param>
public sealed record UpdatedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? MovementInMeters, double? LengthDiffInPercent, double? AreaDiffInPercent,
    ObjectChanges Changes, PaperBounds Bounds)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc, Bounds);

/// <summary>The comparison between two versions.</summary>
public sealed record MapDiff(
    IReadOnlyList<AddedSymbol> Added,
    IReadOnlyList<UpdatedSymbol> Updated,
    IReadOnlyList<DeletedSymbol> Deleted)
{
    public static readonly MapDiff Empty = new([], [], []);

    public int Count(DiffTableView view) => view switch {
        DiffTableView.Added => Added.Count,
        DiffTableView.Updated => Updated.Count,
        DiffTableView.Deleted => Deleted.Count,
        _ => 0
    };
}

/// <summary>Produces the comparison between two loaded versions.</summary>
/// <remarks>
/// The table's view of the diff. <see cref="OcadDiffSource"/> is the real one; the
/// interface stays so the table never learns where its rows come from.
/// </remarks>
public interface IDiffSource
{
    /// <summary>Whether this source can actually produce a comparison.</summary>
    bool IsAvailable { get; }

    /// <summary>Why no comparison is available, for the UI to show plainly.</summary>
    string UnavailableReason { get; }

    Task<MapDiff> CompareAsync(string olderVersion, string newerVersion);
}
