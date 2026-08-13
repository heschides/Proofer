namespace Sati.Contracts.V1;

public static class IncidentSeverities
{
    public const string Warning = "Warning";
    public const string Error = "Error";
    public const string Critical = "Critical";

    public static bool IsValid(string? value) =>
        value is Warning or Error or Critical;
}

public static class IncidentScopes
{
    public const string Agency = "Agency";
    public const string Platform = "Platform";
}

public sealed record IncidentReportRequest(
    string Reference,
    string Source,
    string Severity,
    string Operation,
    string Release,
    string ExceptionFingerprint,
    DateTime OccurredAtUtc);

public sealed record UpdateIncidentStatusRequest(string Status);

public sealed record IncidentGroupDto(
    long Id,
    int AgencyId,
    string Scope,
    string Source,
    string Severity,
    string Operation,
    string FirstRelease,
    string LastRelease,
    string ExceptionFingerprint,
    string Status,
    int OccurrenceCount,
    DateTime FirstSeenUtc,
    DateTime LastSeenUtc,
    string LastReference,
    string LastActorRole);

public sealed record IncidentHealthScoreDto(
    int? Score,
    string Grade,
    int WindowDays,
    int TotalOccurrences,
    int UnresolvedGroups,
    int CriticalOccurrences,
    int ErrorOccurrences,
    int WarningOccurrences,
    int SeverityPenalty,
    int RecurrencePenalty,
    int UnresolvedAgePenalty,
    string FormulaVersion,
    string Explanation,
    string AlertLevel,
    string AlertReason,
    bool HasTelemetry);

public sealed record AdminIncidentDashboardDto(
    DateTime ObservedAtUtc,
    IncidentHealthScoreDto Health,
    IReadOnlyList<IncidentGroupDto> Incidents);

public sealed record PlatformAgencyHealthDto(
    int AgencyId,
    string AgencyName,
    IncidentHealthScoreDto Health);

public sealed record PlatformIncidentDashboardDto(
    DateTime ObservedAtUtc,
    IncidentHealthScoreDto OverallHealth,
    IReadOnlyList<PlatformAgencyHealthDto> Agencies,
    IReadOnlyList<IncidentGroupDto> Incidents);
