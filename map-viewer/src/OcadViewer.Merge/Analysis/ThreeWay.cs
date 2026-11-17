namespace OcadViewer.Merge.Analysis;

/// <summary>
/// The 3-way rule, once, for every kind of item: compare each branch with base,
/// then the branches with each other.
/// </summary>
public static class ThreeWay
{
    public readonly record struct Verdict(Outcome Outcome, ChangeKind ChangeA, ChangeKind ChangeB, ConflictKind? Conflict);

    /// <param name="same">Whether two versions that both exist are the same.</param>
    public static Verdict Classify<T>(T @base, T a, T b, Func<T, T, bool> same) where T : class
    {
        ChangeKind changeA = Change(@base, a, same);
        ChangeKind changeB = Change(@base, b, same);

        if (changeA == ChangeKind.None && changeB == ChangeKind.None)
            return new(Outcome.Unchanged, changeA, changeB, null);
        if (changeB == ChangeKind.None)
            return new(Outcome.OnlyA, changeA, changeB, null);
        if (changeA == ChangeKind.None)
            return new(Outcome.OnlyB, changeA, changeB, null);
        if (Equal(a, b, same))
            return new(Outcome.BothSame, changeA, changeB, null);

        ConflictKind conflict = @base is null ? ConflictKind.BothAdded
                              : a is null || b is null ? ConflictKind.EditDelete
                              : ConflictKind.BothEdited;
        return new(Outcome.Conflict, changeA, changeB, conflict);
    }

    private static ChangeKind Change<T>(T @base, T side, Func<T, T, bool> same) where T : class
        => (@base, side) switch {
            (null, null) => ChangeKind.None,
            (null, _) => ChangeKind.Added,
            (_, null) => ChangeKind.Deleted,
            _ => same(@base, side) ? ChangeKind.None : ChangeKind.Changed,
        };

    private static bool Equal<T>(T x, T y, Func<T, T, bool> same) where T : class
        => x is null ? y is null : y is not null && same(x, y);
}
