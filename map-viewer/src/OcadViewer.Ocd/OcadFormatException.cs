namespace OcadViewer.Ocd;

/// <summary>A file that is not an OCAD map, or not one this engine can read.</summary>
public sealed class OcadFormatException(string message) : Exception(message);
