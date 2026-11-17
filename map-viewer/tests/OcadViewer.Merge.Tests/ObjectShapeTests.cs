using OcadViewer.Ocd;
using OcadViewer.Merge.Geometry;

namespace OcadViewer.Merge.Tests;

[TestFixture]
public sealed class ObjectShapeTests
{
    private static OcadObject Find(DateTime created) => Fixtures.Base.Objects.Single(o => o.CreatedAt == created);

    [Test]
    public void APointIsOnePositionInPaperMillimetres()
    {
        Shape knoll = ObjectShape.Of(Find(Fixtures.Created.Knoll));

        Assert.Multiple(() => {
            Assert.That(knoll.Kind, Is.EqualTo(ShapeKind.Point));
            Assert.That(knoll.Parts.Single(), Is.EqualTo(new[] { (15.11, -4.0) }));
        });
    }

    [Test]
    public void ALineKeepsItsPointsAndDropsBezierControlPoints()
    {
        OcadObject path = Find(Fixtures.Created.Footpath);

        Shape shape = ObjectShape.Of(path);

        Assert.Multiple(() => {
            Assert.That(shape.Kind, Is.EqualTo(ShapeKind.Line));
            Assert.That(shape.Parts.Single(), Has.Count.EqualTo(path.Coords.Count(c => !c.IsControlPoint)));
            Assert.That(shape.Parts.Single()[0], Is.EqualTo((path.Coords[0].X / 100.0, path.Coords[0].Y / 100.0)));
        });
    }

    [Test]
    public void AnAreaIsARing()
    {
        Shape lake = ObjectShape.Of(Find(Fixtures.Created.Lake));

        Assert.Multiple(() => {
            Assert.That(lake.Kind, Is.EqualTo(ShapeKind.Area));
            Assert.That(lake.Parts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void BoundsAreInMillimetres()
    {
        OcadObject lake = Find(Fixtures.Created.Lake);

        (double minX, double minY, double maxX, double maxY) = ObjectShape.Millimetres(lake.Bounds);

        Assert.That(minX, Is.EqualTo(lake.Bounds.MinX / 100.0));
        Assert.That(maxY, Is.EqualTo(lake.Bounds.MaxY / 100.0));
        Assert.That(minY, Is.LessThan(maxY));
        Assert.That(minX, Is.LessThan(maxX));
    }
}
