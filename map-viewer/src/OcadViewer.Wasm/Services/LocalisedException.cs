namespace OcadViewer.Wasm.Services;

/// <summary>
/// A failure to show the user, worded through the localiser.
/// </summary>
/// <remarks>
/// Carries the wording as a function rather than a finished string, so a page that
/// keeps the message on screen can render it again in whatever language is chosen
/// next. <see cref="Message"/> gives the wording in the current language, for logs
/// and for anything that does not know about this type.
/// </remarks>
public sealed class LocalisedException(Func<string> text) : Exception
{
    /// <summary>The message, in the language current when it is called.</summary>
    public Func<string> Text { get; } = text;

    public override string Message => Text();
}
