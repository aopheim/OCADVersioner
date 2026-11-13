namespace OcadViewer.Wasm.Components;

/// <summary>Which pane of the comparison.</summary>
/// <remarks>
/// This replaces an untyped slot index that used to mean the same thing in four
/// places at once - the page, this component, the Leaflet module and the .NET
/// interop - with nothing naming it and nothing checking it. A pane is now a
/// named side, and a map is identified by its version name rather than by which
/// pane it happens to be in.
/// </remarks>
public enum MapSide
{
    Left,
    Right
}

internal static class MapSides
{
    /// <summary>
    /// The name the JavaScript side uses.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than sent as the enum itself: Blazor's interop
    /// serialises an enum as its ordinal, which would put the bare 0 and 1 back on
    /// the wire and undo the point of having a name at all.
    /// </remarks>
    public static string JsName(this MapSide side) => side switch {
        MapSide.Left => "left",
        MapSide.Right => "right",
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "Unknown pane.")
    };
}
