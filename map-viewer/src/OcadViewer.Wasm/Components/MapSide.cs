namespace OcadViewer.Wasm.Components;

/// <summary>Which pane of the comparison.</summary>
/// <remarks>
/// A pane is a named side, and nothing more: a map is identified by its version
/// name, never by which pane it happens to be in. That keeps a parsed map
/// independent of where it is on screen, and lets a pane be added or swapped
/// without touching the page, the Leaflet module and the interop at once.
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
