namespace OcadViewer.Versioning;

/// <summary>
/// What a release folder is called, and what the working file is called.
/// </summary>
/// <remarks>
/// The single place that knows the <c>V&lt;n&gt;</c> rule. It used to be stated
/// three times - in the JavaScript that scans the folder, in the service that
/// names the next version, and again in the one that parsed a number back out -
/// across two languages. The folder layout is a contract with the Angular app, so
/// a rule stated in several places is a rule that eventually disagrees with
/// itself.
///
/// The JavaScript now reports folder names and nothing else; deciding which of
/// them is a release happens here.
/// </remarks>
public static class VersionNaming
{
    /// <summary>
    /// What the Angular app calls the working file, as opposed to a release.
    /// </summary>
    public const string WorkingFileName = "current";

    /// <summary>The folder holding the releases, directly under the project root.</summary>
    public const string ReleasesFolder = "Releases";

    /// <summary>The folder and file name for release <paramref name="number"/>.</summary>
    public static string NameFor(int number)
        => number >= 1
            ? $"V{number}"
            : throw new ArgumentOutOfRangeException(nameof(number), number,
                                                    "Version numbers start at 1.");

    /// <summary>
    /// Reads the number out of a release name.
    /// </summary>
    /// <returns><c>true</c> when <paramref name="name"/> is <c>V</c> followed by a
    /// positive integer and nothing else.</returns>
    public static bool TryParse(string name, out int number)
    {
        number = 0;

        if (string.IsNullOrEmpty(name) || name[0] != 'V' || name.Length < 2)
            return false;

        // int.TryParse on its own would accept "V+1", "V 1" and "V1  ". A release
        // folder is an exact name, so anything but digits is somebody else's folder.
        for (int i = 1; i < name.Length; i++) {
            if (!char.IsAsciiDigit(name[i]))
                return false;
        }

        return int.TryParse(name.AsSpan(1), out number) && number >= 1;
    }

    /// <summary>Whether <paramref name="name"/> names a release folder.</summary>
    public static bool IsReleaseName(string name) => TryParse(name, out _);

    /// <summary>
    /// Whether <paramref name="versionName"/> can change under the app's feet.
    /// </summary>
    /// <remarks>
    /// The working file is whatever is on disk at this instant - the user is
    /// editing it in OCAD while this app is open - so it must be re-read every
    /// time it is shown. A release is immutable once written, so it can be parsed
    /// once and kept.
    /// </remarks>
    public static bool IsVolatile(string versionName)
        => string.Equals(versionName, WorkingFileName, StringComparison.Ordinal);
}
