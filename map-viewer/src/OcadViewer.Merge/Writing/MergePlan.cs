using OcadViewer.Ocd;

namespace OcadViewer.Merge.Writing;

internal enum OperationKind
{
    /// <summary>Put <see cref="Operation{T}.Source"/> where base has <see cref="Operation{T}.Target"/>.</summary>
    Replace,
    /// <summary>Remove <see cref="Operation{T}.Target"/> from base.</summary>
    Delete,
    /// <summary>Add <see cref="Operation{T}.Source"/>, which base does not have.</summary>
    Add,
}

/// <summary>One change to base's file.</summary>
/// <param name="Target">Base's version, for Replace and Delete.</param>
/// <param name="Source">The branch version to write, for Replace and Add.</param>
/// <param name="NewIdentity">Objects only: give the added copy a fresh creation date ("keep both").</param>
internal sealed record Operation<T>(OperationKind Kind, T Target, T Source, bool NewIdentity = false) where T : class;

/// <summary>Exactly what to do to base's file to produce the merged map.</summary>
internal sealed record MergePlan(
    IReadOnlyList<Operation<OcadObject>> Objects,
    IReadOnlyList<Operation<OcadSymbol>> Symbols,
    IReadOnlyList<Operation<OcadSetting>> Settings)
{
    public bool IsEmpty => Objects.Count == 0 && Symbols.Count == 0 && Settings.Count == 0;
}
