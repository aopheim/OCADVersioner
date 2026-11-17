namespace OcadViewer.Wasm.Services;

/// <summary>Which slice of the diff a table is showing.</summary>
public enum DiffTableView
{
    Added,
    Updated,
    Deleted
}

/// <summary>A rectangle on the map, in millimetres on paper, Y up.</summary>
public sealed record PaperRect(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>What about an object changed, as the table names it.</summary>
[Flags]
public enum ChangeKinds
{
    None = 0,
    Symbol = 1,
    /// <summary>Its coordinates, or its kind (point, line, area...).</summary>
    Geometry = 2,
    Angle = 4,
    Text = 8,
    /// <summary>Hidden or shown.</summary>
    Visibility = 16,
    /// <summary>Anything else OCAD keeps for it, such as a graphic object's colour or line width.</summary>
    Other = 32,
}

/// <summary>A symbol as the diff table shows it.</summary>
/// <param name="SymbolNumber">ISOM code, e.g. "109.0"; empty for an object without a symbol.</param>
/// <param name="SymbolName">The symbol's name in the map, or a translation key for an object without a symbol.</param>
/// <param name="Bounds">Where the object is on the map, for zooming to it; null if it has no coordinates.</param>
/// <param name="CreatedAtUtc">Null where the file holds no date for it.</param>
/// <param name="LastEditedAtUtc">Null when never edited since it was created, or when the file keeps no such date.</param>
public abstract record DiffSymbol(
    string SymbolNumber,
    string SymbolName,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? LastEditedAtUtc,
    PaperRect Bounds);

/// <summary>A symbol present in the newer version only.</summary>
public sealed record AddedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? LengthInMeters, double? AreaInSquareMeters, PaperRect Bounds)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc, Bounds);

/// <summary>A symbol present in the older version only.</summary>
public sealed record DeletedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? LengthInMeters, double? AreaInSquareMeters, PaperRect Bounds)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc, Bounds);

/// <summary>A symbol in both versions that differs between them.</summary>
/// <remarks>
/// Only one of the three measures is normally meaningful, depending on whether the
/// symbol is a point, a line or an area, which is why each is nullable rather than
/// defaulted to zero.
/// </remarks>
public sealed record UpdatedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? MovementInMeters, double? LengthDiffInPercent, double? AreaDiffInPercent,
    ChangeKinds Changes, PaperRect Bounds)
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

/// <summary>One comparison's outcome: its rows, or why there are none.</summary>
/// <param name="UnavailableReasonKey">
/// A translation key saying why the versions could not be compared, or null when
/// they were. Part of the result, not of the source, so a result always describes
/// the comparison it came from.
/// </param>
public sealed record DiffResult(MapDiff Diff, string UnavailableReasonKey)
{
    /// <summary>Nothing compared yet: an empty table, with no reason to show.</summary>
    public static readonly DiffResult None = new(MapDiff.Empty, null);

    public bool IsAvailable => UnavailableReasonKey is null;
}

/// <summary>Produces the comparison between two versions of the open project.</summary>
/// <remarks>
/// The table's view of the diff. <see cref="OcadDiffSource"/> is the real one; the
/// interface stays so the table never learns where its rows come from.
/// </remarks>
public interface IDiffSource
{
    /// <param name="olderVersion">
    /// The version to compare against, or <c>null</c> when <paramref name="newerVersion"/>
    /// has nothing before it - then every object counts as added. The same version
    /// on both sides is an ordinary comparison and has no changes.
    /// </param>
    Task<DiffResult> CompareAsync(string olderVersion, string newerVersion);

    /// <summary>Forgets every version read so far, because another project folder is being opened.</summary>
    void Reset();
}
