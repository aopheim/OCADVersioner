using OcadViewer.Merge.Format;
using OcadViewer.Merge.Matching;

namespace OcadViewer.Merge.Analysis;

/// <summary>Compares two branches with their common base.</summary>
public static class MergeAnalyzer
{
    /// <exception cref="OcadFormatException">The files are not all the same OCAD version.</exception>
    public static MergeAnalysis Analyze(OcadFile @base, OcadFile a, OcadFile b, MergeOptions options = null)
    {
        options ??= MergeOptions.Default;
        if (a.Version != @base.Version || b.Version != @base.Version)
            throw new OcadFormatException(
                $"All three files must be saved in the same OCAD version (base is {@base.Version}, " +
                $"the branches are {a.Version} and {b.Version}). Open them in OCAD and save them alike.");

        ContentComparer comparer = new(options.ToleranceUnits(@base.Scale));

        IReadOnlyList<ObjectItem> objects = Objects(@base, a, b, comparer);
        return new MergeAnalysis(@base, a, b, options,
            objects,
            Symbols(@base, a, b, comparer),
            Settings(@base, a, b, comparer),
            OverlapDetector.Find(objects, options.OverlapBufferUnits(@base.Scale)));
    }

    private static List<ObjectItem> Objects(OcadFile @base, OcadFile a, OcadFile b, ContentComparer comparer)
    {
        ObjectPairing inA = ObjectMatcher.Match(@base.Objects, a.Objects);
        ObjectPairing inB = ObjectMatcher.Match(@base.Objects, b.Objects);
        Dictionary<OcadObject, OcadObject> toA = inA.Pairs.ToDictionary(p => p.Base, p => p.Branch);
        Dictionary<OcadObject, OcadObject> toB = inB.Pairs.ToDictionary(p => p.Base, p => p.Branch);

        List<ObjectItem> items = [];
        void Add(string key, OcadObject o, OcadObject x, OcadObject y)
        {
            ThreeWay.Verdict verdict = ThreeWay.Classify(o, x, y, comparer.Same);
            if (verdict.Outcome != Outcome.Unchanged)
                items.Add(new ObjectItem(key, o, x, y, verdict));
        }

        foreach (OcadObject o in @base.Objects)
            Add($"obj:base:{o.Entry}", o, toA.GetValueOrDefault(o), toB.GetValueOrDefault(o));

        // Added in both branches with the same identity - e.g. the same object pasted
        // into both from a shared source - is one item, not two.
        ObjectPairing added = ObjectMatcher.Match(inA.OnlyInBranch, inB.OnlyInBranch);
        foreach ((OcadObject x, OcadObject y) in added.Pairs)
            Add($"obj:a:{x.Entry}", null, x, y);
        foreach (OcadObject x in added.OnlyInBase)
            Add($"obj:a:{x.Entry}", null, x, null);
        foreach (OcadObject y in added.OnlyInBranch)
            Add($"obj:b:{y.Entry}", null, null, y);

        return items;
    }

    private static List<SymbolItem> Symbols(OcadFile @base, OcadFile a, OcadFile b, ContentComparer comparer)
        => Join(@base.Symbols, a.Symbols, b.Symbols, s => $"sym:{s.Number}",
                (key, o, x, y) => Item(key, o, x, y, comparer.Same, v => new SymbolItem(key, o, x, y, v)));

    private static List<SettingItem> Settings(OcadFile @base, OcadFile a, OcadFile b, ContentComparer comparer)
    {
        IReadOnlyDictionary<string, OcadSetting> o = SettingIdentity.Keyed(@base.Settings);
        IReadOnlyDictionary<string, OcadSetting> x = SettingIdentity.Keyed(a.Settings);
        IReadOnlyDictionary<string, OcadSetting> y = SettingIdentity.Keyed(b.Settings);

        return [.. o.Keys.Concat(x.Keys).Concat(y.Keys).Distinct()
                   .Select(key => Item(key, o.GetValueOrDefault(key), x.GetValueOrDefault(key), y.GetValueOrDefault(key),
                                       comparer.Same, v => new SettingItem(key, o.GetValueOrDefault(key),
                                                                           x.GetValueOrDefault(key), y.GetValueOrDefault(key), v)))
                   .Where(i => i is not null)];
    }

    private static List<TItem> Join<T, TItem>(IEnumerable<T> o, IEnumerable<T> x, IEnumerable<T> y, Func<T, string> keyOf,
                                              Func<string, T, T, T, TItem> make)
        where T : class where TItem : class
    {
        Dictionary<string, T> bo = o.ToDictionary(keyOf), bx = x.ToDictionary(keyOf), by = y.ToDictionary(keyOf);
        return [.. bo.Keys.Concat(bx.Keys).Concat(by.Keys).Distinct()
                   .Select(key => make(key, bo.GetValueOrDefault(key), bx.GetValueOrDefault(key), by.GetValueOrDefault(key)))
                   .Where(i => i is not null)];
    }

    private static TItem Item<T, TItem>(string key, T o, T x, T y, Func<T, T, bool> same, Func<ThreeWay.Verdict, TItem> make)
        where T : class where TItem : class
    {
        ThreeWay.Verdict verdict = ThreeWay.Classify(o, x, y, same);
        return verdict.Outcome == Outcome.Unchanged ? null : make(verdict);
    }
}
