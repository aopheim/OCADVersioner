namespace OcadViewer.Ocd;

/// <summary>An object's status, as OCAD keeps it in the object index.</summary>
public enum ObjectStatus : byte
{
    /// <summary>Shown.</summary>
    Normal = 1,
    /// <summary>Hidden by the user.</summary>
    Hidden = 2,
    /// <summary>Deleted: OCAD leaves the record in place with this status.</summary>
    Deleted = 3,
}
