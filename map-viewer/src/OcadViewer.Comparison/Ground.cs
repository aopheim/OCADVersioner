namespace OcadViewer.Comparison;

/// <summary>Between metres on the ground and OCAD's units on paper.</summary>
/// <remarks>
/// Mappers think in metres; files store hundredths of a millimetre on paper. The
/// map scale converts one into the other.
/// </remarks>
public static class Ground
{
    /// <summary>Metres on the ground to whole OCAD units, rounded down so a threshold is never looser than asked for.</summary>
    public static int ToUnits(double meters, double scale)
        => (int)Math.Floor(meters * 100_000 / scale + 1e-9);

    /// <summary>Metres on the ground that one OCAD unit (0.01 mm on paper) stands for.</summary>
    public static double MetresPerUnit(double scale) => scale / 100_000;
}
