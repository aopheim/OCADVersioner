using OcadViewer.Ocd;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// One parsed <see cref="OcadFile"/> per version of the open project, for comparing.
/// </summary>
/// <remarks>
/// <para>Separate from the renderer's store on purpose: comparing needs the file's
/// records, drawing needs PurplePen's model, and neither should know about the
/// other. The price is reading each version's bytes a second time, from the
/// folder the browser already has open.</para>
/// <para>A release never changes once written, so it is read once. The working file
/// can change whenever OCAD saves it, so it is read every time it is compared.
/// Only the versions in the last comparison are kept.</para>
/// </remarks>
public sealed class ComparisonCache(ProjectService project)
{
    private readonly Dictionary<string, OcadFile> files = new(StringComparer.Ordinal);

    /// <summary>The version, parsed; null when it cannot be compared (OCAD 10 and older, or damaged).</summary>
    public async Task<OcadFile> GetAsync(string versionName)
    {
        Versioning.MapSource source = project.SourceFor(versionName);

        if (!source.Reload && files.TryGetValue(versionName, out OcadFile cached))
            return cached;

        byte[] bytes = await project.ReadBytesAsync(source.Path);
        OcadFile file;
        try {
            file = OcadFile.Read(bytes);
        }
        catch (OcadFormatException) {
            file = null;
        }

        files[versionName] = file;
        return file;
    }

    /// <summary>Drops every version but these.</summary>
    public void KeepOnly(params string[] versionNames)
    {
        foreach (string name in files.Keys.Except(versionNames, StringComparer.Ordinal).ToList())
            files.Remove(name);
    }

    /// <summary>Forgets everything: another folder's "V1" is another map.</summary>
    public void Clear() => files.Clear();
}
