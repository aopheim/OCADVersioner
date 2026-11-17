using OcadViewer.Merge;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Wasm.Services;

/// <summary>The side panel's tabs.</summary>
public enum MergeTab { Conflicts, Regions, A, B, SymbolsAndColours }

/// <summary>One row in the side panel: an item or an overlap group.</summary>
/// <param name="Source">The <see cref="MergeItem"/> or <see cref="OverlapGroup"/> it stands for.</param>
/// <param name="Side">Whose it is, for its colour.</param>
/// <param name="Open">A conflict still waiting for a decision.</param>
public sealed record PanelEntry(string Key, string Title, string Detail, ChangeSide Side, bool Open, object Source);

/// <summary>
/// Every tab's rows for one state of the decisions. Built once per
/// <see cref="MergeResolution"/>, so rendering, counting and finding a row cost
/// nothing more until the decisions change.
/// </summary>
public sealed class MergePanelListing
{
    private readonly IReadOnlyDictionary<MergeTab, IReadOnlyList<PanelEntry>> tabs;

    internal MergePanelListing(MergeResolution resolution, string language,
                               IReadOnlyDictionary<MergeTab, IReadOnlyList<PanelEntry>> tabs)
    {
        Resolution = resolution;
        Language = language;
        this.tabs = tabs;
    }

    /// <summary>The decisions the rows were built from.</summary>
    public MergeResolution Resolution { get; }

    /// <summary>The language the rows are written in.</summary>
    public string Language { get; }

    public IReadOnlyList<PanelEntry> this[MergeTab tab] => tabs[tab];

    /// <summary>The first tab listing <paramref name="key"/>, if any.</summary>
    public MergeTab? TabOf(string key)
        => tabs.Where(t => t.Value.Any(e => e.Key == key)).Select(t => (MergeTab?)t.Key).FirstOrDefault();
}

/// <summary>What each tab of the side panel lists, and how each row reads.</summary>
public static class MergePanelEntries
{
    /// <summary>
    /// A change both copies made is listed under both, since each mapper made it;
    /// the conflicts tab lists what needs a decision.
    /// </summary>
    public static MergePanelListing For(MergeSession session, string labelA, string labelB, Localiser l)
    {
        MergeAnalysis analysis = session.Analysis;
        MergeResolution resolution = session.Resolution;
        IReadOnlyDictionary<int, string> symbols = MergeText.SymbolNames(analysis);

        Dictionary<MergeTab, IReadOnlyList<PanelEntry>> tabs = new() {
            [MergeTab.Conflicts] = [
                .. analysis.AllItems.Where(resolution.NeedsDecision)
                                    .Select(i => Entry(i, ChangeSide.Conflict, !resolution.IsResolved(i))),
                .. analysis.Overlaps.Select(g => new PanelEntry(
                    g.Key, l["#_mergeOverlap"], l.Format("#_mergeOverlapCount", ("count", g.Items.Count)),
                    ChangeSide.Conflict, !resolution.IsResolved(g), g)),
            ],
            // Regions are the page's, not the analysis's: the tab shows its own content.
            [MergeTab.Regions] = [],
            [MergeTab.A] = [.. analysis.Objects.Where(i => i.Outcome is Outcome.OnlyA or Outcome.BothSame).Select(Clean)],
            [MergeTab.B] = [.. analysis.Objects.Where(i => i.Outcome is Outcome.OnlyB or Outcome.BothSame).Select(Clean)],
            [MergeTab.SymbolsAndColours] = [.. analysis.Symbols.Cast<MergeItem>().Concat(analysis.Settings)
                                                .Where(i => !resolution.NeedsDecision(i)).Select(Clean)],
        };
        return new MergePanelListing(resolution, l.Language, tabs);

        PanelEntry Clean(MergeItem item) => Entry(item, ChangeSides.Of(item), false);

        PanelEntry Entry(MergeItem item, ChangeSide side, bool open)
            => new(item.Key, MergeText.Title(symbols, item, l), Detail(item), side, open, item);

        string Detail(MergeItem item)
            => item is SymbolItem symbol && MergeText.SymbolUse(symbol, resolution, l) is string note
                ? $"{What(item)} · {note}"
                : What(item);

        string What(MergeItem item)
        {
            if (resolution.NeedsDecision(item))
                return MergeText.Conflict(resolution.ConflictOf(item), l);
            if (item.Outcome == Outcome.BothSame)
                return l["#_mergeSameChange"];
            return string.Join(" · ", new[] {
                Said(labelA, item.ChangeA), Said(labelB, item.ChangeB)
            }.Where(s => s is not null));
        }

        string Said(string label, ChangeKind change)
            => MergeText.Change(change, l) is string word ? $"{label}: {word}" : null;
    }
}
