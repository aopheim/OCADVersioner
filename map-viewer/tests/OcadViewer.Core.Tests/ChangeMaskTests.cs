using NUnit.Framework;

namespace OcadViewer.Core.Tests;

/// <summary>
/// The change mask end to end, on the small maps in map-viewer/testdata.
/// </summary>
public sealed class ChangeMaskTests
{
    private static byte[] Bytes(string name)
        => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "testdata", name));

    private static byte[] Mask(string older, string newer)
    {
        using var store = new MapStore(
            settings: new MapRenderSettings { Templates = PurplePen.MapModel.RenderTemplateOption.MapOnly });

        store.Load("older", older, Bytes(older));
        store.Load("newer", newer, Bytes(newer));

        return store.RenderChangeMask("older", "newer", store.PlanChangeMask("older", "newer"));
    }

    private static int Changed(byte[] mask)
        => mask.Count(v => v is (byte)PixelChange.Appeared or (byte)PixelChange.Vanished or (byte)PixelChange.Altered);

    [Test]
    public void AMapAgainstItselfHasNoChangesButHasInk()
    {
        byte[] mask = Mask("point_line_area.ocd", "point_line_area.ocd");

        Assert.That(Changed(mask), Is.Zero);
        Assert.That(mask.Count(v => v == (byte)PixelChange.UnchangedInk), Is.GreaterThan(0));
    }

    [TestCase("point_line_area_moved.ocd")]
    [TestCase("point_line_area_deleted.ocd")]
    public void ARealEditIsFound(string newer)
    {
        byte[] mask = Mask("point_line_area.ocd", newer);

        Assert.That(Changed(mask), Is.GreaterThan(0));
    }
}
