using Sati.Contracts.V1;

namespace Sati.Models.Billing;

public sealed class RemittanceClaimOutcome
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public int? BillingPeriodId { get; set; }
    public string ClaimReference { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? PaymentDate { get; set; }
    public RemittanceClaimStatus Status { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal? AllowedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal AdjustmentAmount { get; set; }
    public decimal PatientResponsibilityAmount { get; set; }
    public string? ReasonCode { get; set; }
    public string? Explanation { get; set; }
    public string? PaymentReference { get; set; }
    public bool IsSynthetic { get; set; }

    public BillingPeriod? BillingPeriod { get; set; }
}
