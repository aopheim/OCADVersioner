using System.Text.Json;

using Microsoft.Playwright;

using OcadViewer.Versioning;

namespace OcadViewer.E2ETests;

/// <summary>
/// A project folder in the browser's Origin Private File System, for the
/// workspace to open as if the user had picked it.
/// </summary>
/// <remarks>
/// <para>
/// Laid out with <see cref="FolderLayout"/>, the same rules the app reads with,
/// so a fixture cannot drift from what a real project looks like.
/// </para>
/// <para>
/// OPFS belongs to an origin, so the page must already be on the app before a
/// project is created. Each test's browser context starts with an empty one.
/// </para>
/// </remarks>
public sealed class OpfsProject
{
    private readonly IPage page;

    public string Name { get; }

    private OpfsProject(IPage page, string name)
    {
        this.page = page;
        Name = name;
    }

    /// <summary>
    /// Replaces the native folder picker in every page of the context with one
    /// that returns the project last passed to <see cref="PickNextAsync"/>.
    /// </summary>
    /// <remarks>
    /// Before the first navigation; it applies to pages loaded after it. The same
    /// script defines the helpers this class writes files through.
    /// </remarks>
    public static async Task InstallPickerAsync(IBrowserContext context)
        => await context.AddInitScriptAsync(Repo.Script("opfsPicker.js"));

    /// <summary>Creates an empty project folder.</summary>
    public static async Task<OpfsProject> CreateAsync(IPage page, string name)
    {
        await page.EvaluateAsync("async name => { await window.__e2eOpfs.folder(name, '', true); }", name);

        return new OpfsProject(page, name);
    }

    /// <summary>Makes this the folder the picker returns next.</summary>
    public async Task PickNextAsync()
        => await page.EvaluateAsync("name => window.__e2eOpfs.pickNext(name)", Name);

    /// <summary>Puts a working file in the project root, copied from a test map.</summary>
    public async Task AddWorkingFileAsync(string fileName, string testMap)
        => await WriteAsync(fileName, await File.ReadAllBytesAsync(Repo.TestMap(testMap)));

    /// <summary>Adds a release, with its map copied from a test map and its metadata.</summary>
    /// <returns>The release's name, e.g. "V1".</returns>
    public async Task<string> AddReleaseAsync(int number, string testMap, string title, string description = "")
    {
        string name = VersionNaming.NameFor(number);
        NewVersionPlan plan = FolderLayout.PlanNewVersion(name);

        VersionMetadata metadata = new() {
            Title = title,
            Description = description,
            VersionCreatedAt = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero),
            VersionNumber = number,
        };

        await WriteAsync(plan.MapFile, await File.ReadAllBytesAsync(Repo.TestMap(testMap)));
        await WriteAsync(plan.MetadataFile,
                         System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, AngularJson.Options)));
        return name;
    }

    private async Task WriteAsync(string path, byte[] bytes)
        => await page.EvaluateAsync(
            "({ project, path, data }) => window.__e2eOpfs.write(project, path, data)",
            new { project = Name, path, data = Convert.ToBase64String(bytes) });
}
