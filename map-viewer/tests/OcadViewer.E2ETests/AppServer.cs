using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

using NUnit.Framework;

namespace OcadViewer.E2ETests;

/// <summary>
/// Serves the published app for the whole run, the way Azure Static Web Apps
/// serves it.
/// </summary>
/// <remarks>
/// <para>
/// There is no backend: the app is a folder of static files. This starts
/// <c>tools/serve.mjs</c> over the published <c>wwwroot</c> on a free port and
/// waits until it answers.
/// </para>
/// <para>
/// <c>E2E_BASE_URL</c> points the tests at a server that is already running
/// instead. <c>E2E_SITE</c> names the folder to serve; it defaults to the
/// Release publish output.
/// </para>
/// </remarks>
[SetUpFixture]
public sealed class AppServer
{
    /// <summary>Where the app is, with a trailing slash.</summary>
    public static string BaseUrl { get; private set; }

    private static Process server;

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

        int port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}/";

        server = Process.Start(new ProcessStartInfo("node") {
            ArgumentList = { Path.Combine(Repo.Root, "tools", "serve.mjs"), site, port.ToString() },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start node.");

        // Drained so a chatty server can never block on a full pipe.
        server.OutputDataReceived += (_, e) => { if (e.Data is not null) TestContext.Progress.WriteLine($"[serve] {e.Data}"); };
        server.ErrorDataReceived += (_, e) => { if (e.Data is not null) TestContext.Progress.WriteLine($"[serve] {e.Data}"); };
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();

        await WaitUntilAnsweringAsync(TimeSpan.FromSeconds(30));
    }

    [OneTimeTearDown]
    public void Stop()
    {
        if (server is { HasExited: false })
            server.Kill(entireProcessTree: true);
        server?.Dispose();
    }

    private static async Task WaitUntilAnsweringAsync(TimeSpan timeout)
    {
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
        Stopwatch waited = Stopwatch.StartNew();

        while (waited.Elapsed < timeout) {
            if (server.HasExited)
                throw new InvalidOperationException($"serve.mjs exited with code {server.ExitCode}.");

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

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
