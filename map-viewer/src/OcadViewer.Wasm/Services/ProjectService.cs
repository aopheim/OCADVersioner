using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.JSInterop;

namespace OcadViewer.Wasm.Services;

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

    /// <summary>When the version was created. Serialised as ISO-8601 UTC.</summary>
    public DateTimeOffset VersionCreatedAt { get; set; }

    public int VersionNumber { get; set; }
    public int NumberOfAddedSymbols { get; set; }
    public int NumberOfEditedSymbols { get; set; }
    public int NumberOfDeletedSymbols { get; set; }
}

/// <summary>One release found in the project folder.</summary>
public sealed record ProjectVersion(string Name, int Number, VersionMetadata Metadata);

/// <summary>What the project folder contains.</summary>
/// <param name="OcdCandidates">
/// Every <c>.ocd</c> directly in the project root. More than one means the user has
/// to say which is the working file.
/// </param>
public sealed record ProjectInfo(
    string ProjectName,
    string CurrentFileName,
    IReadOnlyList<string> OcdCandidates,
    IReadOnlyList<ProjectVersion> Versions);

/// <summary>
/// Opens a project folder and reads or creates versions in it.
/// </summary>
/// <remarks>
/// Scoped rather than transient: the underlying directory handle lives in the JS
/// module for as long as the page does, and a component being disposed must not take
/// the user's folder permission with it.
/// </remarks>
public sealed class ProjectService(IJSRuntime js) : IAsyncDisposable
{
    private readonly IJSRuntime js = js;
    private IJSObjectReference module;

    /// <summary>JSON exactly as the Angular app writes it: camelCase, nulls omitted.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The version name the Angular app uses for the working file.</summary>
    public const string CurrentVersionName = "current";

    public ProjectInfo Project { get; private set; }

    public bool IsOpen => Project is not null;

    private async Task<IJSObjectReference> ModuleAsync()
        => module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/projectFolder.js");

    /// <summary>Whether this browser has the File System Access API at all.</summary>
    public async Task<bool> IsSupportedAsync()
        => await (await ModuleAsync()).InvokeAsync<bool>("isSupported");

    /// <summary>
    /// Prompts for a project folder. Must be called from a user gesture.
    /// </summary>
    public async Task<ProjectInfo> OpenAsync()
    {
        var module = await ModuleAsync();
        Project = Normalise(await module.InvokeAsync<RawProject>("openProject"));
        return Project;
    }

    /// <summary>Re-reads the folder, picking up changes made outside the app.</summary>
    public async Task<ProjectInfo> RefreshAsync()
    {
        var module = await ModuleAsync();
        Project = Normalise(await module.InvokeAsync<RawProject>("rescan"));
        return Project;
    }

    /// <summary>Chooses the working file when the root holds several <c>.ocd</c> files.</summary>
    public async Task SelectCurrentFileAsync(string fileName)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("selectCurrentFile", fileName);
        await RefreshAsync();
    }

    /// <summary>Reads a version's map bytes. Use <see cref="CurrentVersionName"/> for the working file.</summary>
    public async Task<byte[]> ReadMapAsync(string versionName)
        => await (await ModuleAsync()).InvokeAsync<byte[]>("readMap", versionName);

    /// <summary>
    /// The name the next version should get.
    /// </summary>
    /// <remarks>
    /// Highest existing number plus one. The Angular app counts folders instead,
    /// which gives the same answer until someone deletes a release by hand - after
    /// which counting would reuse a number and overwrite an existing version.
    /// </remarks>
    public string NextVersionName()
    {
        int highest = Project?.Versions.Count > 0 ? Project.Versions.Max(v => v.Number) : 0;
        return $"V{highest + 1}";
    }

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
        var metadata = new VersionMetadata {
            Title = string.IsNullOrWhiteSpace(title) ? null : title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description,
            VersionCreatedAt = DateTimeOffset.UtcNow,
            VersionNumber = int.Parse(name[1..]),
            NumberOfAddedSymbols = 0,
            NumberOfEditedSymbols = 0,
            NumberOfDeletedSymbols = 0
        };

        var module = await ModuleAsync();
        Project = Normalise(await module.InvokeAsync<RawProject>(
            "createVersion", name, JsonSerializer.Serialize(metadata, JsonOptions)));
        return Project;
    }

    /* The JS module returns plain objects; these mirror them for deserialisation. */

    private sealed record RawProject(
        string ProjectName, string CurrentFileName,
        List<string> OcdCandidates, List<RawVersion> Versions);

    private sealed record RawVersion(string Name, int Number, VersionMetadata Metadata);

    private static ProjectInfo Normalise(RawProject raw) => new(
        raw.ProjectName,
        raw.CurrentFileName,
        raw.OcdCandidates ?? [],
        (raw.Versions ?? []).Select(v => new ProjectVersion(
            v.Name, v.Number, v.Metadata ?? new VersionMetadata { VersionNumber = v.Number })).ToList());

    public async ValueTask DisposeAsync()
    {
        try {
            if (module is not null)
                await module.DisposeAsync();
        }
        catch (JSDisconnectedException) {
        }
        catch (ObjectDisposedException) {
        }
    }
}
