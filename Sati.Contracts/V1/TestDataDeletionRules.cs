namespace Sati.Contracts.V1;

/// <summary>
/// Shared safeguards for the deliberately narrow Admin test-consumer deletion command.
/// The attestation is not a password. It is a versioned, auditable statement that keeps
/// an older or hand-written client from accidentally invoking a newer destructive command.
/// </summary>
public static class TestDataDeletionRules
{
    public const string ConsumerConfirmationText =
        "Clicking delete affirms the consumer being deleted was created for testing purposes only.  " +
        "For duplicate consumers or consumers who are no longer receiving services, please click cancel and seek guidance in the help menu.";

    public const string ConsumerAttestation = "consumer-created-for-testing-purposes-only-v1";

    public const string ConsumerHasClaimsMessage =
        "This consumer was not deleted because one or more notes already have billing claim records. " +
        "Billing records are retained even when they were created for testing. Seek guidance in the help menu for a safe cleanup.";

    public static bool HasValidConsumerAttestation(string? attestation) =>
        string.Equals(attestation, ConsumerAttestation, StringComparison.Ordinal);
}
