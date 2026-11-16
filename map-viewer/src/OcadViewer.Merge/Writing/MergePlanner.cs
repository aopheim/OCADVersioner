using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;

namespace OcadViewer.Merge.Writing;

/// <summary>Turns resolved choices into operations on base's file.</summary>
public static class MergePlanner
{
    /// <param name="preview">Plan what the map looks like so far: undecided conflicts
    /// stay as base. A preview is for showing, never for saving.</param>
    /// <exception cref="InvalidOperationException">A conflict or overlap is still undecided
    /// and this is not a preview.</exception>
    public static MergePlan Plan(MergeResolution resolution, bool preview = false)
    {
        if (!preview && !resolution.IsComplete)
            throw new InvalidOperationException(
                $"{resolution.Unresolved.Count} conflict(s) still need a decision before the merged map can be written.");

        MergeAnalysis analysis = resolution.Analysis;
        return new MergePlan(
            [.. analysis.Objects.SelectMany(i => For(i, resolution.ChoiceFor(i) ?? Choice.Base))],
            [.. analysis.Symbols.SelectMany(i => For(i, resolution.ChoiceFor(i) ?? Choice.Base))],
            [.. analysis.Settings.SelectMany(i => For(i, resolution.ChoiceFor(i) ?? Choice.Base))]);
    }

    private static IEnumerable<Operation<T>> For<T>(MergeItem<T> item, Choice choice) where T : class
    {
        switch (choice) {
            case Choice.A:
                return Take(item, Side.A);
            case Choice.B:
                return Take(item, Side.B);
            case Choice.Both:
                return KeepBoth(item);
            default:
                return [];
        }
    }

    /// <summary>One branch's version in base's place. Nothing to do if that branch left it alone.</summary>
    private static IEnumerable<Operation<T>> Take<T>(MergeItem<T> item, Side side) where T : class
    {
        if (item.Change(side) == ChangeKind.None)
            yield break;

        T version = item.Version(side);
        if (item.Base is null) {
            if (version is not null)
                yield return new(OperationKind.Add, null, version);
        }
        else if (version is null) {
            yield return new(OperationKind.Delete, item.Base, null);
        }
        else {
            yield return new(OperationKind.Replace, item.Base, version);
        }
    }

    /// <summary>
    /// A's version keeps the identity; B's is added beside it as a new object. If one
    /// branch deleted the item, keeping both means keeping the one that still exists.
    /// </summary>
    private static IEnumerable<Operation<T>> KeepBoth<T>(MergeItem<T> item) where T : class
    {
        if (item.A is null)
            return Take(item, Side.B);
        if (item.B is null)
            return Take(item, Side.A);
        return Take(item, Side.A).Append(new(OperationKind.Add, null, item.B, NewIdentity: true));
    }
}
