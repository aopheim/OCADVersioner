using System.Text.Json;

using Microsoft.JSInterop;

using OcadViewer.Versioning;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// Opens a project folder and creates versions in it.
/// </summary>
/// <remarks>
/// A client for <c>projectFolder.js</c>, and nothing more. What a release is
/// called, which folders count as releases and which version compares against
/// which all live in <see cref="OcadViewer.Versioning"/>, where they can be tested
/// without a browser. This type used to carry those rules, the on-disk metadata
/// shape and the JavaScript plumbing at once - three reasons to change in one
/// file, two of which could not be exercised by a test.
///
/// Note what is absent: a method to read map bytes. Map data goes straight from
/// the file to the renderer inside JavaScript and never passes through here. See
/// <c>mapWorkspace.js</c>.
///
/// Scoped rather than transient: the underlying directory handle lives in the JS
/// module for as long as the page does, and a component being disposed must not
/// take the user's folder permission with it.
/// </remarks>
public sealed class ProjectService(IJSRuntime js) : IAsyncDisposable
{
    private readonly IJSRuntime js = js;
    private IJSObjectReference module;

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
    public async Task<ProjectInfo> OpenAsync() => await ScanAsync("openProject");

    /// <summary>Re-reads the folder, picking up changes made outside the app.</summary>
    public async Task<ProjectInfo> RefreshAsync() => await ScanAsync("rescan");

    /// <summary>Chooses the working file when the root holds several <c>.ocd</c> files.</summary>
    public async Task SelectCurrentFileAsync(string fileName)
    {
        await (await ModuleAsync()).InvokeVoidAsync("selectCurrentFile", fileName);
        await RefreshAsync();
    }

    /// <summary>The name the next version will get.</summary>
    public string NextVersionName() => ProjectFolder.NextVersionName(Versions);

    /// <summary>
    /// Which two versions a selection compares, newer first.
    /// </summary>
    /// <param name="selectedNumber">0 for the working file.</param>
    public (string Newer, string Older) ComparisonFor(int selectedNumber)
        => ProjectFolder.Comparison(selectedNumber, Versions);

    /// <summary>
    /// Whether this version has to be re-read every time it is shown.
    /// </summary>
    public static bool IsVolatile(string versionName) => VersionNaming.IsVolatile(versionName);

    /// <summary>
    /// Copies the working file into a new release folder and writes its metadata.
    /// </summary>
    /// <remarks>
    /// The symbol counts are written as zero until the C# diff exists. The Angular
    /// app fills them in from its GeoJSON diff; a version created here will show
    /// zeros in both apps' version lists until that is ported.
    /// </remarks>
    public async Task<ProjectInfo> CreateVersionAsync(string title, string description)
    {
        if (!IsOpen)
            throw new InvalidOperationException("No project folder is open.");
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

        return await ScanAsync("createVersion", name,
                               JsonSerializer.Serialize(metadata, AngularJson.Options));
    }

    /// <summary>
    /// Runs a JS call that answers with a folder listing, and interprets it.
    /// </summary>
    private async Task<ProjectInfo> ScanAsync(string method, params object[] arguments)
    {
        var module = await ModuleAsync();
        RawProject raw = await module.InvokeAsync<RawProject>(method, arguments);

        Project = new ProjectInfo(
            raw.ProjectName,
            raw.CurrentFileName,
            raw.OcdCandidates ?? [],
            ProjectFolder.ReleasesIn(raw.ReleaseFolders ?? []));

        return Project;
    }

    /// <summary>
    /// What the JS module reports: names and bytes-on-disk facts, no judgements.
    /// </summary>
    private sealed record RawProject(
        string ProjectName,
        string CurrentFileName,
        List<string> OcdCandidates,
        List<ReleaseFolder> ReleaseFolders);

    public async ValueTask DisposeAsync() => await module.ReleaseAsync();
}
