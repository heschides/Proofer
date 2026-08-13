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
}
