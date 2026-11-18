using NUnit.Framework;

namespace OcadViewer.E2ETests.Viewer;

/// <summary>
/// <c>/viewer</c>: maps chosen from this device, no project folder.
/// </summary>
public sealed class ViewerTests : AppPageTest
{
    /// <summary>Flow 4 in E2E-PLAN.md.</summary>
    [Test]
    public async Task TwoMapsCanBeComparedWithTheChangeOverlay()
    {
        await Page.GotoAsync("viewer");

        // Two panes on arrival, because comparing is the point.
        await Expect(Page.GetByTestId("mode-two-maps")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbtn-primary\b"));

        await Page.GetByTestId("pane-file-Left").SetInputFilesAsync(Repo.TestMap("point_line_area.ocd"));
        await Expect(Page.GetByTestId("pane-name-Left")).ToContainTextAsync("point_line_area.ocd");

        await Page.GetByTestId("pane-file-Right").SetInputFilesAsync(Repo.TestMap("point_line_area_moved.ocd"));
        await Expect(Page.GetByTestId("pane-name-Right")).ToContainTextAsync("point_line_area_moved.ocd");

        await ExpectIdleAsync();
        await ExpectPaneDrawnAsync("Left");
        await ExpectPaneDrawnAsync("Right");

        var toggle = Page.GetByTestId("overlay-toggle");
        await Expect(toggle).ToBeEnabledAsync();
        await Expect(toggle).ToHaveAttributeAsync("data-on", "false");

        await toggle.ClickAsync();

        await Expect(toggle).ToHaveAttributeAsync("data-on", "true");
        await ExpectIdleAsync();

        // Painted on the newer map, where the eye ends up.
        await Expect(Pane("Right").Locator("img.changeMask")).ToBeAttachedAsync();
        await Expect(Pane("Left").Locator("img.changeMask")).ToHaveCountAsync(0);

        await Expect(Page.GetByTestId("change-share")).ToBeVisibleAsync();
        Assert.That(await ChangedShareAsync(), Is.GreaterThan(0),
            "A moved object should light up part of the map.");
    }
}
