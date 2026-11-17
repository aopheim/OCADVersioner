using OcadViewer.Ocd;

namespace OcadViewer.Merge.Analysis;

/// <summary>
/// One thing that exists in at least one of the three files and was changed by at
/// least one branch: an object, a symbol or a setting.
/// </summary>
public abstract class MergeItem
{
    /// <summary>Unique within an analysis and the same every time the same files are analysed.</summary>
    public string Key { get; }

    public abstract ItemKind Kind { get; }

    public Outcome Outcome { get; }
    public ChangeKind ChangeA { get; }
    public ChangeKind ChangeB { get; }

    /// <summary>Why the user must decide, or null for a clean change.</summary>
    public ConflictKind? Conflict { get; }

    protected MergeItem(string key, ThreeWay.Verdict verdict)
    {
        Key = key;
        Outcome = verdict.Outcome;
        ChangeA = verdict.ChangeA;
        ChangeB = verdict.ChangeB;
        Conflict = verdict.Conflict;
    }

    public ChangeKind Change(Side side) => side == Side.A ? ChangeA : ChangeB;

    public override string ToString() => $"{Key} {Outcome} A:{ChangeA} B:{ChangeB}";
}

/// <summary>A merge item and its version in each file; null where it does not exist.</summary>
public abstract class MergeItem<T> : MergeItem where T : class
{
    public T Base { get; }
    public T A { get; }
    public T B { get; }

    protected MergeItem(string key, T @base, T a, T b, ThreeWay.Verdict verdict) : base(key, verdict)
    {
        Base = @base;
        A = a;
        B = b;
    }

    public T Version(Side side) => side == Side.A ? A : B;
}

public sealed class ObjectItem(string key, OcadObject @base, OcadObject a, OcadObject b, ThreeWay.Verdict verdict)
    : MergeItem<OcadObject>(key, @base, a, b, verdict)
{
    public override ItemKind Kind => ItemKind.Object;

    /// <summary>Where the item is in any of its versions.</summary>
    public Bounds Bounds => new[] { Base, A, B }.Where(o => o is not null)
                                               .Aggregate(Ocd.Bounds.Empty, (u, o) => u.Union(o.Bounds));

    /// <summary>The symbol of the newest version: a branch's if it has one, else base's.</summary>
    public int Symbol => (A ?? B ?? Base).Symbol;
}

public sealed class SymbolItem(string key, OcadSymbol @base, OcadSymbol a, OcadSymbol b, ThreeWay.Verdict verdict)
    : MergeItem<OcadSymbol>(key, @base, a, b, verdict)
{
    public override ItemKind Kind => ItemKind.Symbol;

    public int Number => (Base ?? A ?? B).Number;
}

public sealed class SettingItem(string key, OcadSetting @base, OcadSetting a, OcadSetting b, ThreeWay.Verdict verdict)
    : MergeItem<OcadSetting>(key, @base, a, b, verdict)
{
    public override ItemKind Kind => ItemKind.Setting;

    public int Type => (Base ?? A ?? B).Type;
}
