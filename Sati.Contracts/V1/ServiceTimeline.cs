using System.Globalization;

namespace Sati.Contracts.V1;

/// <summary>
/// One time-bearing claim on a case manager's service day.
/// <paramref name="StartMinutes"/> is an offset from <see cref="ServiceTimeline.WindowStart"/>,
/// matching the storage convention of Note.StartTime.
/// </summary>
public sealed record ServiceBlock(
    int NoteId,
    int StartMinutes,
    int Minutes,
    string? Label = null)
{
    public int EndMinutes => StartMinutes + Minutes;
}

public sealed record ServiceTimeConflict(
    ServiceBlock Existing,
    string Reason);

/// <summary>
/// Authoritative owner of the billable service-day timeline.
///
/// Two rules live here, and only here:
///   1. The logging window is 7:00 AM to 7:00 PM. Note.StartTime is stored as
///      minutes elapsed from 7:00 AM, so 0 is 7:00 AM and 720 is 7:00 PM.
///   2. A single case manager may not claim the same minute twice on the same
///      calendar date. Billing a 9:00–9:15 contact and a 9:10–9:20 contact
///      double-bills ten minutes of one person's time regardless of which
///      clients the notes belong to, so the scope of the rule is the case
///      manager and the date — never the client.
///
/// Both the desktop client and the API evaluate this type. The client uses it to
/// preview and to refuse a save; the API uses it as the enforcing check, because
/// a rule that decides billability cannot be owned by a distributed client.
///
/// Start times are OPTIONAL. Notes recorded before this rule existed carry no
/// start time, and a note without one claims no minutes and conflicts with
/// nothing. Only notes that both carry a start time and hold a status that
/// occupies time participate.
/// </summary>
public static class ServiceTimeline
{
    /// <summary>Earliest loggable moment; the zero point of Note.StartTime.</summary>
    public static readonly TimeSpan WindowStart = new(7, 0, 0);

    /// <summary>Length of the loggable day, in minutes — 7:00 AM through 7:00 PM.</summary>
    public const int WindowLengthMinutes = 12 * 60;

    /// <summary>Granularity offered for start-time selection.</summary>
    public const int SlotMinutes = 5;

    // Statuses whose notes do NOT hold time: the service either did not happen
    // or the draft is dead. Everything else — planned, drafted, submitted,
    // approved, returned, or blocked — represents time the case manager has
    // committed, and must not be claimed twice.
    private static readonly HashSet<string> ReleasedStatuses =
        new(StringComparer.Ordinal) { "Cancelled", "Delayed", "Abandoned" };

    /// <summary>
    /// True when a note in this status holds its minutes against the day.
    /// A note with no status is treated as holding time: an unclassified draft
    /// is more safely assumed to be real work than assumed to be free.
    /// </summary>
    public static bool OccupiesTime(string? status) =>
        string.IsNullOrWhiteSpace(status) || !ReleasedStatuses.Contains(status);

    public static bool IsWithinWindow(int startMinutes, int minutes) =>
        startMinutes >= 0 &&
        minutes >= 0 &&
        startMinutes + minutes <= WindowLengthMinutes;

    /// <summary>
    /// Builds the block a note claims, or null when the note claims nothing —
    /// no start time, no duration, or a status that has released its time.
    /// </summary>
    public static ServiceBlock? TryCreateBlock(
        int noteId,
        int? startMinutes,
        int? minutes,
        string? status,
        string? label = null)
    {
        if (startMinutes is not int start || minutes is not int length)
            return null;
        if (length <= 0 || !OccupiesTime(status))
            return null;

        return new ServiceBlock(noteId, start, length, label);
    }

    /// <summary>
    /// Half-open interval comparison: a note ending at 9:15 and a note starting
    /// at 9:15 are adjacent, not overlapping. Back-to-back contacts are normal
    /// and must not be rejected.
    /// </summary>
    public static bool Overlaps(ServiceBlock first, ServiceBlock second) =>
        first.StartMinutes < second.EndMinutes &&
        second.StartMinutes < first.EndMinutes;

    /// <summary>
    /// Every already-recorded block the candidate would double-claim. The
    /// candidate's own note id is excluded so editing a note never conflicts
    /// with the copy of itself already on file.
    /// </summary>
    public static IReadOnlyList<ServiceTimeConflict> FindConflicts(
        ServiceBlock candidate,
        IEnumerable<ServiceBlock> sameDayBlocks)
    {
        var conflicts = new List<ServiceTimeConflict>();
        foreach (var existing in sameDayBlocks)
        {
            if (existing.NoteId != 0 && existing.NoteId == candidate.NoteId)
                continue;
            if (!Overlaps(candidate, existing))
                continue;

            conflicts.Add(new ServiceTimeConflict(existing, DescribeConflict(existing)));
        }

        return conflicts;
    }

    /// <summary>Formats an offset from the window start as a clock time, e.g. "9:15 AM".</summary>
    public static string Describe(int minutesFromWindowStart) =>
        DateTime.Today
            .Add(WindowStart)
            .AddMinutes(minutesFromWindowStart)
            .ToString("h:mm tt", CultureInfo.CurrentCulture);

    public static string DescribeRange(ServiceBlock block) =>
        $"{Describe(block.StartMinutes)} to {Describe(block.EndMinutes)}";

    private static string DescribeConflict(ServiceBlock existing)
    {
        var who = string.IsNullOrWhiteSpace(existing.Label)
            ? "another note"
            : existing.Label;
        return $"{DescribeRange(existing)} is already claimed by {who}.";
    }

    /// <summary>
    /// Validation message for a candidate that falls outside the loggable day.
    /// Null when the candidate fits.
    /// </summary>
    public static string? DescribeWindowViolation(int startMinutes, int minutes)
    {
        if (startMinutes < 0)
            return $"Service time cannot start before {Describe(0)}.";
        if (startMinutes >= WindowLengthMinutes)
            return $"Service time must start before {Describe(WindowLengthMinutes)}.";
        if (startMinutes + minutes > WindowLengthMinutes)
        {
            return $"A {minutes}-minute service starting at {Describe(startMinutes)} runs past " +
                   $"{Describe(WindowLengthMinutes)}, the end of the loggable day.";
        }

        return null;
    }
}
