using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Writing;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// Merges in the older formats and with the kinds of content the main fixtures
/// do not have: text objects, hidden objects, strings tied to an object.
/// </summary>
[TestFixture]
public sealed class FormatCoverageTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0);

    private static byte[] Merge(OcadFile @base, OcadFile a, OcadFile b, Choice conflicts = Choice.A)
    {
        MergeAnalysis analysis = MergeAnalyzer.Analyze(@base, a, b);
        MergeDecisions decisions = new();
        MergeResolution open = new(analysis, decisions);
        foreach (MergeItem item in analysis.AllItems.Where(open.NeedsDecision))
            decisions.Decide(item, conflicts == Choice.Both && item.Kind != ItemKind.Object ? Choice.A : conflicts);
        foreach (OverlapGroup group in analysis.Overlaps)
            decisions.DecideOverlap(group.Key, Choice.Both);
        return MergeWriter.Write(new MergeResolution(analysis, decisions), Now);
    }

    private static string Shape(OcadObject o) => $"{o.Symbol}:{string.Join(';', o.Coords.Select(c => $"{c.X},{c.Y}"))}";

    // ---- OCAD 12 and 11 ----

    [Test]
    public void Ocad12FilesMergeLikeOcad2018Files()
    {
        // OCAD 12 keeps creation dates, so identity works as in 2018 even though
        // saving as 12 renumbers the entries.
        MergeAnalysis v12 = MergeAnalyzer.Analyze(Fixtures.Read("base_v12.ocd"), Fixtures.Read("kari_v12.ocd"), Fixtures.Read("ola_v12.ocd"));
        MergeAnalysis v2018 = MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Kari, Fixtures.Ola);

        // Objects and symbols: the copies were saved as 12 on different days, which may
        // touch parameter strings in ways that have nothing to do with the format.
        static string[] Summary(MergeAnalysis a)
            => [.. a.Objects.Cast<MergeItem>().Concat(a.Symbols)
                    .Select(i => $"{i.Kind} {i.Outcome} {i.ChangeA} {i.ChangeB} {i.Conflict}").Order()];

        Assert.That(Summary(v12), Is.EqualTo(Summary(v2018)));
    }

    [Test]
    public void FilesWithANewerVersionNumberMergeLike2018Files()
    {
        // Decided: version numbers above 2018 are accepted and read with its layout.
        static OcadFile As2025(string name)
            => OcadFile.Read(Patch(Fixtures.Bytes(name), bytes => BitConverter.TryWriteBytes(bytes.AsSpan(FileLayout.VersionOffset), (short)2025)));

        OcadFile merged = OcadFile.Read(Merge(As2025("base.ocd"), As2025("kari.ocd"), As2025("ola.ocd")));
        OcadFile expected = OcadFile.Read(Merge(Fixtures.Base, Fixtures.Kari, Fixtures.Ola));

        Assert.Multiple(() => {
            Assert.That(merged.Version, Is.EqualTo(2025));
            Assert.That(merged.Objects.Select(Shape), Is.EquivalentTo(expected.Objects.Select(Shape)));
        });
    }

    [TestCase("base_v12.ocd", "kari_v12.ocd", "ola_v12.ocd")]
    [TestCase("base_v11.ocd", "kari_v11.ocd", "ola_v11.ocd")]
    public void EveryCleanChangeReachesTheMergedFile(string baseName, string kariName, string olaName)
    {
        // OCAD 11 written by OCAD 2020 has no creation dates: identity falls back to
        // identical geometry, so an edit becomes a deletion plus an addition. Noisy,
        // but nothing is lost - each branch's own shapes end up in the merge.
        OcadFile @base = Fixtures.Read(baseName), kari = Fixtures.Read(kariName), ola = Fixtures.Read(olaName);
        OcadFile merged = OcadFile.Read(Merge(@base, kari, ola));
        string[] shapes = [.. merged.Objects.Select(Shape)];

        string Lake(OcadFile f) => Shape(f.Objects.Single(o => o.Symbol == 301000));
        string OpenLand(OcadFile f) => Shape(f.Objects.Single(o => o.Symbol == 401000));

        Assert.Multiple(() => {
            Assert.That(merged.Version, Is.EqualTo(@base.Version));
            Assert.That(shapes, Does.Contain(Lake(kari)), "Kari's lake");
            Assert.That(shapes, Does.Contain(OpenLand(ola)), "Ola's open land");
            Assert.That(shapes, Does.Not.Contain(Lake(@base)), "base's lake");
            Assert.That(shapes, Does.Not.Contain(OpenLand(@base)), "base's open land");
        });
    }

    [TestCase("base_v12.ocd", "kari_v12.ocd", "ola_v12.ocd")]
    [TestCase("base_v11.ocd", "kari_v11.ocd", "ola_v11.ocd")]
    public void KeepingBothWritesAFileOfTheSameVersionThatReadsBack(string baseName, string kariName, string olaName)
    {
        OcadFile @base = Fixtures.Read(baseName);
        OcadFile merged = OcadFile.Read(Merge(@base, Fixtures.Read(kariName), Fixtures.Read(olaName), Choice.Both));

        Assert.Multiple(() => {
            Assert.That(merged.Version, Is.EqualTo(@base.Version));
            Assert.That(merged.Objects, Has.Count.GreaterThanOrEqualTo(@base.Objects.Count));
        });
    }

    // ---- Text objects ----

    [Test]
    public void AnEditedTextIsOneChangeAndIsWrittenWhole()
    {
        // texts.ocd adds an unformatted text and a formatted text to base;
        // texts_edited.ocd changes the first from "Storigarden" to "Storigarden nord".
        OcadFile texts = Fixtures.Read("texts.ocd"), edited = Fixtures.Read("texts_edited.ocd");
        MergeAnalysis analysis = MergeAnalyzer.Analyze(texts, edited, texts);
        OcadFile merged = OcadFile.Read(Merge(texts, edited, texts));

        ObjectItem change = analysis.Objects.Single();
        OcadObject written = merged.Objects.Single(o => o.CreatedAt == change.Base.CreatedAt);

        Assert.Multiple(() => {
            Assert.That(change.Outcome, Is.EqualTo(Outcome.OnlyA));
            Assert.That(change.Base.Type, Is.EqualTo(ObjectType.UnformattedText));
            Assert.That(written.SameContent(change.A, ObjectFields.None, 0), Is.True, "the text, byte for byte");
        });
    }

    [Test]
    public void TextsAreNotChangedByMergingThemWithThemselves()
    {
        OcadFile texts = Fixtures.Read("texts.ocd");

        Assert.Multiple(() => {
            Assert.That(MergeAnalyzer.Analyze(texts, texts, texts).AllItems, Is.Empty);
            Assert.That(Merge(texts, texts, texts), Is.EqualTo(Fixtures.Bytes("texts.ocd")));
            Assert.That(texts.Objects.Select(o => o.Type), Does.Contain(ObjectType.FormattedText));
        });
    }

    // ---- Hidden objects and strings tied to an object (files OCAD was not made to write) ----

    [Test]
    public void HidingAnObjectIsAChangeAndStaysHidden()
    {
        OcadFile @base = Fixtures.Base;
        OcadObject lake = @base.Objects.Single(o => o.CreatedAt == Fixtures.Created.Lake);
        OcadFile hidden = OcadFile.Read(Patch(Fixtures.Bytes("base.ocd"),
            bytes => bytes[@base.ObjectIndex.EntryOffset(lake.Entry - 1) + FileLayout.IndexStatusOffset] = (byte)ObjectStatus.Hidden));

        MergeAnalysis analysis = MergeAnalyzer.Analyze(@base, hidden, @base);
        OcadFile merged = OcadFile.Read(Merge(@base, hidden, @base));

        Assert.Multiple(() => {
            Assert.That(analysis.Objects.Single().Outcome, Is.EqualTo(Outcome.OnlyA));
            Assert.That(merged.Objects.Single(o => o.CreatedAt == Fixtures.Created.Lake).Status, Is.EqualTo(ObjectStatus.Hidden));
        });
    }

    [Test]
    public void AStringTiedToAnObjectNeverTakesPart()
    {
        // No real map has one; they belong to objects, and entries are not identities.
        // Kari renamed colour 5; tied to an object in both files, the rename is not merged.
        static OcadFile Tied(string name)
        {
            OcadFile file = Fixtures.Read(name);
            OcadSetting brown = file.Settings.Single(s => s.Type == SettingType.Color && s.Field('n') == "5");
            return OcadFile.Read(Patch(Fixtures.Bytes(name), bytes =>
                BitConverter.TryWriteBytes(bytes.AsSpan(file.StringIndex.EntryOffset(brown.Entry) + FileLayout.StringObjectIndexOffset), 1)));
        }

        OcadFile @base = Tied("base.ocd");

        Assert.Multiple(() => {
            Assert.That(MergeAnalyzer.Analyze(Fixtures.Base, Fixtures.Read("kari_settings.ocd"), Fixtures.Base).Settings,
                        Is.Not.Empty, "the rename, when the string is map-wide");
            Assert.That(MergeAnalyzer.Analyze(@base, Tied("kari_settings.ocd"), @base).Settings, Is.Empty);
        });
    }

    private static byte[] Patch(byte[] bytes, Action<byte[]> patch)
    {
        patch(bytes);
        return bytes;
    }
}
