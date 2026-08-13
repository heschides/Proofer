namespace Sati.Models.Billing;

public sealed record BillingConfiguration(
    string ProcedureCode,
    string? Modifier,
    decimal? UnitRate,
    string EdiSubmitterId,
    string PayerName,
    string PayerId,
    string ContactName,
    string ContactPhone);
