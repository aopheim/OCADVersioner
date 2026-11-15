using OcadViewer.Core;

using PurplePen.MapModel;

namespace OcadViewer.Wasm;

/// <summary>The one <see cref="MapStore"/> the page has.</summary>
/// <remarks>
/// Two readers share it. The JavaScript shim parses maps into it and draws tiles
/// from it; the diff reads the snapshot kept beside each parsed map. One store
/// means a version is parsed once however many things look at it, and that the
/// diff compares exactly what is on screen.
/// </remarks>
internal static class LoadedMaps
{
    /// <summary>
    /// Templates are off because nothing could satisfy them. A template is a
    /// separate file beside the map on the user's disk, and the browser hands us
    /// one file through a directory picker - there is no directory for the parser
    /// to search. Asking for templates we cannot load only costs work.
    /// </summary>
    public static MapStore Store { get; } = new(
        MapStore.DefaultCapacity,
        new MapRenderSettings { Templates = RenderTemplateOption.MapOnly });
}
