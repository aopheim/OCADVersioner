namespace OcadViewer.Versioning;

/// <summary>
/// Metadata stored alongside each release, as <c>Releases/V&lt;n&gt;/V&lt;n&gt;.json</c>.
/// </summary>
/// <remarks>
/// The shape is fixed by the Angular app's <c>OcadVersionMetaData</c> and must stay
/// compatible: both apps read and write these files while the migration is under
/// way, and a user may create a version in one and open it in the other.
/// </remarks>
public sealed class VersionMetadata
{
    public string Title { get; set; }
    public string Description { get; set; }

    /// <summary>When the version was created. Written as the browser writes it.</summary>
    public DateTimeOffset VersionCreatedAt { get; set; }

    public int VersionNumber { get; set; }
    public int NumberOfAddedSymbols { get; set; }
    public int NumberOfEditedSymbols { get; set; }
    public int NumberOfDeletedSymbols { get; set; }
}

/// <summary>One release found in the project folder.</summary>
public sealed record ProjectVersion(string Name, int Number, VersionMetadata Metadata);

/// <summary>
/// A folder found under <c>Releases/</c>, before anything has decided whether it
/// is a release at all.
/// </summary>
/// <remarks>
/// Deliberately raw: a name, whether its map file is there, and the metadata
/// text if any. <see cref="ProjectFolder.ReleasesIn"/> applies the naming rule.
/// </remarks>
public sealed record ReleaseFolder(string Name, bool HasMapFile, string MetadataJson);

/// <summary>What the project folder contains.</summary>
/// <param name="OcdCandidates">
/// Every <c>.ocd</c> directly in the project root. More than one means the user has
/// to say which is the working file.
/// </param>
public sealed record ProjectInfo(
    string ProjectName,
    string CurrentFileName,
    IReadOnlyList<string> OcdCandidates,
    IReadOnlyList<ProjectVersion> Versions)
{
    public static readonly ProjectInfo None = new(null, null, [], []);
}
