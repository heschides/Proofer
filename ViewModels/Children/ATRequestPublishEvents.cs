namespace Sati.ViewModels.Children;

/// <summary>
/// Interaction request: the view is asked to confirm publication and writes the
/// answer back into <see cref="Confirmed"/>. An event rather than a direct call
/// on a dialog service because publishing must show the case manager the exact
/// statement they are attesting to, and that statement belongs to the request,
/// not to whatever the dialog happens to hard-code.
///
/// The default is <c>false</c>: a view that ignores this event fails closed, and
/// nothing is published.
/// </summary>
public sealed class ATRequestPublishConfirmationEventArgs(
    string clientName,
    string attestationStatement,
    decimal totalCost,
    int screenshotCount,
    int itemCount) : EventArgs
{
    public string ClientName { get; } = clientName;
    public string AttestationStatement { get; } = attestationStatement;
    public decimal TotalCost { get; } = totalCost;

    /// <summary>
    /// How many pasted evidence clips will appear on the screenshot page, and out
    /// of how many items. Surfaced at the point of no return because publishing
    /// locks the request: a missing clip is cheap to fix now and expensive after,
    /// when correcting it means discarding the attestation.
    /// </summary>
    public int ScreenshotCount { get; } = screenshotCount;
    public int ItemCount { get; } = itemCount;

    public bool Confirmed { get; set; }
}

/// <summary>
/// Raised when an AT request PDF has been rendered and is ready to be written to
/// disk. Mirrors the AdminDashboard export pattern: the view model produces the
/// bytes, the view owns the file dialog.
/// </summary>
public sealed class ATRequestPdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

/// <summary>
/// Raised when an operation the user initiated could not complete — a stale
/// request, a publication lock, a failed save. Carries text already phrased for
/// the user; the view decides how to show it.
/// </summary>
public sealed class ATRequestProblemEventArgs(string title, string message) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
}
