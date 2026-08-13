namespace Sati.Models;

public sealed class IncidentGroup
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string FirstRelease { get; set; } = string.Empty;
    public string LastRelease { get; set; } = string.Empty;
    public string ExceptionFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public int OccurrenceCount { get; set; } = 1;
    public DateTime FirstSeenUtc { get; set; }
    public DateTime LastSeenUtc { get; set; }
    public string LastReference { get; set; } = string.Empty;
    public string LastActorRole { get; set; } = string.Empty;
}
