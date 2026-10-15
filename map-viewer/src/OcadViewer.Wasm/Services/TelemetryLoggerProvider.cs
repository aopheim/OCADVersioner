namespace OcadViewer.Wasm.Services;

/// <summary>
/// Forwards logged errors to Application Insights.
/// </summary>
/// <remarks>
/// Blazor's equivalent of Angular's <c>ErrorHandlerService</c>. There is no
/// <c>ErrorHandler</c> to override here: an exception a component does not catch
/// is reported by the renderer through <c>ILogger</c>, so hooking the logging
/// pipeline catches the same class of failure.
/// </remarks>
public sealed class TelemetryLoggerProvider : ILoggerProvider
{
    /// <summary>
    /// Assigned once the host is built. Logging providers must be registered
    /// before the container exists, and building a throwaway container here would
    /// hand out a different TelemetryService than the one that gets initialised -
    /// which would silently report nothing.
    /// </summary>
    public IServiceProvider Services { get; set; }

    public ILogger CreateLogger(string categoryName)
        => new TelemetryLogger(categoryName, () => Services);

    public void Dispose() { }

    private sealed class TelemetryLogger(string category, Func<IServiceProvider> services) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                                Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            // Never report our own failures through ourselves; that is how a
            // logging loop starts.
            if (category.StartsWith(nameof(OcadViewer) + ".Wasm.Services.Telemetry", StringComparison.Ordinal))
                return;

            var telemetry = services()?.GetService<TelemetryService>();
            if (telemetry is not { IsEnabled: true })
                return;

            var properties = new Dictionary<string, object> {
                ["category"] = category,
                ["eventId"] = eventId.ToString()
            };

            var severity = logLevel == LogLevel.Critical
                ? TelemetrySeverity.Critical
                : TelemetrySeverity.Error;

            // Fire and forget: logging is not allowed to block or throw, and the
            // renderer that called us is not awaiting anything.
            _ = exception is not null
                ? telemetry.TrackExceptionAsync(exception, severity, properties)
                : telemetry.TrackAsync(formatter(state, exception), severity, properties);
        }
    }
}
