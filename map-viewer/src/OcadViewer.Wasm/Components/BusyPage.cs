namespace OcadViewer.Wasm.Components;

/// <summary>
/// A page that does one thing at a time and says what it is doing.
/// </summary>
/// <remarks>
/// Both map pages load files, build overlays and report failures the same way:
/// one action at a time, a busy flag that disables the controls, a status line,
/// and an exception turned into a message rather than a broken page. Stated here
/// once so the two pages cannot drift apart on what "busy" means.
/// </remarks>
public abstract class BusyPage : LocalisedComponent
{
    /// <summary>True while an action started through <see cref="Run"/> is running.</summary>
    protected bool Busy { get; private set; }

    /// <summary>What is happening, or why the last action failed; null when idle.</summary>
    protected string Status { get; private set; }

    /// <summary>Whether <see cref="Status"/> is an error.</summary>
    protected bool Failed { get; private set; }

    /// <summary>Runs an action with the progress bar, busy flag and error reporting.</summary>
    /// <remarks>
    /// Ignores the request while another action is running. Buttons are
    /// disabled while busy, but a key press, a click on a version or a dropped
    /// file are not stopped by markup, and a second action started on top of the
    /// first interleaves with it: each sets one pane, and the two end up from
    /// different comparisons under a heading that names neither.
    /// </remarks>
    protected async Task Run(string message, Func<Task> action)
    {
        if (Busy)
            return;

        Busy = true;
        Failed = false;
        Status = message;
        StateHasChanged();
        await Task.Yield();

        try {
            await action();
            Status = null;
        }
        catch (Exception ex) {
            Failed = true;
            Status = ex.Message;
        }
        finally {
            Busy = false;
            StateHasChanged();
        }
    }
}
