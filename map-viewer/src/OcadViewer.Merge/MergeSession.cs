using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Merge.Writing;

namespace OcadViewer.Merge;

/// <summary>
/// One merge in progress: the analysis, the user's decisions and their history.
/// What a merge screen holds, with no screen in it.
/// </summary>
/// <remarks>
/// Every change goes through one door, so each is one undo step.
/// </remarks>
public sealed class MergeSession(MergeAnalysis analysis)
{
    private readonly Stack<MergeDecisionsSnapshot> undo = new();
    private readonly Stack<MergeDecisionsSnapshot> redo = new();
    private MergeResolution resolution;

    public MergeAnalysis Analysis { get; } = analysis;

    public MergeDecisions Decisions { get; private set; } = new();

    /// <summary>The decisions applied to the analysis. Recomputed after each change.</summary>
    public MergeResolution Resolution => resolution ??= new MergeResolution(Analysis, Decisions);

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void Decide(MergeItem item, Choice choice) => Change(d => d.Decide(item, choice));

    /// <summary>
    /// Back to the default for an item or overlap, by key: a clean change accepted,
    /// a conflict open again.
    /// </summary>
    public void Forget(string key) => Change(d => d.Forget(key));

    public void DecideOverlap(OverlapGroup group, Choice choice) => Change(d => d.DecideOverlap(group.Key, choice));

    /// <summary>Several decisions as one undo step - a region, say.</summary>
    public void DecideAll(IEnumerable<(MergeItem Item, Choice Choice)> choices)
        => Change(d => {
            foreach ((MergeItem item, Choice choice) in choices)
                d.Decide(item, choice);
        });

    public void Undo() => Step(undo, redo);

    public void Redo() => Step(redo, undo);

    /// <summary>Replaces the decisions with saved ones, starting a fresh history.</summary>
    public void Restore(MergeDecisionsSnapshot snapshot)
    {
        undo.Clear();
        redo.Clear();
        Set(MergeDecisions.FromSnapshot(snapshot));
    }

    /// <exception cref="InvalidOperationException">A conflict or overlap is still undecided.</exception>
    public byte[] Write(DateTime now) => MergeWriter.Write(Resolution, now);

    public byte[] WritePreview(DateTime now) => MergeWriter.Preview(Resolution, now);

    private void Change(Action<MergeDecisions> change)
    {
        MergeDecisions next = MergeDecisions.FromSnapshot(Decisions.ToSnapshot());
        change(next);
        undo.Push(Decisions.ToSnapshot());
        redo.Clear();
        Set(next);
    }

    private void Step(Stack<MergeDecisionsSnapshot> from, Stack<MergeDecisionsSnapshot> to)
    {
        if (from.Count == 0)
            return;
        to.Push(Decisions.ToSnapshot());
        Set(MergeDecisions.FromSnapshot(from.Pop()));
    }

    private void Set(MergeDecisions decisions)
    {
        Decisions = decisions;
        resolution = null;
    }
}
