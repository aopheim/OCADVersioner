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
    /// <remarks>Before the first navigation; it applies to pages loaded after it.</remarks>
    public static async Task InstallPickerAsync(IBrowserContext context)
        => await context.AddInitScriptAsync(Repo.Script("opfsPicker.js"));

    /// <summary>Creates an empty project folder.</summary>
    public static async Task<OpfsProject> CreateAsync(IPage page, string name)
    {
        await page.EvaluateAsync("""
            async name => {
                const root = await navigator.storage.getDirectory();
                const projects = await root.getDirectoryHandle('e2e-projects', { create: true });
                await projects.getDirectoryHandle(name, { create: true });
            }
            """, name);

        return new OpfsProject(page, name);
    }

    /// <summary>Makes this the folder the picker returns next.</summary>
    public async Task PickNextAsync()
        => await page.EvaluateAsync("name => localStorage.setItem('e2e.project', name)", Name);

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

    /// <summary>A file's bytes, or null if it does not exist.</summary>
    public async Task<byte[]> ReadAsync(string path)
    {
        string base64 = await page.EvaluateAsync<string>("""
            async ({ project, path }) => {
                try {
                    let dir = await (await navigator.storage.getDirectory()).getDirectoryHandle('e2e-projects');
                    dir = await dir.getDirectoryHandle(project);
                    const parts = path.split('/');
                    const name = parts.pop();
                    for (const part of parts) dir = await dir.getDirectoryHandle(part);
                    const bytes = new Uint8Array(await (await (await dir.getFileHandle(name)).getFile()).arrayBuffer());
                    let binary = '';
                    for (let i = 0; i < bytes.length; i += 0x8000)
                        binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
                    return btoa(binary);
                } catch (e) {
                    if (e.name === 'NotFoundError') return null;
                    throw e;
                }
            }
            """, new { project = Name, path });

        return base64 is null ? null : Convert.FromBase64String(base64);
    }

    private async Task WriteAsync(string path, byte[] bytes)
        => await page.EvaluateAsync("""
            async ({ project, path, data }) => {
                let dir = await (await navigator.storage.getDirectory()).getDirectoryHandle('e2e-projects');
                dir = await dir.getDirectoryHandle(project);
                const parts = path.split('/');
                const name = parts.pop();
                for (const part of parts) dir = await dir.getDirectoryHandle(part, { create: true });
                const file = await dir.getFileHandle(name, { create: true });
                const writable = await file.createWritable();
                await writable.write(Uint8Array.from(atob(data), c => c.charCodeAt(0)));
                await writable.close();
            }
            """, new { project = Name, path, data = Convert.ToBase64String(bytes) });
}
