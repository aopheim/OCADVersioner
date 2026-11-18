using OcadViewer.Merge.Analysis;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

/// <summary>The checks a merge screen shows before the merge starts.</summary>
[TestFixture]
public sealed class MergeCheckTests
{
    [Test]
    public void FilesOfDifferentVersionsCannotBeMerged()
    {
        OcadFile v12 = Fixtures.Read("kari_v12.ocd");

        Assert.Multiple(() => {
            Assert.That(MergeCheck.SameVersion(Fixtures.Base, Fixtures.Kari, Fixtures.Ola), Is.True);
            Assert.That(MergeCheck.SameVersion(Fixtures.Base, v12, Fixtures.Ola), Is.False);
            Assert.Throws<MergeException>(() => MergeAnalyzer.Analyze(Fixtures.Base, v12, Fixtures.Ola));
        });
    }

    [Test]
    public void ACopyOfBaseSharesAllOfIt()
        => Assert.That(MergeCheck.SharedPercent(Fixtures.Base, Fixtures.Base), Is.EqualTo(100));

    [Test]
    public void AnEditedCopySharesMostOfBaseAndDescendsFromIt()
    {
        int percent = MergeCheck.SharedPercent(Fixtures.Base, Fixtures.Kari);

        Assert.Multiple(() => {
            Assert.That(percent, Is.InRange(MergeCheck.DescendsFromPercent, 100));
            Assert.That(MergeCheck.Descends(percent), Is.True);
        });
    }

    [Test]
    public void AnUnrelatedMapDoesNotDescend()
    {
        int percent = MergeCheck.SharedPercent(Fixtures.Base, Fixtures.Read("sprint_base.ocd"));

        Assert.That(MergeCheck.Descends(percent), Is.False);
    }
}
