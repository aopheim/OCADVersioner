using NUnit.Framework;

using OcadViewer.Versioning;

namespace OcadViewer.E2ETests.Workspace;

/// <summary>
/// <c>/</c>: the project workspace, over a folder in the browser's private
/// file system standing in for the one the user would pick.
/// </summary>
public sealed class WorkspaceTests : AppPageTest
{
    [SetUp]
    public async Task InstallPickerAsync() => await OpfsProject.InstallPickerAsync(Context);

    /// <summary>Flow 5 in E2E-PLAN.md.</summary>
    [Test]
    public async Task ChoosingAFolderOpensTheProjectAndDrawsBothMaps()
    {
        await Page.GotoAsync("");

        // The start dialog opens by itself: there is nothing else to do yet.
        var selector = Page.GetByTestId("project-selector");
        await Expect(selector).ToBeVisibleAsync();

        OpfsProject project = await OpfsProject.CreateAsync(Page, "Storigarden");
        await project.AddWorkingFileAsync("storigarden.ocd", "point_line_area.ocd");
        string release = await project.AddReleaseAsync(1, "point_line_area_moved.ocd", "First draft");
        await project.PickNextAsync();

        await Page.GetByTestId("choose-folder").ClickAsync();

        // One .ocd in the root, so the working file is settled without asking.
        await Expect(selector).ToBeHiddenAsync();
        await Expect(Page.GetByTestId("project-name")).ToContainTextAsync("Storigarden");
        await Expect(Page.GetByTestId("project-name")).ToContainTextAsync("storigarden.ocd");

        await Expect(Page.GetByTestId("version-item")).ToHaveCountAsync(1);
        await Expect(Page.GetByTestId("version-item")).ToContainTextAsync("First draft");

        // The working file on the right, against the newest release on the left.
        await Expect(Page.GetByTestId("older-version")).ToHaveValueAsync(release);
        await Expect(Page.GetByTestId("newer-version")).ToHaveValueAsync(VersionNaming.WorkingFileName);

        await ExpectPaneDrawnAsync("Right");
        await ExpectPaneDrawnAsync("Left");
        await ExpectIdleAsync();

        await Expect(Page.GetByTestId("overlay-toggle")).ToBeEnabledAsync();
    }
}
