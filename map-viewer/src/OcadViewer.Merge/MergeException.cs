namespace OcadViewer.Merge;

/// <summary>
/// Readable OCAD files that cannot be merged together, e.g. saved in different
/// versions. A damaged or unreadable file is an <c>OcadFormatException</c> instead.
/// </summary>
public sealed class MergeException(string message) : Exception(message);
