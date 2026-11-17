using OcadViewer.Merge.Analysis;
using OcadViewer.Merge.Decisions;
using OcadViewer.Ocd;

namespace OcadViewer.Wasm.Services;

/// <summary>How merge items are named and described on screen.</summary>
public static class MergeText
{
    /// <summary>"406 Vegetation: slow running": the symbol number as OCAD shows it and its name.</summary>
    public static string Symbol(MergeAnalysis analysis, int number)
    {
        string description = new[] { analysis.Base, analysis.A, analysis.B }
            .SelectMany(f => f.Symbols)
            .FirstOrDefault(s => s.Number == number)?.Description;
        return $"{number / 1000.0:0.###} {description}".Trim();
    }

    public static string Title(MergeAnalysis analysis, MergeItem item, Localiser l) => item switch {
        ObjectItem o => Symbol(analysis, o.Symbol),
        SymbolItem s => Symbol(analysis, s.Number),
        SettingItem s when s.Type == SettingType.Color
            => l.Format("#_mergeColour", ("name", (s.Base ?? s.A ?? s.B).FirstField)),
        SettingItem s => l.Format("#_mergeSettingType", ("type", s.Type)),
        _ => item.Key,
    };

    /// <summary>
    /// Why a symbol is kept against the user's choice, or what goes with it if its
    /// deletion is taken; null when objects do not decide for it.
    /// </summary>
    public static string SymbolUse(SymbolItem symbol, MergeResolution resolution, Localiser l)
    {
        int users = resolution.UsersOf(symbol.Number).Count;
        if (symbol.Conflict == ConflictKind.SymbolInUse)
            return l.Format("#_mergeSymbolGoesWith", ("count", users));
        return MergeChoices.LockedByObjects(symbol, resolution) ? l.Format("#_mergeSymbolKeptForObjects", ("count", users)) : null;
    }

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
