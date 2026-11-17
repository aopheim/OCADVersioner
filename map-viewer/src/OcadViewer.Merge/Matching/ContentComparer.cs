using OcadViewer.Ocd;

namespace OcadViewer.Merge.Matching;

/// <summary>
/// Decides whether two versions of the same thing are the same: the definition
/// of "changed" for objects, symbols and parameter strings.
/// </summary>
/// <remarks>
/// Content is compared, except for what OCAD rewrites without the user changing
/// anything: an object's dates; the colour field of an object that has a symbol
/// (only graphic objects are drawn in it, yet OCAD sets it on symbol objects too);
/// a symbol's selection flag, symbol-box status and own file position; and in
/// parameter strings, whether a background map is shown and whether a terrain
/// model or point cloud is loaded (OCAD sets these as it opens the map, from
/// what it finds on the computer).
/// Coordinates may differ by the tolerance.
/// </remarks>
public sealed class ContentComparer(int toleranceUnits)
{
    private const ObjectFields Dates = ObjectFields.CreationDate | ObjectFields.ModificationDate;
    private const SymbolFields VolatileSymbolFields = SymbolFields.Selected | SymbolFields.BoxStatus | SymbolFields.FilePosition;

    /// <summary>Per type of parameter string, the fields OCAD rewrites by itself.</summary>
    private static readonly Dictionary<int, string> VolatileSettingFields = new() {
        [SettingType.Template] = "s",
        [SettingType.TerrainModel] = "l",
        [SettingType.PointCloud] = "l",
    };

    public int ToleranceUnits { get; } = toleranceUnits;

    public bool Same(OcadObject a, OcadObject b)
        => a.SameContent(b, a.Symbol > 0 ? Dates | ObjectFields.Color : Dates, ToleranceUnits);

    public bool Same(OcadSymbol a, OcadSymbol b) => a.SameContent(b, VolatileSymbolFields);

    public bool Same(OcadSetting a, OcadSetting b)
        => VolatileSettingFields.TryGetValue(a.Type, out string fields) ? a.SameContent(b, fields) : a.SameContent(b);
}
