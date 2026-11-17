namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class MergeOptionsTests
{
    [Test]
    public void ThresholdsAreMetresOnTheGroundConvertedWithTheMapScale()
    {
        // 0.1 m at 1:10 000 is 0.01 mm on paper: one coordinate unit.
        Assert.Multiple(() => {
            Assert.That(MergeOptions.Default.ToleranceUnits(10000), Is.EqualTo(1));
            Assert.That(MergeOptions.Default.ToleranceUnits(4000), Is.EqualTo(2));
            Assert.That(new MergeOptions { OverlapBufferMeters = 10 }.OverlapBufferUnits(10000), Is.EqualTo(100));
        });
    }
}
