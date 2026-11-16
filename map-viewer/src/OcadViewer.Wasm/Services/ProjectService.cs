using System.Text.Json;

using Microsoft.JSInterop;

using OcadViewer.Versioning;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// Opens a project folder, reads what is in it and creates versions in it.
/// </summary>
/// <remarks>
/// <c>projectFolder.js</c> does the disk access, and only that: it lists
/// folders, reads and writes files at the paths it is given. What those paths
/// are is <see cref="FolderLayout"/>; which file is the working file, which
/// folders are releases and what the next one is called is
/// <see cref="ProjectFolder"/>. Both are tested without a browser.
///
/// Map bytes do not pass through here. The workspace reads them in JavaScript
/// and hands them straight to the renderer; this only says where they are, via
/// <see cref="SourceFor"/>.
///
/// Scoped rather than transient: the directory handle lives in the JS module for
/// as long as the page does, and a component being disposed must not take the
/// user's folder permission with it.
/// </remarks>
public sealed class ProjectService(IJSRuntime js) : IAsyncDisposable
{
    private readonly IJSRuntime js = js;
    private IJSObjectReference module;

    /// <summary>The working file the user picked when the root holds several.</summary>
    private string chosenFileName;

    /// <summary>
    /// Every folder the last scan found under <c>Releases/</c>, release or not,
    /// because an incomplete <c>V&lt;n&gt;</c> folder still takes its name.
    /// </summary>
    private IReadOnlyList<ReleaseFolder> releaseFolders = [];

    public ProjectInfo Project { get; private set; }

    public bool IsOpen => Project is not null;

    private IReadOnlyList<ProjectVersion> Versions => Project?.Versions ?? [];

    private async Task<IJSObjectReference> ModuleAsync()
        => module ??= await js.ImportAsync("./js/projectFolder.js");

    /// <summary>Whether this browser has the File System Access API at all.</summary>
    public async Task<bool> IsSupportedAsync()
        => await (await ModuleAsync()).InvokeAsync<bool>("isSupported");

    /// <summary>
    /// Prompts for a project folder. Must be called from a user gesture.
    /// </summary>
    public async Task<ProjectInfo> OpenAsync()
    {
        string projectName = await (await ModuleAsync()).InvokeAsync<string>("openProject");
        chosenFileName = null;
        return await ScanAsync(projectName);
    }

    /// <summary>Re-reads the folder, picking up changes made outside the app.</summary>
    public async Task<ProjectInfo> RefreshAsync()
    {
        if (!IsOpen)
            throw new InvalidOperationException("No project folder is open.");

        return await ScanAsync(Project.ProjectName);
    }

    /// <summary>Chooses the working file when the root holds several <c>.ocd</c> files.</summary>
    public async Task SelectCurrentFileAsync(string fileName)
    {
        chosenFileName = fileName;
        await RefreshAsync();
    }

    /// <summary>The name the next version will get.</summary>
    public string NextVersionName() => ProjectFolder.NextVersionName(releaseFolders);

    /// <summary>
    /// Which two versions a selection compares, newer first.
    /// </summary>
    /// <param name="selectedNumber">0 for the working file.</param>
    public (string Newer, string Older) ComparisonFor(int selectedNumber)
        => ProjectFolder.Comparison(selectedNumber, Versions);

    /// <summary>Two versions picked in any order, as a comparison with the newer first.</summary>
    public static (string Newer, string Older) Ordered(string first, string second)
        => ProjectFolder.Ordered(first, second);

    /// <summary>Whether a version is still in the folder.</summary>
    public bool Contains(string versionName) => ProjectFolder.Contains(Versions, versionName);

    /// <summary>Where a version's map is, and whether to read it again each time.</summary>
    public MapSource SourceFor(string versionName)
        => FolderLayout.SourceFor(versionName, Project?.CurrentFileName);

    /// <summary>
    /// Whether this version has to be re-read every time it is shown.
    /// </summary>
    public static bool IsVolatile(string versionName) => VersionNaming.IsVolatile(versionName);

    /// <summary>
    /// Copies the working file into a new release folder and writes its metadata.
    /// </summary>
    /// <remarks>
    /// The symbol counts are still written as zero: the C# diff exists, but filling
    /// them needs the working file and the newest release both parsed here (see
    /// DIFF.md). The Angular app fills them in from its GeoJSON diff.
    ///
    /// The folder is re-read first. The Angular app writes to the same folders,
    /// so a name worked out from the last scan may already be taken - and
    /// creating the release folder fails if it exists, rather than writing into
    /// somebody else's.
    /// </remarks>
    public async Task<ProjectInfo> CreateVersionAsync(string title, string description)
    {
        await RefreshAsync();

        if (string.IsNullOrEmpty(Project.CurrentFileName))
            throw new InvalidOperationException("No working .ocd file is selected.");

        string name = NextVersionName();
        VersionNaming.TryParse(name, out int number);

        var metadata = new VersionMetadata {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            VersionCreatedAt = DateTimeOffset.UtcNow,
            VersionNumber = number,
            NumberOfAddedSymbols = 0,
            NumberOfEditedSymbols = 0,
            NumberOfDeletedSymbols = 0
        };

        NewVersionPlan plan = FolderLayout.PlanNewVersion(name);
        var files = await ModuleAsync();

        await files.InvokeVoidAsync("createFolder", plan.Folder);
        await files.InvokeVoidAsync("copyFile", Project.CurrentFileName, plan.MapFile);
        await files.InvokeVoidAsync("writeText", plan.MetadataFile,
                                    JsonSerializer.Serialize(metadata, AngularJson.Options));

        return await RefreshAsync();
    }

    /// <summary>Lists the folder and interprets what is there.</summary>
    private async Task<ProjectInfo> ScanAsync(string projectName)
    {
        var files = await ModuleAsync();

        Listing root = await files.InvokeAsync<Listing>("listFolder", "", false);
        Listing releases = await files.InvokeAsync<Listing>("listFolder", VersionNaming.ReleasesFolder, true);

        IReadOnlyList<string> candidates = ProjectFolder.WorkingFileCandidates(root?.Files ?? []);

        FolderListing[] folders = [.. (releases?.Folders ?? [])
            .Select(folder => new FolderListing(folder.Name, folder.Files ?? []))];

        string[] metadataPaths = [.. folders.Select(ProjectFolder.MetadataPathIn)];
        string[] metadata = await files.InvokeAsync<string[]>("readTexts", [metadataPaths]);

        releaseFolders = [.. folders.Select((folder, i) => ProjectFolder.ReleaseFolderFrom(folder, metadata[i]))];

        Project = new ProjectInfo(
            projectName,
            ProjectFolder.ChooseWorkingFile(candidates, chosenFileName),
            candidates,
            ProjectFolder.ReleasesIn(releaseFolders));

        return Project;
    }

    /// <summary>What <c>listFolder</c> answers with: names only, no judgements.</summary>
    private sealed record Listing(List<string> Files, List<ListedFolder> Folders);

    private sealed record ListedFolder(string Name, List<string> Files);

    public async ValueTask DisposeAsync() => await module.ReleaseAsync();
}
