namespace Sati.Contracts.V1;

/// <summary>
/// What the mock clearinghouse should pretend happened.
/// </summary>
/// <remarks>
/// The point of naming scenarios rather than always producing a happy path is that a
/// denial worklist cannot be exercised by a payer that never denies, and a deposit
/// reconciliation screen cannot be exercised by one whose totals always agree. Each of
/// these drives a state the read models already know how to display and that nothing has
/// ever actually produced.
///
/// Every scenario emits test interchanges only — ISA15 is <c>T</c> — so anything ingested
/// from them records itself as synthetic without depending on where it ran.
/// </remarks>
public enum MockClearinghouseScenario
{
    /// <summary>Syntax accepted, claims accepted, paid in full.</summary>
    Accepted,

    /// <summary>The 999 rejects the file. Nothing reaches the payer, so no 277CA and no 835.</summary>
    SyntaxRejected,

    /// <summary>Syntax accepted, every claim rejected by the payer. No 835 follows.</summary>
    ClaimsRejected,

    /// <summary>Syntax accepted, some claims accepted and some rejected.</summary>
    PartiallyAccepted,

    /// <summary>Accepted and paid below the billed amount, with a contractual adjustment.</summary>
    PartialPayment,

    /// <summary>Accepted, then denied on the remittance with a reason code.</summary>
    Denied,

    /// <summary>Paid in full, with a provider-level adjustment so the deposit differs from the claim total.</summary>
    ProviderLevelAdjustment,

    /// <summary>A previously paid claim reversed.</summary>
    Reversal
}

/// <param name="Scenario">What the mock should produce.</param>
public sealed record MockClearinghouseRequest(MockClearinghouseScenario Scenario);

/// <summary>
/// The documents the mock produced, returned alongside what ingesting them recorded.
/// </summary>
/// <param name="FunctionalAcknowledgement">The 999, always produced.</param>
/// <param name="ClaimAcknowledgement">The 277CA, absent when the 999 rejected the file.</param>
/// <param name="RemittanceAdvice">The 835, absent when nothing was accepted for payment.</param>
/// <param name="StagesRecorded">The submission stages ingestion wrote, in order.</param>
/// <param name="ClaimOutcomesRecorded">How many claim outcomes were written.</param>
/// <param name="DepositRecorded">Whether a deposit row was written.</param>
public sealed record MockClearinghouseResultDto(
    string Scenario,
    string FunctionalAcknowledgement,
    string? ClaimAcknowledgement,
    string? RemittanceAdvice,
    IReadOnlyList<string> StagesRecorded,
    int ClaimOutcomesRecorded,
    bool DepositRecorded);

/// <param name="Document">A 999, 277CA, or 835 to ingest.</param>
public sealed record ClaimResponseIngestRequest(string Document);

/// <param name="Kind">What the document was recognised as.</param>
/// <param name="IsSynthetic">Taken from the document's own ISA15 usage indicator.</param>
/// <param name="StageRecorded">The submission stage written, when one was.</param>
/// <param name="ClaimOutcomesRecorded">How many claim outcomes were written.</param>
/// <param name="DepositRecorded">Whether a deposit row was written.</param>
public sealed record ClaimResponseIngestResultDto(
    string Kind,
    bool IsSynthetic,
    string? StageRecorded,
    int ClaimOutcomesRecorded,
    bool DepositRecorded,
    string Explanation);
