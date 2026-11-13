using System.Text.RegularExpressions;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// Finds the source tree these tests are about.
/// </summary>
/// <remarks>
/// The tests read the repository rather than only the compiled output, because
/// several of the rules they enforce are about JavaScript, project files and
/// comments - none of which survive into an assembly.
/// </remarks>
internal static class Repo
{
    /// <summary>The <c>map-viewer</c> directory, found by walking up from the test binaries.</summary>
    public static DirectoryInfo Root { get; } = Find();

    public static string Src => Path.Combine(Root.FullName, "src");

    public static string Tests => Path.Combine(Root.FullName, "tests");

    /// <summary>The app's JavaScript, which is a first-class part of the architecture here.</summary>
    public static string Scripts =>
        Path.Combine(Src, "OcadViewer.Wasm", "wwwroot", "js");

    private static DirectoryInfo Find()
    {
        DirectoryInfo directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MapViewer.slnx")))
            directory = directory.Parent;

        return directory
            ?? throw new DirectoryNotFoundException(
                $"No MapViewer.slnx above {AppContext.BaseDirectory}; these tests need the source tree.");
    }

    /// <summary>
    /// Every project file in the solution, vendored wrappers included.
    /// </summary>
    public static IReadOnlyList<FileInfo> ProjectFiles()
        => [.. Directory.EnumerateFiles(Root.FullName, "*.csproj", SearchOption.AllDirectories)
                        .Where(NotBuildOutput)
                        .Select(path => new FileInfo(path))
                        .OrderBy(file => file.Name, StringComparer.Ordinal)];

    /// <summary>
    /// Our own source files. The vendored PurplePen tree is excluded: it is not
    /// ours to hold to these rules, and it is 100× the size of what is.
    /// </summary>
    public static IReadOnlyList<SourceFile> OurSources(params string[] extensions)
    {
        IEnumerable<string> paths = extensions
            .SelectMany(extension => Directory.EnumerateFiles(
                Src, $"*{extension}", SearchOption.AllDirectories))
            .Where(NotBuildOutput)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}vendor{Path.DirectorySeparatorChar}",
                                          StringComparison.OrdinalIgnoreCase))
            // Third-party scripts we serve but did not write.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}",
                                          StringComparison.OrdinalIgnoreCase));

        return [.. paths.Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(path => new SourceFile(
                            Path.GetFileName(path),
                            Path.GetRelativePath(Root.FullName, path),
                            File.ReadAllText(path)))
                        .OrderBy(file => file.RelativePath, StringComparer.Ordinal)];
    }

    private static bool NotBuildOutput(string path)
        => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One file, with its text, as the rules see it.</summary>
internal sealed record SourceFile(string Name, string RelativePath, string Text)
{
    private string code;

    /// <summary>
    /// The file with its comments removed.
    /// </summary>
    /// <remarks>
    /// Every rule in this suite is about code. Matching the raw text instead would
    /// make a file fail for explaining, in a comment, the very thing it no longer
    /// does - which is exactly what the comments here are for.
    /// </remarks>
    public string Code => code ??= StripComments(Text);

    public bool Mentions(string fragment)
        => Code.Contains(fragment, StringComparison.Ordinal);

    public override string ToString() => RelativePath;

    private static string StripComments(string text)
    {
        text = Regex.Replace(text, @"@\*.*?\*@", " ", RegexOptions.Singleline);   // Razor
        text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);  // HTML
        text = Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline);   // block

        // Not preceded by a colon, so that a URL inside a string literal survives.
        return Regex.Replace(text, @"(?<!:)//[^\n]*", " ");
    }
}
