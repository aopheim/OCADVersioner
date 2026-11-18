using OcadViewer.Merge.Analysis;

namespace OcadViewer.Merge.Decisions;

/// <summary>What ends up in the merged map for an item.</summary>
public enum Choice
{
    /// <summary>Base's version: both branches' changes are rejected.</summary>
    Base,
    /// <summary>Branch A's version.</summary>
    A,
    /// <summary>Branch B's version.</summary>
    B,
    /// <summary>Objects only: both branches' versions, the second as a new object.</summary>
    Both,
}

/// <summary>The user's choices, by item and overlap key. Plain data: no analysis inside.</summary>
/// <remarks>
/// Keys rather than items, so decisions survive being saved and restored against a
/// fresh analysis of the same files (keys are stable; see MergeItem.Key).
/// </remarks>
public sealed class MergeDecisions
{
    private readonly Dictionary<string, Choice> items;
    private readonly Dictionary<string, Choice> overlaps;

    public MergeDecisions() : this([], []) { }

    private MergeDecisions(Dictionary<string, Choice> items, Dictionary<string, Choice> overlaps)
    {
        this.items = items;
        this.overlaps = overlaps;
    }

    public IReadOnlyDictionary<string, Choice> Items => items;
    public IReadOnlyDictionary<string, Choice> Overlaps => overlaps;

    public void Decide(string itemKey, Choice choice) => items[itemKey] = choice;

    /// <exception cref="ArgumentException">"Both" for a symbol or setting, which can only exist once.</exception>
    public void Decide(MergeItem item, Choice choice)
    {
        if (choice == Choice.Both && item.Kind != ItemKind.Object)
            throw new ArgumentException($"A {item.Kind.ToString().ToLowerInvariant()} can only be taken from one file.", nameof(choice));
        Decide(item.Key, choice);
    }

    public void DecideOverlap(string overlapKey, Choice choice) => overlaps[overlapKey] = choice;

    public void Forget(string key)
    {
        items.Remove(key);
        overlaps.Remove(key);
    }

    public MergeDecisionsSnapshot ToSnapshot() => new(new Dictionary<string, Choice>(items), new Dictionary<string, Choice>(overlaps));

    public static MergeDecisions FromSnapshot(MergeDecisionsSnapshot snapshot)
        => new(new Dictionary<string, Choice>(snapshot?.Items ?? new Dictionary<string, Choice>()),
               new Dictionary<string, Choice>(snapshot?.Overlaps ?? new Dictionary<string, Choice>()));
}

/// <summary>An immutable copy of a set of decisions, for undo and for saving.</summary>
public sealed record MergeDecisionsSnapshot(IReadOnlyDictionary<string, Choice> Items, IReadOnlyDictionary<string, Choice> Overlaps);
