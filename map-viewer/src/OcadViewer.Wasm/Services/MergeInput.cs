using System.Security.Cryptography;
using OcadViewer.Merge.Format;

namespace OcadViewer.Wasm.Services;

/// <summary>Which of the three files a role is.</summary>
public enum MergeRole { Base, A, B }

/// <summary>One file picked for a merge: its bytes, as read, and its name.</summary>
/// <param name="Hash">SHA-256 of the bytes, to recognise the same file again later.</param>
public sealed record MergeInput(string FileName, byte[] Bytes, OcadFile File, string Hash)
{
    /// <exception cref="OcadFormatException">Not an OCAD file this merge can read.</exception>
    public static MergeInput Read(string fileName, byte[] bytes)
        => new(fileName, bytes, OcadFile.Read(bytes), Convert.ToHexString(SHA256.HashData(bytes)));

    /// <summary>The name a branch is shown under until the user renames it: the file name without .ocd.</summary>
    public string DefaultLabel => Path.GetFileNameWithoutExtension(FileName);
}
