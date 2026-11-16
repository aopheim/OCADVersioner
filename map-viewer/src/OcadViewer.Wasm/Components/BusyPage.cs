using OcadViewer.Wasm.Services;

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
    protected bool Busy => running > 0;

    /// <summary>
    /// True while an action is running that a newer one may not take over -
    /// anything but an action started with <see cref="RunTakingOver"/>.
    /// </summary>
    protected bool Blocked => blocking > 0;

    /// <summary>What is happening, or why the last action failed; null when idle.</summary>
    /// <remarks>
    /// Held as a function and read at render time, so a message on screen
    /// follows a language switch like every other string on the page.
    /// </remarks>
    protected string Status => status?.Invoke();

    private Func<string> status;

    /// <summary>Whether <see cref="Status"/> is an error.</summary>
    protected bool Failed { get; private set; }

    /// <summary>
    /// How far the running action is, 0 to 1, or null when it cannot say.
    /// </summary>
    protected double? Progress { get; private set; }

    private int running;
    private int blocking;

    /// <summary>Completes when nothing is running; already complete when idle.</summary>
    private TaskCompletionSource idle = Completed();

    /// <summary>Runs an action with the progress bar, busy flag and error reporting.</summary>
    /// <remarks>
    /// Ignores the request while another action is running. Buttons are
    /// disabled while busy, but a key press, a click on a version or a dropped
    /// file are not stopped by markup, and a second action started on top of the
    /// first interleaves with it: each sets one pane, and the two end up from
    /// different comparisons under a heading that names neither.
    /// </remarks>
    protected Task Run(Func<string> message, Func<Task> action)
        => Busy ? Task.CompletedTask : RunCore(message, action, takesOver: false);

    /// <summary>
    /// Like <see cref="Run"/>, but also starts while another action of this kind
    /// is running.
    /// </summary>
    /// <remarks>
    /// For loads that make the one before them pointless - picking a version
    /// while the last pick is still loading. The action itself has to make the
    /// older run stand down at its next await, or the two interleave. Still
    /// ignored while anything else runs: a refresh or a version being written
    /// changes what the load would be reading.
    /// </remarks>
    protected Task RunTakingOver(Func<string> message, Func<Task> action)
        => Blocked ? Task.CompletedTask : RunCore(message, action, takesOver: true);

    /// <summary>
    /// Like <see cref="Run"/>, but waits for the running action instead of
    /// being ignored.
    /// </summary>
    /// <remarks>
    /// For requests that cannot simply be pressed again: a dropped or chosen
    /// file is a one-off event, and ignoring it would lose it without a word.
    /// The loop matters - several waiters wake together, the first starts its
    /// action and the rest find the page busy again and go back to waiting.
    /// </remarks>
    protected async Task RunWhenIdle(Func<string> message, Func<Task> action)
    {
        while (Busy)
            await idle.Task;

        await Run(message, action);
    }

    /// <summary>Says what the running action is doing now, and how far it is.</summary>
    protected void Report(Func<string> message, double? progress)
    {
        status = message;
        Progress = progress;
        StateHasChanged();
    }

    /// <remarks>
    /// Runs can overlap when one takes over another, so the page is busy until
    /// the last has finished, not the first. A run that succeeds clears the
    /// message only when nothing else is running and nothing has failed
    /// meanwhile, so it never wipes out a successor's message or its error.
    /// </remarks>
    private async Task RunCore(Func<string> message, Func<Task> action, bool takesOver)
    {
        if (running++ == 0)
            idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!takesOver)
            blocking++;

        Failed = false;
        Report(message, null);
        await Task.Yield();

        try {
            await action();

            if (running == 1 && !Failed)
                status = null;
        }
        catch (Exception ex) {
            Failed = true;
            // A localised failure keeps its key, so it follows a language switch
            // like the rest of the page; anything else has only its message.
            string failure = ex.Message;
            status = ex is LocalisedException localised ? localised.Text : () => failure;
        }
        finally {
            if (!takesOver)
                blocking--;
            if (--running == 0) {
                Progress = null;
                idle.TrySetResult();
            }
            StateHasChanged();
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}
