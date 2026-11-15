using System.Text.Json;

using NUnit.Framework;

using OcadViewer.Versioning;

namespace OcadViewer.ArchitectureTests;

/// <summary>
/// The project-folder rules, which are a contract with the Angular app.
/// </summary>
/// <remarks>
/// These could not be written before: the naming rule lived in a JavaScript
/// module, the next-version rule in a service that needed a browser, and the
/// comparison rule inside a Razor page. Moving all three into
/// <c>OcadViewer.Versioning</c> was most of the point of extracting it.
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
