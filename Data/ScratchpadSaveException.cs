namespace Sati.Data;

/// <summary>
/// A scratchpad save failure whose message is safe to show to the signed-in user.
/// Infrastructure exceptions remain available as the inner exception for the local technical log.
/// </summary>
public class ScratchpadSaveException(string message, Exception innerException)
    : Exception(message, innerException);

/// <summary>
/// The cloud rejected a scratchpad write before it reached the save endpoint because
/// the short-lived desktop session expired. The visible draft remains authoritative
/// until the user signs in again; callers must not keep retrying on a timer.
/// </summary>
public sealed class ScratchpadSessionExpiredException(Exception innerException)
    : ScratchpadSaveException(
        "Your Demo session has expired. Your text remains visible; restart Sati and sign in again before saving.",
        innerException);
