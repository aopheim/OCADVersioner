using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class MergeSessionTests
{
    private static MergeSession Start() => new(MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola));

    private static ObjectItem Forest(MergeSession session)
        => session.Analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Forest);

    [Test]
    public void StartsWithTheDefaults()
    {
        MergeSession session = Start();

        Assert.Multiple(() => {
            Assert.That(session.Resolution.ChoiceFor(Forest(session)), Is.Null);
            Assert.That(session.CanUndo, Is.False);
        });
    }

    [Test]
    public void ADecisionCanBeUndoneAndRedone()
    {
        MergeSession session = Start();
        session.Decide(Forest(session), Choice.A);

        session.Undo();
        Assert.That(session.Resolution.ChoiceFor(Forest(session)), Is.Null);

        session.Redo();
        Assert.That(session.Resolution.ChoiceFor(Forest(session)), Is.EqualTo(Choice.A));
    }

    [Test]
    public void ANewDecisionClearsTheRedoHistory()
    {
        MergeSession session = Start();
        session.Decide(Forest(session), Choice.A);
        session.Undo();

        session.Decide(Forest(session), Choice.B);

        Assert.That(session.CanRedo, Is.False);
    }

    [Test]
    public void SeveralDecisionsAtOnceAreOneUndoStep()
    {
        MergeSession session = Start();
        MergeItem[] all = [.. session.Analysis.AllItems];

        session.DecideAll(all.Select(i => (i, i.Kind == ItemKind.Object ? Choice.Both : Choice.A)));
        session.Undo();

        Assert.That(session.Decisions.Items, Is.Empty);
    }

    [Test]
    public void ForgettingAnOverlapDecisionOpensItAgain()
    {
        MergeSession session = Start();
        OverlapGroup group = session.Analysis.Overlaps[0];
        session.DecideOverlap(group, Choice.Both);

        session.Forget(group.Key);

        Assert.Multiple(() => {
            Assert.That(session.Resolution.IsResolved(group), Is.False);
            Assert.That(session.CanUndo, Is.True);
        });
    }

    [Test]
    public void ForgettingARejectionAcceptsTheCleanChangeAgain()
    {
        MergeSession session = Start();
        ObjectItem lake = session.Analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Lake);
        session.Decide(lake, Choice.Base);

        session.Forget(lake.Key);

        Assert.That(session.Resolution.ChoiceFor(lake), Is.EqualTo(Choice.A));
    }

    [Test]
    public void APreviewCanBeWrittenWhileConflictsAreOpen()
    {
        MergeSession session = Start();

        OcadFile preview = OcadFile.Read(session.WritePreview(DateTime.Now));

        // Open conflicts show as base: the forest is base's, Kari's clean lake is in.
        Assert.Multiple(() => {
            Assert.That(preview.Objects.Single(o => o.CreatedAt == Fixtures.Created.Forest).Coords,
                        Is.EqualTo(Fixtures.Base.Objects.Single(o => o.CreatedAt == Fixtures.Created.Forest).Coords));
            Assert.That(preview.Objects.Single(o => o.CreatedAt == Fixtures.Created.Lake).Coords,
                        Is.EqualTo(Fixtures.Kari.Objects.Single(o => o.CreatedAt == Fixtures.Created.Lake).Coords));
        });
    }

    [Test]
    public void TheMergeCannotBeWrittenUntilComplete()
        => Assert.Throws<InvalidOperationException>(() => Start().Write(DateTime.Now));

    [Test]
    public void RestoresSavedDecisions()
    {
        MergeSession first = Start();
        first.Decide(Forest(first), Choice.B);

        MergeSession second = Start();
        second.Restore(first.Decisions.ToSnapshot());

        Assert.That(second.Resolution.ChoiceFor(Forest(second)), Is.EqualTo(Choice.B));
    }
}
