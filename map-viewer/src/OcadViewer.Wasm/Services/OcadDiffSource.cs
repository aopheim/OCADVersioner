using OcadViewer.Comparison;
using OcadViewer.Ocd;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// The diff table's rows, from <see cref="VersionDiff"/> over two versions of the
/// open project.
/// </summary>
/// <remarks>
/// Identity, change and measurement all come from OcadViewer.Comparison - the same
/// answers the merge gets. This only reads the versions through the cache and
/// turns the result into table rows.
/// </remarks>
public sealed class OcadDiffSource(ComparisonCache cache) : IDiffSource
{
    private const string CannotCompare = "#_diffCannotCompare";

    public async Task<DiffResult> CompareAsync(string olderVersion, string newerVersion)
    {
        OcadFile newer = await cache.GetAsync(newerVersion);
        OcadFile older = olderVersion is null ? null : await cache.GetAsync(olderVersion);
        cache.KeepOnly(olderVersion ?? newerVersion, newerVersion);

        if (newer is null || (olderVersion is not null && older is null))
            return new DiffResult(MapDiff.Empty, CannotCompare);

        try {
            return new DiffResult(ToTable(VersionDiff.Compare(older, newer)), null);
        }
        catch (Exception e) when (e is ArgumentException or OverflowException or OcadFormatException) {
            // A file the reader accepted but whose contents cannot be compared -
            // a date out of range, say - leaves the maps on screen and says so,
            // rather than failing the whole comparison.
            return new DiffResult(MapDiff.Empty, CannotCompare);
        }
    }

    public void Reset() => cache.Clear();

    private static MapDiff ToTable(VersionDiff diff) => new(
        [.. diff.Added.Select(a => new AddedSymbol(
            Number(a.Object.Symbol), Name(a.Object.Symbol, a.SymbolDescription),
            Utc(a.Object.CreatedAt), LastEdited(a.Object),
            a.LengthInMeters, a.AreaInSquareMeters, Paper(a.Bounds)))],
        [.. diff.Changed.Select(c => new UpdatedSymbol(
            Number(c.Newer.Symbol), Name(c.Newer.Symbol, c.SymbolDescription),
            Utc(c.Newer.CreatedAt), LastEdited(c.Newer),
            c.MovementInMeters, c.LengthChangeInPercent, c.AreaChangeInPercent,
            KindsOf(c.Changes), Paper(c.Bounds)))],
        [.. diff.Deleted.Select(d => new DeletedSymbol(
            Number(d.Object.Symbol), Name(d.Object.Symbol, d.SymbolDescription),
            Utc(d.Object.CreatedAt), LastEdited(d.Object),
            d.LengthInMeters, d.AreaInSquareMeters, Paper(d.Bounds)))]);

    /// <summary>OCAD's own rendering of a symbol number: 109000 is "109.0", 401002 is "401.2".</summary>
    private static string Number(int symbol) => symbol < 0 ? "" : $"{symbol / 1000}.{symbol % 1000}";

    /// <summary>
    /// Negative numbers are objects without a symbol - graphics, images, layout -
    /// which have no name in the map, so the table translates what they are.
    /// </summary>
    private static string Name(int symbol, string description) => symbol switch {
        >= 0 => description,
        -2 => "#_graphicObject",
        -3 => "#_imageObject",
        -4 => "#_layoutObject",
        _ => "#_importedObject",
    };

    private static ChangeKinds KindsOf(ObjectFields fields)
    {
        ChangeKinds kinds = ChangeKinds.None;
        if (fields.HasFlag(ObjectFields.Symbol)) kinds |= ChangeKinds.Symbol;
        if ((fields & (ObjectFields.Coordinates | ObjectFields.Type)) != 0) kinds |= ChangeKinds.Geometry;
        if (fields.HasFlag(ObjectFields.Angle)) kinds |= ChangeKinds.Angle;
        if (fields.HasFlag(ObjectFields.Text)) kinds |= ChangeKinds.Text;
        if (fields.HasFlag(ObjectFields.Status)) kinds |= ChangeKinds.Visibility;
        if ((fields & (ObjectFields.Other | ObjectFields.Color)) != 0) kinds |= ChangeKinds.Other;
        return kinds;
    }

    /// <summary>A last-edited date equal to the creation date says nothing, so it is left out - in every tab alike.</summary>
    private static DateTimeOffset? LastEdited(OcadObject o)
        => o.ModifiedAt == o.CreatedAt ? null : Utc(o.ModifiedAt);

    private static PaperRect Paper(Bounds b)
        => b.IsEmpty ? null : new PaperRect(b.MinX / 100.0, b.MinY / 100.0, b.MaxX / 100.0, b.MaxY / 100.0);

    private static DateTimeOffset? Utc(DateTime? value)
        => value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc), TimeSpan.Zero);
}
