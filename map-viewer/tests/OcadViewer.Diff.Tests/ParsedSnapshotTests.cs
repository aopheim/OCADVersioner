namespace OcadViewer.Diff.Tests;

/// <summary>
/// What a snapshot takes from PurplePen's parsed map: symbol names and the scale.
/// </summary>
/// <remarks>
/// The only diff tests that load the vendored assemblies, apart from
/// <see cref="MapStoreSnapshotTests"/>. Where the machine's application control
/// policy refuses them, these fail with 0x800711C7 and the rest still run.
/// </remarks>
[TestFixture]
public sealed class ParsedSnapshotTests
{
    [Test]
    public void SymbolNamesComeFromTheMapsOwnSymbolTable()
    {
        OcadMapSnapshot knoll = Fixture.ParsedSnapshot(Fixture.OneKnoll);

        Assert.That(knoll.SymbolFor(109000), Is.EqualTo(new ObjectSymbol("109.0", "Small knoll")));
    }

    [Test]
    public void ASymbolTheMapDoesNotDefineHasAnEmptyName()
    {
        OcadMapSnapshot knoll = Fixture.ParsedSnapshot(Fixture.OneKnoll);

        Assert.That(knoll.SymbolFor(999999).Name, Is.Empty);
    }

    [TestCase(Fixture.OneKnoll)]
    [TestCase(Fixture.PointLineArea)]
    [TestCase(Fixture.PointLineAreaMoved)]
    [TestCase(Fixture.PointLineAreaDeleted)]
    public void EveryFixtureIsDrawnAtTheScaleTheOtherTestsAssume(string fixture)
    {
        Assert.That(Fixture.ParsedSnapshot(fixture).MetresPerUnit, Is.EqualTo(Fixture.Scale / 100_000.0));
    }
}
