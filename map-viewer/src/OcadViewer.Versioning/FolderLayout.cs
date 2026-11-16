namespace OcadViewer.Versioning;

/// <summary>
/// Where things are inside a project folder, as paths relative to its root.
/// </summary>
/// <remarks>
/// The layout is shared with the Angular app, which reads and writes the same
/// folders:
/// <code>
///   &lt;project&gt;/&lt;anything&gt;.ocd          the working file
///   &lt;project&gt;/Releases/V3/V3.ocd      a release
///   &lt;project&gt;/Releases/V3/V3.json     its metadata
/// </code>
/// This is the only place that layout is written down. The JavaScript that
/// touches the disk is handed paths and knows nothing about what they mean.
/// Paths use <c>/</c> and never start with one.
/// </remarks>
public static class FolderLayout
{
    public const string MapExtension = ".ocd";
    public const string MetadataExtension = ".json";

    /// <summary>Whether <paramref name="fileName"/> is an OCAD map, by its extension.</summary>
    public static bool IsMapFile(string fileName)
        => fileName is not null && fileName.EndsWith(MapExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder holding release <paramref name="name"/>.</summary>
    public static string ReleaseFolder(string name) => $"{VersionNaming.ReleasesFolder}/{name}";

    /// <summary>The map file's name inside its release folder.</summary>
    public static string MapFileName(string name) => name + MapExtension;

    /// <summary>The metadata file's name inside its release folder.</summary>
    public static string MetadataFileName(string name) => name + MetadataExtension;

    /// <summary>Where release <paramref name="name"/>'s map is.</summary>
    public static string ReleaseMapPath(string name) => $"{ReleaseFolder(name)}/{MapFileName(name)}";

    /// <summary>Where release <paramref name="name"/>'s metadata is.</summary>
    public static string ReleaseMetadataPath(string name) => $"{ReleaseFolder(name)}/{MetadataFileName(name)}";

    /// <summary>
    /// Where a version's map is read from, and whether it must be read again
    /// every time it is shown.
    /// </summary>
    /// <param name="versionName">A release name, or <see cref="VersionNaming.WorkingFileName"/>.</param>
    /// <param name="workingFileName">The working file in the project root, if one is settled.</param>
    /// <exception cref="InvalidOperationException">
    /// The working file was asked for and none is settled.
    /// </exception>
    public static MapSource SourceFor(string versionName, string workingFileName)
    {
        if (!VersionNaming.IsVolatile(versionName))
            return new MapSource(versionName, ReleaseMapPath(versionName), Reload: false);

        if (string.IsNullOrEmpty(workingFileName))
            throw new InvalidOperationException("No working .ocd file is selected.");

        return new MapSource(versionName, workingFileName, Reload: true);
    }

    /// <summary>The folder and files a new release called <paramref name="name"/> is written to.</summary>
    public static NewVersionPlan PlanNewVersion(string name)
        => new(ReleaseFolder(name), ReleaseMapPath(name), ReleaseMetadataPath(name));
}

/// <summary>Where one version's map is read from.</summary>
/// <param name="Name">The version name the map is held under, e.g. "V3" or "current".</param>
/// <param name="Path">The map file, relative to the project root.</param>
/// <param name="Reload">
/// True when the file can change while the app is open, so it is read again
/// every time it is shown. A release never changes once written.
/// </param>
public sealed record MapSource(string Name, string Path, bool Reload);

/// <summary>Where a new release is written, relative to the project root.</summary>
public sealed record NewVersionPlan(string Folder, string MapFile, string MetadataFile);

/// <summary>A folder and the names of the files directly inside it.</summary>
public sealed record FolderListing(string Name, IReadOnlyList<string> Files);
