using PurplePen.MapModel;

namespace OcadViewer.Diff;

/// <summary>Everything the diff needs from one version of a map, and nothing else.</summary>
/// <remarks>
/// Taken once, when a version is parsed, so a comparison never has to go back to
/// the file or hold the map's lock. It is small - a few numbers and strings per
/// object - which is what makes keeping one beside every parsed map affordable.
/// </remarks>
public sealed class OcadMapSnapshot
{
    private readonly IReadOnlyDictionary<string, string> symbolNames;

    private OcadMapSnapshot(IReadOnlyDictionary<int, MapObject> objects,
                            IReadOnlyDictionary<string, string> symbolNames,
                            double metresPerUnit)
    {
        Objects = objects;
        this.symbolNames = symbolNames;
        MetresPerUnit = metresPerUnit;
    }

    /// <summary>
    /// The objects that count, keyed by index slot: those OCAD has as normal.
    /// Deleted ones kept for undo, and hidden ones, are not part of the map a
    /// person sees, so they are not part of what changed either.
    /// </summary>
    public IReadOnlyDictionary<int, MapObject> Objects { get; }

    /// <summary>
    /// Real-world metres per OCAD unit. OCAD units are hundredths of a paper
    /// millimetre, so this is the map scale divided by 100 000.
    /// </summary>
    public double MetresPerUnit { get; }

    /// <summary>Takes a snapshot of one version.</summary>
    /// <param name="map">
    /// The parsed map, for its symbol names and scale. Must not already be held
    /// under its read lock by the caller: this takes the lock itself, and the
    /// lock does not allow recursion.
    /// </param>
    /// <param name="file">The bytes <paramref name="map"/> was parsed from, for the object index.</param>
    public static OcadMapSnapshot Create(Map map, byte[] file)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(file);

        Dictionary<int, MapObject> objects = OcadObjectIndexReader.Read(file)
            .Where(o => o.Status == MapObjectStatus.Normal)
            .ToDictionary(o => o.Index);

        Dictionary<string, string> names = new(StringComparer.Ordinal);
        double scale;

        using (map.Read()) {
            scale = map.MapScale;
            foreach (SymDef symdef in map.AllSymdefs) {
                if (symdef.SymbolId is not null)
                    names.TryAdd(symdef.SymbolId, symdef.Name ?? "");
            }
        }

        return new OcadMapSnapshot(objects, names, scale / 100_000.0);
    }

    /// <summary>The symbol an OCAD symbol number stands for in this version.</summary>
    public DiffSymbol SymbolFor(int symbolNumber)
    {
        // Negative numbers are OCAD's objects without a symbol: imported, broken
        // apart into graphics, images and layout objects.
        if (symbolNumber < 0) {
            return new DiffSymbol("", symbolNumber switch {
                -2 => "Graphic object",
                -3 => "Image object",
                -4 => "Layout object",
                _ => "Imported object",
            });
        }

        // Same rendering PurplePen uses for OCAD 9 and later, which is also what
        // OCAD itself shows: 109000 is "109.0", 401002 is "401.2".
        string number = $"{symbolNumber / 1000}.{symbolNumber % 1000}";
        return new DiffSymbol(number, symbolNames.TryGetValue(number, out string name) ? name : "");
    }
}
