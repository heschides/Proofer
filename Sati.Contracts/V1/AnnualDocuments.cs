namespace Sati.Contracts.V1;

public enum AnnualDocumentKind
{
    ReleaseAgency,
    ReleaseDhhs,
    ReleaseMedical,
    SafetyPlan,
    PrivacyPractices,
    MedicalRecordsRequest
}

public enum DocumentArtifactOrigin
{
    GeneratedInSati,
    Draft,
    RecordedAsExternal
}

public sealed record AnnualDocumentCatalogEntry(
    AnnualDocumentKind Kind,
    string DisplayName,
    string? SatisfiesFormType,
    bool IncludedInAnnualPacket,
    bool CanRenderWithoutConsumerInput);

/// <summary>Single mapping owner for annual-document identity and compliance meaning.</summary>
public static class AnnualDocumentCatalog
{
    public static IReadOnlyList<AnnualDocumentCatalogEntry> All { get; } =
    [
        new(AnnualDocumentKind.ReleaseAgency, "Agency release", "Release_Agency", true, false),
        new(AnnualDocumentKind.ReleaseDhhs, "DHHS authorization to release", "Release_DHHS", true, false),
        new(AnnualDocumentKind.ReleaseMedical, "Medical release", "Release_Medical", true, false),
        new(AnnualDocumentKind.SafetyPlan, "Consumer safety plan", "SafetyPlan", true, false),
        new(AnnualDocumentKind.PrivacyPractices, "Notice of Privacy Practices", "PrivacyPractices", true, true),
        new(AnnualDocumentKind.MedicalRecordsRequest, "Medical records request", null, true, true)
    ];

    public static AnnualDocumentCatalogEntry? ForFormType(string formType) =>
        All.SingleOrDefault(entry => string.Equals(
            entry.SatisfiesFormType, formType, StringComparison.OrdinalIgnoreCase));

    public static AnnualDocumentCatalogEntry ForKind(AnnualDocumentKind kind) =>
        All.Single(entry => entry.Kind == kind);
}

public static class AnnualDocumentCycle
{
    public static DateTime CurrentStart(DateTime effectiveDate, DateTime onDate)
    {
        var start = effectiveDate.AddYears(onDate.Year - effectiveDate.Year).Date;
        return start > onDate.Date ? start.AddYears(-1).Date : start;
    }
}

public sealed record DocumentArtifactDto(
    int Id,
    int PersonId,
    int AgencyId,
    string Kind,
    DateTime CycleStart,
    string Origin,
    DateTime GeneratedAtUtc,
    int GeneratedByUserId,
    string? ContentSha256,
    long? ByteCount,
    string? SuggestedFileName,
    IReadOnlyList<string> BlankFields,
    string? ExternalNote,
    string? TemplateOwner = null,
    string? TemplateKey = null,
    int? TemplateVersion = null);

public sealed record RecordExternalDocumentRequest(DateTime CycleStart, string Note);

public static class AnnualDocumentRules
{
    public const int ExternalNoteMaxLength = 1_000;

    public static string? ValidateExternalNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return "A note is required for an external document.";
        return note.Trim().Length > ExternalNoteMaxLength
            ? $"The external-document note cannot exceed {ExternalNoteMaxLength} characters."
            : null;
    }
}

public sealed record RenderAnnualDocumentRequest(
    DateTime? CycleStart = null,
    AgencyReleaseRequest? Release = null,
    DhhsFormRequest? Dhhs = null);

public sealed record FormPrerequisiteStatusDto(
    string Kind,
    bool IsSatisfied,
    string Summary,
    IReadOnlyList<int> ArtifactIds,
    bool CanSupervisorOverride);
