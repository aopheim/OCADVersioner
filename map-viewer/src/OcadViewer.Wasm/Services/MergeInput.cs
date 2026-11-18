using OcadViewer.Ocd;

namespace OcadViewer.Wasm.Services;

/// <summary>Which of the three files a role is.</summary>
public enum MergeRole { Base, A, B }

/// <summary>One file picked for a merge: its bytes, as read, and its name.</summary>
/// <remarks>
/// The bytes go to the map renderer; <see cref="File"/> is the engine's reading of
/// them. <see cref="OcadFile.Read"/> keeps its own copy so nothing can change a file
/// under the merge, which is why the bytes are held twice.
/// </remarks>
public sealed record MergeInput(string FileName, byte[] Bytes, OcadFile File)
{
    /// <exception cref="OcadFormatException">Not an OCAD file this merge can read.</exception>
    public static MergeInput Read(string fileName, byte[] bytes) => new(fileName, bytes, OcadFile.Read(bytes));

    /// <summary>The name a branch is shown under until the user renames it: the file name without .ocd.</summary>
    public string DefaultLabel => Path.GetFileNameWithoutExtension(FileName);
}
