using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

using NUnit.Framework;

namespace OcadViewer.E2ETests;

/// <summary>
/// Serves the published app for the whole run, the way Azure Static Web Apps
/// serves it.
/// </summary>
/// <remarks>
/// <para>
/// There is no backend: the app is a folder of static files. This starts
/// <c>tools/serve.mjs</c> over the published <c>wwwroot</c> on whatever port the
/// OS hands it, reads that port back from the server's first line, and waits
/// until it answers.
/// </para>
/// <para>
/// <c>E2E_BASE_URL</c> points the tests at a server that is already running
/// instead. <c>E2E_SITE</c> names the folder to serve; it defaults to the
/// Release publish output.
/// </para>
/// </remarks>
[SetUpFixture]
public sealed partial class AppServer
{
    /// <summary>Where the app is, with a trailing slash.</summary>
    public static string BaseUrl { get; private set; }

    private static Process server;

    [GeneratedRegex(@"on http://[^:]+:(\d+)")]
    private static partial Regex ServingLine();

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        string external = Environment.GetEnvironmentVariable("E2E_BASE_URL");
        if (!string.IsNullOrWhiteSpace(external)) {
            BaseUrl = external.TrimEnd('/') + "/";
            return;
        }

        string site = Environment.GetEnvironmentVariable("E2E_SITE")
            ?? Path.Combine(Repo.Root, "src", "OcadViewer.Wasm", "bin", "Release", "net10.0", "publish", "wwwroot");

        if (!File.Exists(Path.Combine(site, "index.html")))
            throw new InvalidOperationException(
                $"No published site at {site}. Publish it first:\n"
                + "  dotnet publish src/OcadViewer.Wasm -c Release -p:RunAOTCompilation=false -p:WasmStripILAfterAOT=false\n"
                + "or set E2E_SITE to the folder holding index.html, or E2E_BASE_URL to a running server.");

        Process node = Process.Start(new ProcessStartInfo("node") {
            ArgumentList = { Path.Combine(Repo.Root, "tools", "serve.mjs"), site, "0" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start node.");
        server = node;

        // OneTimeTearDown does not run when a run is stopped from the IDE or with
        // Ctrl+C. Without this the server would outlive every aborted run.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();

        TaskCompletionSource<int> port = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Drained so a chatty server can never block on a full pipe.
        node.OutputDataReceived += (_, e) => {
            if (e.Data is null) return;
            TestContext.Progress.WriteLine($"[serve] {e.Data}");
            if (ServingLine().Match(e.Data) is { Success: true } match)
                port.TrySetResult(int.Parse(match.Groups[1].Value));
        };
        node.ErrorDataReceived += (_, e) => { if (e.Data is not null) TestContext.Progress.WriteLine($"[serve] {e.Data}"); };
        // Reads nothing from the process: Stop may already have disposed it.
        node.Exited += (_, _) => port.TrySetException(
            new InvalidOperationException("serve.mjs exited before it was serving; see the [serve] lines above."));
        node.EnableRaisingEvents = true;
        node.BeginOutputReadLine();
        node.BeginErrorReadLine();

        int bound = await port.Task.WaitAsync(TimeSpan.FromSeconds(30));
        BaseUrl = $"http://127.0.0.1:{bound}/";

        await WaitUntilAnsweringAsync(TimeSpan.FromSeconds(30));
    }

    [OneTimeTearDown]
    public void Stop()
    {
        Process running = Interlocked.Exchange(ref server, null);
        if (running is null)
            return;

        try {
            if (!running.HasExited)
                running.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) {
            // Exited between the check and the kill.
        }
        running.Dispose();
    }

    private static async Task WaitUntilAnsweringAsync(TimeSpan timeout)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
        Stopwatch waited = Stopwatch.StartNew();

        while (waited.Elapsed < timeout) {
            try {
                using HttpResponseMessage response = await http.GetAsync(BaseUrl);
                if (response.StatusCode == HttpStatusCode.OK)
                    return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(200);
        }

        throw new TimeoutException($"The app did not answer at {BaseUrl} within {timeout.TotalSeconds:0} s.");
    }
}
