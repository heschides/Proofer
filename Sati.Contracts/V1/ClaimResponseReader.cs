using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>Which kind of response an interchange carries.</summary>
public enum ClaimResponseKind
{
    /// <summary>999 — the clearinghouse accepted or rejected the file's syntax.</summary>
    FunctionalAcknowledgement,

    /// <summary>277CA — the payer accepted or rejected the claims themselves.</summary>
    ClaimAcknowledgement,

    /// <summary>835 — payment and adjustment detail.</summary>
    RemittanceAdvice,

    /// <summary>Something this reader does not interpret. Never guessed at.</summary>
    Unrecognised
}

/// <param name="Kind">What the interchange carries.</param>
/// <param name="IsTestInterchange">
/// ISA15, the X12 usage indicator: <c>T</c> for test, <c>P</c> for production. This is
/// what decides whether the records written from this document are marked synthetic.
/// The document declares its own status and Sati records that faithfully, rather than
/// inferring it from which environment happened to be running — an inference that would
/// be wrong the moment a real response arrived somewhere unexpected.
/// </param>
/// <param name="ControlNumber">ISA13, for correlating a response with what was sent.</param>
public sealed record ClaimResponseEnvelope(
    ClaimResponseKind Kind,
    bool IsTestInterchange,
    string? ControlNumber);

/// <param name="Stage">The submission stage this acknowledgement moves the batch to.</param>
/// <param name="ResponseCode">The code the payer or clearinghouse gave, when it gave one.</param>
public sealed record ClaimAcknowledgementResult(
    ClaimResponseEnvelope Envelope,
    BillingSubmissionStage Stage,
    string? ResponseCode,
    string Explanation);

/// <summary>One claim's outcome inside an 835.</summary>
public sealed record RemittanceClaimResult(
    string ClaimReference,
    decimal BilledAmount,
    decimal PaidAmount,
    decimal AdjustmentAmount,
    decimal PatientResponsibilityAmount,
    RemittanceClaimStatus Status,
    string? GroupCode,
    string? ReasonCode,
    string Explanation);

/// <param name="ClaimPaymentTotal">The sum of what the claims were paid.</param>
/// <param name="ProviderLevelAdjustment">
/// PLB — adjustments made against the provider rather than any claim, which is why a
/// deposit can differ from the claim total without any claim being wrong.
/// </param>
/// <param name="RemittancePaymentAmount">BPR02, what the payer says it is sending.</param>
public sealed record RemittanceResult(
    ClaimResponseEnvelope Envelope,
    string? PaymentReference,
    string? PayerName,
    DateTime? PaymentDate,
    decimal ClaimPaymentTotal,
    decimal ProviderLevelAdjustment,
    decimal RemittancePaymentAmount,
    IReadOnlyList<RemittanceClaimResult> Claims);

/// <summary>
/// Sole owner of how an inbound 999, 277CA, or 835 is interpreted into the record status
/// Sati stores.
/// </summary>
/// <remarks>
/// <para>
/// This is the permanent half of the claim exchange. A mock clearinghouse and a real one
/// both produce X12; only the transport differs. Keeping interpretation here means the
/// day Office Ally replaces the simulator, none of this changes — and it means the
/// desktop and the API cannot reach different conclusions about whether a claim was paid,
/// which is the failure this project treats as a defect rather than an inconvenience.
/// </para>
/// <para>
/// It reads. It does not fabricate, transmit, or decide who may do either. Anything it
/// does not recognise is reported as <see cref="ClaimResponseKind.Unrecognised"/> rather
/// than interpreted optimistically.
/// </para>
/// </remarks>
public static class ClaimResponseReader
{
    /// <summary>Identifies an interchange without interpreting its contents.</summary>
    public static ClaimResponseEnvelope ReadEnvelope(string x12)
    {
        ArgumentNullException.ThrowIfNull(x12);
        var segments = Parse(x12);

        var isa = Find(segments, "ISA");
        var isTest = isa is { Length: > 15 } && isa[15].Trim() == "T";
        var controlNumber = isa is { Length: > 13 } ? isa[13].Trim() : null;

        var st = Find(segments, "ST");
        var kind = st is { Length: > 1 } ? st[1].Trim() switch
        {
            "999" or "997" => ClaimResponseKind.FunctionalAcknowledgement,
            "277" => ClaimResponseKind.ClaimAcknowledgement,
            "835" => ClaimResponseKind.RemittanceAdvice,
            _ => ClaimResponseKind.Unrecognised
        } : ClaimResponseKind.Unrecognised;

        return new ClaimResponseEnvelope(kind, isTest, controlNumber);
    }

    /// <summary>
    /// Interprets a 999 or a 277CA. The two are separate stages on purpose: a file can be
    /// syntactically accepted and still have every claim rejected, and collapsing them
    /// would lose the distinction between "the clearinghouse could read it" and "the payer
    /// will pay it".
    /// </summary>
    public static ClaimAcknowledgementResult ReadAcknowledgement(string x12)
    {
        var envelope = ReadEnvelope(x12);
        var segments = Parse(x12);

        if (envelope.Kind == ClaimResponseKind.FunctionalAcknowledgement)
        {
            // AK9 carries the functional group's verdict; IK5/AK5 the transaction set's.
            // A is accepted, E is accepted with errors, everything else is a rejection.
            var ak9 = Find(segments, "AK9");
            var ik5 = Find(segments, "IK5") ?? Find(segments, "AK5");
            var code = (ak9 is { Length: > 1 } ? ak9[1].Trim() : null)
                       ?? (ik5 is { Length: > 1 } ? ik5[1].Trim() : null);

            return code is "A" or "E"
                ? new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.FunctionalAccepted, code,
                    "The clearinghouse accepted the file's syntax.")
                : new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.FunctionalRejected, code,
                    "The clearinghouse rejected the file's syntax. No claim in it was forwarded to the payer.");
        }

        if (envelope.Kind == ClaimResponseKind.ClaimAcknowledgement)
        {
            // STC01 is a composite: category:status:entity. The leading category is what
            // decides the verdict — A* accepted, R* rejected, E* errored.
            var statuses = segments
                .Where(segment => segment[0] == "STC" && segment.Length > 1)
                .Select(segment => segment[1].Split(':')[0].Trim())
                .Where(value => value.Length > 0)
                .ToList();

            if (statuses.Count == 0)
            {
                return new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.ClaimRejected, null,
                    "The acknowledgement carried no claim status, so nothing can be treated as accepted.");
            }

            var accepted = statuses.Count(value => value.StartsWith('A'));
            var code = string.Join(",", statuses.Distinct());

            if (accepted == statuses.Count)
            {
                return new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.ClaimAccepted, code,
                    "The payer accepted every claim in the batch.");
            }

            return accepted == 0
                ? new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.ClaimRejected, code,
                    "The payer rejected every claim in the batch.")
                : new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.PartiallyAccepted, code,
                    $"The payer accepted {accepted} of {statuses.Count} claims. The rest need correction and resubmission.");
        }

        return new ClaimAcknowledgementResult(envelope, BillingSubmissionStage.TransportFailed, null,
            "The response was not a recognised acknowledgement, so no status was inferred from it.");
    }

    /// <summary>Interprets an 835 into per-claim outcomes and the deposit totals.</summary>
    public static RemittanceResult ReadRemittance(string x12)
    {
        var envelope = ReadEnvelope(x12);
        var segments = Parse(x12);

        var bpr = Find(segments, "BPR");
        var remittanceAmount = bpr is { Length: > 2 } ? Money(bpr[2]) : 0m;
        var paymentDate = bpr is { Length: > 16 } ? Date(bpr[16]) : null;

        var trn = Find(segments, "TRN");
        var paymentReference = trn is { Length: > 2 } ? trn[2].Trim() : null;

        var payer = segments.FirstOrDefault(segment =>
            segment[0] == "N1" && segment.Length > 2 && segment[1].Trim() == "PR");
        var payerName = payer is { Length: > 2 } ? payer[2].Trim() : null;

        // PLB is provider-level: it moves money without belonging to any claim, which is
        // exactly why a deposit can fail to equal the claim total with every claim correct.
        var providerLevelAdjustment = segments
            .Where(segment => segment[0] == "PLB")
            .SelectMany(segment => segment.Skip(3).Where((_, index) => index % 2 == 1))
            .Sum(Money);

        var claims = ReadClaims(segments);

        return new RemittanceResult(
            envelope,
            paymentReference,
            payerName,
            paymentDate,
            claims.Sum(claim => claim.PaidAmount),
            providerLevelAdjustment,
            remittanceAmount,
            claims);
    }

    private static List<RemittanceClaimResult> ReadClaims(IReadOnlyList<string[]> segments)
    {
        var claims = new List<RemittanceClaimResult>();

        for (var index = 0; index < segments.Count; index++)
        {
            var clp = segments[index];
            if (clp[0] != "CLP" || clp.Length <= 4)
                continue;

            var reference = clp[1].Trim();
            var statusCode = clp[2].Trim();
            var billed = Money(clp[3]);
            var paid = Money(clp[4]);
            var patientResponsibility = clp.Length > 5 ? Money(clp[5]) : 0m;

            decimal adjustment = 0m;
            string? groupCode = null;
            string? reasonCode = null;

            // Everything up to the next CLP belongs to this claim.
            for (var detail = index + 1; detail < segments.Count && segments[detail][0] != "CLP"; detail++)
            {
                var candidate = segments[detail];
                if (candidate[0] != "CAS" || candidate.Length <= 3)
                    continue;

                groupCode ??= candidate[1].Trim();
                reasonCode ??= candidate[2].Trim();
                // CAS repeats reason/amount/quantity triplets after the group code.
                for (var field = 3; field < candidate.Length; field += 3)
                    adjustment += Money(candidate[field]);
            }

            var status = InterpretClaimStatus(statusCode, billed, paid);
            claims.Add(new RemittanceClaimResult(
                reference, billed, paid, adjustment, patientResponsibility,
                status, groupCode, reasonCode, ExplainStatus(status, statusCode)));
        }

        return claims;
    }

    /// <summary>
    /// CLP02 is the payer's own verdict and is trusted first. The amounts only decide the
    /// shade of "processed" — a claim the payer processed but paid nothing on is not the
    /// same as one it denied, and calling both "denied" would put work in the wrong queue.
    /// </summary>
    private static RemittanceClaimStatus InterpretClaimStatus(string statusCode, decimal billed, decimal paid) =>
        statusCode switch
        {
            "4" => RemittanceClaimStatus.Denied,
            "22" => RemittanceClaimStatus.Reversed,
            "1" or "2" or "3" or "19" or "20" or "21" => paid <= 0m
                ? RemittanceClaimStatus.NeedsReview
                : paid >= billed
                    ? RemittanceClaimStatus.Paid
                    : RemittanceClaimStatus.PartiallyPaid,
            _ => RemittanceClaimStatus.NeedsReview
        };

    private static string ExplainStatus(RemittanceClaimStatus status, string statusCode) => status switch
    {
        RemittanceClaimStatus.Paid => "Paid in full.",
        RemittanceClaimStatus.PartiallyPaid => "Paid less than billed. The adjustment reason explains the difference.",
        RemittanceClaimStatus.Denied => "Denied by the payer. It needs correction before it can be resubmitted.",
        RemittanceClaimStatus.Reversed => "A previous payment was reversed. The original outcome no longer stands.",
        _ => $"Processed with claim status {statusCode} and no payment. Needs review before it is worked."
    };

    private static List<string[]> Parse(string x12) => x12
        .Split('~', StringSplitOptions.RemoveEmptyEntries)
        .Select(segment => segment.Trim().Split('*'))
        .Where(segment => segment.Length > 0 && segment[0].Length > 0)
        .ToList();

    private static string[]? Find(IEnumerable<string[]> segments, string name) =>
        segments.FirstOrDefault(segment => segment[0] == name);

    private static decimal Money(string value) =>
        decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static DateTime? Date(string value) =>
        DateTime.TryParseExact(value?.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
