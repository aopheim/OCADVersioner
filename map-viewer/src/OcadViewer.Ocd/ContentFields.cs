namespace OcadViewer.Ocd;

/// <summary>
/// Fields of an object: what a content comparison reports as different, and can
/// be told to ignore.
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
    /// <summary>The symbol number.</summary>
    Symbol = 8,
    /// <summary>Point, line, area, text and so on.</summary>
    Type = 16,
    /// <summary>The rotation.</summary>
    Angle = 32,
    /// <summary>Any coordinate, its flags, or how many there are.</summary>
    Coordinates = 64,
    /// <summary>The object's text.</summary>
    Text = 128,
    /// <summary>Shown or hidden, from the object index.</summary>
    Status = 256,
    /// <summary>
    /// Anything else in the record: line width, flags, object and database strings.
    /// Always set for objects from files of different OCAD versions, whose records
    /// cannot be compared byte for byte.
    /// </summary>
    Other = 512,
    /// <summary>
    /// The unnamed byte after the object-string type (OCAD 12 and later), which
    /// PurplePen reads and discards. OCAD sets it on every object when the map is
    /// opened for App Data Exchange, with nothing exported or edited.
    /// </summary>
    Reserved = 1024,
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
