using Microsoft.JSInterop;

namespace OcadViewer.Wasm.Services;

/// <summary>
/// Importing an ES module, and letting go of one safely.
/// </summary>
/// <remarks>
/// The teardown is the point. Every component and service that imported a module
/// carried its own copy of the same five lines - dispose inside a try, swallow
/// <see cref="JSDisconnectedException"/> and <see cref="ObjectDisposedException"/>
/// - because a page can be torn down while JavaScript is already gone, and a
/// throwing <c>DisposeAsync</c> takes the rest of the teardown with it. Four
/// copies of a rule is four chances to get it subtly different.
/// </remarks>
public static class JsModule
{
    /// <summary>Imports a module from <c>wwwroot</c>, e.g. <c>./js/thing.js</c>.</summary>
    public static ValueTask<IJSObjectReference> ImportAsync(this IJSRuntime js, string path)
    {
        ArgumentNullException.ThrowIfNull(js);
        return js.InvokeAsync<IJSObjectReference>("import", path);
    }

    /// <summary>
    /// Releases a JS reference, tolerating a page that has already gone away.
    /// </summary>
    public static ValueTask ReleaseAsync(this IJSObjectReference reference)
        => IfStillThereAsync(reference, static module => module.DisposeAsync());

    /// <summary>
    /// Calls a method on a JS object and then releases it, for teardown work such
    /// as <c>dispose()</c>.
    /// </summary>
    public static async ValueTask ReleaseAsync(this IJSObjectReference reference, string disposeMethod)
    {
        await IfStillThereAsync(reference, module => module.InvokeVoidAsync(disposeMethod));
        await reference.ReleaseAsync();
    }

    /// <summary>
    /// Makes a call that does not matter if the page has gone.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget work such as telemetry wants the same tolerance as
    /// teardown: by the time it runs, the thing it would have told may not be
    /// there. Anything else the call throws is the caller's to deal with.
    /// </remarks>
    public static ValueTask IfStillThereAsync(
        this IJSObjectReference reference, Func<IJSObjectReference, ValueTask> call)
        => reference is null ? ValueTask.CompletedTask : Attempt(reference, call);

    private static async ValueTask Attempt(
        IJSObjectReference reference, Func<IJSObjectReference, ValueTask> call)
    {
        try {
            await call(reference);
        }
        catch (JSDisconnectedException) {
            // The circuit or the page is gone; there is nothing to talk to.
        }
        catch (ObjectDisposedException) {
            // Already released, which is the state we wanted anyway.
        }
    }
}
