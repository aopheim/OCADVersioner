using OcadViewer.Core;

namespace OcadViewer.Diff.Tests;

/// <summary>The .ocd files in Fixtures/, by name.</summary>
/// <remarks>
/// Every fixture is a real file saved by OCAD 2018, copied from the Angular app's
/// <c>src/assets/tests</c> so they outlive it. What each one contains is written
/// down in <see cref="OcadDiffServiceTests"/>, because the expectations lean on it.
/// Cases no fixture shows are made by patching a copy's bytes; see <see cref="OcadBytes"/>.
/// </remarks>
internal static class Fixture
{
    /// <summary>One knoll (109).</summary>
    public const string OneKnoll = "1knoll";

    /// <summary>One point, one line and one area object.</summary>
    public const string PointLineArea = "point_line_area";

    /// <summary><see cref="PointLineArea"/> with all three objects moved.</summary>
    public const string PointLineAreaMoved = "point_line_area_moved";

    /// <summary><see cref="PointLineArea"/> with all three objects deleted.</summary>
    public const string PointLineAreaDeleted = "point_line_area_deleted";

    public static string PathOf(string name)
        => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name + ".ocd");

    public static byte[] Bytes(string name) => File.ReadAllBytes(PathOf(name));

    /// <summary>Every fixture is drawn at 1:15 000; <see cref="ParsedSnapshotTests"/> checks it.</summary>
    public const double Scale = 15_000;

    /// <summary>
    /// A fixture's snapshot from its object index alone, without PurplePen: symbol
    /// names come back empty, and the scale is <see cref="Scale"/>.
    /// </summary>
    /// <remarks>
    /// What the comparison is tested with, because it is everything the comparison
    /// reads, and because it keeps these tests running where the machine's
    /// application control policy refuses the vendored assemblies.
    /// </remarks>
    /// <param name="bytes">The file's contents, when a test has patched them; otherwise read from disk.</param>
    public static OcadMapSnapshot Snapshot(string name, byte[] bytes = null)
        => OcadMapSnapshot.Create(bytes ?? Bytes(name), Scale, new Dictionary<string, string>());

    /// <summary>Parses a fixture the way the app does, PurplePen and all.</summary>
    public static OcadMapSnapshot ParsedSnapshot(string name)
    {
        byte[] bytes = Bytes(name);
        OcadMapLoadResult loaded = OcadMapLoader.Load(bytes, PathOf(name));

        using (loaded.Map)
            return OcadMapSnapshot.Create(loaded.Map, bytes);
    }
}
