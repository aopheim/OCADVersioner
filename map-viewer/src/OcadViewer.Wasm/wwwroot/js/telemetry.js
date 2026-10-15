/* Application Insights, loaded only when a key is configured.
 *
 * The SDK is vendored rather than pulled from a CDN: a map tool that promises
 * files never leave the machine should not be making unannounced requests to a
 * third-party host just to boot.
 *
 * Nothing here is loaded at all unless initialise() is called with a key, so a
 * local build makes no network requests and costs nothing.
 */

let insights = null;

/**
 * Starts telemetry. Safe to call more than once; later calls are ignored.
 * @returns true if telemetry is now running.
 */
export async function initialise(connectionString) {
    if (insights) return true;
    if (!connectionString) return false;

    await loadSdk();

    const AppInsights = globalThis.Microsoft?.ApplicationInsights?.ApplicationInsights;
    if (!AppInsights) {
        console.warn('[telemetry] SDK did not expose ApplicationInsights; telemetry is off.');
        return false;
    }

    // connectionString covers both forms: a bare GUID is still accepted as an
    // instrumentation key, which is what the Angular app passes.
    const config = connectionString.includes('=')
        ? { connectionString }
        : { instrumentationKey: connectionString };

    insights = new AppInsights({ config: { ...config, enableAutoRouteTracking: true } });
    insights.loadAppInsights();
    insights.trackPageView();
    return true;
}

function loadSdk() {
    return new Promise((resolve, reject) => {
        if (globalThis.Microsoft?.ApplicationInsights) return resolve();
        const script = document.createElement('script');
        script.src = 'lib/appinsights/applicationinsights-web.min.js';
        script.onload = () => resolve();
        script.onerror = () => reject(new Error('could not load the Application Insights SDK'));
        document.head.appendChild(script);
    });
}

export function trackPageView(name, uri) {
    insights?.trackPageView({ name, uri });
}

export function trackEvent(name, properties) {
    insights?.trackEvent({ name }, properties);
}

/**
 * @param severity 1 Information, 2 Warning, 3 Error, 4 Critical - the SDK's own
 *                 SeverityLevel values, passed through from .NET.
 */
export function trackTrace(message, severity, properties) {
    insights?.trackTrace({ message, severityLevel: severity }, properties);
}

export function trackException(message, stack, type, severity, properties) {
    if (!insights) return;
    // The SDK wants a real Error; .NET exceptions arrive as their parts.
    //
    // Its stack parser only recognises JavaScript frames, so a .NET stack arrives
    // with parsedStack empty and hasFullStack false. The portal then shows the raw
    // stack string rather than a structured frame list - the whole trace is there
    // and legible, verified against a captured payload.
    const error = new Error(message);
    error.name = type || 'Error';
    error.stack = stack || '';
    insights.trackException({ exception: error, severityLevel: severity, properties });
}
