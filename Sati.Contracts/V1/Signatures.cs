using System.Text;

namespace Sati.Contracts.V1;

public enum SignerCapacity { Consumer, Guardian, AuthorizedRepresentative }
public enum SignatureMeaning { Authorization, ReceiptAcknowledgment, PlanAgreement, None }
public enum SignaturePolicyStatus { SyntheticTestingOnly, PendingProgramConfirmation, NotSignable }

public sealed record SignatureMeaningEntry(AnnualDocumentKind Kind, string DisplayName,
    SignatureMeaning Meaning, SignaturePolicyStatus PolicyStatus,
    IReadOnlyList<SignerCapacity> Capacities, string IntentText, string Explanation);

/// <summary>Meaning and permitted scope are distinct from a legal or agency approval.</summary>
public static class SignatureMeaningCatalog
{
    private static readonly SignerCapacity[] AllCapacities =
        [SignerCapacity.Consumer, SignerCapacity.Guardian, SignerCapacity.AuthorizedRepresentative];
    public static IReadOnlyList<SignatureMeaningEntry> All { get; } =
    [
        new(AnnualDocumentKind.ReleaseAgency, "Agency release", SignatureMeaning.Authorization,
            SignaturePolicyStatus.SyntheticTestingOnly, AllCapacities,
            "I intend to sign the authorization in this exact document, in the name and capacity shown. My signature applies only to the document's stated choices, recipients, purpose and time period.",
            "Fictional-data testing only. The agency must approve the authorization wording and permitted signing authority before real use."),
        new(AnnualDocumentKind.ReleaseMedical, "Medical release", SignatureMeaning.Authorization,
            SignaturePolicyStatus.SyntheticTestingOnly, AllCapacities,
            "I intend to sign the medical-information authorization in this exact document, in the name and capacity shown. My signature applies only to the document's stated choices, recipients, purpose and time period.",
            "Fictional-data testing only. Electronic signing does not approve the wording or special-record disclosure rules."),
        new(AnnualDocumentKind.PrivacyPractices, "Notice of Privacy Practices", SignatureMeaning.ReceiptAcknowledgment,
            SignaturePolicyStatus.SyntheticTestingOnly, AllCapacities,
            "I acknowledge receipt of this exact Notice of Privacy Practices. This acknowledges receipt only. It is not agreement to the notice or authorization to disclose information.",
            "Receipt acknowledgment only. It does not record agreement, permission to disclose, or completion of another form."),
        new(AnnualDocumentKind.SafetyPlan, "Consumer safety plan", SignatureMeaning.PlanAgreement,
            SignaturePolicyStatus.PendingProgramConfirmation, [SignerCapacity.Consumer, SignerCapacity.Guardian],
            "I intend to sign this exact plan in the name and capacity shown.",
            "Signing is unavailable pending written agency/program confirmation of the required meaning, signers and accepted method."),
        new(AnnualDocumentKind.ReleaseDhhs, "DHHS authorization", SignatureMeaning.Authorization,
            SignaturePolicyStatus.PendingProgramConfirmation, AllCapacities,
            "I intend to sign this exact authorization in the name and capacity shown.",
            "Signing is unavailable pending written confirmation that this state-owned form and evidence method are accepted."),
        new(AnnualDocumentKind.MedicalRecordsRequest, "Medical records request", SignatureMeaning.None,
            SignaturePolicyStatus.NotSignable, [], "",
            "This agency-to-provider request is not a consumer authorization. It relies on the separately obtained medical release.")
    ];

    public static SignatureMeaningEntry? Find(AnnualDocumentKind kind) => All.SingleOrDefault(x => x.Kind == kind);
    public static bool CanRequest(AnnualDocumentKind kind, SignerCapacity capacity) =>
        Find(kind) is { PolicyStatus: SignaturePolicyStatus.SyntheticTestingOnly } entry &&
        entry.Capacities.Contains(capacity);
}

public static class SigningPinRules
{
    public const int MinimumDigits = 8;
    public const int MaximumDigits = 12;
    public const int MaximumAttempts = 5;
    public const string Explanation = "Choose a new code of 8 to 12 digits. Avoid birth dates, identifiers, repeated digits and counting sequences. Never send the code with the email link.";

    public static bool IsValid(string? pin, DateTime? signerBirthDate = null)
    {
        if (pin is null || pin.Length is < MinimumDigits or > MaximumDigits || pin.Any(x => x is < '0' or > '9')) return false;
        if (pin.Distinct().Count() < 3 || "01234567890123456789".Contains(pin, StringComparison.Ordinal) ||
            "98765432109876543210".Contains(pin, StringComparison.Ordinal)) return false;
        if (signerBirthDate is { } birth && new[] { "yyyyMMdd", "MMddyyyy", "ddMMyyyy" }
            .Any(format => pin == birth.ToString(format, System.Globalization.CultureInfo.InvariantCulture))) return false;
        return true;
    }
}

public static class SignatureRules
{
    public const int MaximumPdfBytes = 15 * 1024 * 1024;
    public const int DefaultExpiryHours = 72;
    public const int MinimumExpiryHours = 24;
    public const int MaximumExpiryHours = 168;
    public const int SessionMinutes = 30;
    public const string DisclosureVersion = "synthetic-electronic-records-v1";
    public const string DisclosureText = "FICTIONAL-DATA TESTING ONLY. Electronic signing is your choice. Paper, in-person and assisted options remain available without disadvantage; choosing them must not affect services. " +
        "For this request only, you may choose to receive and sign the displayed document electronically. You need an internet connection, a current browser, and a PDF reader that lets you open, save and print the document. " +
        "Before agreeing, open the actual PDF and confirm that you can access and keep it. You may ask the agency for an accessible format or a free paper copy. " +
        "Contact your case manager through the contact information you already have to correct an email address, arrange assistance, obtain a copy, or withdraw consent to electronic signing. You can also withdraw during this session. " +
        "Withdrawing electronic-signing consent stops this unfinished request; it does not erase earlier signatures. Withdrawing permission to disclose health information is a separate action governed by the authorization and applicable law. " +
        "Code verification, document access, choices and signing time are recorded as evidence. Your plain signing code is not retained. This does not prove that you read or understood a document. No signature is inferred from opening or downloading it. " +
        "The agency must approve its actual disclosure, contact procedures and document wording before real-client use.";
    public const string ScopeNotice = "Sati records an electronic signing action and its evidence. This does not establish legal sufficiency, agency or state acceptance, service authorization, billing approval, or completion by every required signer.";
    public const string RetainedHistoryMessage = "This consumer has retained signature records. Archive the consumer instead of deleting the record.";

    public static bool IsOpen(string? state) => state is "Issued" or "Viewed";
    public static bool IsTerminal(string? state) => state is "Signed" or "Declined" or "ChangesRequested" or "Expired" or "Revoked";
    public static bool NamesMatch(string? expected, string? entered) => !string.IsNullOrWhiteSpace(expected) &&
        string.Equals(NormalizeName(expected), NormalizeName(entered), StringComparison.OrdinalIgnoreCase);
    private static string NormalizeName(string? value) => string.Join(" ", (value ?? "").Normalize(NormalizationForm.FormKC)
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed record SignatureAvailabilityDto(bool Enabled, string Explanation, string DeliveryMode);
public sealed record SignatureSignerDto(SignerCapacity Capacity, int? ContactId, string Name, string? Email);
public sealed record FreezeSignatureDocumentRequest(Guid ClientRequestId, byte[] Pdf, bool CompletenessReviewed);
public sealed record FrozenSignatureDocumentDto(int Id, int DocumentArtifactId, string ContentSha256,
    long ByteCount, DateTime StoredAtUtc);
public sealed record CreateSignatureRequest(Guid ClientRequestId, int PersonId, int DocumentArtifactId,
    SignerCapacity SignerCapacity, int? SignerContactId, string Pin, string ConfirmPin,
    bool IdentityConfirmed, bool EmailConfirmed, string? AuthorityEvidence,
    int ExpiryHours = SignatureRules.DefaultExpiryHours, string? ExpectedSignerName = null, string? ExpectedDeliveryEmail = null);
public sealed record ReplaceSignatureRequest(Guid ClientRequestId, long ExpectedRevision, string Pin,
    string ConfirmPin, bool IdentityConfirmed, bool EmailConfirmed, string Reason,
    string? ExpectedSignerName = null, string? ExpectedDeliveryEmail = null);
public sealed record SignatureReasonRequest(long ExpectedRevision, string Reason);
public sealed record SignatureEventDto(long Sequence, string Kind, string ActorKind, DateTime OccurredAtUtc);
public sealed record SignatureRequestDto(int Id, Guid ClientRequestId, int PersonId, int DocumentArtifactId,
    string DocumentName, string Meaning, string SignerName, string SignerCapacity, string DeliveryEmail,
    string State, long Revision, DateTime IssuedAtUtc, DateTime ExpiresAtUtc,
    int FailedPinAttempts, bool IsLocked, string DeliveryState, bool HasSignedPackage,
    DateTime? CompletedAtUtc, DateTime? AuthorizationRevokedAtUtc, string? TerminalReason,
    IReadOnlyList<SignatureEventDto> Events, int? SignerContactId = null,
    string ReceiptDeliveryState = "NotQueued", DateTime? ExternalAccessRevokedAtUtc = null);
