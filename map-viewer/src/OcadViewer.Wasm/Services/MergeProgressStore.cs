using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Wasm.Services;

/// <summary>A region as it is kept between visits.</summary>
public sealed record SavedRegion(string Id, string Name, double[][] Ring, Side? Suggested);

/// <summary>
/// An unfinished merge as it is kept in the browser: which files, what is
/// decided, and the regions drawn. Never the files themselves.
/// </summary>
/// <param name="Key">The three files' hashes: the same files give the same key.</param>
public sealed record MergeProgress(
    string Key, DateTimeOffset SavedAt,
    string BaseName, string LabelA, string LabelB,
    int Decided, int Total,
    MergeDecisionsSnapshot Decisions,
    IReadOnlyList<SavedRegion> Regions)
{
    public static string KeyFor(MergeStart start) => KeyFor(start.Base, start.A, start.B);

    public static string KeyFor(MergeInput @base, MergeInput a, MergeInput b) => $"{@base.Hash}|{a.Hash}|{b.Hash}";

    public static MergeProgress Of(MergeStart start, MergeSession session, IEnumerable<MergeRegion> regions)
    {
        int total = session.Analysis.AllItems.Count(i => i.Outcome == Outcome.Conflict) + session.Analysis.Overlaps.Count;
        return new MergeProgress(
            KeyFor(start), DateTimeOffset.Now, start.Base.FileName, start.LabelA, start.LabelB,
            total - session.Resolution.Unresolved.Count, total,
            session.Decisions.ToSnapshot(),
            [.. regions.Select(r => new SavedRegion(r.Id, r.Name, [.. r.Region.Ring.Select(p => new[] { p.X, p.Y })], r.Suggested))]);
    }

    public IEnumerable<MergeRegion> RestoreRegions()
        => Regions.Select(r => new MergeRegion(r.Id, r.Name, new(r.Ring.Select(p => (p[0], p[1])).ToArray()), r.Suggested));
}

/// <summary>
/// Unfinished merges in this browser (mergeStore.js), keyed by the files' hashes.
/// </summary>
/// <remarks>
/// A convenience and nothing more: storage can be refused (a private window,
/// cleared site data), so every failure here is swallowed and the merge carries on.
/// </remarks>
public sealed class MergeProgressStore(IJSRuntime js) : IAsyncDisposable
{
    private readonly IJSRuntime js = js;
    private IJSObjectReference module;

    public async Task SaveAsync(MergeProgress progress)
        => await Try(async m => {
            await m.InvokeVoidAsync("save", JsonSerializer.Serialize(progress, MergeProgressJson.Default.MergeProgress));
            return true;
        });

    public async Task<MergeProgress> LoadAsync(string key)
        => await Try(async m => await m.InvokeAsync<string>("load", key) is string json
            ? JsonSerializer.Deserialize(json, MergeProgressJson.Default.MergeProgress)
            : null);

    public async Task<IReadOnlyList<MergeProgress>> ListAsync()
        => await Try(async m => JsonSerializer.Deserialize(await m.InvokeAsync<string>("list"), MergeProgressJson.Default.MergeProgressArray)
                                ?.OrderByDescending(p => p.SavedAt).ToArray())
           ?? [];

    public async Task RemoveAsync(string key)
        => await Try(async m => {
            await m.InvokeVoidAsync("remove", key);
            return true;
        });

    private async Task<T> Try<T>(Func<IJSObjectReference, Task<T>> work)
    {
        try {
            module ??= await js.ImportAsync("./js/mergeStore.js");
            return await work(module);
        }
        catch (JSException) {
            return default;
        }
        catch (JsonException) {
            return default;
        }
    }

    public async ValueTask DisposeAsync() => await module.ReleaseAsync();
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             UseStringEnumConverter = true)]
[JsonSerializable(typeof(MergeProgress))]
[JsonSerializable(typeof(MergeProgress[]))]
internal sealed partial class MergeProgressJson : JsonSerializerContext
{
}
