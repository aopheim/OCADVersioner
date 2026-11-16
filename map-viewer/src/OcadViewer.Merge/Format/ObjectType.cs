namespace OcadViewer.Merge.Format;

/// <summary>OCAD's object types, as stored in each object record.</summary>
public enum ObjectType : byte
{
    Point = 1,
    Line = 2,
    Area = 3,
    UnformattedText = 4,
    FormattedText = 5,
    LineText = 6,
    Rectangle = 7,
}
