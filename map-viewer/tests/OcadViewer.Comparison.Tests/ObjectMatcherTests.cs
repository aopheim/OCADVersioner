using OcadViewer.Ocd;
using OcadViewer.Comparison;

namespace OcadViewer.Comparison.Tests;

[TestFixture]
public sealed class ObjectMatcherTests
{
    [Test]
    public void PairsEveryObjectOfAFileWithItself()
    {
        OcadFile file = Fixtures.Base;

        ObjectPairing pairing = ObjectMatcher.Match(file.Objects, file.Objects);

        Assert.Multiple(() => {
            Assert.That(pairing.Pairs.Select(p => (p.Base.Entry, p.Branch.Entry)),
                        Is.EqualTo(file.Objects.Select(o => (o.Entry, o.Entry))));
            Assert.That(pairing.OnlyInBase, Is.Empty);
            Assert.That(pairing.OnlyInBranch, Is.Empty);
        });
    }

    [Test]
    public void FollowsEditedObjectsByCreationDate()
    {
        ObjectPairing pairing = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Kari.Objects);

        OcadObject forestInKari = pairing.Pairs.Single(p => p.Base.CreatedAt == Fixtures.Created.Forest).Branch;

        Assert.That(forestInKari.CreatedAt, Is.EqualTo(Fixtures.Created.Forest));
    }

    [Test]
    public void ReportsDeletedAndAddedObjects()
    {
        ObjectPairing pairing = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Kari.Objects);

        Assert.Multiple(() => {
            Assert.That(pairing.OnlyInBase.Select(o => o.CreatedAt), Is.EqualTo(new DateTime?[] { Fixtures.Created.EastBoulder }));
            Assert.That(pairing.OnlyInBranch.Select(o => o.Symbol), Is.EquivalentTo(new[] { 508000, 101000 }));
        });
    }

    [Test]
    public void EntryNumbersAreNotIdentity()
    {
        // Kari's entry 10 is a new ride, Ola's entry 10 is a new knoll.
        ObjectPairing kari = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Kari.Objects);
        ObjectPairing ola = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Ola.Objects);

        Assert.That(kari.OnlyInBranch.Single(o => o.Entry == 10).Symbol,
                    Is.Not.EqualTo(ola.OnlyInBranch.Single(o => o.Entry == 10).Symbol));
    }

    [Test]
    public void ACopyIsANewObjectEvenThoughItSharesTheCreationDate()
    {
        // In identity_edits.ocd the east boulder was copied and pasted, and the knoll
        // duplicated: OCAD gives the copies the original's creation date.
        ObjectPairing pairing = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Read("identity_edits.ocd").Objects);

        Assert.Multiple(() => {
            Assert.That(pairing.Pairs, Has.Count.EqualTo(Fixtures.Base.Objects.Count));
            Assert.That(pairing.OnlyInBase, Is.Empty);
            Assert.That(pairing.OnlyInBranch.Select(o => o.CreatedAt),
                        Is.EquivalentTo(new DateTime?[] { Fixtures.Created.EastBoulder, Fixtures.Created.Knoll }));
        });
    }

    [Test]
    public void TheOriginalKeepsItsIdentityWhenACopyIsMade()
    {
        ObjectPairing pairing = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Read("identity_edits.ocd").Objects);

        (OcadObject b, OcadObject branch) = pairing.Pairs.Single(p => p.Base.CreatedAt == Fixtures.Created.EastBoulder);

        Assert.That(branch.Coords, Is.EqualTo(b.Coords));
    }

    [Test]
    public void ACutAndPastedObjectKeepsItsIdentity()
    {
        ObjectPairing pairing = ObjectMatcher.Match(Fixtures.Base.Objects, Fixtures.Read("identity_edits.ocd").Objects);

        (OcadObject b, OcadObject branch) = pairing.Pairs.Single(p => p.Base.CreatedAt == Fixtures.Created.MiddleBoulder);

        Assert.That(branch.Coords, Is.Not.EqualTo(b.Coords), "it was pasted somewhere else");
    }

    [Test]
    public void FallsBackToGeometryWhenFilesHaveNoCreationDates()
    {
        ObjectPairing pairing = ObjectMatcher.Match(Fixtures.Read("base_v11.ocd").Objects, Fixtures.Read("base_v11.ocd").Objects);

        Assert.Multiple(() => {
            Assert.That(pairing.Pairs, Has.Count.EqualTo(8));
            Assert.That(pairing.OnlyInBase, Is.Empty);
            Assert.That(pairing.OnlyInBranch, Is.Empty);
        });
    }
}
