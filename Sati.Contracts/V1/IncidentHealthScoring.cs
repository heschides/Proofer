namespace Sati.Contracts.V1;

public static class IncidentHealthScoring
{
    public const string FormulaVersion = "incident-health-v1";

    public static IncidentHealthScoreDto Calculate(
        IEnumerable<IncidentGroupDto> incidents,
        DateTime observedAtUtc,
        int windowDays)
    {
        if (windowDays is < 1 or > 90)
            throw new ArgumentOutOfRangeException(nameof(windowDays));

        var start = observedAtUtc.AddDays(-windowDays);
        var current = incidents.Where(item => item.LastSeenUtc >= start).ToList();
        var critical = current.Where(item => item.Severity == IncidentSeverities.Critical)
            .Sum(item => item.OccurrenceCount);
        var errors = current.Where(item => item.Severity == IncidentSeverities.Error)
            .Sum(item => item.OccurrenceCount);
        var warnings = current.Where(item => item.Severity == IncidentSeverities.Warning)
            .Sum(item => item.OccurrenceCount);
        var unresolved = current.Where(item => item.Status != "Resolved").ToList();

        var severityPenalty = Math.Min(55, critical * 10 + errors * 4 + warnings);
        var recurrencePenalty = Math.Min(25,
            current.Sum(item => Math.Max(0, item.OccurrenceCount - 1)));
        var agePenalty = Math.Min(20, unresolved.Sum(item =>
        {
            var ageDays = Math.Max(0, (observedAtUtc - item.FirstSeenUtc).TotalDays);
            return ageDays >= 14 ? 4 : ageDays >= 7 ? 2 : ageDays >= 2 ? 1 : 0;
        }));
        var score = Math.Max(0, 100 - severityPenalty - recurrencePenalty - agePenalty);

        return new IncidentHealthScoreDto(
            score,
            score >= 95 ? "Healthy" : score >= 80 ? "Watch" : score >= 60 ? "Degraded" : "Critical",
            windowDays,
            current.Sum(item => item.OccurrenceCount),
            unresolved.Count,
            critical,
            errors,
            warnings,
            severityPenalty,
            recurrencePenalty,
            agePenalty,
            FormulaVersion,
            "100 minus severity, recurrence, and unresolved-age penalties. It includes groups active in the selected window; occurrence counts are cumulative for each group. This version measures recorded incidents only and does not claim crash-free session coverage.");
    }
}
