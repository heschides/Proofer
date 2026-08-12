namespace Sati.Models;

public sealed class PersonVersion
{
    public long Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public int Version { get; set; }
    public string ChangeKind { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public byte[] SnapshotGzip { get; set; } = [];
    public byte[] ChangesGzip { get; set; } = [];
}
