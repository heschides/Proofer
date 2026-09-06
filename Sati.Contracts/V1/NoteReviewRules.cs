namespace Sati.Contracts.V1;

public sealed record NoteReviewPage<T>(IReadOnlyList<T> Notes, int? NextAfterId, int ThroughId);

/// <summary>Additional eligibility for supervisor-requested automatic approval.
/// Compliance, reviewer scope, revision, and overlapping time are checked at persistence.</summary>
public static class NoteReviewRules
{
    public const int PageSize = 10;
    public const int DefaultMaximumUnits = 4;
    public static bool ValidThreshold(int maximumUnits) => maximumUnits is >= 1 and <= 96;

    public static bool Eligible(int maximumUnits, int? status, string? noteType,
        string? narrative, DateTime? eventDate, int? minutes, int? startTime, DateTime today) =>
        ValidThreshold(maximumUnits) && status == NoteWorkflow.Logged &&
        noteType is "Visit" or "Contact" or "Phone" or "Email" or "Form" or "Other" &&
        !string.IsNullOrWhiteSpace(narrative) && narrative.Length <= 1_000_000 &&
        eventDate is DateTime date && date.Date <= today.Date &&
        minutes is > 0 and <= 1440 && (minutes.Value + 14) / 15 <= maximumUnits &&
        (startTime is null || ServiceTimeline.IsWithinWindow(startTime.Value, minutes.Value));
}
