using Microsoft.JSInterop;

namespace OcadViewer.Wasm.Services;

/// <summary>Matches Application Insights' own severity values.</summary>
public enum TelemetrySeverity
{
    Information = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

/// <summary>
/// Application Insights, mirroring what the Angular app reports so the two feed
/// the same dashboards during the migration.
/// </summary>
/// <remarks>
/// Off unless a key is configured. The Angular app injects its key at build time
/// from <c>APPINSIGHTSINSTRUMENTATIONKEY_PROD</c> and disables itself in dev mode;
/// here the key lives in <c>wwwroot/appsettings.json</c> under
/// <c>Telemetry:ConnectionString</c>, which the deployment workflow overwrites. No
/// key means no SDK is even fetched, so local builds make no network requests.
/// </remarks>
public sealed class TelemetryService(IJSRuntime js, ILogger<TelemetryService> logger) : IAsyncDisposable
{
    private readonly IJSRuntime js = js;
    private readonly ILogger<TelemetryService> logger = logger;

    private IJSObjectReference module;
    private bool enabled;

    public bool IsEnabled => enabled;

    /// <summary>Starts telemetry if a key is configured. Safe to call once at startup.</summary>
    public async Task InitialiseAsync(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) {
            logger.LogInformation("No telemetry key configured; telemetry is off.");
            return;
        }

        try {
            module = await js.ImportAsync("./js/appInsights.js");
            enabled = await module.InvokeAsync<bool>("initialise", connectionString);
        }
        catch (Exception ex) {
            // Telemetry failing to start must never stop the app. An ad blocker
            // refusing the SDK is a perfectly ordinary reason to end up here.
            logger.LogWarning(ex, "Telemetry could not start; continuing without it.");
            enabled = false;
        }
    }

    /// <remarks>
    /// Traces and exceptions only. The page-view and custom-event wrappers that
    /// used to sit here had no callers at all - this app reports what the logging
    /// pipeline hands it, which is errors. Adding them back is a few lines, on the
    /// day something actually wants to call them.
    /// </remarks>
    public async Task TrackAsync(string message, TelemetrySeverity severity,
                                 IDictionary<string, object> properties = null)
        => await SafeAsync(m => m.InvokeVoidAsync("trackTrace", message, (int)severity, properties));

    public async Task TrackExceptionAsync(Exception exception,
                                          TelemetrySeverity severity = TelemetrySeverity.Critical,
                                          IDictionary<string, object> properties = null)
    {
        if (exception is null)
            return;

        await SafeAsync(m => m.InvokeVoidAsync("trackException",
            exception.Message, exception.StackTrace, exception.GetType().FullName,
            (int)severity, properties));
    }

    private async Task SafeAsync(Func<IJSObjectReference, ValueTask> call)
    {
        if (!enabled || module is null)
            return;

        try {
            // A page that has gone away is not a telemetry failure worth reporting.
            await module.IfStillThereAsync(call);
        }
        catch (Exception ex) {
            // Reporting a problem must not itself become a problem, and must never
            // recurse back into the logger that may have called us.
            Console.Error.WriteLine($"[telemetry] {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync() => await module.ReleaseAsync();
}
