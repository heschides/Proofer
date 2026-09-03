namespace Sati.Models;

/// <summary>Versioned clinical safety-plan content. Narrative remains in this record, never audit metadata.</summary>
public sealed class SafetyPlan
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person Person { get; set; } = null!;
    public int AuthorUserId { get; set; }
    public User AuthorUser { get; set; } = null!;
    public DateTime CycleStart { get; set; }
    public string Status { get; set; } = "Draft";
    public int Version { get; set; } = 1;
    public int Revision { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }
    public string? ReturnReason { get; set; }
    public string DocumentJson { get; set; } = "{}";
}
