namespace Sati.Services
{
    /// <summary>
    /// Asks the case manager whether unsaved work may be thrown away, returning
    /// true only on an explicit yes.
    /// </summary>
    /// <remarks>
    /// A named delegate rather than a bare <c>Func&lt;string, string, bool&gt;</c>
    /// so the container resolves it unambiguously, and so a headless test can
    /// supply a fixed answer instead of a window. The view model asks; it never
    /// constructs the dialog itself.
    /// </remarks>
    public delegate bool DiscardChangesPrompt(string title, string message);
}
