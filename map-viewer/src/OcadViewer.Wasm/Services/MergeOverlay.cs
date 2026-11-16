using System.Text.Json;
using System.Text.Json.Serialization;
using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Format;
using OcadViewer.Merge.Geometry;

namespace OcadViewer.Wasm.Services;

/// <summary>One outline for mergeOverlay.js to draw.</summary>
/// <param name="Key">The item or overlap it belongs to; clicking it picks that.</param>
/// <param name="Side">"a", "b", "both" or "conflict": whose colour.</param>
/// <param name="Change">"added", "changed", "deleted" or "conflict": which stroke.</param>
/// <param name="Kind">"point", "line", "area" or "box".</param>
/// <param name="Parts">Paper-millimetre positions, [part][point][x, y].</param>
/// <param name="State">"open", "accepted" or "rejected".</param>
public sealed record OverlayFeature(string Key, string Side, string Change, string Kind, double[][][] Parts, string State);

/// <summary>
/// What the overlay shows for a merge: every change, coloured by whose it is,
/// stroked by what kind it is, and drawn as decided.
/// </summary>
/// <remarks>
/// The meaning of each outline is decided here, in C#, from the session; the
/// JavaScript only styles it. An open conflict is drawn in crimson over base's
/// shape with both branches' versions outlined beside it; once decided it is
/// drawn as the version chosen. A clean change the user rejected is drawn grey.
/// </remarks>
public static class MergeOverlay
{
    public static IReadOnlyList<OverlayFeature> Features(MergeSession session)
    {
        MergeResolution resolution = session.Resolution;
        List<OverlayFeature> features = [];

        foreach (OverlapGroup group in session.Analysis.Overlaps.Where(g => !resolution.IsResolved(g)))
            features.Add(Box(group));

        foreach (ObjectItem item in session.Analysis.Objects)
            features.AddRange(For(item, resolution.ChoiceFor(item)));

        return features;
    }

    public static string Json(MergeSession session)
        => JsonSerializer.Serialize(Features(session), MergeOverlayJson.Default.IReadOnlyListOverlayFeature);

    private static IEnumerable<OverlayFeature> For(ObjectItem item, Choice? choice)
    {
        if (choice is null) {
            yield return Outline(item.Key, "conflict", "conflict", item.Base ?? item.A ?? item.B, "open");
            foreach (Side side in new[] { Side.A, Side.B }) {
                if (item.Version(side) is OcadObject version)
                    yield return Outline(item.Key, Name(side), Word(item.Change(side)), version, "open");
            }
            yield break;
        }

        if (choice == Choice.Base) {
            // A rejected change, drawn where it would have been.
            Side own = item.ChangeA != ChangeKind.None ? Side.A : Side.B;
            yield return Outline(item.Key, Name(own), Word(item.Change(own)), item.Version(own) ?? item.Base, "rejected");
            yield break;
        }

        string sideName = item.Outcome == Outcome.BothSame ? "both" : null;
        foreach (Side side in choice == Choice.Both ? new[] { Side.A, Side.B } : new[] { choice == Choice.A ? Side.A : Side.B }) {
            if (item.Change(side) == ChangeKind.None)
                continue;
            yield return Outline(item.Key, sideName ?? Name(side), Word(item.Change(side)), item.Version(side) ?? item.Base, "accepted");
        }
    }

    private static OverlayFeature Outline(string key, string side, string change, OcadObject version, string state)
    {
        Shape shape = ObjectShape.Of(version);
        string kind = shape.Kind switch { ShapeKind.Area => "area", ShapeKind.Line => "line", _ => "point" };
        double[][][] parts = [.. shape.Parts.Select(part => part.Select(p => new[] { p.X, p.Y }).ToArray())];
        return new OverlayFeature(key, side, change, kind, parts, state);
    }

    private static OverlayFeature Box(OverlapGroup group)
    {
        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(group.Bounds);
        return new OverlayFeature(group.Key, "conflict", "conflict", "box",
                                  [[[minX, minY], [maxX, maxY]]], "open");
    }

    private static string Name(Side side) => side == Side.A ? "a" : "b";

    private static string Word(ChangeKind change) => change switch {
        ChangeKind.Added => "added",
        ChangeKind.Deleted => "deleted",
        _ => "changed",
    };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IReadOnlyList<OverlayFeature>))]
internal sealed partial class MergeOverlayJson : JsonSerializerContext
{
}
