using OcadViewer.Merge.Format;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// The OCAD files in tests/TestData/merge, as drawn in OCAD. Their README says
/// what each one contains and what merging them must report.
/// </summary>
internal static class Fixtures
{
    public static byte[] Bytes(string name)
        => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "merge", name));

    public static OcadFile Read(string name) => OcadFile.Read(Bytes(name));

    public static OcadFile Base => Read("base.ocd");
    public static OcadFile Kari => Read("kari.ocd");
    public static OcadFile Ola => Read("ola.ocd");

    /// <summary>A copy of <paramref name="obj"/> with every coordinate passed through <paramref name="change"/>.</summary>
    public static OcadObject WithCoords(OcadObject obj, Func<OcadCoord, OcadCoord> change)
    {
        byte[] record = obj.Record.ToArray();
        for (int i = 0; i < obj.Coords.Count; i++)
            change(obj.Coords[i]).Write(record.AsSpan(obj.Layout.ObjectHeaderSize + i * OcadCoord.Size));
        return new OcadObject(obj.Layout, obj.Slot, obj.IndexEntry, record);
    }

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
