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
/// <param name="CreatedAtUtc">
/// Null where OCAD held a date the parser could not make sense of, which happens
/// often enough in real files to be worth tolerating rather than rejecting.
/// </param>
public abstract record DiffSymbol(
    string SymbolNumber,
    string SymbolName,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? LastEditedAtUtc);

/// <summary>A symbol present in the newer version only.</summary>
public sealed record AddedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? LengthInMeters, double? AreaInSquareMeters)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc);

/// <summary>A symbol present in the older version only.</summary>
public sealed record DeletedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? LengthInMeters, double? AreaInSquareMeters)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc);

/// <summary>
/// A symbol in both versions whose geometry or symbol number changed.
/// </summary>
/// <remarks>
/// Only one of the three measures is normally meaningful, depending on whether the
/// symbol is a point, a line or an area, which is why each is nullable rather than
/// defaulted to zero.
/// </remarks>
public sealed record UpdatedSymbol(
    string SymbolNumber, string SymbolName,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastEditedAtUtc,
    double? MovementInMeters, double? LengthDiffInPercent, double? AreaDiffInPercent)
    : DiffSymbol(SymbolNumber, SymbolName, CreatedAtUtc, LastEditedAtUtc);

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
/// An interface with one empty implementation, deliberately. Comparing two maps is
/// deferred work, and the alternative - wiring the table straight to a diff that
/// does not exist - would mean reshaping the UI later when one does.
/// </remarks>
public interface IDiffSource
{
    /// <summary>Whether this source can actually produce a comparison.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Why no comparison is available, as a translation key the UI looks up.
    /// </summary>
    string UnavailableReasonKey { get; }

    Task<MapDiff> CompareAsync(string olderVersion, string newerVersion);
}

/// <summary>
/// Stands in until a real comparison exists, so the table renders an honest empty
/// state rather than plausible-looking rows that are not real.
/// </summary>
public sealed class NoDiffSource : IDiffSource
{
    public bool IsAvailable => false;

    public string UnavailableReasonKey => "#_diffNotImplemented";

    public Task<MapDiff> CompareAsync(string olderVersion, string newerVersion)
        => Task.FromResult(MapDiff.Empty);
}
