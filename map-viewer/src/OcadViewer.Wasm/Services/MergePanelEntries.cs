using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Wasm.Services;

/// <summary>The side panel's tabs.</summary>
public enum MergeTab { Conflicts, Regions, A, B, SymbolsAndColours }

/// <summary>One row in the side panel: an item or an overlap group.</summary>
/// <param name="Source">The <see cref="MergeItem"/> or <see cref="OverlapGroup"/> it stands for.</param>
/// <param name="Side">"a", "b", "both" or "conflict", for its colour.</param>
/// <param name="Open">A conflict still waiting for a decision.</param>
public sealed record PanelEntry(string Key, string Title, string Detail, string Side, bool Open, object Source);

/// <summary>What each tab of the side panel lists, and how each row reads.</summary>
public static class MergePanelEntries
{
    public static IReadOnlyList<PanelEntry> For(MergeTab tab, MergeSession session, string labelA, string labelB, Localiser l)
    {
        MergeAnalysis analysis = session.Analysis;
        MergeResolution resolution = session.Resolution;

        return tab switch {
            MergeTab.Conflicts => [
                .. analysis.AllItems.Where(i => i.Outcome == Outcome.Conflict)
                                    .Select(i => Entry(i, "conflict", !resolution.IsResolved(i))),
                .. analysis.Overlaps.Select(g => new PanelEntry(
                    g.Key, l["#_mergeOverlap"], l.Format("#_mergeOverlapCount", ("count", g.Items.Count)),
                    "conflict", !resolution.IsResolved(g), g)),
            ],
            // Regions are the page's, not the analysis's: the tab shows its own content.
            MergeTab.Regions => [],
            MergeTab.A => [.. analysis.Objects.Where(i => i.Outcome is Outcome.OnlyA or Outcome.BothSame)
                                              .Select(i => Entry(i, i.Outcome == Outcome.BothSame ? "both" : "a", false))],
            MergeTab.B => [.. analysis.Objects.Where(i => i.Outcome == Outcome.OnlyB).Select(i => Entry(i, "b", false))],
            _ => [.. analysis.Symbols.Cast<MergeItem>().Concat(analysis.Settings)
                             .Where(i => i.Outcome != Outcome.Conflict)
                             .Select(i => Entry(i, i.Outcome == Outcome.OnlyB ? "b" : i.Outcome == Outcome.OnlyA ? "a" : "both", false))],
        };

        PanelEntry Entry(MergeItem item, string side, bool open)
            => new(item.Key, MergeText.Title(analysis, item, l), Detail(item), side, open, item);

        string Detail(MergeItem item)
        {
            if (item.Outcome == Outcome.Conflict)
                return MergeText.Conflict(item.Conflict, l);
            if (item.Outcome == Outcome.BothSame)
                return l["#_mergeSameChange"];
            return string.Join(" · ", new[] {
                Said(labelA, item.ChangeA), Said(labelB, item.ChangeB)
            }.Where(s => s is not null));
        }

        string Said(string label, ChangeKind change)
            => MergeText.Change(change, l) is string word ? $"{label}: {word}" : null;
    }

    public static int Count(MergeTab tab, MergeSession session, string labelA, string labelB, Localiser l)
        => For(tab, session, labelA, labelB, l).Count;
}
