using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Writing;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

/// <summary>"Keep both": what it means for each kind of change.</summary>
[TestFixture]
public sealed class KeepBothTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0);

    private static OcadFile Write(MergeAnalysis analysis, MergeDecisions decisions)
        => OcadFile.Read(MergeWriter.Write(new MergeResolution(analysis, decisions), Now));

    private static void DecideTheRest(MergeAnalysis analysis, MergeDecisions decisions)
    {
        foreach (MergeItem item in analysis.AllItems.Where(i => i.Outcome == Outcome.Conflict && !decisions.Items.ContainsKey(i.Key)))
            decisions.Decide(item, Choice.A);
        foreach (OverlapGroup group in analysis.Overlaps.Where(g => !decisions.Overlaps.ContainsKey(g.Key)))
            decisions.DecideOverlap(group.Key, Choice.Both);
    }

    [Test]
    public void KeepingBothOfAChangeOnlyOneBranchMadeTakesThatBranch()
    {
        // Only Kari changed the lake. "Both" must not add base's lake as a second copy.
        MergeAnalysis analysis = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola);
        ObjectItem lake = analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Lake);
        MergeDecisions decisions = new();
        decisions.Decide(lake, Choice.Both);
        DecideTheRest(analysis, decisions);

        OcadFile merged = Write(analysis, decisions);

        Assert.Multiple(() => {
            Assert.That(new MergeResolution(analysis, decisions).ChoiceFor(lake), Is.EqualTo(Choice.A));
            Assert.That(merged.Objects.Where(o => o.Symbol == lake.Symbol).Select(o => o.Coords),
                        Is.EqualTo(new[] { lake.A.Coords }));
        });
    }

    [Test]
    public void KeepingBothOfAChangeBothMadeAlikeKeepsOne()
    {
        MergeAnalysis analysis = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Kari);
        MergeDecisions decisions = new();
        foreach (MergeItem item in analysis.Objects)
            decisions.Decide(item, Choice.Both);

        Assert.That(Write(analysis, decisions).Objects, Has.Count.EqualTo(Fixtures.Kari.Objects.Count));
    }

    [Test]
    public void EveryCopyKeptBesideItsOriginalGetsADateOfItsOwn()
    {
        // Kari and Ola both moved the lake and the knoll, differently.
        MergeAnalysis analysis = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Read("kari_symbols.ocd"), Fixtures.Read("ola_symbols.ocd"));
        MergeDecisions decisions = new();
        foreach (ObjectItem item in analysis.Objects.Where(i => i.Conflict == ConflictKind.BothEdited))
            decisions.Decide(item, Choice.Both);
        DecideTheRest(analysis, decisions);

        OcadFile merged = Write(analysis, decisions);
        DateTime?[] copies = [.. merged.Objects.Select(o => o.CreatedAt).Where(d => d >= Now)];

        Assert.That(copies, Is.EquivalentTo(new DateTime?[] { Now, Now.AddMilliseconds(1) }));
    }
}
