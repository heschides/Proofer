using Sati.Models;

namespace Sati.Data;

internal static class LocalAuditTrail
{
    public static void Record(
        SatiContext context,
        User actor,
        string action,
        string resourceType,
        int? resourceId = null,
        string metadataJson = "{}")
    {
        context.AuditEvents.Add(new AuditEvent
        {
            AgencyId = actor.AgencyId,
            ActorUserId = actor.Id,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CorrelationId = $"desktop-{Guid.NewGuid():N}",
            MetadataJson = metadataJson
        });
    }
}

internal static class LocalAuditActions
{
    public const string AuthenticationSucceeded = "authentication.succeeded";
    public const string PersonCreated = "person.created";
    public const string PersonUpdated = "person.updated";
    public const string PersonJournalUpdated = "person.journal-updated";
    public const string PersonHistoryViewed = "person-history.viewed";
    public const string PersonHistoryPdfGenerated = "person-history-pdf.generated";
    public const string AuditExported = "audit.exported";
    public const string AssessmentCreated = "assessment.created";
    public const string AssessmentUpdated = "assessment.updated";
    public const string AssessmentSubmitted = "assessment.submitted";
    public const string NoteCreated = "note.created";
    public const string NoteUpdated = "note.updated";
    public const string NoteApproved = "note.approved";
    public const string NoteApprovalOverridden = "note.approval-overridden";
    public const string NoteReturned = "note.returned";
    public const string BillingClaimLineCreated = "billing-claim-line.created";
    public const string BillingPeriodSubmitted = "billing-period.submitted";
    public const string IncidentStatusUpdated = "incident-status.updated";
    public const string BillingEdiGenerated = "billing-edi.generated";
    public const string BillingConfigurationUpdated = "billing-configuration.updated";
}
