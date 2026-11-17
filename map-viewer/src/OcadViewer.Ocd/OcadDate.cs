namespace OcadViewer.Ocd;

/// <summary>
/// OCAD's timestamps: Delphi TDateTime, days since 1899-12-30 as a double.
/// </summary>
/// <remarks>
/// OCAD writes them with millisecond precision, and the merge uses an object's
/// creation date as its identity, so the conversion rounds to the millisecond
/// rather than trusting floating-point days to land on a tick.
/// </remarks>
internal static class OcadDate
{
    private static readonly DateTime Epoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    public static DateTime? ToDateTime(double days)
        => days <= 0 || double.IsNaN(days) || days > 2958465
            ? null
            : Epoch.AddMilliseconds(Math.Round(days * 86_400_000d));

    public static double FromDateTime(DateTime value)
        => (value - Epoch).TotalMilliseconds / 86_400_000d;
}
