namespace OcadViewer.Diff.Tests;

/// <summary>
/// The diff against real projects, release by release, read from where they live.
/// </summary>
/// <remarks>
/// <para>
/// The maps are copyrighted and are <b>never copied into this repository</b>. The
/// tests read them in place, read-only, from the folder named by the
/// <c>OCAD_REAL_MAPS</c> environment variable (default <c>C:\repos</c>), and are
/// ignored where they are not there - on CI, and on any machine without them.
/// </para>
/// <para>
/// The expected counts are a baseline: each was checked by hand when it was
/// recorded (see DIFF.md), and a change to the matching rules that moves one must
/// explain why. Two checks need no baseline at all and hold for any pair:
/// </para>
/// <list type="bullet">
///   <item>The counts add up. Every object in the older version is unchanged,
///   edited or deleted; every object in the newer one unchanged, edited or added.
///   So added minus deleted is exactly the change in object count.</item>
///   <item>Nothing identical is reported as gone and back. An added object with an
///   identical deleted one is the same object under a new index entry.</item>
/// </list>
/// <para>
/// Snapshots come from the object index alone, at 1:15 000. The counts do not
/// depend on the scale, only the lengths and areas would.
/// </para>
/// </remarks>
[TestFixture]
[Category("RealMaps")]
public sealed class RealMapTests
{
    private readonly IOcadDiffService diff = new OcadDiffService();

    private static string Root
        => Environment.GetEnvironmentVariable("OCAD_REAL_MAPS") is { Length: > 0 } root ? root : @"C:\repos";

    private const string Bymarka = @"BIL-kart\Bymarkabasen";
    private const string BymarkaWorking = "Byåsens del av Bymarkabasen.ocd";
    private const string Storigarden = "ocad-storigarden";
    private const string StorigardenWorking = "storigarden.ocd";

    /// <summary>Project, older, newer, then the expected added, edited and deleted.</summary>
    private static IEnumerable<TestCaseData> Pairs()
    {
        yield return Pair(Bymarka, "V1", "V2", 1288, 889, 320).SetDescription(
            "Renumbered, and every date two hours earlier.");
        yield return Pair(Bymarka, "V2", "V3", 472, 364, 103).SetDescription(
            "Renumbered.");
        yield return Pair(Bymarka, "V3", BymarkaWorking, 0, 0, 1).SetDescription(
            "The temporary forbidden-area marking (709.1) removed.");

        yield return Pair(Storigarden, "V1", "V2", 20, 3, 0);
        yield return Pair(Storigarden, "V2", "V3", 8, 3, 1);
        yield return Pair(Storigarden, "V3", "V4", 7, 5, 0);
        yield return Pair(Storigarden, "V4", "V5", 29, 5, 2);
        yield return Pair(Storigarden, "V5", "V6", 14, 2, 1);
        yield return Pair(Storigarden, "V6", StorigardenWorking, 0, 0, 0);
    }

    private static TestCaseData Pair(string project, string older, string newer, int added, int edited, int deleted)
        => new TestCaseData(project, older, newer, added, edited, deleted)
            .SetName($"{project.Split('\\').Last()} {older} to {Path.GetFileNameWithoutExtension(newer)}");

    [TestCaseSource(nameof(Pairs))]
    public void MatchesTheBaseline(string project, string older, string newer, int added, int edited, int deleted)
    {
        ObjectDiff result = diff.Compare(Load(project, older), Load(project, newer));

        TestContext.Out.WriteLine($"{result.Added.Count} added, {result.Edited.Count} edited, {result.Deleted.Count} deleted");
        Assert.That((result.Added.Count, result.Edited.Count, result.Deleted.Count),
                    Is.EqualTo((added, edited, deleted)));
    }

    [TestCaseSource(nameof(Pairs))]
    public void TheCountsAddUp(string project, string older, string newer, int _, int __, int ___)
    {
        OcadMapSnapshot before = Load(project, older), after = Load(project, newer);

        ObjectDiff result = diff.Compare(before, after);

        Assert.That(result.Added.Count - result.Deleted.Count,
                    Is.EqualTo(after.Objects.Count - before.Objects.Count));
    }

    [TestCaseSource(nameof(Pairs))]
    public void NothingIdenticalIsReportedAsDeletedAndAdded(string project, string older, string newer, int _, int __, int ___)
    {
        OcadMapSnapshot before = Load(project, older), after = Load(project, newer);

        ObjectDiff result = diff.Compare(before, after);

        var deletedContent = result.Deleted.Select(d => Content(before.Objects[d.Index])).ToHashSet();
        int identical = result.Added.Count(a => deletedContent.Contains(Content(after.Objects[a.Index])));

        Assert.That(identical, Is.Zero);
    }

    private static string Content(MapObject o)
        => $"{o.SymbolNumber}|{o.Kind}|{o.AngleInDegrees}|{o.Text}|{string.Join(";", o.Points)}";

    /// <summary>A version's snapshot, read in place; "V3" is a release, anything else the working file.</summary>
    private static OcadMapSnapshot Load(string project, string version)
    {
        string folder = Path.Combine(Root, project);
        string path = version.EndsWith(".ocd", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(folder, version)
            : Path.Combine(folder, "Releases", version, version + ".ocd");

        if (!File.Exists(path))
            Assert.Ignore($"{path} is not on this machine. Set OCAD_REAL_MAPS to the folder holding {project}.");

        return Fixture.Snapshot(version, File.ReadAllBytes(path));
    }
}
