namespace OcadViewer.TestData;

/// <summary>
/// The merge's own scenarios in tests/TestData/merge. Reading files, base.ocd's
/// objects and nudging coordinates are shared, in tests/TestData/Fixtures.cs.
/// </summary>
internal static partial class Fixtures
{
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
