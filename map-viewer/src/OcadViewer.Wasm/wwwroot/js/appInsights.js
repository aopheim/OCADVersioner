/* Application Insights, loaded only when a key is configured.
 *
 * The SDK is vendored rather than pulled from a CDN: a map tool that promises
 * files never leave the machine should not be making unannounced requests to a
 * third-party host just to boot.
 *
 * Nothing here is loaded at all unless initialise() is called with a key, so a
 * local build makes no network requests and costs nothing.
 *
 * What it sends is kept to what fits Azure's free allowance and needs no consent:
 * page views, the first page's load timings, and errors - no cookies, no
 * identifiers, no record of the app's own requests. Errors are rationed per tab,
 * because a component that throws on every render is the one realistic way to
 * send gigabytes.
 */

let insights = null;

/** The same error is sent this many times per tab, then dropped. */
const MaxRepeats = 3;

/** Errors of any kind sent per tab, after which no more are. */
const MaxErrors = 50;

/** How often each error has been sent from this tab, keyed by errorKey. */
const sentByError = new Map();
let errorsSent = 0;

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

    insights = new AppInsights({
        config: {
            ...config,
            enableAutoRouteTracking: true,

            // No ai_user / ai_session cookies: nothing stored on the device, so no
            // consent is needed. Page views still count; distinct users and
            // sessions do not, and are not wanted.
            disableCookiesUsage: true,

            // The SDK's own diagnostics are not sent as traces. Without the
            // cookie every page load is a "new user", and the SDK then reports
            // that - with the browser's user agent - on every load. Noise, and it
            // would eat into the error ration below.
            loggingLevelTelemetry: 0,

            // The app's own fetches and XHRs are not recorded as dependencies,
            // and no tracing headers are added to them. For an app with no
            // backend they were noise.
            disableAjaxTracking: true,
            disableFetchTracking: true
        }
    });
    insights.loadAppInsights();

    // Before anything is tracked, so the first error is already counted.
    insights.addTelemetryInitializer(withinErrorRation);

    insights.trackPageView();
    return true;
}

/**
 * Lets an error through only while this tab is within its ration - MaxRepeats of
 * the same error and MaxErrors in all. Everything that is not an error passes.
 *
 * Here rather than in .NET so it covers both ways errors arrive: .NET's, through
 * trackException and trackTrace below, and uncaught JavaScript errors, which the
 * SDK catches and tracks by itself.
 * @returns false to drop the item.
 */
function withinErrorRation(item) {
    const key = errorKey(item);
    if (key === null) return true;

    const sent = sentByError.get(key) ?? 0;
    if (sent >= MaxRepeats || errorsSent >= MaxErrors) return false;

    sentByError.set(key, sent + 1);
    errorsSent++;
    return true;
}

/**
 * What makes two errors the same one: kind and message. Null for anything that
 * is not an error.
 *
 * Every trace this app sends is an error - the .NET logger forwards Error and
 * above only - so traces are rationed too.
 */
function errorKey(item) {
    const data = item.baseData ?? {};

    if (item.baseType === 'ExceptionData') {
        // trackException passes the Error under `exception`; the SDK's own
        // window.onerror handler passes its auto-exception there, with typeName.
        const exception = data.exception ?? data.exceptions?.[0] ?? {};
        return `exception|${exception.typeName ?? exception.name ?? ''}|${exception.message ?? data.message ?? ''}`;
    }

    if (item.baseType === 'MessageData')
        return `trace|${data.message ?? ''}`;

    return null;
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
