namespace OcadViewer.Ocd.Tests;

/// <summary>
/// The OCAD files in tests/TestData/merge, as drawn in OCAD. Their README says
/// what each one contains.
/// </summary>
internal static class Fixtures
{
    public static byte[] Bytes(string name)
        => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "ocd", name));

    public static OcadFile Read(string name) => OcadFile.Read(Bytes(name));

    public static OcadFile Base => Read("base.ocd");
    public static OcadFile Kari => Read("kari.ocd");

    /// <summary>OCAD's creation timestamps for some objects in base.ocd.</summary>
    public static class Created
    {
        public static readonly DateTime Lake = new(2026, 11, 16, 22, 33, 29, 289);
        public static readonly DateTime Knoll = new(2026, 11, 16, 22, 34, 21, 606);
    }
}
