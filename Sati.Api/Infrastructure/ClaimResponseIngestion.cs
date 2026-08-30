using Microsoft.EntityFrameworkCore;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

/// <param name="Kind">What the document was recognised as.</param>
/// <param name="IsSynthetic">From the document's own ISA15 usage indicator.</param>
/// <param name="StageRecorded">The submission stage written, when one was.</param>
internal sealed record ClaimResponseIngestOutcome(
    ClaimResponseKind Kind,
    bool IsSynthetic,
    BillingSubmissionStage? StageRecorded,
    int ClaimOutcomesRecorded,
    bool DepositRecorded,
    string Explanation);

/// <summary>
/// Writes what an inbound 999, 277CA, or 835 says into the exchange read models.
/// </summary>
/// <remarks>
/// <para>
/// This is the permanent path and it takes documents, not scenarios. The mock
/// clearinghouse and a real one both reach it the same way, so nothing here needs to
/// change when the simulator goes away — which is the whole reason ingestion and
/// fabrication were kept apart rather than fused into one convenient endpoint.
/// </para>
/// <para>
/// Everything it writes is scoped to the actor's agency and the billing period it was
/// told about. It never reads a tenant from the document: an 837 could name any agency it
/// liked, and trusting that would let a crafted response write into another tenant's
/// billing history.
/// </para>
/// <para>
/// <c>IsSynthetic</c> comes from ISA15 rather than from configuration, so a genuine
/// production remittance is recorded as real wherever it arrives, and anything the mock
/// produced is recorded as synthetic wherever it arrives.
/// </para>
/// </remarks>
internal sealed class ClaimResponseIngestion(ApiDbContext db)
{
    public async Task<ClaimResponseIngestOutcome> IngestAsync(
        string document,
        int agencyId,
        int billingPeriodId,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var envelope = ClaimResponseReader.ReadEnvelope(document);
        if (envelope.Kind == ClaimResponseKind.Unrecognised)
        {
            return new ClaimResponseIngestOutcome(
                envelope.Kind, envelope.IsTestInterchange, null, 0, false,
                "The document was not a 999, 277CA, or 835, so nothing was recorded from it.");
        }

        return envelope.Kind == ClaimResponseKind.RemittanceAdvice
            ? await IngestRemittanceAsync(document, envelope, agencyId, billingPeriodId, receivedAtUtc, cancellationToken)
            : IngestAcknowledgement(document, envelope, agencyId, billingPeriodId, receivedAtUtc);
    }

    private ClaimResponseIngestOutcome IngestAcknowledgement(
        string document,
        ClaimResponseEnvelope envelope,
        int agencyId,
        int billingPeriodId,
        DateTime receivedAtUtc)
    {
        var result = ClaimResponseReader.ReadAcknowledgement(document);

        db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
        {
            AgencyId = agencyId,
            BillingPeriodId = billingPeriodId,
            OccurredAtUtc = receivedAtUtc,
            Stage = result.Stage,
            Reference = envelope.ControlNumber,
            ResponseType = envelope.Kind == ClaimResponseKind.FunctionalAcknowledgement ? "999" : "277CA",
            ResponseCode = result.ResponseCode,
            Explanation = result.Explanation,
            IsSynthetic = envelope.IsTestInterchange
        });

        return new ClaimResponseIngestOutcome(
            envelope.Kind, envelope.IsTestInterchange, result.Stage, 0, false, result.Explanation);
    }

    private async Task<ClaimResponseIngestOutcome> IngestRemittanceAsync(
        string document,
        ClaimResponseEnvelope envelope,
        int agencyId,
        int billingPeriodId,
        DateTime receivedAtUtc,
        CancellationToken cancellationToken)
    {
        var remittance = ClaimResponseReader.ReadRemittance(document);

        foreach (var claim in remittance.Claims)
        {
            db.RemittanceClaimOutcomes.Add(new ServerRemittanceClaimOutcome
            {
                AgencyId = agencyId,
                BillingPeriodId = billingPeriodId,
                ClaimReference = claim.ClaimReference,
                PayerName = remittance.PayerName ?? string.Empty,
                ReceivedAtUtc = receivedAtUtc,
                PaymentDate = remittance.PaymentDate,
                Status = claim.Status,
                BilledAmount = claim.BilledAmount,
                AllowedAmount = claim.PaidAmount + claim.PatientResponsibilityAmount,
                PaidAmount = claim.PaidAmount,
                AdjustmentAmount = claim.AdjustmentAmount,
                PatientResponsibilityAmount = claim.PatientResponsibilityAmount,
                ReasonCode = claim.ReasonCode,
                Explanation = claim.Explanation,
                PaymentReference = remittance.PaymentReference,
                IsSynthetic = envelope.IsTestInterchange
            });
        }

        // The deposit is recorded from what the payer said it sent, not from the sum of
        // the claims. They differ whenever a provider-level adjustment is present, and
        // deriving one from the other would hide exactly the discrepancy the
        // reconciliation screen exists to surface.
        db.RemittanceDeposits.Add(new ServerRemittanceDeposit
        {
            AgencyId = agencyId,
            PaymentReference = remittance.PaymentReference ?? string.Empty,
            PayerName = remittance.PayerName ?? string.Empty,
            ReceivedAtUtc = receivedAtUtc,
            PaymentDate = remittance.PaymentDate,
            ClaimPaymentAmount = remittance.ClaimPaymentTotal,
            ProviderLevelAdjustmentAmount = remittance.ProviderLevelAdjustment,
            ProviderLevelAdjustmentSummary = remittance.ProviderLevelAdjustment == 0m
                ? null
                : "Provider-level adjustment reported on the remittance.",
            RemittancePaymentAmount = remittance.RemittancePaymentAmount,
            EftDepositAmount = null,
            IsSynthetic = envelope.IsTestInterchange
        });

        // Paid is the batch reaching payment. Reconciled is a claim about the money having
        // been tied out against a deposit, which nothing has done yet, so it is not
        // asserted here.
        db.BillingSubmissionEvents.Add(new ServerBillingSubmissionEvent
        {
            AgencyId = agencyId,
            BillingPeriodId = billingPeriodId,
            OccurredAtUtc = receivedAtUtc,
            Stage = BillingSubmissionStage.Paid,
            Reference = remittance.PaymentReference ?? envelope.ControlNumber,
            ResponseType = "835",
            ResponseCode = null,
            Explanation =
                $"Remittance received for {remittance.Claims.Count} claim(s). " +
                "The deposit is not reconciled until an EFT amount is matched against it.",
            IsSynthetic = envelope.IsTestInterchange
        });

        await Task.CompletedTask;
        return new ClaimResponseIngestOutcome(
            envelope.Kind,
            envelope.IsTestInterchange,
            BillingSubmissionStage.Paid,
            remittance.Claims.Count,
            true,
            $"Recorded {remittance.Claims.Count} claim outcome(s) and one deposit.");
    }
}
