using OcadViewer.Core;

namespace OcadViewer.Diff.Tests;

/// <summary>
/// The map store keeps a diff snapshot beside every parsed map, so the app can
/// compare two versions it has already loaded without reading either file again.
/// </summary>
[TestFixture]
public sealed class MapStoreSnapshotTests
{
    [Test]
    public void ALoadedMapCarriesASnapshotOfItsObjects()
    {
        using MapStore store = new();

        StoredMap map = store.Load("V1", Fixture.PathOf(Fixture.PointLineArea), Fixture.Bytes(Fixture.PointLineArea));

        Assert.That(map.Snapshot.Objects.Keys, Is.EquivalentTo(new[] { 3, 4, 5 }));
    }

    [Test]
    public void TwoLoadedVersionsCanBeComparedFromTheStore()
    {
        using MapStore store = new();
        store.Load("V1", Fixture.PathOf(Fixture.PointLineArea), Fixture.Bytes(Fixture.PointLineArea));
        store.Load("V2", Fixture.PathOf(Fixture.PointLineAreaMoved), Fixture.Bytes(Fixture.PointLineAreaMoved));

        ObjectDiff diff = new OcadDiffService().Compare(store.Get("V1").Snapshot, store.Get("V2").Snapshot);

        Assert.That(diff.Edited, Has.Count.EqualTo(3));
    }
}
