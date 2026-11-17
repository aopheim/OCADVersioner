using OcadViewer.Ocd;

namespace OcadViewer.Merge.Analysis;

/// <summary>Everything the two branches changed, and where they clash.</summary>
public sealed class MergeAnalysis(
    OcadFile @base, OcadFile a, OcadFile b, MergeOptions options,
    IReadOnlyList<ObjectItem> objects, IReadOnlyList<SymbolItem> symbols,
    IReadOnlyList<SettingItem> settings, IReadOnlyList<OverlapGroup> overlaps)
{
    public OcadFile Base { get; } = @base;
    public OcadFile A { get; } = a;
    public OcadFile B { get; } = b;
    public MergeOptions Options { get; } = options;

    public IReadOnlyList<ObjectItem> Objects { get; } = objects;
    public IReadOnlyList<SymbolItem> Symbols { get; } = symbols;
    public IReadOnlyList<SettingItem> Settings { get; } = settings;
    public IReadOnlyList<OverlapGroup> Overlaps { get; } = overlaps;

    public IEnumerable<MergeItem> AllItems => Objects.Cast<MergeItem>().Concat(Symbols).Concat(Settings);

    public OcadFile File(Side side) => side == Side.A ? A : B;

    public MergeItem Item(string key) => AllItems.FirstOrDefault(i => i.Key == key);
}
