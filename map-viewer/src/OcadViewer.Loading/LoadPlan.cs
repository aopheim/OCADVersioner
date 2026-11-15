namespace OcadViewer.Loading;

/// <summary>
/// One map a load has to put on screen, and what it is expected to cost.
/// </summary>
/// <param name="BytesToParse">
/// The size of the file that has to be read and parsed, or zero when that map is
/// already in memory and only has to be drawn into its pane.
/// </param>
/// <param name="TilesToDraw">
/// How many tiles the pane is expected to ask for. An estimate from the size of
/// the pane, which is all that can be known before the layer is attached.
/// </param>
public sealed record LoadStep(long BytesToParse, int TilesToDraw);

/// <summary>
/// How far along a load is, weighted so that the number means time rather than
/// steps.
/// </summary>
/// <remarks>
/// Showing a comparison is four pieces of work - read and parse each map, then
/// draw each one into its pane - and they are nothing like the same size. A bar
/// that gave them a quarter each would race through the reads and then sit still
/// for a second on a parse, which is worse than no bar: it says the app has
/// stalled at the exact moment it is working hardest.
///
/// So each piece is weighed by what it is expected to cost, and the bar shows
/// cost done over cost total. The two constants below are the whole model. They
/// are estimates and will drift as the renderer changes; being roughly right is
/// the point, and nothing else depends on them being exact.
///
/// They were measured against a development build, where everything runs
/// interpreted. A published build is AOT-compiled, which the csproj records as
/// 16x on parsing and 3.3-4.4x on tiles - so the balance there tips further
/// towards the drawing than these figures say, and they are worth taking again
/// from a published build when there is one to measure.
///
/// This lives away from the browser on purpose. JavaScript knows the file sizes
/// and counts the tiles, but what that work is worth is a judgement, and a
/// judgement in a dependency-free project is one a test can reach.
/// </remarks>
public sealed class LoadPlan
{
    /// <summary>
    /// What reading and parsing a megabyte of .ocd costs.
    /// </summary>
    /// <remarks>
    /// Measured in the browser on 2026-11-15, over four 0.41 MB maps of one real
    /// project: 466, 507 and 551 ms per megabyte once warm, and 817 on the very
    /// first map of the session, which is left out because nothing after it looks
    /// like that. Reading from disk is inside this figure - a few milliseconds
    /// against hundreds, and the two cannot be told apart from outside anyway,
    /// because the read is one await and the parse one blocking call.
    /// </remarks>
    public const double ParseMillisecondsPerMegabyte = 510;

    /// <summary>
    /// What drawing one 256-pixel tile costs.
    /// </summary>
    /// <remarks>
    /// The renderer's own figure, from the viewport's tile statistics, was 47-49
    /// ms a tile over 48 tiles, and a tile also waits a frame for the pump, which
    /// runs one job per frame so the browser can paint between them. Tiles vary
    /// by an order of magnitude - an empty corner of the map against the middle
    /// of a town - so this is a mean over a screenful and nothing finer.
    /// </remarks>
    public const double RenderMillisecondsPerTile = 65;

    private const double BytesPerMegabyte = 1024 * 1024;

    private readonly double[] parseCost;
    private readonly double[] renderCost;

    /// <summary>How much of each piece is done, 0 to 1.</summary>
    private readonly double[] parsed;
    private readonly double[] drawn;

    private readonly double total;

    private LoadPlan(double[] parseCost, double[] renderCost)
    {
        this.parseCost = parseCost;
        this.renderCost = renderCost;
        parsed = new double[parseCost.Length];
        drawn = new double[parseCost.Length];
        total = parseCost.Sum() + renderCost.Sum();
    }

    /// <summary>Weighs the steps of a load, in the order they will be done.</summary>
    public static LoadPlan For(IReadOnlyList<LoadStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        return new LoadPlan(
            [.. steps.Select(step => Math.Max(0, step.BytesToParse) / BytesPerMegabyte
                                     * ParseMillisecondsPerMegabyte)],
            [.. steps.Select(step => Math.Max(0, step.TilesToDraw) * RenderMillisecondsPerTile)]);
    }

    /// <summary>How many maps this load covers.</summary>
    public int Steps => parseCost.Length;

    /// <summary>
    /// Whether there is anything to wait for.
    /// </summary>
    /// <remarks>
    /// A load with no cost at all - every map in memory and no tile to draw -
    /// should show no bar rather than an empty one that vanishes again.
    /// </remarks>
    public bool HasWork => total > 0;

    /// <summary>How much of the expected cost is behind us, 0 to 1.</summary>
    public double Fraction
    {
        get {
            if (!HasWork)
                return 0;

            double done = 0;
            for (int i = 0; i < Steps; i++)
                done += parseCost[i] * parsed[i] + renderCost[i] * drawn[i];

            return Math.Clamp(done / total, 0, 1);
        }
    }

    /// <summary>
    /// Which map is being worked on, or <see cref="Steps"/> when all are done.
    /// </summary>
    public int Step
    {
        get {
            for (int i = 0; i < Steps; i++) {
                if (parsed[i] < 1 || drawn[i] < 1)
                    return i;
            }

            return Steps;
        }
    }

    /// <summary>Whether the map being worked on is past its parse and on screen.</summary>
    public bool IsDrawing => Step < Steps && parsed[Step] >= 1;

    /// <summary>
    /// Takes in what the browser saw.
    /// </summary>
    /// <param name="step">Which map, by its position in the plan.</param>
    /// <param name="parsed">Whether that map has been read and parsed by now.</param>
    /// <param name="tilesDrawn">How many of its tiles have been drawn.</param>
    /// <param name="tilesExpected">
    /// How many the pane actually asked for, which is known only once the layer is
    /// attached and may differ from the estimate this plan was weighed with.
    /// </param>
    /// <param name="finished">
    /// Whether that map is done with. Needed in its own right: a pane can ask for
    /// no tiles at all - it is off screen, or the window is tiny - and then there
    /// is no count that reaching the end could be read from.
    /// </param>
    /// <remarks>
    /// Out-of-range steps are ignored rather than thrown on. A report can outlive
    /// the load it belongs to - the user picks another version and the one in
    /// flight is dropped - and a late report is not a programming error.
    /// </remarks>
    public void Report(int step, bool parsed, int tilesDrawn, int tilesExpected, bool finished)
    {
        if (step < 0 || step >= Steps)
            return;

        // A map that is done stays done. Tile reports are sent without waiting
        // for an answer, so one can arrive after the map it belongs to has
        // finished, and taking it would walk the bar backwards.
        if (drawn[step] >= 1 && !finished)
            return;

        if (parsed || finished)
            this.parsed[step] = 1;

        if (finished)
            drawn[step] = 1;
        else if (tilesExpected > 0)
            drawn[step] = Math.Clamp((double)tilesDrawn / tilesExpected, 0, 1);
    }
}
