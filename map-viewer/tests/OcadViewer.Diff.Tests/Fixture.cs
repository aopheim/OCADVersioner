using OcadViewer.Core;

namespace OcadViewer.Diff.Tests;

/// <summary>The .ocd files in Fixtures/, by name.</summary>
/// <remarks>
/// Every fixture is a real file saved by OCAD in its 2018 format. The first four were
/// copied from the Angular app's <c>src/assets/tests</c> so they outlive it; the rest
/// were made from them in OCAD 2020.8. What each one contains is written
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

    /// <summary><see cref="OneKnoll"/> rotated 45° in OCAD.</summary>
    public const string OneKnollRotated = "1knoll_rotated";

    /// <summary><see cref="PointLineArea"/> with the area changed from 403.0 to 406.0 in OCAD.</summary>
    public const string PointLineArea406 = "point_line_area_406";

    /// <summary>A new map with <see cref="PointLineArea"/>'s symbols and no objects at all.</summary>
    public const string Blank = "blank";

    /// <summary>
    /// <see cref="Blank"/> with <see cref="PointLineArea"/> imported into it (File, Import,
    /// offset 0): the same three objects, renumbered 1-3, creation dates kept.
    /// </summary>
    public const string PointLineAreaImported = "point_line_area_imported";

    /// <summary>
    /// <see cref="Blank"/> with <see cref="PointLineArea"/>'s objects copied and pasted
    /// into it: renumbered 1-3, creation dates kept, and every object shifted by the
    /// paste, (-1.27, +1.18) mm.
    /// </summary>
    public const string PointLineAreaPasted = "point_line_area_pasted";

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
