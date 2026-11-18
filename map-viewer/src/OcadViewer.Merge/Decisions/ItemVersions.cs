using OcadViewer.Merge.Analysis;

namespace OcadViewer.Merge.Decisions;

/// <summary>Which version of an item a choice means. Shared by the precedence and symbol rules.</summary>
internal static class ItemVersions
{
    /// <summary>The choice a clean change carries by default: its own side. Null for a conflict.</summary>
    public static Choice? Own(MergeItem item) => item.Outcome switch {
        Outcome.OnlyA or Outcome.BothSame => Choice.A,
        Outcome.OnlyB => Choice.B,
        _ => null,
    };

    /// <summary>A file's version of an item; base's where that branch left it alone.</summary>
    public static T Version<T>(MergeItem<T> item, Choice choice) where T : class => choice switch {
        Choice.A => item.ChangeA == ChangeKind.None ? item.Base : item.A,
        Choice.B => item.ChangeB == ChangeKind.None ? item.Base : item.B,
        _ => item.Base,
    };

    /// <summary>The versions a choice keeps: two for "keep both" on an object, else one; none where it is deleted.</summary>
    public static IEnumerable<T> Versions<T>(MergeItem<T> item, Choice choice) where T : class
    {
        IEnumerable<T> versions = choice == Choice.Both && item.Kind == ItemKind.Object
            ? [Version(item, Choice.A), Version(item, Choice.B)]
            : [Version(item, choice == Choice.Both ? Choice.A : choice)];
        return versions.Where(v => v is not null);
    }
}
