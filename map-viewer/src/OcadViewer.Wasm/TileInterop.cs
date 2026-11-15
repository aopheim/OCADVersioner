using System.Drawing;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;

using OcadViewer.Core;

using PurplePen.MapModel;

namespace OcadViewer.Wasm;

/// <summary>
/// The JavaScript face of <see cref="MapStore"/>: hold maps by name, draw tiles.
/// </summary>
/// <remarks>
/// A marshalling shim and nothing else. Which map is held, how a mask is planned
/// and how long a parse took are <see cref="MapStore"/>'s business, in a project
/// with no browser in it.
///
/// Each caller gets its own store, named by the integer <see cref="CreateStore"/>
/// returns. A <c>[JSExport]</c> method has to be static, so the store cannot be
/// an object JavaScript holds; the integer is a handle to one, nothing more. A
/// workspace that is disposed or reset takes its maps with it, and two
/// workspaces never see each other's.
///
/// These are <c>[JSExport]</c> rather than <c>[JSInvokable]</c> on purpose. A tile
/// is 256 KB of raw RGBA; <c>[JSExport]</c> hands that to JavaScript as a typed
/// array, whereas <c>DotNetObjectReference.invokeMethodAsync</c> would base64 it
/// inside JSON and make every tile pay for a string and a parse.
/// </remarks>
public static partial class TileInterop
{
    /// <summary>
    /// Templates are off because nothing could satisfy them. A template is a
    /// separate file beside the map on the user's disk, and the parser has no
    /// directory to search for it in the browser. Asking for templates that
    /// cannot load only costs work.
    /// </summary>
    private static readonly MapRenderSettings Settings = new() { Templates = RenderTemplateOption.MapOnly };

    private static readonly Dictionary<int, MapStore> Stores = [];
    private static int lastStore;

    /// <summary>Creates an empty store and returns its handle.</summary>
    [JSExport]
    internal static int CreateStore()
    {
        int handle = ++lastStore;
        Stores.Add(handle, new MapStore(MapStore.DefaultCapacity, Settings));
        return handle;
    }

    /// <summary>Forgets every map in a store, which stays usable.</summary>
    [JSExport]
    internal static void ClearStore(int store) => StoreFor(store).Clear();

    /// <summary>Disposes a store. Its handle means nothing afterwards.</summary>
    [JSExport]
    internal static void DisposeStore(int store)
    {
        if (Stores.Remove(store, out MapStore removed))
            removed.Dispose();
    }

    /// <summary>Whether a map is already parsed under <paramref name="key"/>.</summary>
    [JSExport]
    internal static bool IsLoaded(int store, string key) => StoreFor(store).Contains(key);

    /// <summary>
    /// Parses <paramref name="data"/> and keeps it under <paramref name="key"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="MapSummary"/> as JSON. Parse failures come back as
    /// <c>{ ok: false, error }</c> rather than as an exception, because a map that
    /// will not open is an ordinary thing for a user to have and the page needs to
    /// say so.
    /// </returns>
    [JSExport]
    internal static string LoadMap(int store, string key, string fileName, byte[] data)
    {
        try {
            return Write(SummaryOf(StoreFor(store).Load(key, fileName, data)));
        }
        catch (Exception ex) {
            return Write(new MapSummary { Ok = false, Key = key, Error = $"{ex.GetType().Name}: {ex.Message}" });
        }
    }

    /// <summary>What is held under <paramref name="key"/>, as <see cref="LoadMap"/> reported it.</summary>
    [JSExport]
    internal static string Describe(int store, string key)
    {
        StoredMap map = StoreFor(store).Get(key);

        return Write(map is null
            ? new MapSummary { Ok = false, Key = key, Error = $"\"{key}\" is not loaded." }
            : SummaryOf(map));
    }

    /// <summary>
    /// Draws a square tile of <paramref name="key"/>'s map as raw RGBA.
    /// </summary>
    /// <returns><c>null</c> when no map is held under that name - which happens
    /// legitimately when a tile is drawn just after its map was evicted, or after
    /// its store was disposed.</returns>
    [JSExport]
    internal static byte[] RenderTile(int store, string key, double minX, double minY,
                                      double width, double height, int pixels)
        => Stores.TryGetValue(store, out MapStore maps)
            ? maps.Render(key, new RectangleF((float)minX, (float)minY, (float)width, (float)height), pixels)?.Rgba
            : null;

    /// <summary>
    /// The size and extent to compare two versions at, to be handed back to
    /// <see cref="RenderChangeMask"/>.
    /// </summary>
    [JSExport]
    internal static string PlanChangeMask(int store, string olderKey, string newerKey)
    {
        ChangeMaskPlan plan = StoreFor(store).PlanChangeMask(olderKey, newerKey);

        ChangeMaskInfo info = plan is null
            ? new ChangeMaskInfo { Ok = false, Error = $"\"{olderKey}\" or \"{newerKey}\" is not loaded." }
            : new ChangeMaskInfo {
                Ok = true,
                Width = plan.Width,
                Height = plan.Height,
                MinX = plan.Area.Left,
                MinY = plan.Area.Top,
                MaxX = plan.Area.Right,
                MaxY = plan.Area.Bottom
            };

        return JsonSerializer.Serialize(info, TileInteropJson.Default.ChangeMaskInfo);
    }

    /// <summary>
    /// Where the two versions differ over the planned rectangle: one byte per
    /// pixel, row by row.
    /// </summary>
    /// <returns>
    /// 0 unchanged, 1 appeared, 2 vanished, 3 altered - or <c>null</c> if either
    /// version has gone from the store.
    /// </returns>
    [JSExport]
    internal static byte[] RenderChangeMask(int store, string olderKey, string newerKey,
                                            double minX, double minY, double maxX, double maxY,
                                            int width, int height)
        => StoreFor(store).RenderChangeMask(
            olderKey, newerKey,
            new ChangeMaskPlan(width, height,
                               RectangleF.FromLTRB((float)minX, (float)minY, (float)maxX, (float)maxY)));

    private static MapStore StoreFor(int store)
        => Stores.TryGetValue(store, out MapStore maps)
            ? maps
            : throw new InvalidOperationException($"Map store {store} does not exist; it was never created or has been disposed.");

    private static MapSummary SummaryOf(StoredMap map) => new() {
        Ok = true,
        Key = map.Key,
        LoadId = map.LoadId,
        Version = map.FormatVersion,
        MinX = map.Bounds.Left,
        MinY = map.Bounds.Top,
        MaxX = map.Bounds.Right,
        MaxY = map.Bounds.Bottom,
        ParseMs = map.ParseTime.TotalMilliseconds,
        Georeference = map.Georeference
    };

    private static string Write(MapSummary summary)
        => JsonSerializer.Serialize(summary, TileInteropJson.Default.MapSummary);
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
/// Source-generated serialisation, so the shapes survive IL trimming without a
/// rooted reflection path.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MapSummary))]
[JsonSerializable(typeof(ChangeMaskInfo))]
internal sealed partial class TileInteropJson : JsonSerializerContext
{
}
