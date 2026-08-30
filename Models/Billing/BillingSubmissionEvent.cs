using Sati.Contracts.V1;

namespace Sati.Models.Billing;

public sealed class BillingSubmissionEvent
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int BillingPeriodId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public BillingSubmissionStage Stage { get; set; }
    public string? Reference { get; set; }
    public string? ResponseType { get; set; }
    public string? ResponseCode { get; set; }
    public string? Explanation { get; set; }
    public bool IsSynthetic { get; set; }

    public BillingPeriod BillingPeriod { get; set; } = null!;
}
