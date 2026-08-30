namespace Sati.Models;

public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    public string CorrelationId { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";
}
