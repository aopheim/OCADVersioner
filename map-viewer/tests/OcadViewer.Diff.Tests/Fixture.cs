using OcadViewer.Core;

using PurplePen.MapModel;

namespace OcadViewer.Diff.Tests;

/// <summary>The .ocd files in Fixtures/, by name.</summary>
/// <remarks>
/// Every fixture is a real file saved by OCAD 2018. What each one contains is
/// written down here, once, because the expectations in the tests lean on it.
/// </remarks>
internal static class Fixture
{
    /// <summary>A blank map with the same symbol set as the others.</summary>
    public const string Empty = "empty";

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

    /// <summary>Parses a fixture the way the app does, into a snapshot the diff can read.</summary>
    /// <param name="bytes">The file's contents, when a test has patched them; otherwise read from disk.</param>
    public static OcadMapSnapshot Snapshot(string name, byte[] bytes = null)
    {
        bytes ??= Bytes(name);
        OcadMapLoadResult loaded = OcadMapLoader.Load(bytes, PathOf(name));

        using (loaded.Map)
            return OcadMapSnapshot.Create(loaded.Map, bytes);
    }
}
