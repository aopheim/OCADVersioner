using Microsoft.AspNetCore.Components;

using OcadViewer.Wasm.Services;

namespace OcadViewer.Wasm.Components;

/// <summary>
/// A component that repaints when the language changes.
/// </summary>
/// <remarks>
/// <para>
/// Subscribing to <see cref="Localiser.Changed"/> and unsubscribing on dispose is
/// a few lines that every component owning text has to get right, and getting the
/// second half wrong leaks the component for the lifetime of the app. Stating it
/// once means a component that shows text only has to inherit.
/// </para>
/// <para>
/// The subscription is made where the localiser is injected rather than in
/// <c>OnInitialized</c>, so a component that overrides <c>OnInitialized</c> and
/// forgets to call the base still repaints.
/// </para>
/// <para>
/// Disposal is asynchronous only. Blazor calls <c>DisposeAsync</c> alone on a
/// component that implements both interfaces, so a synchronous <c>Dispose</c>
/// here was silently skipped by every component that also released JavaScript.
/// Such a component overrides <see cref="DisposeAsyncCore"/> instead, and the
/// unsubscribe happens whatever it does.
/// </para>
/// </remarks>
public abstract class LocalisedComponent : ComponentBase, IAsyncDisposable
{
    private Localiser localiser;

    [Inject]
    protected Localiser L
    {
        get => localiser;
        set {
            if (localiser is not null)
                localiser.Changed -= OnLanguageChanged;

            localiser = value;
            localiser.Changed += OnLanguageChanged;
        }
    }

    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    public async ValueTask DisposeAsync()
    {
        try {
            await DisposeAsyncCore();
        }
        finally {
            if (localiser is not null)
                localiser.Changed -= OnLanguageChanged;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Releases whatever the component itself holds, such as JavaScript modules.</summary>
    protected virtual ValueTask DisposeAsyncCore() => ValueTask.CompletedTask;
}
