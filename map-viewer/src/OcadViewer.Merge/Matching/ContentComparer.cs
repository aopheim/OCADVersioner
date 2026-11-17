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
/// and a symbol's selection flag, symbol-box status and own file position.
/// Coordinates may differ by the tolerance.
/// </remarks>
public sealed class ContentComparer(int toleranceUnits)
{
    private const ObjectFields Dates = ObjectFields.CreationDate | ObjectFields.ModificationDate;
    private const SymbolFields VolatileSymbolFields = SymbolFields.Selected | SymbolFields.BoxStatus | SymbolFields.FilePosition;

    public int ToleranceUnits { get; } = toleranceUnits;

    public bool Same(OcadObject a, OcadObject b)
        => a.SameContent(b, a.Symbol > 0 ? Dates | ObjectFields.Color : Dates, ToleranceUnits);

    public bool Same(OcadSymbol a, OcadSymbol b) => a.SameContent(b, VolatileSymbolFields);

    public bool Same(OcadSetting a, OcadSetting b) => a.SameContent(b);
}
