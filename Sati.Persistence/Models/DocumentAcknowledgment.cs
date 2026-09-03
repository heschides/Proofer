namespace Sati.Models;

public sealed class DocumentAcknowledgment
{
    public int Id { get; set; }
    public int DocumentArtifactId { get; set; }
    public DateTime? ReceivedOn { get; set; }
    public string? GoodFaithEffortReason { get; set; }
    public int RecordedByUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}
