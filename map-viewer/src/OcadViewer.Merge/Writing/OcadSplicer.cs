using OcadViewer.Ocd;

namespace OcadViewer.Merge.Writing;

/// <summary>Carries out a merge plan on a copy of base's file.</summary>
/// <remarks>
/// <see cref="OcadEditor"/> does the byte-faithful writing; this decides only what
/// to give it. An object kept twice is added with a fresh creation date so the two
/// copies stay distinct in later merges; dates count up from <c>now</c> by a
/// millisecond each, which is OCAD's own precision.
/// </remarks>
public static class OcadSplicer
{
    public static byte[] Write(OcadFile @base, MergePlan plan, DateTime now)
    {
        OcadEditor editor = new(@base);
        DateTime nextIdentity = now;

        foreach (Operation<OcadObject> op in plan.Objects) {
            OcadObject source = op.Source;
            if (op.Kind == OperationKind.Add && op.NewIdentity) {
                source = source.WithCreatedAt(nextIdentity);
                nextIdentity = nextIdentity.AddMilliseconds(1);
            }
            Apply(op, source, editor.Replace, editor.Add, editor.Delete);
        }

        foreach (Operation<OcadSymbol> op in plan.Symbols)
            Apply(op, op.Source, editor.Replace, editor.Add, editor.Delete);

        foreach (Operation<OcadSetting> op in plan.Settings)
            Apply(op, op.Source, editor.Replace, editor.Add, editor.Delete);

        return editor.ToArray();
    }

    private static void Apply<T>(Operation<T> op, T source, Action<T, T> replace, Action<T> add, Action<T> delete) where T : class
    {
        switch (op.Kind) {
            case OperationKind.Replace: replace(op.Target, source); break;
            case OperationKind.Add: add(source); break;
            case OperationKind.Delete: delete(op.Target); break;
        }
    }
}
