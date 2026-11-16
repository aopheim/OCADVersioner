using System.Text.Json;

using NUnit.Framework;

using OcadViewer.Versioning;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// The project-folder rules, which are a contract with the Angular app.
/// </summary>
/// <remarks>
/// Every rule about the folder - the names, the layout, which file is the working
/// file, what the next release is called and what compares to what - lives in
/// <c>OcadViewer.Versioning</c>, with no browser in it, so all of it can be
/// tested here by behaviour.
/// </remarks>
[TestFixture]
public sealed class VersioningTests
{
    [TestCase("V1", 1)]
    [TestCase("V2", 2)]
    [TestCase("V10", 10)]
    [TestCase("V2026", 2026)]
    public void AReleaseNameIsVAndDigits(string name, int expected)
    {
        Assert.That(VersionNaming.TryParse(name, out int number), Is.True);
        Assert.That(number, Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("V")]
    [TestCase("V0")]
    [TestCase("v1")]
    [TestCase("V 1")]
    [TestCase("V+1")]
    [TestCase("V1 ")]
    [TestCase("V1.2")]
    [TestCase("Versions")]
    [TestCase("Releases")]
    [TestCase("current")]
    public void AnythingElseIsSomebodyElsesFolder(string name)
    {
        Assert.That(VersionNaming.TryParse(name, out _), Is.False,
            $"\"{name}\" is not a release, and treating it as one would put a stranger's folder "
            + "in the version list - or, worse, let a new version overwrite it.");
    }

    [Test]
    public void VersionNumbersStartAtOne()
        => Assert.Throws<ArgumentOutOfRangeException>(() => VersionNaming.NameFor(0));

    [Test]
    public void OnlyTheWorkingFileIsVolatile()
    {
        Assert.Multiple(() => {
            Assert.That(VersionNaming.IsVolatile("current"), Is.True,
                "The user is editing it in OCAD while this app is open.");
            Assert.That(VersionNaming.IsVolatile("V3"), Is.False,
                "A release never changes once written, so showing it again must not re-read it.");
        });
    }

    /* ------------------------------------------------------- reading a folder */

    [Test]
    public void OnlyFoldersThatLookLikeReleasesAndHoldAMapCount()
    {
        IReadOnlyList<ProjectVersion> releases = ProjectFolder.ReleasesIn([
            new ReleaseFolder("V1", HasMapFile: true, MetadataJson: null),
            new ReleaseFolder("V2", HasMapFile: false, MetadataJson: null),   // half-written
            new ReleaseFolder("notes", HasMapFile: true, MetadataJson: null), // not ours
            new ReleaseFolder("V3", HasMapFile: true, MetadataJson: null),
        ]);

        Assert.That(releases.Select(r => r.Name), Is.EqualTo(new[] { "V1", "V3" }));
    }

    [Test]
    public void ReleasesComeBackInNumericOrderNotAlphabetical()
    {
        IReadOnlyList<ProjectVersion> releases = ProjectFolder.ReleasesIn([
            new ReleaseFolder("V10", true, null),
            new ReleaseFolder("V2", true, null),
            new ReleaseFolder("V1", true, null),
        ]);

        Assert.That(releases.Select(r => r.Number), Is.EqualTo(new[] { 1, 2, 10 }),
            "Sorted as text, V10 comes before V2, and the newest version is then the wrong one.");
    }

    [Test]
    public void ABrokenMetadataFileDoesNotHideTheRelease()
    {
        IReadOnlyList<ProjectVersion> releases = ProjectFolder.ReleasesIn([
            new ReleaseFolder("V1", true, "{ this is not json"),
            new ReleaseFolder("V2", true, null),
        ]);

        Assert.That(releases, Has.Count.EqualTo(2),
            "The map file is what makes a release openable. Metadata is a label on it.");
        Assert.That(releases[0].Metadata.VersionNumber, Is.EqualTo(1),
            "A stub still has to know which version it belongs to.");
    }

    [Test]
    public void MetadataIsReadTheWayAngularWroteIt()
    {
        const string json = """
            {
              "title": "Rydda stiar",
              "description": "Etter befaring",
              "versionCreatedAt": "2026-09-09T08:30:00.000Z",
              "versionNumber": 4,
              "numberOfAddedSymbols": 12,
              "numberOfEditedSymbols": 3,
              "numberOfDeletedSymbols": 1
            }
            """;

        VersionMetadata metadata = ProjectFolder
            .ReleasesIn([new ReleaseFolder("V4", true, json)])
            .Single()
            .Metadata;

        Assert.Multiple(() => {
            Assert.That(metadata.Title, Is.EqualTo("Rydda stiar"));
            Assert.That(metadata.VersionNumber, Is.EqualTo(4));
            Assert.That(metadata.NumberOfAddedSymbols, Is.EqualTo(12));
            Assert.That(metadata.VersionCreatedAt,
                Is.EqualTo(new DateTimeOffset(2026, 9, 9, 8, 30, 0, TimeSpan.Zero)));
        });
    }

    /* ------------------------------------------------------- where things are */

    [Test]
    public void AReleaseWrittenWhereThePlanSaysIsFoundByTheScan()
    {
        // The contract end to end: create a release where FolderLayout says, list
        // the folder the way projectFolder.js reports it, and read it back.
        NewVersionPlan plan = FolderLayout.PlanNewVersion("V4");

        string[] folderPath = plan.Folder.Split('/');
        var listing = new FolderListing(
            folderPath[^1],
            [Path.GetFileName(plan.MapFile), Path.GetFileName(plan.MetadataFile)]);

        Assert.Multiple(() => {
            Assert.That(folderPath, Is.EqualTo(new[] { VersionNaming.ReleasesFolder, "V4" }),
                "Releases sit one level under the releases folder, named for themselves.");
            Assert.That(ProjectFolder.MetadataPathIn(listing), Is.EqualTo(plan.MetadataFile),
                "The scan must read the metadata from where creating the release wrote it.");

            ProjectVersion release = ProjectFolder
                .ReleasesIn([ProjectFolder.ReleaseFolderFrom(listing, """{"versionNumber":4}""")])
                .Single();
            Assert.That(release.Name, Is.EqualTo("V4"));

            Assert.That(FolderLayout.SourceFor("V4", "map.ocd").Path, Is.EqualTo(plan.MapFile),
                "Showing the release must read the map from where creating it wrote it.");
        });
    }

    [Test]
    public void TheWorkingFileIsReadFromTheRootAndReadAgainEveryTime()
    {
        MapSource source = FolderLayout.SourceFor(VersionNaming.WorkingFileName, "Storigarden.ocd");

        Assert.That(source, Is.EqualTo(new MapSource("current", "Storigarden.ocd", Reload: true)));
    }

    [Test]
    public void AReleaseIsReadOnce()
        => Assert.That(FolderLayout.SourceFor("V3", "map.ocd").Reload, Is.False,
            "A release never changes once written, so showing it again must not re-read it.");

    [Test]
    public void TheWorkingFileCannotBeShownBeforeOneIsChosen()
        => Assert.Throws<InvalidOperationException>(() => FolderLayout.SourceFor("current", null));

    [Test]
    public void AFolderWithoutMetadataHasNoMetadataToRead()
        => Assert.That(ProjectFolder.MetadataPathIn(new FolderListing("V2", ["V2.ocd"])), Is.Null);

    [Test]
    public void AFolderMissingItsMapIsNotARelease()
    {
        ReleaseFolder folder = ProjectFolder.ReleaseFolderFrom(new FolderListing("V2", ["V2.json", "other.ocd"]), null);

        Assert.That(folder.HasMapFile, Is.False,
            "Only V2.ocd makes V2 a release; some other map in the folder is not it.");
    }

    [Test]
    public void OnlyMapsInTheRootCanBeTheWorkingFile()
    {
        IReadOnlyList<string> candidates = ProjectFolder.WorkingFileCandidates(
            ["notes.txt", "Map.OCD", "map.ocd.bak", "contours.ocd"]);

        Assert.That(candidates, Is.EqualTo(new[] { "contours.ocd", "Map.OCD" }));
    }

    [Test]
    public void TheWorkingFileIsTheOnlyMapOrTheOneChosen()
    {
        Assert.Multiple(() => {
            Assert.That(ProjectFolder.ChooseWorkingFile(["a.ocd"], null), Is.EqualTo("a.ocd"));
            Assert.That(ProjectFolder.ChooseWorkingFile(["a.ocd", "b.ocd"], null), Is.Null,
                "Two maps and no choice is a question for the user, not a guess.");
            Assert.That(ProjectFolder.ChooseWorkingFile(["a.ocd", "b.ocd"], "b.ocd"), Is.EqualTo("b.ocd"));
            Assert.That(ProjectFolder.ChooseWorkingFile(["a.ocd", "c.ocd"], "b.ocd"), Is.Null,
                "A choice that is no longer on disk is asked again, not trusted.");
        });
    }

    /* -------------------------------------------------------- naming the next */

    [Test]
    public void TheFirstVersionIsVOne()
        => Assert.That(ProjectFolder.NextVersionName([]), Is.EqualTo("V1"));

    [Test]
    public void TheNextVersionIsOneAboveTheHighestNotOneAboveTheCount()
    {
        ReleaseFolder[] folders = [
            new ReleaseFolder("V1", true, null),
            new ReleaseFolder("V5", true, null),
        ];

        Assert.That(ProjectFolder.NextVersionName(folders), Is.EqualTo("V6"),
            "Counting folders would answer V3 here, and writing V3 beside an existing V5 is how "
            + "a user loses a release they deleted the middle of.");
    }

    [Test]
    public void AnIncompleteReleaseFolderStillTakesItsName()
    {
        ReleaseFolder[] folders = [
            new ReleaseFolder("V1", true, null),
            new ReleaseFolder("V2", false, "{\"title\":\"half written\"}"),
            new ReleaseFolder("Backup", true, null),
        ];

        Assert.That(ProjectFolder.NextVersionName(folders), Is.EqualTo("V3"),
            "V2 is not a release without its map file, but the folder exists. Naming the next "
            + "version V2 would write into it and replace whatever is there.");
    }

    /* ------------------------------------------------------- what compares to what */

    [Test]
    public void TheWorkingFileComparesAgainstTheNewestRelease()
    {
        var versions = Releases(1, 2, 3);

        Assert.That(ProjectFolder.Comparison(0, versions), Is.EqualTo(("current", "V3")));
    }

    [Test]
    public void AReleaseComparesAgainstTheOneBelowIt()
    {
        var versions = Releases(1, 2, 3);

        Assert.That(ProjectFolder.Comparison(2, versions), Is.EqualTo(("V2", "V1")));
    }

    [Test]
    public void TheOldestReleaseComparesAgainstItself()
    {
        var versions = Releases(1, 2, 3);

        Assert.That(ProjectFolder.Comparison(1, versions), Is.EqualTo(("V1", "V1")),
            "Shown against itself rather than against an empty pane, which reads as a failure.");
    }

    [Test]
    public void AFolderWithNoReleasesComparesTheWorkingFileWithItself()
        => Assert.That(ProjectFolder.Comparison(0, []), Is.EqualTo(("current", "current")));

    [Test]
    public void AGapInTheNumbersIsSkippedOver()
    {
        var versions = Releases(1, 4);

        Assert.That(ProjectFolder.Comparison(4, versions), Is.EqualTo(("V4", "V1")),
            "V2 and V3 were deleted by hand; V4's predecessor is whatever is actually there.");
    }

    /* ------------------------------------------------- any two, newest on the right */

    [TestCase("V2", "V3")]
    [TestCase("V3", "V2")]
    public void APairComesOutNewerFirstWhicheverWayItWasPicked(string first, string second)
        => Assert.That(ProjectFolder.Ordered(first, second), Is.EqualTo(("V3", "V2")));

    [Test]
    public void VersionsAreOrderedByNumberNotByName()
        => Assert.That(ProjectFolder.Ordered("V9", "V10"), Is.EqualTo(("V10", "V9")),
            "Compared as text, V9 would come after V10.");

    [TestCase("V1", "current")]
    [TestCase("current", "V7")]
    public void TheWorkingFileIsAlwaysTheNewer(string first, string second)
        => Assert.That(ProjectFolder.Ordered(first, second).Newer, Is.EqualTo("current"));

    [Test]
    public void TheSameVersionTwiceIsAllowed()
        => Assert.That(ProjectFolder.Ordered("V2", "V2"), Is.EqualTo(("V2", "V2")));

    [Test]
    public void ANameThatIsNoVersionIsRefused()
        => Assert.Throws<ArgumentException>(() => ProjectFolder.Ordered("V2", "Backup"));

    [Test]
    public void APairCanOnlyNameVersionsStillInTheFolder()
    {
        var versions = Releases(1, 3);

        Assert.Multiple(() => {
            Assert.That(ProjectFolder.Contains(versions, "V3"), Is.True);
            Assert.That(ProjectFolder.Contains(versions, "V2"), Is.False, "Deleted by hand.");
            Assert.That(ProjectFolder.Contains(versions, "current"), Is.True);
        });
    }

    /* ----------------------------------------------- what we write back to disk */

    [Test]
    public void TimestampsAreWrittenTheWayTheBrowserWritesThem()
    {
        var metadata = new VersionMetadata {
            VersionNumber = 2,
            VersionCreatedAt = new DateTimeOffset(2026, 11, 13, 21, 5, 0, TimeSpan.FromHours(1))
        };

        string json = JsonSerializer.Serialize(metadata, AngularJson.Options);

        Assert.That(json, Does.Contain("\"versionCreatedAt\":\"2026-11-13T20:05:00.000Z\""),
            "The Angular app writes Date.toISOString(); .NET's default is a seven-digit fraction "
            + "and a +00:00 offset. Both parse, but the two apps then write visibly different "
            + "files into the same folder, and anything comparing the field as text is wrong.");
    }

    [Test]
    public void EmptyFieldsAreLeftOutRatherThanWrittenAsNull()
    {
        var metadata = new VersionMetadata { VersionNumber = 1 };

        string json = JsonSerializer.Serialize(metadata, AngularJson.Options);

        Assert.Multiple(() => {
            Assert.That(json, Does.Not.Contain("title"));
            Assert.That(json, Does.Contain("\"versionNumber\":1"));
        });
    }

    [Test]
    public void WhatWeWriteIsWhatWeReadBack()
    {
        var written = new VersionMetadata {
            Title = "Ny sti",
            Description = "Langs bekken",
            VersionCreatedAt = new DateTimeOffset(2026, 5, 1, 6, 0, 0, TimeSpan.Zero),
            VersionNumber = 7,
            NumberOfAddedSymbols = 5
        };

        string json = JsonSerializer.Serialize(written, AngularJson.Options);
        VersionMetadata read = JsonSerializer.Deserialize<VersionMetadata>(json, AngularJson.Options);

        Assert.Multiple(() => {
            Assert.That(read.Title, Is.EqualTo(written.Title));
            Assert.That(read.VersionCreatedAt, Is.EqualTo(written.VersionCreatedAt));
            Assert.That(read.VersionNumber, Is.EqualTo(written.VersionNumber));
            Assert.That(read.NumberOfAddedSymbols, Is.EqualTo(written.NumberOfAddedSymbols));
        });
    }

    private static IReadOnlyList<ProjectVersion> Releases(params int[] numbers)
        => ProjectFolder.ReleasesIn(
            numbers.Select(n => new ReleaseFolder(VersionNaming.NameFor(n), true, null)));
}
