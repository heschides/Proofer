namespace Sati.Data;

/// <summary>
/// A scratchpad save failure whose message is safe to show to the signed-in user.
/// Infrastructure exceptions remain available as the inner exception for the local technical log.
/// </summary>
public sealed class ScratchpadSaveException(string message, Exception innerException)
    : Exception(message, innerException);
