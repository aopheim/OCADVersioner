using OcadViewer.Ocd;

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

    /// <summary>
    /// The objects in sprint_base.ocd (ISSprOM 2019, 1:4000), by the part they play
    /// in the scenario base.ocd plays out with forest symbols.
    /// </summary>
    public static class Sprint
    {
        /// <summary>Building 521 that both edit, like the forest.</summary>
        public static readonly DateTime WestBuilding = At(8, 59, 454);
        public static readonly DateTime EastBuilding = At(9, 20, 618);
        /// <summary>Open land 401 that only Ola moves.</summary>
        public static readonly DateTime OpenLand = At(9, 40, 836);
        /// <summary>Water 301 that only Kari moves, like the lake.</summary>
        public static readonly DateTime Water = At(10, 15, 419);
        /// <summary>Wall 515 that neither touches, like the footpath.</summary>
        public static readonly DateTime Wall = At(12, 33, 824);
        /// <summary>Tree 417 that Ola deletes, like the middle boulder.</summary>
        public static readonly DateTime MiddleTree = At(12, 47, 545);
        /// <summary>Tree 417 that Kari deletes and Ola moves, like the east boulder.</summary>
        public static readonly DateTime EastTree = At(12, 48, 754);
        /// <summary>Bush 418 whose symbol both change, like the knoll's.</summary>
        public static readonly DateTime Bush = At(13, 0, 243);

        private static DateTime At(int m, int s, int ms) => new(2026, 11, 17, 11, m, s, ms);
    }

    /// <summary>The objects kari_symbols.ocd and ola_symbols.ocd add or both edit.</summary>
    public static class Symbols
    {
        /// <summary>Kari's duplicate of the boulder symbol, which base does not have.</summary>
        public const int KarisBoulder = 204002;
        public const int Boulder = 204000;
        /// <summary>Kari's new object with the boulder symbol Ola deleted.</summary>
        public static readonly DateTime KarisNewBoulder = new(2026, 11, 17, 10, 55, 25, 437);
    }
}
