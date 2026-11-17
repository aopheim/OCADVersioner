using OcadViewer.Merge.Analysis;
using OcadViewer.Ocd;

namespace OcadViewer.Wasm.Services;

/// <summary>How merge items are named and described on screen.</summary>
public static class MergeText
{
    /// <summary>
    /// "406 Vegetation: slow running" for every symbol number in the three files:
    /// the number as OCAD shows it and the symbol's name, base's if it has one.
    /// Built once, so naming many rows does not search the files for each.
    /// </summary>
    public static IReadOnlyDictionary<int, string> SymbolNames(MergeAnalysis analysis)
    {
        Dictionary<int, string> names = [];
        foreach (OcadSymbol symbol in new[] { analysis.Base, analysis.A, analysis.B }.SelectMany(f => f.Symbols))
            names.TryAdd(symbol.Number, $"{symbol.Number / 1000.0:0.###} {symbol.Description}".Trim());
        return names;
    }

    public static string Title(IReadOnlyDictionary<int, string> symbols, MergeItem item, Localiser l) => item switch {
        ObjectItem o => SymbolName(symbols, o.Symbol),
        SymbolItem s => SymbolName(symbols, s.Number),
        SettingItem s => Setting(s, l),
        _ => item.Key,
    };

    /// <summary>A setting by what it is and its first field: "Colour: Brown", "Background map: dtm.tif".</summary>
    private static string Setting(SettingItem item, Localiser l)
    {
        string name = (item.Base ?? item.A ?? item.B).FirstField;
        return item.Type switch {
            SettingType.Color => l.Format("#_mergeColour", ("name", name)),
            SettingType.Template => l.Format("#_mergeBackgroundMap", ("name", FileName(name))),
            _ when string.IsNullOrWhiteSpace(name) => l.Format("#_mergeSettingType", ("type", item.Type)),
            _ => l.Format("#_mergeSettingNamed", ("type", item.Type), ("name", name)),
        };
    }

    /// <summary>
    /// The file name of a background map, which OCAD stores by its Windows path.
    /// Path.GetFileName will not do: in the browser only '/' separates folders.
    /// </summary>
    public static string FileName(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    private static string SymbolName(IReadOnlyDictionary<int, string> symbols, int number)
        => symbols.TryGetValue(number, out string name) ? name : $"{number / 1000.0:0.###}";

    /// <summary>What a branch did, in a word: added, edited, deleted, or nothing.</summary>
    public static string Change(ChangeKind change, Localiser l) => change switch {
        ChangeKind.Added => l["#_added"],
        ChangeKind.Changed => l["#_edited"],
        ChangeKind.Deleted => l["#_deleted"],
        _ => null,
    };

    public static string Conflict(ConflictKind? kind, Localiser l) => kind switch {
        ConflictKind.BothEdited => l["#_mergeBothEdited"],
        ConflictKind.EditDelete => l["#_mergeEditDelete"],
        ConflictKind.SymbolInUse => l["#_mergeSymbolInUse"],
        ConflictKind.BothAdded => l["#_mergeBothAdded"],
        ConflictKind.Overlap => l["#_mergeOverlap"],
        _ => null,
    };
}
