using OcadViewer.Core;
using OcadViewer.Diff;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// The diff table's rows, from the C# diff over the two versions on screen.
/// </summary>
/// <remarks>
/// Both versions have already been parsed by the time a comparison is asked for -
/// the page shows them first - and <see cref="MapStore"/> took a diff snapshot of
/// each while it had the file's bytes. So comparing reads no file and parses
/// nothing; it is the diff itself and the translation into table rows.
/// </remarks>
public sealed class OcadDiffSource(IOcadDiffService diff) : IDiffSource
{
    private readonly IOcadDiffService diff = diff;

    public bool IsAvailable { get; private set; } = true;

    public string UnavailableReason { get; private set; }

    public Task<MapDiff> CompareAsync(MapStore maps, string olderVersion, string newerVersion)
    {
        OcadMapSnapshot newer = maps.Get(newerVersion)?.Snapshot;

        // No older version: a release with nothing before it. Its objects are all
        // new, as they were in the Angular app, so compare it with nothing. The
        // same version on both sides is an ordinary comparison, with no changes.
        bool firstVersion = olderVersion is null;
        OcadMapSnapshot older = firstVersion ? null : maps.Get(olderVersion)?.Snapshot;

        if (newer is null || (!firstVersion && older is null)) {
            IsAvailable = false;
            UnavailableReason =
                "These versions cannot be compared object by object: the comparison reads "
                + "OCAD 9 to 2018 maps only. Both maps are still shown above.";
            return Task.FromResult(MapDiff.Empty);
        }

        IsAvailable = true;
        UnavailableReason = null;
        return Task.FromResult(ToTable(diff.Compare(older, newer)));
    }

    private static MapDiff ToTable(ObjectDiff result) => new(
        [.. result.Added.Select(a => new AddedSymbol(
            a.Symbol.Number, a.Symbol.Name, Utc(a.CreatedUtc), Utc(a.LastEditedUtc),
            a.LengthInMeters, a.AreaInSquareMeters, a.Bounds))],
        [.. result.Edited.Select(e => new UpdatedSymbol(
            e.Symbol.Number, e.Symbol.Name, Utc(e.CreatedUtc), Utc(e.LastEditedUtc),
            e.MovementInMeters, e.LengthChangeInPercent, e.AreaChangeInPercent, e.Changes, e.Bounds))],
        [.. result.Deleted.Select(d => new DeletedSymbol(
            d.Symbol.Number, d.Symbol.Name, Utc(d.CreatedUtc), Utc(d.LastEditedUtc),
            d.LengthInMeters, d.AreaInSquareMeters, d.Bounds))]);

    private static DateTimeOffset? Utc(DateTime? value)
        => value is null ? null : new DateTimeOffset(value.Value, TimeSpan.Zero);
}
