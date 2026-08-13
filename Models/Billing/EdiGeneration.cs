namespace Sati.Models.Billing;

public sealed class EdiGeneration
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int ActorUserId { get; set; }
    public int BillingPeriodId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public bool IsTest { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public BillingPeriod BillingPeriod { get; set; } = null!;
}
