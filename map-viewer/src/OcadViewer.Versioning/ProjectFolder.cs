using System.Text.Json;

namespace OcadViewer.Versioning;

/// <summary>
/// The rules of a project folder: which file is the working file, which folders
/// count as releases, what the next release is called, and which version is
/// compared against which.
/// </summary>
/// <remarks>
/// Pure functions over what somebody else read off the disk, so every rule can
/// be tested without a browser. Where the files are is <see cref="FolderLayout"/>.
/// </remarks>
public static class ProjectFolder
{
    /// <summary>The files in the project root that could be the working file.</summary>
    public static IReadOnlyList<string> WorkingFileCandidates(IEnumerable<string> rootFiles)
        => [.. (rootFiles ?? []).Where(FolderLayout.IsMapFile).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Which candidate is the working file: the one the user chose, or the only
    /// one there is. <c>null</c> when that is ambiguous and nobody has chosen.
    /// </summary>
    /// <remarks>
    /// A choice that no longer exists on disk is ignored rather than trusted, so
    /// a rescan after the file was renamed falls back to asking again.
    /// </remarks>
    public static string ChooseWorkingFile(IReadOnlyList<string> candidates, string chosen)
    {
        candidates ??= [];

        if (chosen is not null && candidates.Contains(chosen, StringComparer.Ordinal))
            return chosen;

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// The metadata file to read for a folder under <c>Releases/</c>, or
    /// <c>null</c> when it has none.
    /// </summary>
    public static string MetadataPathIn(FolderListing folder)
        => folder is not null && Holds(folder, FolderLayout.MetadataFileName(folder.Name))
            ? FolderLayout.ReleaseMetadataPath(folder.Name)
            : null;

    /// <summary>
    /// What a folder under <c>Releases/</c> holds, before anything has decided
    /// whether it is a release.
    /// </summary>
    /// <param name="metadataJson">The text of its metadata file, if it has one.</param>
    public static ReleaseFolder ReleaseFolderFrom(FolderListing folder, string metadataJson)
    {
        ArgumentNullException.ThrowIfNull(folder);
        return new ReleaseFolder(folder.Name, Holds(folder, FolderLayout.MapFileName(folder.Name)), metadataJson);
    }

    private static bool Holds(FolderListing folder, string fileName)
        => folder.Files?.Contains(fileName, StringComparer.Ordinal) == true;

    /// <summary>
    /// Turns a raw directory listing into the releases the app will show.
    /// </summary>
    /// <remarks>
    /// A folder is a release when it is named <c>V&lt;n&gt;</c> and holds the map
    /// file. Metadata is optional: a release whose <c>.json</c> is missing or
    /// corrupt still exists and must still be openable, so it gets a stub rather
    /// than being dropped.
    /// </remarks>
    public static IReadOnlyList<ProjectVersion> ReleasesIn(IEnumerable<ReleaseFolder> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var releases = new List<ProjectVersion>();

        foreach (ReleaseFolder folder in folders) {
            if (folder is null || !folder.HasMapFile)
                continue;
            if (!VersionNaming.TryParse(folder.Name, out int number))
                continue;

            releases.Add(new ProjectVersion(folder.Name, number, ReadMetadata(folder, number)));
        }

        releases.Sort((left, right) => left.Number.CompareTo(right.Number));
        return releases;
    }

    private static VersionMetadata ReadMetadata(ReleaseFolder folder, int number)
    {
        if (!string.IsNullOrWhiteSpace(folder.MetadataJson)) {
            try {
                VersionMetadata parsed = JsonSerializer.Deserialize<VersionMetadata>(
                    folder.MetadataJson, AngularJson.Options);
                if (parsed is not null)
                    return parsed;
            }
            catch (JsonException) {
                // Hand-edited, half-written, or produced by a version of the
                // Angular app that wrote something else. The release itself is
                // still perfectly readable.
            }
        }

        return new VersionMetadata { VersionNumber = number };
    }

    /// <summary>
    /// What the next release should be called, judged by every folder under
    /// <c>Releases/</c> rather than by the releases that are complete.
    /// </summary>
    /// <remarks>
    /// Highest existing number plus one. The Angular app counts folders instead,
    /// which gives the same answer until someone deletes a release by hand - after
    /// which counting reuses a number and overwrites an existing version.
    ///
    /// A <c>V&lt;n&gt;</c> folder without its map file is not a release, so
    /// <see cref="ReleasesIn"/> leaves it out - but its name is still taken.
    /// Naming from the releases alone picks that name again, and creating the
    /// version then writes into a folder somebody else owns.
    /// </remarks>
    public static string NextVersionName(IEnumerable<ReleaseFolder> folders)
    {
        int highest = 0;
        foreach (ReleaseFolder folder in folders ?? []) {
            if (folder is not null && VersionNaming.TryParse(folder.Name, out int number))
                highest = Math.Max(highest, number);
        }

        return VersionNaming.NameFor(highest + 1);
    }

    /// <summary>
    /// The two sides of the comparison for <paramref name="selectedNumber"/>.
    /// </summary>
    /// <param name="selectedNumber">The chosen version, 0 meaning the working file.</param>
    /// <remarks>
    /// The newer side is the selection. The older side is the highest-numbered
    /// release below it, so the working file compares against the newest release -
    /// which is the comparison the Angular app opens with. With nothing below it,
    /// both sides are the same version: a release with no predecessor is shown
    /// against itself rather than against an empty pane.
    /// </remarks>
    public static (string Newer, string Older) Comparison(
        int selectedNumber, IReadOnlyList<ProjectVersion> versions)
    {
        string newer = selectedNumber == 0
            ? VersionNaming.WorkingFileName
            : VersionNaming.NameFor(selectedNumber);

        string older = null;
        int best = 0;

        for (int i = 0; i < (versions?.Count ?? 0); i++) {
            ProjectVersion version = versions[i];
            bool below = selectedNumber == 0 || version.Number < selectedNumber;

            if (below && version.Number > best) {
                best = version.Number;
                older = version.Name;
            }
        }

        return (newer, older ?? newer);
    }
}
