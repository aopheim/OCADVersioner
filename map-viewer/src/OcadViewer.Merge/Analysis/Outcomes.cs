namespace OcadViewer.Merge.Analysis;

/// <summary>The two edited copies. The UI gives them the user's names (e.g. Kari and Ola).</summary>
public enum Side { A, B }

/// <summary>What one branch did to an item, compared with base.</summary>
public enum ChangeKind { None, Added, Changed, Deleted }

/// <summary>What the 3-way comparison concluded about an item.</summary>
public enum Outcome
{
    /// <summary>Neither branch changed it. Such items are not reported.</summary>
    Unchanged,
    /// <summary>Only branch A changed it: a clean change.</summary>
    OnlyA,
    /// <summary>Only branch B changed it: a clean change.</summary>
    OnlyB,
    /// <summary>Both branches made the identical change: a clean change.</summary>
    BothSame,
    /// <summary>Both branches changed it differently: the user must decide.</summary>
    Conflict,
}

/// <summary>Why an item or a group of items needs the user.</summary>
public enum ConflictKind
{
    /// <summary>Both branches edited the same object, symbol or setting, differently.</summary>
    BothEdited,
    /// <summary>One branch edited it, the other deleted it.</summary>
    EditDelete,
    /// <summary>Both branches added something with the same identity, differently.</summary>
    BothAdded,
    /// <summary>Clean changes from both branches lie close together in the terrain.</summary>
    Overlap,
    /// <summary>
    /// One branch deleted a symbol that objects in the merge still use: the other
    /// branch's new or edited objects, or objects the deleting branch kept (OCAD
    /// asks whether to delete them with the symbol). Taking the deletion removes them.
    /// </summary>
    SymbolInUse,
}

public enum ItemKind { Object, Symbol, Setting }
