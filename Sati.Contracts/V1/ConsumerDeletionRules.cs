namespace Sati.Contracts.V1;

/// <summary>
/// Whether a person's billing has reached the point where deleting the local record would leave
/// Sati's books disagreeing with a payer's. A pure predicate over already-loaded facts, not a
/// <c>DbContext</c>, so it is testable from a literal and so the API and the transitional
/// desktop-local service cannot evaluate it two different ways.
/// </summary>
/// <param name="HasTransmittedBillingSubmissionEvent">
/// True when any of the person's claim lines belong to a <c>BillingPeriod</c> with a
/// <c>BillingSubmissionEvent</c> where <c>IsSynthetic == false</c> and
/// <c>Stage &gt;= BillingSubmissionStage.Transmitted</c>. <c>Generated</c> is local and does not
/// set this — generating an EDI file does not send it.
/// </param>
/// <param name="HasNonSyntheticRemittanceClaimOutcome">
/// True when any of the person's claim lines belong to a <c>BillingPeriod</c> with a
/// <c>RemittanceClaimOutcome</c> where <c>IsSynthetic == false</c>.
/// </param>
/// <param name="HasSubmittedOrNonDraftBillingPeriod">
/// True when any of the person's claim lines belong to a <c>BillingPeriod</c> with
/// <c>SubmittedAt != null</c> or <c>Status != BillingStatus.Draft</c>.
/// </param>
public sealed record BillingIntegrityFacts(
    bool HasTransmittedBillingSubmissionEvent,
    bool HasNonSyntheticRemittanceClaimOutcome,
    bool HasSubmittedOrNonDraftBillingPeriod)
{
    public static readonly BillingIntegrityFacts None = new(false, false, false);
}

/// <summary>
/// Rules for HANDOFF_CLIENT_DELETION_POLICY.md's rule-3 deletion: an Admin may permanently
/// delete an ordinary (non-test) consumer created within the last <see cref="DeletionWindowDays"/>
/// days, provided none of their billing actually reached a payer and no legal hold is active.
///
/// <para>
/// This is deliberately permissive about everything else — notes, assessments, contacts, AT
/// requests, and draft or synthetic claim lines are all deletable inside the window. That
/// permissiveness is the point of the window: a record created to try something out will
/// normally carry exactly that kind of content, and blocking on it would defeat the purpose.
/// </para>
///
/// <para>
/// A linked chat room is a separate retained record and blocks either deletion command,
/// including when the room has been archived. The consumer must be archived instead.
/// </para>
///
/// <para>
/// Distinct from <see cref="TestDataDeletionRules"/>: that command requires a creation-time
/// <c>IsTestData</c> marker and works on any consumer, however old, but only ones an Admin
/// explicitly attested were synthetic at birth. This command needs no marker but is bounded by
/// time and by billing integrity instead. An older client must not be able to invoke this
/// broader command under the older attestation, which is why the two attestation strings differ.
/// </para>
/// </summary>
public static class ConsumerDeletionRules
{
    public const int DeletionWindowDays = 20;

    public const string ConsumerAttestation = "consumer-deleted-in-window-v1";

    public const string HasChatHistoryMessage =
        "This consumer has retained chat history. Archive the consumer instead of deleting the record.";

    public const string OutsideWindowMessage =
        "This consumer was created more than 20 days ago and can no longer be permanently " +
        "deleted. Use the archive status instead, or seek guidance in the help menu.";

    public const string TransmittedBillingMessage =
        "This consumer was not deleted because billing for their services has already reached " +
        "a payer. Billing records are retained even when a consumer record is later found to " +
        "be a mistake. Seek guidance in the help menu for a safe cleanup.";

    public const string LegalHoldActiveMessage =
        "This consumer was not deleted because a legal hold is active on the record.";

    public const string LegalHoldUnavailableMessage =
        "This consumer was not deleted because Sati could not confirm whether a legal hold " +
        "applies to this record. Try again, and seek guidance in the help menu if this continues.";

    /// <summary>
    /// UTC, exclusive at the far end, and always computed server-side from a server clock —
    /// never from a client-supplied timestamp, since this decides a destructive permission.
    /// </summary>
    public static bool IsWithinDeletionWindow(DateTime createdAtUtc, DateTime nowUtc) =>
        createdAtUtc.AddDays(DeletionWindowDays) > nowUtc;

    /// <summary>
    /// True when any of the three billing-integrity facts block deletion. Everything else —
    /// notes, assessments, contacts, AT requests, draft or synthetic claim lines — is silent
    /// about this predicate on purpose, per the class-level remarks.
    /// </summary>
    public static bool HasTransmittedBilling(BillingIntegrityFacts facts) =>
        facts.HasTransmittedBillingSubmissionEvent ||
        facts.HasNonSyntheticRemittanceClaimOutcome ||
        facts.HasSubmittedOrNonDraftBillingPeriod;

    public static bool HasValidConsumerAttestation(string? attestation) =>
        string.Equals(attestation, ConsumerAttestation, StringComparison.Ordinal);
}
