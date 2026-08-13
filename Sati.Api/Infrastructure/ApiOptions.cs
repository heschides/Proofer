using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class ApiAuthenticationOptions
{
    public const string SectionName = "Authentication";
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public string SigningKey { get; init; } = string.Empty;
    public int TokenMinutes { get; init; } = 30;
}

internal sealed class SatiApiOptions
{
    public const string SectionName = "Sati";
    public string ExpectedDatabaseName { get; init; } = "SatiDemo";
    public string ExpectedEnvironment { get; init; } = "Demo";
    public string TimeZoneId { get; init; } = "Eastern Standard Time";
    public int AuditRetentionDays { get; init; } = OperationalPolicyDefaults.AuditRetentionDays;
    public int EdiReplayRetentionDays { get; init; } = OperationalPolicyDefaults.EdiReplayRetentionDays;
}
