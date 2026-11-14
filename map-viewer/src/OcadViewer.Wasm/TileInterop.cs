using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

using OcadViewer.Changes;
using OcadViewer.Core;

using PurplePen.MapModel;

namespace OcadViewer.Wasm;

/// <summary>
/// The JavaScript face of <see cref="MapStore"/> and the change grouping: hold
/// maps by name, draw tiles, say where two versions differ.
/// </summary>
/// <remarks>
/// A marshalling shim and nothing else. Everything it used to decide - which map
/// is where, how long a render took, how to format the answer - now lives in
/// <see cref="MapStore"/>, in a project with no browser in it, so the same code
/// serves the console harness and can be reasoned about without a page open.
///
/// These are <c>[JSExport]</c> rather than <c>[JSInvokable]</c> on purpose. A tile
/// is 256 KB of raw RGBA; <c>[JSExport]</c> hands that to JavaScript as a typed
/// array, whereas <c>DotNetObjectReference.invokeMethodAsync</c> would base64 it
/// inside JSON and make every tile pay for a string and a parse.
///
/// Maps are addressed by name, never by pane. See <see cref="MapStore"/> for why
/// that matters.
/// </remarks>
public static partial class TileInterop
{
    /// <summary>
    /// Templates are off because nothing could satisfy them. A template is a
    /// separate file beside the map on the user's disk, and the browser hands us
    /// one file through a directory picker - there is no directory for the parser
    /// to search. Asking for templates we cannot load only costs work.
    /// </summary>
    private static readonly MapStore Store = new(
        MapStore.DefaultCapacity,
        new MapRenderSettings { Templates = RenderTemplateOption.MapOnly });

    /// <summary>Whether a map is already parsed under <paramref name="key"/>.</summary>
    /// <remarks>
    /// This is what lets the caller skip re-reading a multi-megabyte file it
    /// already has in memory.
    /// </remarks>
    [JSExport]
    internal static bool IsLoaded(string key) => Store.Contains(key);

    /// <summary>
    /// Parses <paramref name="data"/> and keeps it under <paramref name="key"/>.
    /// </summary>
    /// <returns>
    /// A JSON summary. Parse failures come back as <c>{ ok: false, error }</c>
    /// rather than as an exception, because a map that will not open is an
    /// ordinary thing for a user to have and the page needs to say so.
    /// </returns>
    [JSExport]
    internal static string LoadMap(string key, string fileName, byte[] data)
    {
        try {
            StoredMap map = Store.Load(key, fileName, data);

            return Write(new MapLoadResult {
                Ok = true,
                Key = map.Key,
                Version = map.FormatVersion,
                MinX = map.Bounds.Left,
                MinY = map.Bounds.Top,
                MaxX = map.Bounds.Right,
                MaxY = map.Bounds.Bottom,
                ParseMs = map.ParseTime.TotalMilliseconds,
                Georeference = map.Georeference
            });
        }
        catch (Exception ex) {
            return Write(new MapLoadResult {
                Ok = false,
                Key = key,
                Error = $"{ex.GetType().Name}: {ex.Message}"
            });
        }
    }

    /// <summary>Forgets the map held under <paramref name="key"/>.</summary>
    [JSExport]
    internal static void UnloadMap(string key) => Store.Unload(key);

    /// <summary>
    /// Draws a square tile of <paramref name="key"/>'s map as raw RGBA.
    /// </summary>
    /// <returns><c>null</c> when no map is held under that name - which happens
    /// legitimately when a tile is drawn just after its map was evicted.</returns>
    [JSExport]
    internal static byte[] RenderTile(string key, double minX, double minY,
                                      double width, double height, int pixels)
        => Store.Render(key,
                        new System.Drawing.RectangleF((float)minX, (float)minY,
                                                      (float)width, (float)height),
                        pixels)?.Rgba;

    /// <summary>
    /// How big the change mask for this comparison will be, and what it covers.
    /// </summary>
    /// <remarks>
    /// Asked before <see cref="RenderChangeMask"/> so the caller can lay the
    /// overlay out and say how much work it is about to start. Nothing is
    /// remembered between the two calls; both derive from the map's bounds.
    /// </remarks>
    [JSExport]
    internal static string PlanChangeMask(string newerKey)
    {
        ChangeMaskPlan plan = Store.PlanChangeMask(newerKey);

        return plan is null
            ? JsonSerializer.Serialize(
                new ChangeMaskInfo { Ok = false, Error = $"\"{newerKey}\" is not loaded." },
                TileInteropJson.Default.ChangeMaskInfo)
            : JsonSerializer.Serialize(
                new ChangeMaskInfo {
                    Ok = true,
                    Width = plan.Width,
                    Height = plan.Height,
                    MinX = plan.Area.Left,
                    MinY = plan.Area.Top,
                    MaxX = plan.Area.Right,
                    MaxY = plan.Area.Bottom
                },
                TileInteropJson.Default.ChangeMaskInfo);
    }

    /// <summary>
    /// Where the two versions differ: one byte per pixel, row by row.
    /// </summary>
    /// <returns>
    /// 0 unchanged, 1 appeared, 2 vanished, 3 altered - or <c>null</c> if either
    /// version has gone from the store.
    /// </returns>
    [JSExport]
    internal static byte[] RenderChangeMask(string olderKey, string newerKey)
        => Store.RenderChangeMask(olderKey, newerKey);

    /// <summary>
    /// The places a change mask is pointing at, largest first.
    /// </summary>
    /// <remarks>
    /// The mask comes back in because the caller already has it. Grouping needs
    /// the same bytes <see cref="RenderChangeMask"/> just produced, and the two
    /// honest alternatives are worse: rendering both whole maps a second time
    /// costs seconds, and keeping the mask on this side would be a second cache
    /// of it, to go stale against the one the caller keeps for painting. One
    /// byte per pixel crossing once per comparison is the cheap option here.
    ///
    /// <para>No <c>ok</c> flag, unlike the calls around it. A map that will not
    /// open is an ordinary thing for a user to have; a mask that does not match
    /// its own dimensions is a bug, and should arrive as one.</para>
    /// </remarks>
    [JSExport]
    internal static string GroupChanges(byte[] mask, int width, int height,
                                        double minX, double minY, double maxX, double maxY)
    {
        ChangeGrouping found = ChangeGroupers.Default.Group(
            new ChangeMask(mask, width, height, new PaperRectangle(minX, minY, maxX, maxY)));

        return JsonSerializer.Serialize(found, TileInteropJson.Default.ChangeGrouping);
    }

    private static string Write(MapLoadResult result)
        => JsonSerializer.Serialize(result, TileInteropJson.Default.MapLoadResult);
}

/// <summary>What <see cref="TileInterop.PlanChangeMask"/> answers with.</summary>
internal sealed record ChangeMaskInfo
{
    public bool Ok { get; init; }
    public string Error { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public double? MinX { get; init; }
    public double? MinY { get; init; }
    public double? MaxX { get; init; }
    public double? MaxY { get; init; }
}

/// <summary>
/// What <see cref="TileInterop.LoadMap"/> answers with.
/// </summary>
/// <remarks>
/// One declared shape, serialised properly. It was three: a JSON string built by
/// concatenation on this side - with a hand-written escaper that replaced a
/// backslash with a space - a plain object in JavaScript, and a record in the
/// component. Numbers are nullable so a failure carries an error and nothing else,
/// instead of a page of zeroes.
/// </remarks>
internal sealed record MapLoadResult
{
    public bool Ok { get; init; }
    public string Key { get; init; }
    public string Error { get; init; }
    public int? Version { get; init; }
    public double? MinX { get; init; }
    public double? MinY { get; init; }
    public double? MaxX { get; init; }
    public double? MaxY { get; init; }
    public double? ParseMs { get; init; }
    public string Georeference { get; init; }
}

/// <summary>
/// Source-generated serialisation, so the shape survives IL trimming without a
/// rooted reflection path.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MapLoadResult))]
[JsonSerializable(typeof(ChangeMaskInfo))]
[JsonSerializable(typeof(ChangeGrouping))]
internal sealed partial class TileInteropJson : JsonSerializerContext
{
}
