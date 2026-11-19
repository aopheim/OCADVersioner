namespace OcadViewer.Wasm;

/// <summary>
/// What parsing found out about one map, as the page sees it.
/// </summary>
/// <remarks>
/// One declared shape for both ends of the trip: <see cref="TileInterop"/>
/// serialises it for JavaScript, and <c>OcadMapPair</c> reads the same type back
/// when the workspace hands it on. Numbers are nullable so that a failure carries
/// an error and nothing else, instead of a page of zeroes.
/// </remarks>
public sealed record MapSummary
{
    public bool Ok { get; init; }
    public string Key { get; init; }
    public string Error { get; init; }

    /// <summary>
    /// Which parse of <see cref="Key"/> this describes. A new read of the same
    /// file gets a new one, which is how a cached change mask knows it is stale.
    /// </summary>
    public int? LoadId { get; init; }

    /// <summary>The OCAD format version the file was written in.</summary>
    public int? Version { get; init; }

    public double? MinX { get; init; }
    public double? MinY { get; init; }
    public double? MaxX { get; init; }
    public double? MaxY { get; init; }
    public double? ParseMs { get; init; }

    /// <summary>The map's projection, or null when it is not georeferenced.</summary>
    public string Georeference { get; init; }

    /// <summary>
    /// Paper millimetres to Web Mercator metres as <c>[a, b, c, d, e, f]</c>, where
    /// <c>mercatorX = a·x + b·y + c</c> and <c>mercatorY = d·x + e·y + f</c>; what a
    /// background web map is laid out by. Null when the map has no usable georeference.
    /// </summary>
    public double[] PaperToMercator { get; init; }
}
