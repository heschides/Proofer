using Sati.Api.Data;

namespace Sati.Api.Security;

internal sealed class AuditTrail(ApiDbContext db, IHttpContextAccessor httpContextAccessor)
{
    public void Record(
        Actor actor,
        string action,
        string resourceType,
        int? resourceId = null,
        string metadataJson = "{}")
    {
        db.AuditEvents.Add(new ServerAuditEvent
        {
            AgencyId = actor.AgencyId,
            ActorUserId = actor.UserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            CorrelationId = httpContextAccessor.HttpContext?.TraceIdentifier ?? string.Empty,
            MetadataJson = metadataJson
        });
    }
}

internal static class AuditActions
{
    public const string AuthenticationSucceeded = "authentication.succeeded";
    public const string UserCreated = "user.created";
    public const string UserUpdated = "user.updated";
    public const string UserPasswordReset = "user.password-reset";
    public const string UserPasswordChanged = "user.password-changed";
    public const string NoteApproved = "note.approved";
    public const string NoteApprovalOverridden = "note.approval-overridden";
    public const string NoteReturned = "note.returned";
    public const string AssessmentCreated = "assessment.created";
    public const string AssessmentUpdated = "assessment.updated";
    public const string AssessmentSubmitted = "assessment.submitted";
    public const string SettingsUpdated = "settings.updated";
    public const string ScratchpadUpdated = "scratchpad.updated";
    public const string PersonCreated = "person.created";
    public const string PersonUpdated = "person.updated";
    public const string PersonJournalUpdated = "person.journal-updated";
    public const string PersonHistoryViewed = "person-history.viewed";
    public const string PersonHistoryPdfGenerated = "person-history-pdf.generated";
    public const string BillingPeriodSubmitted = "billing-period.submitted";
    public const string BillingClaimLineCreated = "billing-claim-line.created";
    public const string BillingEdiGenerated = "billing-edi.generated";
}
