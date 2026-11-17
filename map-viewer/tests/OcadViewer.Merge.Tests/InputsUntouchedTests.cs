using System.Security.Cryptography;
using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Format;
using OcadViewer.Merge.Writing;

namespace OcadViewer.Merge.Tests;

/// <summary>
/// A merge never changes the files it merges: base and both edited copies are
/// byte-for-byte what the user picked, before and after, whatever was decided.
/// </summary>
/// <remarks>
/// The merged map is a new file. If the engine ever wrote into the bytes it was
/// handed - patching a creation date in place for "keep both", say, or splicing
/// into base's array instead of a copy - the user's own maps would be altered in
/// memory and could be saved back by whatever held them. These tests pin that it
/// does not, for every kind of decision the engine can carry out.
/// </remarks>
[TestFixture]
public sealed class InputsUntouchedTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 12, 0, 0);

    /// <summary>Every kind of decision, so each code path that writes is exercised.</summary>
    public static IEnumerable<TestCaseData> Merges()
    {
        yield return Case("take each side", "base.ocd", "kari.ocd", "ola.ocd", (analysis, decisions) => {
            decisions.Decide(Forest(analysis), Choice.A);
            decisions.Decide(EastBoulder(analysis), Choice.B);
            decisions.Decide(analysis.Symbols.Single(), Choice.A);
            foreach (OverlapGroup group in analysis.Overlaps)
                decisions.DecideOverlap(group.Key, Choice.Both);
        });

        yield return Case("keep both copies of a conflict", "base.ocd", "kari.ocd", "ola.ocd", (analysis, decisions) => {
            decisions.Decide(Forest(analysis), Choice.Both);
            decisions.Decide(EastBoulder(analysis), Choice.Both);
            decisions.Decide(analysis.Symbols.Single(), Choice.B);
            foreach (OverlapGroup group in analysis.Overlaps)
                decisions.DecideOverlap(group.Key, Choice.A);
        });

        yield return Case("reject everything", "base.ocd", "kari.ocd", "ola.ocd", (analysis, decisions) => {
            foreach (MergeItem item in analysis.AllItems)
                decisions.Decide(item, Choice.Base);
            foreach (OverlapGroup group in analysis.Overlaps)
                decisions.DecideOverlap(group.Key, Choice.Base);
        });

        yield return Case("colours and symbols added by both", "base.ocd", "kari_settings.ocd", "ola_settings.ocd", (analysis, decisions) => {
            decisions.Decide(analysis.Settings.Single(), Choice.B);
            decisions.Decide(analysis.Symbols.Single(), Choice.A);
        });

        yield return Case("copies that share a creation date", "base.ocd", "identity_edits.ocd", "ola.ocd", (analysis, decisions) => {
            foreach (MergeItem item in analysis.AllItems.Where(i => i.Outcome == Outcome.Conflict))
                decisions.Decide(item, item.Kind == ItemKind.Object ? Choice.Both : Choice.A);
            foreach (OverlapGroup group in analysis.Overlaps)
                decisions.DecideOverlap(group.Key, Choice.Both);
        });
    }

    [TestCaseSource(nameof(Merges))]
    public void TheBytesHandedToTheMergeAreUnchanged(string baseName, string aName, string bName,
                                                    Action<MergeAnalysis, MergeDecisions> decide)
    {
        byte[] @base = Fixtures.Bytes(baseName), a = Fixtures.Bytes(aName), b = Fixtures.Bytes(bName);
        byte[] baseBefore = [.. @base], aBefore = [.. a], bBefore = [.. b];

        byte[] merged = Merge(@base, a, b, decide);

        Assert.Multiple(() => {
            Assert.That(@base, Is.EqualTo(baseBefore), "base was changed");
            Assert.That(a, Is.EqualTo(aBefore), $"{aName} was changed");
            Assert.That(b, Is.EqualTo(bBefore), $"{bName} was changed");
            Assert.That(merged, Is.Not.SameAs(@base), "the merge must be a new array, not base's");
        });
    }

    [TestCaseSource(nameof(Merges))]
    public void TheFilesOnDiskAreUnchanged(string baseName, string aName, string bName,
                                           Action<MergeAnalysis, MergeDecisions> decide)
    {
        string[] names = [baseName, aName, bName];
        string[] before = [.. names.Select(HashOfFile)];

        Merge(Fixtures.Bytes(baseName), Fixtures.Bytes(aName), Fixtures.Bytes(bName), decide);

        Assert.That(names.Select(HashOfFile), Is.EqualTo(before));
    }

    [Test]
    public void ChangingTheMergedFileDoesNotReachTheInputs()
    {
        byte[] @base = Fixtures.Bytes("base.ocd"), a = Fixtures.Bytes("kari.ocd"), b = Fixtures.Bytes("ola.ocd");
        byte[] baseBefore = [.. @base];
        byte[] merged = Merge(@base, a, b, (analysis, decisions) => {
            decisions.Decide(Forest(analysis), Choice.Both);
            decisions.Decide(EastBoulder(analysis), Choice.B);
            decisions.Decide(analysis.Symbols.Single(), Choice.A);
            foreach (OverlapGroup group in analysis.Overlaps)
                decisions.DecideOverlap(group.Key, Choice.Both);
        });

        Array.Fill(merged, (byte)0xFF);

        Assert.That(@base, Is.EqualTo(baseBefore));
    }

    [Test]
    public void MergingAFileWithItselfLeavesTheInputAndTheResultIdentical()
    {
        byte[] @base = Fixtures.Bytes("base.ocd");
        byte[] before = [.. @base];

        byte[] merged = Merge(@base, @base, @base, (_, _) => { });

        Assert.Multiple(() => {
            Assert.That(@base, Is.EqualTo(before));
            Assert.That(merged, Is.EqualTo(before));
            Assert.That(merged, Is.Not.SameAs(@base));
        });
    }

    private static byte[] Merge(byte[] @base, byte[] a, byte[] b, Action<MergeAnalysis, MergeDecisions> decide)
    {
        MergeAnalysis analysis = MergeAnalyzer.Analyze(OcadFile.Read(@base), OcadFile.Read(a), OcadFile.Read(b));
        MergeDecisions decisions = new();
        decide(analysis, decisions);
        return MergeWriter.Write(new MergeResolution(analysis, decisions), Now);
    }

    private static TestCaseData Case(string name, string baseName, string aName, string bName,
                                     Action<MergeAnalysis, MergeDecisions> decide)
        => new TestCaseData(baseName, aName, bName, decide).SetArgDisplayNames(name);

    private static string HashOfFile(string name)
        => Convert.ToHexString(SHA256.HashData(Fixtures.Bytes(name)));

    private static ObjectItem Forest(MergeAnalysis analysis)
        => analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.Forest);

    private static ObjectItem EastBoulder(MergeAnalysis analysis)
        => analysis.Objects.Single(i => i.Base?.CreatedAt == Fixtures.Created.EastBoulder);
}
