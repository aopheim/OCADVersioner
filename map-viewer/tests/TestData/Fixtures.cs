using OcadViewer.Ocd;

namespace OcadViewer.TestData;

/// <summary>
/// The OCAD files under tests/TestData, as drawn in OCAD: <c>merge/</c> for the
/// merge (its README says what each contains and what merging them must report)
/// and <c>diff/</c> for the two-way comparison.
/// </summary>
/// <remarks>
/// Shared by every test project as a linked source file, which copies the folders
/// it uses into its output under <c>TestData/</c>. A project's own scenarios go in
/// its own part of this partial class.
/// </remarks>
internal static partial class Fixtures
{
    /// <summary>A file from <c>merge/</c>.</summary>
    public static byte[] Bytes(string name) => Bytes("merge", name);

    public static byte[] Bytes(string folder, string name)
        => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", folder, name));

    /// <summary>A file from <c>merge/</c>, parsed.</summary>
    public static OcadFile Read(string name) => OcadFile.Read(Bytes(name));

    /// <summary>A file from <c>diff/</c>, parsed.</summary>
    public static OcadFile Diff(string name) => OcadFile.Read(Bytes("diff", name));

    public static OcadFile Base => Read("base.ocd");
    public static OcadFile Kari => Read("kari.ocd");
    public static OcadFile Ola => Read("ola.ocd");

    /// <summary>A copy of <paramref name="obj"/> with every coordinate passed through <paramref name="change"/>.</summary>
    public static OcadObject WithCoords(OcadObject obj, Func<OcadCoord, OcadCoord> change)
        => obj.WithCoords([.. obj.Coords.Select(change)]);

    /// <summary>OCAD's creation timestamps for the objects in base.ocd, by what they are.</summary>
    public static class Created
    {
        public static readonly DateTime OpenLand = At(22, 32, 13, 139);
        public static readonly DateTime Lake = At(22, 33, 29, 289);
        public static readonly DateTime Forest = At(22, 33, 47, 520);
        public static readonly DateTime Footpath = At(22, 34, 5, 870);
        public static readonly DateTime Contour = At(22, 34, 10, 353);
        public static readonly DateTime MiddleBoulder = At(22, 34, 11, 987);
        public static readonly DateTime EastBoulder = At(22, 34, 12, 570);
        public static readonly DateTime Knoll = At(22, 34, 21, 606);

        private static DateTime At(int h, int m, int s, int ms) => new(2026, 11, 16, h, m, s, ms);
    }
}
