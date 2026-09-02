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
    public const string NoteReassigned = "note.reassigned";
    public const string AssessmentCreated = "assessment.created";
    public const string AssessmentUpdated = "assessment.updated";
    public const string AssessmentSubmitted = "assessment.submitted";
    public const string SettingsUpdated = "settings.updated";
    public const string ScratchpadUpdated = "scratchpad.updated";
    public const string PersonCreated = "person.created";
    public const string PersonUpdated = "person.updated";
    public const string PersonJournalUpdated = "person.journal-updated";
    // Distinct from PersonJournalUpdated so the trail separates an entry the
    // application stamped and placed from a case manager's own free-text edit.
    public const string PersonJournalReminderAdded = "person.journal-reminder-added";
    // Moving a consumer between caseloads changes who may read a clinical record, so it is
    // its own action rather than a person.updated with different fields.
    public const string PersonReassigned = "person.reassigned";
    public const string PersonSsnUpdated = "person.ssn-updated";
    // Recorded on every decryption, separately from the form generation that caused
    // it. An SSN read is a disclosure, and accounting of disclosures needs the read
    // itself in the trail, not merely the document that occasioned it.
    public const string PersonSsnDecrypted = "person.ssn-decrypted";
    public const string DhhsFormGenerated = "dhhs-form.generated";
    public const string AgencyReleaseGenerated = "agency-release.generated";
    public const string PersonHistoryViewed = "person-history.viewed";
    public const string PersonHistoryPdfGenerated = "person-history-pdf.generated";
    public const string TestConsumerDeleted = "test-data.consumer-deleted";
    public const string ProviderMerged = "provider.merged";
    public const string BillingPeriodSubmitted = "billing-period.submitted";
    public const string BillingClaimLineCreated = "billing-claim-line.created";
    public const string BillingEdiGenerated = "billing-edi.generated";
    public const string BillingConfigurationUpdated = "billing-configuration.updated";
    public const string AtRequestPublished = "at-request.published";
    public const string AtRequestReopened = "at-request.reopened";
    public const string AuditExported = "audit.exported";
    public const string PlatformIncidentsViewed = "platform-incidents.viewed";
    public const string IncidentStatusUpdated = "incident-status.updated";
}
