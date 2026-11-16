using System.Text.Json;
using System.Text.Json.Serialization;
using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Regions;

namespace OcadViewer.Wasm.Services;

/// <summary>Geoman's names for the shapes a region can be drawn as.</summary>
public static class RegionShapes
{
    public const string Rectangle = "Rectangle";
    public const string Polygon = "Polygon";
}

/// <summary>A region on the merge screen: the engine's polygon plus what the screen calls it.</summary>
/// <param name="Suggested">The side a suggestion proposed, or null.</param>
public sealed record MergeRegion(string Id, string Name, Region Region, Side? Suggested);

/// <summary>A region as regionTools.js reports and receives it: paper-millimetre ring.</summary>
public sealed record RegionShape(string Id, double[][] Ring, string Label = null, string Side = null);

/// <summary>Regions between the engine, the screen and regionTools.js.</summary>
public static class MergeRegions
{
    private static readonly Choice[] Choices = [Choice.A, Choice.B, Choice.Both, Choice.Base];

    /// <summary>What a region's choice currently is: the one every object inside agrees with, if any.</summary>
    public static Choice? Current(MergeSession session, MergeRegion region)
    {
        IReadOnlyList<ObjectItem> inside = RegionDecision.Contents(session.Analysis, region.Region).Inside;
        if (inside.Count == 0)
            return null;
        return Choices.Cast<Choice?>().FirstOrDefault(choice => inside.All(item =>
            session.Decisions.Items.TryGetValue(item.Key, out Choice decided) && decided == GroupChoice.For(choice.Value, item)));
    }

    /// <summary>The regions the user's drawing left, keeping names and suggestions of those that survived.</summary>
    public static List<MergeRegion> FromShapes(string json, IReadOnlyList<MergeRegion> before)
        => [.. JsonSerializer.Deserialize(json, MergeRegionJson.Default.RegionShapeArray)
                .Where(s => s.Ring.Length >= 3)
                .Select(s => {
                    MergeRegion old = before.FirstOrDefault(r => r.Id == s.Id);
                    return new MergeRegion(s.Id, old?.Name ?? s.Id.ToUpperInvariant(),
                                           new Region([.. s.Ring.Select(p => (p[0], p[1]))]), old?.Suggested);
                })];

    /// <summary>Labels and colours for regionTools.js; <paramref name="withRings"/> also creates regions it lacks.</summary>
    public static string Describe(IEnumerable<MergeRegion> regions, Func<MergeRegion, string> label, bool withRings)
        => JsonSerializer.Serialize(
            regions.Select(r => new RegionShape(
                r.Id,
                withRings ? [.. r.Region.Ring.Select(p => new[] { p.X, p.Y })] : null,
                label(r),
                r.Suggested switch { Side.A => "a", Side.B => "b", _ => null })).ToArray(),
            MergeRegionJson.Default.RegionShapeArray);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RegionShape[]))]
internal sealed partial class MergeRegionJson : JsonSerializerContext
{
}
