using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Ocd;
using OcadViewer.Merge.Writing;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class MergeWriterTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 9, 0, 0);

    private static byte[] Merge(OcadFile @base, OcadFile a, OcadFile b, Action<MergeAnalysis, MergeDecisions> decide = null)
    {
        MergeAnalysis analysis = MergeAnalyzer.Analyze(@base, a, b);
        MergeDecisions decisions = new();
        decide?.Invoke(analysis, decisions);
        return MergeWriter.Write(new MergeResolution(analysis, decisions), Now);
    }

    private static OcadFile MergeFixtures(Action<MergeAnalysis, MergeDecisions> extra = null)
        => OcadFile.Read(Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Ola, (analysis, decisions) => {
            DecideAll(analysis, decisions);
            extra?.Invoke(analysis, decisions);
        }));

    private static void DecideAll(MergeAnalysis analysis, MergeDecisions decisions)
    {
        decisions.Decide(analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Forest).Key, Choice.A);
        decisions.Decide(analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.EastBoulder).Key, Choice.B);
        decisions.Decide(analysis.Symbols.Single(), Choice.A);
        foreach (OverlapGroup group in analysis.Overlaps)
            decisions.DecideOverlap(group.Key, Choice.Both);
    }

    private static OcadObject Only(OcadFile file, DateTime created) => file.Objects.Single(o => o.CreatedAt == created);

    [Test]
    public void MergingAFileWithItselfLeavesEveryByteAlone()
    {
        byte[] original = Fixtures.Bytes("base.ocd");

        Assert.That(Merge(Fixtures.Base, Fixtures.Base, Fixtures.Base), Is.EqualTo(original));
    }

    [Test]
    public void TheMergedFileCanBeReadBack()
        => Assert.That(() => MergeFixtures(), Throws.Nothing);

    [Test]
    public void TakesEachCleanChangeFromTheBranchThatMadeIt()
    {
        OcadFile merged = MergeFixtures();

        Assert.Multiple(() => {
            Assert.That(Only(merged, Fixtures.Created.Lake).Coords, Is.EqualTo(Only(Fixtures.Kari, Fixtures.Created.Lake).Coords));
            Assert.That(Only(merged, Fixtures.Created.OpenLand).Coords, Is.EqualTo(Only(Fixtures.Ola, Fixtures.Created.OpenLand).Coords));
        });
    }

    [Test]
    public void AppliesTheConflictDecisions()
    {
        OcadFile merged = MergeFixtures();

        Assert.Multiple(() => {
            Assert.That(Only(merged, Fixtures.Created.Forest).Coords, Is.EqualTo(Only(Fixtures.Kari, Fixtures.Created.Forest).Coords));
            Assert.That(Only(merged, Fixtures.Created.EastBoulder).Coords, Is.EqualTo(Only(Fixtures.Ola, Fixtures.Created.EastBoulder).Coords));
            Assert.That(merged.Symbols.Single(s => s.Number == 109000).Description, Is.EqualTo("Small knoll, min 1 m (Kari)"));
        });
    }

    [Test]
    public void DeletesWhatABranchDeleted()
        => Assert.That(MergeFixtures().Objects.Where(o => o.CreatedAt == Fixtures.Created.MiddleBoulder), Is.Empty);

    [Test]
    public void AddsWhatEitherBranchAdded()
    {
        int[] symbols = [.. MergeFixtures().Objects.Select(o => o.Symbol)];

        Assert.That(symbols, Is.EquivalentTo(new[] {
            401000, 301000, 406000, 507000, 101000, 204000, 109000,   // base, minus the middle boulder
            508000, 101000,                                           // Kari's ride and contour
            109000, 109000,                                           // Ola's knolls
        }));
    }

    [Test]
    public void LeavesUntouchedObjectsInTheirSlots()
    {
        OcadFile merged = MergeFixtures();

        Assert.That(Only(merged, Fixtures.Created.Footpath).Entry, Is.EqualTo(Only(Fixtures.Base, Fixtures.Created.Footpath).Entry));
    }

    [Test]
    public void TakingOneBranchWhereTheOtherChangedNothingGivesThatBranch()
    {
        OcadFile merged = OcadFile.Read(Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Base));

        Assert.That(MergeAnalyzer.Analyze(Fixtures.Kari, merged, merged).AllItems, Is.Empty);
    }

    [Test]
    public void KeepingBothGivesTheSecondCopyANewIdentity()
    {
        OcadFile merged = MergeFixtures((analysis, decisions)
            => decisions.Decide(analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Forest).Key, Choice.Both));

        OcadObject[] forests = [.. merged.Objects.Where(o => o.Symbol == 406000)];
        Assert.Multiple(() => {
            Assert.That(forests, Has.Length.EqualTo(2));
            Assert.That(Only(merged, Fixtures.Created.Forest).Coords, Is.EqualTo(Only(Fixtures.Kari, Fixtures.Created.Forest).Coords));
            Assert.That(forests.Single(o => o.CreatedAt != Fixtures.Created.Forest).Coords,
                        Is.EqualTo(Only(Fixtures.Ola, Fixtures.Created.Forest).Coords));
            Assert.That(forests.Single(o => o.CreatedAt != Fixtures.Created.Forest).CreatedAt, Is.EqualTo(Now));
        });
    }

    [Test]
    public void RejectingEverythingGivesBase()
    {
        OcadFile merged = OcadFile.Read(Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Ola, (analysis, decisions) => {
            foreach (MergeItem item in analysis.AllItems)
                decisions.Decide(item, Choice.Base);
            foreach (OverlapGroup group in analysis.Overlaps)
                decisions.DecideOverlap(group.Key, Choice.Base);
        }));

        Assert.That(MergeAnalyzer.Analyze(Fixtures.Base, merged, merged).AllItems, Is.Empty);
    }

    [Test]
    public void RefusesToWriteWhileConflictsAreOpen()
        => Assert.Throws<InvalidOperationException>(() => Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Ola));

    [Test]
    public void TheMergeCanBeMergedAgain()
    {
        // A merged file must be as good an input as one OCAD wrote.
        OcadFile merged = MergeFixtures();

        Assert.That(() => MergeAnalyzer.Analyze(merged, Fixtures.Kari, Fixtures.Ola), Throws.Nothing);
    }

    [Test]
    public void ACopyOcadDuplicatedIsKeptAsANewObject()
    {
        OcadFile edits = Fixtures.Read("identity_edits.ocd");

        OcadFile merged = OcadFile.Read(Merge(Fixtures.Base, edits, Fixtures.Base));

        Assert.That(merged.Objects, Has.Count.EqualTo(edits.Objects.Count));
    }

    /// <summary>
    /// Writes the fixtures' merge (and a keep-both variant) where OCAD can open it:
    /// the check no unit test can make is that OCAD itself accepts the file.
    /// </summary>
    [Test, Explicit("Writes merged files for opening in OCAD by hand.")]
    public void WriteMergedFixturesForOcad()
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "merged");
        Directory.CreateDirectory(folder);

        File.WriteAllBytes(Path.Combine(folder, "merged.ocd"),
            Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Ola, DecideAll));
        File.WriteAllBytes(Path.Combine(folder, "merged_keep_both.ocd"),
            Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Ola, (analysis, decisions) => {
                DecideAll(analysis, decisions);
                decisions.Decide(analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Forest).Key, Choice.Both);
            }));
        TestContext.Out.WriteLine(folder);
    }

    [TestCase("base_v12.ocd")]
    [TestCase("base_v11.ocd")]
    public void OlderFormatsMergeWithThemselvesUntouched(string name)
        => Assert.That(Merge(Fixtures.Read(name), Fixtures.Read(name), Fixtures.Read(name)), Is.EqualTo(Fixtures.Bytes(name)));
}
