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
    // Mirrors AuditActions.PersonJournalReminderAdded on the API side.
    public const string PersonJournalReminderAdded = "person.journal-reminder-added";
    public const string PersonHistoryViewed = "person-history.viewed";
    public const string PersonHistoryPdfGenerated = "person-history-pdf.generated";
    public const string TestConsumerDeleted = "test-data.consumer-deleted";
    public const string ProviderMerged = "provider.merged";
    public const string AuditExported = "audit.exported";
    public const string AssessmentCreated = "assessment.created";
    public const string AssessmentUpdated = "assessment.updated";
    public const string AssessmentSubmitted = "assessment.submitted";
    public const string NoteCreated = "note.created";
    public const string NoteUpdated = "note.updated";
    public const string NoteReassigned = "note.reassigned";
    public const string NoteApproved = "note.approved";
    public const string NoteApprovalOverridden = "note.approval-overridden";
    public const string NoteReturned = "note.returned";
    public const string BillingClaimLineCreated = "billing-claim-line.created";
    public const string BillingPeriodSubmitted = "billing-period.submitted";
    public const string IncidentStatusUpdated = "incident-status.updated";
    public const string BillingEdiGenerated = "billing-edi.generated";
    public const string BillingConfigurationUpdated = "billing-configuration.updated";
    // Mirrors AuditActions.PersonReassigned on the API side.
    public const string PersonReassigned = "person.reassigned";
    public const string PersonSsnUpdated = "person.ssn-updated";
    // A read is the disclosure, so it is recorded separately from whatever document
    // occasioned it. Mirrors AuditActions.PersonSsnDecrypted on the API side.
    public const string PersonSsnRevealed = "person.ssn-revealed";
    // Mirrors AuditActions.DhhsFormGenerated on the API side. Generating a release
    // form is a disclosure whichever environment produced it, so both paths record it
    // under the same action name and an audit export does not have to know which
    // client filled the form.
    public const string DhhsFormGenerated = "dhhs-form.generated";
    public const string AgencyReleaseGenerated = "agency-release.generated";
    // Recorded by FormDuplicateRepair when it collapses duplicate compliance form
    // rows. Deleting a billing-relevant record leaves evidence even though the
    // deletion is automated and no one is signed in when it runs.
    public const string FormDuplicateRemoved = "form.duplicate-removed";
    public const string FormAttested = "form.attested";
    public const string FormAttestationRevoked = "form.attestation-revoked";
    public const string FormPrerequisiteOverridden = "form.prerequisite-overridden";
    public const string DocumentGenerated = "document.generated";
    public const string DocumentRecordedExternal = "document.recorded-external";
    public const string DocumentTemplatePublished = "document-template.published";
    // Mirrors AuditActions.ConsumerArchived / ConsumerUnarchived on the API side.
    public const string ConsumerArchived = "consumer.archived";
    public const string ConsumerUnarchived = "consumer.unarchived";
    // Mirrors AuditActions.LegalHoldPlaced / LegalHoldReleased on the API side.
    public const string LegalHoldPlaced = "legal-hold.placed";
    public const string LegalHoldReleased = "legal-hold.released";
    // Rule-3 deletion tombstone. Mirrors AuditActions.ConsumerDeletedInWindow on the API side.
    public const string ConsumerDeletedInWindow = "consumer.deleted-in-window";
}
