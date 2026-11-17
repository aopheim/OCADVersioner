using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Writing;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// Sprint maps (ISSprOM 2019, 1:4000) merge exactly like forest maps (ISOM 2017,
/// 1:10 000): the same scenario, drawn once with each symbol set, gives the same
/// result.
/// </summary>
/// <remarks>
/// sprint_base.ocd was started in OCAD 2020 from "Orienteering Map ISSprOM 2019
/// 4000" and drawn with buildings, open land, water, a wall, trees and a bush.
/// sprint_kari.ocd and sprint_ola.ocd repeat what kari.ocd and ola.ocd do to the
/// forest map, object for object (see the README). The engine knows nothing about
/// symbol sets; these tests pin that a different scale and different symbols do
/// not change what it concludes.
/// </remarks>
[TestFixture]
public sealed class SprintMapTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0);

    /// <summary>A fixture set and which object plays which part in the scenario.</summary>
    public sealed record Scenario(
        string Name, string Base, string Kari, string Ola, int Scale,
        DateTime BothEdit, DateTime OnlyKari, DateTime OnlyOla, DateTime OlaDeletes,
        DateTime KariDeletesOlaMoves, DateTime NeitherTouches, int BothChangeSymbol, int KariAdds, int OlaAdds)
    {
        public override string ToString() => Name;
    }

    public static IEnumerable<Scenario> Scenarios()
    {
        yield return new Scenario("forest (ISOM 2017)", "base.ocd", "kari.ocd", "ola.ocd", 10000,
            Fixtures.Created.Forest, Fixtures.Created.Lake, Fixtures.Created.OpenLand, Fixtures.Created.MiddleBoulder,
            Fixtures.Created.EastBoulder, Fixtures.Created.Footpath, 109000, KariAdds: 2, OlaAdds: 2);
        yield return new Scenario("sprint (ISSprOM 2019)", "sprint_base.ocd", "sprint_kari.ocd", "sprint_ola.ocd", 4000,
            Fixtures.Sprint.WestBuilding, Fixtures.Sprint.Water, Fixtures.Sprint.OpenLand, Fixtures.Sprint.MiddleTree,
            Fixtures.Sprint.EastTree, Fixtures.Sprint.Wall, 418000, KariAdds: 1, OlaAdds: 2);
    }

    private static MergeAnalysis Analyse(Scenario s)
        => MergeAnalyzer.Analyze(Fixtures.Read(s.Base), Fixtures.Read(s.Kari), Fixtures.Read(s.Ola));

    private static ObjectItem Item(MergeAnalysis analysis, DateTime created)
        => analysis.Objects.SingleOrDefault(i => i.Base?.CreatedAt == created);

    [TestCaseSource(nameof(Scenarios))]
    public void TheMapScaleIsRead(Scenario s) => Assert.That(Fixtures.Read(s.Base).Scale, Is.EqualTo(s.Scale));

    [TestCaseSource(nameof(Scenarios))]
    public void EachChangeIsClassifiedTheSame(Scenario s)
    {
        MergeAnalysis analysis = Analyse(s);

        Assert.Multiple(() => {
            Assert.That(Item(analysis, s.BothEdit)?.Conflict, Is.EqualTo(ConflictKind.BothEdited), "both edited");
            Assert.That(Item(analysis, s.OnlyKari)?.Outcome, Is.EqualTo(Outcome.OnlyA), "only Kari");
            Assert.That(Item(analysis, s.OnlyOla)?.Outcome, Is.EqualTo(Outcome.OnlyB), "only Ola");
            Assert.That(Item(analysis, s.OlaDeletes)?.ChangeB, Is.EqualTo(ChangeKind.Deleted), "Ola deletes");
            Assert.That(Item(analysis, s.OlaDeletes)?.Outcome, Is.EqualTo(Outcome.OnlyB), "Ola deletes");
            Assert.That(Item(analysis, s.KariDeletesOlaMoves)?.Conflict, Is.EqualTo(ConflictKind.EditDelete), "edit vs delete");
            Assert.That(Item(analysis, s.NeitherTouches), Is.Null, "untouched");
            Assert.That(analysis.Symbols.Single().Number, Is.EqualTo(s.BothChangeSymbol));
            Assert.That(analysis.Symbols.Single().Conflict, Is.EqualTo(ConflictKind.BothEdited));
            Assert.That(analysis.Settings, Is.Empty, "settings");
            Assert.That(analysis.Objects.Count(i => i.Base is null && i.Outcome == Outcome.OnlyA), Is.EqualTo(s.KariAdds), "Kari adds");
            Assert.That(analysis.Objects.Count(i => i.Base is null && i.Outcome == Outcome.OnlyB), Is.EqualTo(s.OlaAdds), "Ola adds");
        });
    }

    [TestCaseSource(nameof(Scenarios))]
    public void KarisAndOlasAdditionsNearbyAreAnOverlap(Scenario s)
    {
        MergeAnalysis analysis = Analyse(s);

        Assert.That(analysis.Overlaps.Any(g => g.Items.Any(i => i.Base is null && i.Outcome == Outcome.OnlyA)
                                            && g.Items.Any(i => i.Base is null && i.Outcome == Outcome.OnlyB)), Is.True);
    }

    [TestCaseSource(nameof(Scenarios))]
    public void TheDecidedMergeHoldsTheChosenVersions(Scenario s)
    {
        MergeAnalysis analysis = Analyse(s);
        ObjectItem bothEdit = Item(analysis, s.BothEdit), moved = Item(analysis, s.KariDeletesOlaMoves);
        MergeDecisions decisions = new();
        decisions.Decide(bothEdit, Choice.A);
        decisions.Decide(moved, Choice.B);
        decisions.Decide(analysis.Symbols.Single(), Choice.B);
        foreach (OverlapGroup group in analysis.Overlaps)
            decisions.DecideOverlap(group.Key, Choice.Both);

        OcadFile merged = OcadFile.Read(MergeWriter.Write(new MergeResolution(analysis, decisions), Now));
        OcadObject Only(DateTime created) => merged.Objects.SingleOrDefault(o => o.CreatedAt == created);

        Assert.Multiple(() => {
            Assert.That(Only(s.BothEdit).Coords, Is.EqualTo(bothEdit.A.Coords), "Kari's edit");
            Assert.That(Only(s.KariDeletesOlaMoves).Coords, Is.EqualTo(moved.B.Coords), "Ola's move");
            Assert.That(Only(s.OnlyKari).Coords, Is.EqualTo(Item(analysis, s.OnlyKari).A.Coords), "Kari's clean change");
            Assert.That(Only(s.OnlyOla).Coords, Is.EqualTo(Item(analysis, s.OnlyOla).B.Coords), "Ola's clean change");
            Assert.That(Only(s.OlaDeletes), Is.Null, "Ola's deletion");
            Assert.That(merged.Symbols.Single(x => x.Number == s.BothChangeSymbol).Description, Does.EndWith("(Ola)"));
            Assert.That(merged.Objects, Has.Count.EqualTo(Fixtures.Read(s.Base).Objects.Count - 1 + s.KariAdds + s.OlaAdds));
        });
    }

    [TestCaseSource(nameof(Scenarios))]
    public void MergingTheBaseWithItselfLeavesEveryByteAlone(Scenario s)
    {
        OcadFile @base = Fixtures.Read(s.Base);
        MergeAnalysis analysis = MergeAnalyzer.Analyze(@base, @base, @base);

        Assert.That(MergeWriter.Write(new MergeResolution(analysis, new MergeDecisions()), Now), Is.EqualTo(Fixtures.Bytes(s.Base)));
    }
}
