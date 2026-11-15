using System.Text.Json;

namespace OcadViewer.Versioning;

/// <summary>
/// The rules of a project folder: which folders count, what the next release is
/// called, and which version is compared against which.
/// </summary>
/// <remarks>
/// Pure functions over what somebody else read off the disk. This used to be
/// spread between a Razor page (the comparison rule), a service (the next name)
/// and a JavaScript module (which folders count) - three places, none of which
/// could be tested without the other two.
/// </remarks>
public static class ProjectFolder
{
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
    /// What the next release should be called.
    /// </summary>
    /// <remarks>
    /// Highest existing number plus one. The Angular app counts folders instead,
    /// which gives the same answer until someone deletes a release by hand - after
    /// which counting reuses a number and overwrites an existing version.
    /// </remarks>
    public static string NextVersionName(IReadOnlyList<ProjectVersion> versions)
    {
        int highest = 0;
        for (int i = 0; i < (versions?.Count ?? 0); i++)
            highest = Math.Max(highest, versions[i].Number);

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

    /// <summary>
    /// Any two versions as a comparison, newer first, whichever order they came in.
    /// </summary>
    /// <remarks>
    /// The newer map always goes on the right, so a pair picked the wrong way round
    /// is swapped rather than refused. Newer means a higher version number; the
    /// working file is newer than every release. The same version twice is a
    /// comparison with no changes, which is allowed.
    /// </remarks>
    /// <exception cref="ArgumentException">Either name is neither a release nor the working file.</exception>
    public static (string Newer, string Older) Ordered(string first, string second)
        => Age(first) >= Age(second) ? (first, second) : (second, first);

    /// <summary>
    /// Whether <paramref name="versionName"/> is still in the folder: the working
    /// file always is, a release only while its folder is there.
    /// </summary>
    public static bool Contains(IReadOnlyList<ProjectVersion> versions, string versionName)
        => VersionNaming.IsVolatile(versionName) ||
           (versions ?? []).Any(v => string.Equals(v.Name, versionName, StringComparison.Ordinal));

    /// <summary>How late a version comes: its number, or past every number for the working file.</summary>
    private static int Age(string versionName)
        => VersionNaming.IsVolatile(versionName) ? int.MaxValue
         : VersionNaming.TryParse(versionName, out int number) ? number
         : throw new ArgumentException($"\"{versionName}\" is neither a release nor the working file.",
                                       nameof(versionName));
}
