using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

using NUnit.Framework;
using NUnit.Framework.Interfaces;

namespace OcadViewer.E2ETests;

/// <summary>
/// A fresh browser context and page per test, pointed at the served app.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>English is pinned, unless a test has already chosen a language - so a
/// test that switches language and reloads keeps its choice.</item>
/// <item>Console errors and uncaught exceptions fail an otherwise passing test.
/// A happy path that only works while logging errors is not a happy path.</item>
/// <item>A trace is kept for every failed test, under <c>playwright-traces</c>
/// next to the test binaries.</item>
/// </list>
/// Nothing sleeps. Waiting is on state the app exposes: the busy bar's
/// <c>data-busy</c>, the overlay switch's <c>data-on</c>, and Leaflet's
/// loaded-tile class.
/// </remarks>
[Category("E2E")]
[Parallelizable(ParallelScope.Self)]
public abstract class AppPageTest : PageTest
{
    /// <summary>
    /// Generous, because the first page in a context downloads the .NET runtime,
    /// the app and the map fonts before it can render anything.
    /// </summary>
    private const float TimeoutMs = 60_000;

    private readonly List<string> errors = [];

    public override BrowserNewContextOptions ContextOptions() => new() {
        BaseURL = AppServer.BaseUrl,
        Locale = "en-GB",
        ViewportSize = new() { Width = 1400, Height = 900 },
    };

    [SetUp]
    public async Task SetUpAppPageAsync()
    {
        Context.SetDefaultTimeout(TimeoutMs);
        SetDefaultExpectTimeout(TimeoutMs);

        await Context.AddInitScriptAsync("""
            if (!localStorage.getItem('appSettings'))
                localStorage.setItem('appSettings', JSON.stringify({ languageSelection: { selectedLanguageCode: 'en' } }));
            """);

        Page.Console += (_, message) => {
            if (message.Type == "error")
                errors.Add($"console: {message.Text}");
        };
        Page.PageError += (_, error) => errors.Add($"page error: {error}");

        await Context.Tracing.StartAsync(new() {
            Title = TestContext.CurrentContext.Test.FullName,
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
    }

    [TearDown]
    public async Task TearDownAppPageAsync()
    {
        // Everything that can fail the test is known before tracing stops, so a
        // test failed only by the checks below still keeps its trace.
        if (await Page.Locator("#blazor-error-ui").IsVisibleAsync())
            errors.Add("Blazor's unhandled-error bar is showing.");

        bool failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;

        // Written out whatever the outcome: when a flow times out, these are
        // usually why.
        foreach (string error in errors)
            TestContext.Out.WriteLine(error);

        string trace = failed || errors.Count > 0
            ? Path.Combine(TestContext.CurrentContext.WorkDirectory, "playwright-traces",
                           $"{SafeFileName(TestContext.CurrentContext.Test.FullName)}.zip")
            : null;

        await Context.Tracing.StopAsync(new() { Path = trace });
        if (trace is not null)
            TestContext.AddTestAttachment(trace);

        if (!failed)
            Assert.That(errors, Is.Empty, "The page logged errors.");
    }

    /// <summary>
    /// A test name made fit for a file name: parameterised names carry quotes
    /// and brackets, and long ones can pass the path limit.
    /// </summary>
    private static string SafeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
        return safe.Length <= 120 ? safe : safe[..100] + "_" + (uint)name.GetHashCode();
    }

    /// <summary>Waits until the page has finished what it was doing.</summary>
    /// <remarks>
    /// Only meaningful once something has visibly started or finished: the bar
    /// is also idle in the moment before work begins.
    /// </remarks>
    protected async Task ExpectIdleAsync()
        => await Expect(Page.GetByTestId("busy-bar")).ToHaveAttributeAsync("data-busy", "false");

    /// <summary>A map pane, by side: "Left" (older) or "Right" (newer).</summary>
    protected ILocator Pane(string side) => Page.GetByTestId($"map-pane-{side}");

    /// <summary>Waits until a pane has drawn at least one tile.</summary>
    protected async Task ExpectPaneDrawnAsync(string side)
        => await Expect(Pane(side).Locator(".leaflet-tile-loaded").First).ToBeVisibleAsync();

    /// <summary>
    /// The change overlay's share of the map as a percentage, from the label
    /// next to the switch.
    /// </summary>
    protected async Task<double> ChangedShareAsync()
    {
        string text = await Page.GetByTestId("change-share").InnerTextAsync();
        string number = text.Trim().Split('%')[0];
        return double.Parse(number, System.Globalization.CultureInfo.InvariantCulture);
    }
}
