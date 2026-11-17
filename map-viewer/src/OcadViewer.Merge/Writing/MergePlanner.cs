using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Ocd;

namespace OcadViewer.Merge.Writing;

/// <summary>Turns resolved choices into operations on base's file.</summary>
/// <remarks>
/// What each item becomes is the resolution's business (<see cref="MergeResolution.ResultOf"/>);
/// this only compares that with base: nothing left means a deletion, a different
/// first version a replacement, and a second version - "keep both" - an addition
/// with a new identity.
/// </remarks>
internal static class MergePlanner
{
    /// <exception cref="InvalidOperationException">A conflict or overlap is still undecided.</exception>
    public static MergePlan Plan(MergeResolution resolution)
    {
        if (!resolution.IsComplete)
            throw new InvalidOperationException(
                $"{resolution.Unresolved.Count} conflict(s) still need a decision before the merged map can be written.");

        MergeAnalysis analysis = resolution.Analysis;
        return new MergePlan(
            [.. analysis.Objects.SelectMany(i => Operations(i, resolution.ResultOf(i)))
                                .Concat(resolution.RemovedWithSymbols.Select(o => new Operation<OcadObject>(OperationKind.Delete, o, null)))],
            [.. analysis.Symbols.SelectMany(i => Operations(i, resolution.ResultOf(i)))],
            [.. analysis.Settings.SelectMany(i => Operations(i, resolution.ResultOf(i)))]);
    }

    private static IEnumerable<Operation<T>> Operations<T>(MergeItem<T> item, IReadOnlyList<T> result) where T : class
    {
        if (item.Base is not null) {
            if (result.Count == 0)
                yield return new(OperationKind.Delete, item.Base, null);
            else if (!ReferenceEquals(result[0], item.Base))
                yield return new(OperationKind.Replace, item.Base, result[0]);
        }
        else if (result.Count > 0) {
            yield return new(OperationKind.Add, null, result[0]);
        }

        foreach (T copy in result.Skip(1))
            yield return new(OperationKind.Add, null, copy, NewIdentity: true);
    }
}
