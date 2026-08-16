namespace Sati.Contracts.V1;

/// <summary>
/// The single owner of what counts as an acceptable item screenshot.
///
/// Both the desktop client and Sati.Api enforce this. The desktop checks at the
/// paste boundary so the case manager finds out immediately; the API checks
/// again because a client-side limit is a convenience, not a control — a request
/// body arrives with whatever bytes the caller chose to send.
/// </summary>
public static class AtRequestScreenshot
{
    /// <summary>
    /// Longest edge, in pixels, that a pasted screenshot is scaled down to. A
    /// full 4K screen grab is several megabytes of PNG for a picture of a product
    /// page; at this width the text in a vendor listing is still legible in the
    /// generated PDF, which is the only thing the image has to achieve.
    /// </summary>
    public const int MaxEdgePixels = 1400;

    /// <summary>
    /// Hard ceiling on stored bytes, checked after any downscaling. Generous
    /// enough that a legitimate screenshot never trips it, small enough that a
    /// request with a dozen items cannot quietly become a hundred-megabyte row.
    /// </summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// PNG magic number. Screenshots are stored as PNG rather than JPEG because
    /// the subject is a page of text and UI, where JPEG's ringing artefacts land
    /// exactly on the characters someone needs to read — a price, in a document
    /// that authorises a payment.
    /// </summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// True when the bytes are a plausible PNG of an acceptable size. This is a
    /// format and size gate, not a safety guarantee about image contents — it
    /// exists so that a mistyped upload or an oversized paste is refused with a
    /// clear reason rather than stored and discovered later.
    /// </summary>
    public static bool IsAcceptable(byte[]? bytes) => Describe(bytes) is null;

    /// <summary>
    /// Why the bytes are unacceptable, phrased for the person who pasted them,
    /// or null when they are fine. Null input is fine: an item without a
    /// screenshot is normal.
    /// </summary>
    public static string? Describe(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
            return null;

        if (bytes.Length > MaxBytes)
            return $"The screenshot is too large. The limit is {MaxBytes / (1024 * 1024)} MB.";

        if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            return "The screenshot must be a PNG image.";

        return null;
    }

    /// <summary>
    /// The target size for an image whose longest edge exceeds
    /// <see cref="MaxEdgePixels"/>, preserving aspect ratio. Returns the original
    /// dimensions when no scaling is needed, so a caller can compare and skip the
    /// re-encode. Guards against zero so a degenerate image cannot produce a
    /// zero-dimension render target.
    /// </summary>
    public static (int Width, int Height) ScaleToFit(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return (0, 0);

        var longest = Math.Max(width, height);
        if (longest <= MaxEdgePixels)
            return (width, height);

        var factor = (double)MaxEdgePixels / longest;
        return (Math.Max(1, (int)Math.Round(width * factor)), Math.Max(1, (int)Math.Round(height * factor)));
    }
}
