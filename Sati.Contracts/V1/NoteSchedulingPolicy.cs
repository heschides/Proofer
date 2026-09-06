namespace Sati.Contracts.V1;

/// <summary>
/// Authoritative normalization for planned and reminder input. Future work is
/// always Scheduled, retains its selected type and estimated minutes, and carries
/// no actual start time, justification, or completed-visit facts. A Reminder is
/// still a separate non-service shape with no minutes or form/visit facts.
/// </summary>
public static class NoteSchedulingPolicy
{
    public const string ReminderType = "Reminder";
    public const string ScheduledStatus = "Scheduled";

    public static bool IsFutureDate(DateTime? eventDate, DateTime today) =>
        eventDate?.Date > today.Date;

    public static NoteSchedulingValues Normalize(
        DateTime? eventDate,
        DateTime today,
        string? status,
        int? minutes,
        int? startTime,
        string? formType,
        string? noteType,
        string? caseManagerJustification,
        string? visitDocumentationJson)
    {
        var isFutureDate = IsFutureDate(eventDate, today);
        var isReminderType = string.Equals(
            noteType, ReminderType, StringComparison.Ordinal);
        if (!isFutureDate && !isReminderType)
        {
            return new NoteSchedulingValues(
                eventDate,
                status,
                minutes,
                startTime,
                formType,
                noteType,
                caseManagerJustification,
                visitDocumentationJson,
                IsCalendarReminder: false);
        }

        if (isReminderType)
        {
            return new NoteSchedulingValues(
                eventDate?.Date,
                ScheduledStatus,
                Minutes: null,
                StartTime: null,
                FormType: null,
                ReminderType,
                CaseManagerJustification: null,
                VisitDocumentationJson: null,
                IsCalendarReminder: eventDate.HasValue);
        }

        return new NoteSchedulingValues(
            eventDate?.Date,
            ScheduledStatus,
            minutes,
            StartTime: null,
            formType,
            noteType,
            CaseManagerJustification: null,
            VisitDocumentationJson: null,
            IsCalendarReminder: false);
    }

    public static SaveNoteRequest Normalize(SaveNoteRequest request, DateTime today)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = Normalize(
            request.EventDate,
            today,
            request.Status,
            request.Minutes,
            request.StartTime,
            request.FormType,
            request.NoteType,
            request.CaseManagerJustification,
            request.VisitDocumentationJson);

        return request with
        {
            EventDate = values.EventDate,
            Status = values.Status,
            Minutes = values.Minutes,
            StartTime = values.StartTime,
            FormType = values.FormType,
            NoteType = values.NoteType,
            CaseManagerJustification = values.CaseManagerJustification,
            VisitDocumentationJson = values.VisitDocumentationJson
        };
    }
}

public sealed record NoteSchedulingValues(
    DateTime? EventDate,
    string? Status,
    int? Minutes,
    int? StartTime,
    string? FormType,
    string? NoteType,
    string? CaseManagerJustification,
    string? VisitDocumentationJson,
    bool IsCalendarReminder);
