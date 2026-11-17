using System.Text.Json;
using System.Text.Json.Serialization;
using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Ocd;
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
            features.AddRange(For(item, resolution));

        return features;
    }

    public static string Json(MergeSession session)
        => JsonSerializer.Serialize(Features(session), MergeOverlayJson.Default.IReadOnlyListOverlayFeature);

    /// <summary>
    /// An open conflict: base's shape and both branches' versions. Otherwise each
    /// branch's change, accepted if the merged map holds it (as
    /// <see cref="MergeResolution.ResultOf{T}"/> says) and rejected if not.
    /// </summary>
    private static IEnumerable<OverlayFeature> For(ObjectItem item, MergeResolution resolution)
    {
        if (resolution.ChoiceFor(item) is null) {
            yield return Outline(item.Key, ChangeSide.Conflict, "conflict", item.Base ?? item.A ?? item.B, "open");
            foreach (Side side in new[] { Side.A, Side.B }) {
                if (item.Version(side) is OcadObject version)
                    yield return Outline(item.Key, ChangeSides.Of(side), Word(item.Change(side)), version, "open");
            }
            yield break;
        }

        IReadOnlyList<OcadObject> held = resolution.ResultOf(item);
        bool shared = item.Outcome == Outcome.BothSame;
        foreach (Side side in shared ? new[] { Side.A } : new[] { Side.A, Side.B }) {
            ChangeKind change = item.Change(side);
            if (change == ChangeKind.None)
                continue;

            bool kept = change == ChangeKind.Deleted
                ? !held.Contains(item.Base)
                : held.Contains(item.Version(side)) || (shared && held.Contains(item.B));
            yield return Outline(item.Key, shared ? ChangeSide.Both : ChangeSides.Of(side), Word(change),
                                 item.Version(side) ?? item.Base, kept ? "accepted" : "rejected");
        }
    }

    private static OverlayFeature Outline(string key, ChangeSide side, string change, OcadObject version, string state)
    {
        Shape shape = ObjectShape.Of(version);
        string kind = shape.Kind switch { ShapeKind.Area => "area", ShapeKind.Line => "line", _ => "point" };
        double[][][] parts = [.. shape.Parts.Select(part => part.Select(p => new[] { p.X, p.Y }).ToArray())];
        return new OverlayFeature(key, side.Name(), change, kind, parts, state);
    }

    private static OverlayFeature Box(OverlapGroup group)
    {
        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(group.Bounds);
        return new OverlayFeature(group.Key, ChangeSide.Conflict.Name(), "conflict", "box",
                                  [[[minX, minY], [maxX, maxY]]], "open");
    }

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
