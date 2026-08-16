namespace Sati.Data;

/// <summary>
/// Thrown when a save is attempted against an AT request that has already been
/// published. Distinct from <see cref="AtRequestConcurrencyException"/> because
/// the remedy is different: a stale request should be reloaded and reapplied,
/// whereas a published request must be deliberately reopened, which discards the
/// case manager's attestation.
/// </summary>
public sealed class AtRequestLockedException : Exception
{
    public AtRequestLockedException()
        : base("This AT request has been published and can no longer be edited. Reopen it for correction to make changes; reopening removes the attestation and requires publishing again.")
    {
    }

    public AtRequestLockedException(Exception innerException)
        : base("This AT request has been published and can no longer be edited. Reopen it for correction to make changes; reopening removes the attestation and requires publishing again.", innerException)
    {
    }
}
