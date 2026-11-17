namespace OcadViewer.Ocd;

/// <summary>OCAD's types of parameter string that callers refer to by name.</summary>
public static class SettingType
{
    /// <summary>A background map (template).</summary>
    public const int Template = 8;
    /// <summary>A colour.</summary>
    public const int Color = 9;
    /// <summary>The zoom and scroll position.</summary>
    public const int Zoom = 12;
    /// <summary>The selected objects.</summary>
    public const int Selection = 19;
    /// <summary>Recently opened files.</summary>
    public const int RecentFiles = 24;
    /// <summary>The display mode.</summary>
    public const int Display = 1024;
    /// <summary>View options.</summary>
    public const int View = 1030;
    /// <summary>The map scale and georeferencing.</summary>
    public const int Scale = 1039;
    /// <summary>A terrain model (DEM).</summary>
    public const int TerrainModel = 1045;
    /// <summary>A point cloud (LAS data).</summary>
    public const int PointCloud = 1078;
}
