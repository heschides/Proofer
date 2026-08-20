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

/// <summary>
/// Where the key that wraps per-record SSN data keys lives.
///
/// <see cref="KeyUri"/> is deliberately versionless — wrapping always uses the
/// current version and each row records the version that wrapped it, so rotating in
/// Key Vault needs no deployment and no backfill.
///
/// Demo and Production must name DIFFERENT keys. One environment's ciphertext being
/// inert against the other's vault is the safeguard that makes a mis-pointed
/// connection string fail closed instead of decrypting the wrong environment's data.
///
/// Left empty, SSN protection is unconfigured and every SSN operation fails closed.
/// The API still starts: an environment that never stores an SSN should not be
/// unable to serve notes and billing because a vault was not provisioned.
/// </summary>
internal sealed class SsnProtectionOptions
{
    public const string SectionName = "Ssn";
    public string KeyUri { get; init; } = string.Empty;
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
