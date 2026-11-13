using Microsoft.AspNetCore.Components;

using OcadViewer.Wasm.Services;

namespace OcadViewer.Wasm.Components;

/// <summary>
/// A component that repaints when the language changes.
/// </summary>
/// <remarks>
/// Subscribing to <see cref="Localiser.Changed"/> and unsubscribing on dispose is
/// four lines that every page owning text has to get right, and forgetting the
/// second half leaks the component for the lifetime of the app. Stating it once
/// means a page that shows text only has to inherit.
/// </remarks>
public abstract class LocalisedComponent : ComponentBase, IDisposable
{
    [Inject] protected Localiser L { get; set; }

    protected override void OnInitialized()
    {
        L.Changed += OnLanguageChanged;
        base.OnInitialized();
    }

    private void OnLanguageChanged() => InvokeAsync(StateHasChanged);

    public virtual void Dispose()
    {
        L.Changed -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
