namespace OcadViewer.Ocd;

/// <summary>
/// Fields of an object record that a content comparison can be told to ignore.
/// </summary>
/// <remarks>
/// Which fields count as noise is the caller's decision; where they are in each
/// OCAD version is this project's.
/// </remarks>
[Flags]
public enum ObjectFields
{
    None = 0,
    CreationDate = 1,
    /// <summary>Absent in OCAD 11, so ignoring it there changes nothing.</summary>
    ModificationDate = 2,
    /// <summary>The colour field, which only graphic objects (no symbol) are drawn in.</summary>
    Color = 4,
}

/// <summary>Fields of a symbol record that a content comparison can be told to ignore.</summary>
[Flags]
public enum SymbolFields
{
    None = 0,
    /// <summary>Whether the symbol is selected in the symbol box.</summary>
    Selected = 1,
    /// <summary>The symbol box status: normal, protected or hidden.</summary>
    BoxStatus = 2,
    /// <summary>The symbol's own position in the file, which moves whenever it is rewritten.</summary>
    FilePosition = 4,
}
