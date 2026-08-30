namespace Sati.Models.Billing;

public sealed class RemittanceDeposit
{
    public long Id { get; set; }
    public int AgencyId { get; set; }
    public string PaymentReference { get; set; } = string.Empty;
    public string PayerName { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; }
    public DateTime? PaymentDate { get; set; }
    public decimal ClaimPaymentAmount { get; set; }
    public decimal ProviderLevelAdjustmentAmount { get; set; }
    public string? ProviderLevelAdjustmentSummary { get; set; }
    public decimal RemittancePaymentAmount { get; set; }
    public decimal? EftDepositAmount { get; set; }
    public bool IsSynthetic { get; set; }
}
