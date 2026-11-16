using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Format;
using OcadViewer.Merge.Writing;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// kari_settings.ocd and ola_settings.ocd: both renamed colour 5 (brown)
/// differently, and both duplicated symbol 101 into 101.004 with different
/// descriptions. OCAD also rewrote the display-mode string in both.
/// </summary>
[TestFixture]
public sealed class SettingsMergeTests
{
    private static readonly OcadFile Kari = Fixtures.Read("kari_settings.ocd");
    private static readonly OcadFile Ola = Fixtures.Read("ola_settings.ocd");

    private static MergeAnalysis Analysis => MergeAnalyzer.Analyze(Fixtures.Base, Kari, Ola);

    [Test]
    public void BothRenamingTheSameColourIsAConflict()
    {
        SettingItem brown = Analysis.Settings.Single();

        Assert.Multiple(() => {
            Assert.That(brown.Type, Is.EqualTo(OcadSetting.ColorType));
            Assert.That(brown.Key, Is.EqualTo("set:9:n5"));
            Assert.That(brown.Conflict, Is.EqualTo(ConflictKind.BothEdited));
        });
    }

    [Test]
    public void ViewStateStringsNeverTakePart()
        => Assert.That(Analysis.Settings.Select(s => s.Type), Has.None.EqualTo(1024));

    [Test]
    public void BothAddingTheSameSymbolNumberDifferentlyIsAConflict()
    {
        SymbolItem copy = Analysis.Symbols.Single();

        Assert.Multiple(() => {
            Assert.That(copy.Number, Is.EqualTo(101004));
            Assert.That(copy.Conflict, Is.EqualTo(ConflictKind.BothAdded));
        });
    }

    [Test]
    public void AColourOnlyOneBranchChangedIsClean()
        => Assert.That(MergeAnalyzer.Analyze(Fixtures.Base, Kari, Fixtures.Base).Settings.Single().Outcome, Is.EqualTo(Outcome.OnlyA));

    [Test]
    public void TheChosenColourAndSymbolAreWritten()
    {
        MergeAnalysis analysis = Analysis;
        MergeDecisions decisions = new();
        decisions.Decide(analysis.Settings.Single(), Choice.B);
        decisions.Decide(analysis.Symbols.Single(), Choice.A);

        OcadFile merged = OcadFile.Read(MergeWriter.Write(new MergeResolution(analysis, decisions), DateTime.Now));

        Assert.Multiple(() => {
            Assert.That(merged.Settings.Single(s => s.Type == OcadSetting.ColorType && s.Field('n') == "5").FirstField,
                        Is.EqualTo("Brown - points (Ola)"));
            Assert.That(merged.Symbols.Single(s => s.Number == 101004).Description, Is.EqualTo("Contour, copy (Kari)"));
            Assert.That(merged.Symbols, Has.Count.EqualTo(Fixtures.Base.Symbols.Count + 1));
            Assert.That(merged.Settings, Has.Count.EqualTo(Fixtures.Base.Settings.Count));
        });
    }

    [Test]
    public void KeepingBaseLeavesTheColourAsItWas()
    {
        MergeAnalysis analysis = Analysis;
        MergeDecisions decisions = new();
        decisions.Decide(analysis.Settings.Single(), Choice.Base);
        decisions.Decide(analysis.Symbols.Single(), Choice.Base);

        OcadFile merged = OcadFile.Read(MergeWriter.Write(new MergeResolution(analysis, decisions), DateTime.Now));

        Assert.That(MergeAnalyzer.Analyze(Fixtures.Base, merged, merged).AllItems, Is.Empty);
    }
}
