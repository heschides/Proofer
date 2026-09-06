using Sati.Data;
using Sati.Models;

namespace Sati.Services;

public enum WorkAgendaSection
{
    Paperwork,
    Visits,
    Calls,
    Emails,
    Freeform
}

/// <summary>
/// One piece of planned work shown in Today's Work. The backing record is the
/// existing Scheduled note, so starting the item can continue that same record
/// instead of creating a duplicate clinical note.
/// </summary>
public sealed record WorkAgendaItem(Note Note)
{
    public WorkAgendaSection Section => Note.NoteType switch
    {
        NoteType.Form => WorkAgendaSection.Paperwork,
        NoteType.Visit => WorkAgendaSection.Visits,
        NoteType.Contact or NoteType.Phone => WorkAgendaSection.Calls,
        NoteType.Email => WorkAgendaSection.Emails,
        _ => WorkAgendaSection.Freeform
    };

    public string ClientName
    {
        get
        {
            var name = Note.Person is null
                ? string.Empty
                : $"{Note.Person.FirstName} {Note.Person.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? "Client unavailable" : name;
        }
    }

    public string TypeLabel => Note.NoteType switch
    {
        NoteType.Form when Note.FormType is FormType formType => Person.FormDisplayName(formType),
        NoteType.Contact => "Contact (legacy)",
        NoteType.Phone => "Phone",
        NoteType.Email => "Email",
        NoteType.Visit => "Visit",
        NoteType.Form => "Form",
        NoteType.Reminder => "Reminder",
        NoteType.Other => "Other",
        _ => "Type needed"
    };

    public string Summary
    {
        get
        {
            var text = Note.Narrative?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return "Scheduled work";

            return text
                .Replace("\r\n", " ", StringComparison.Ordinal)
                .Replace('\n', ' ')
                .Replace('\r', ' ');
        }
    }

    public string DurationText => Note.Minutes is > 0
        ? $"{Note.Minutes} min"
        : "Duration needed";

    public string AutomationName =>
        $"{Section}: {Summary}; {ClientName}; {TypeLabel}; {DurationText}. Start this work.";
}

public sealed record WorkAgendaAddResult(int AddedCount, int ExistingCount);

public interface IWorkAgendaService
{
    Task<IReadOnlyList<WorkAgendaItem>> LoadAsync(int userId, DateTime date);

    Task<WorkAgendaAddResult> AddFromDailyAgendaAsync(
        int userId,
        DateTime date,
        IReadOnlyList<DailyAgendaItem> selectedItems);
}

/// <summary>
/// Adapts the existing note boundary into the structured Today's Work view.
/// There is no second task store: Scheduled notes are the durable plan, and the
/// freeform Scratchpad remains a separate free-text draft.
/// </summary>
public sealed class WorkAgendaService(INoteService notes) : IWorkAgendaService
{
    // One billing unit is a useful editable starting estimate for paperwork
    // selected at sign-in. It is planned time only and never reaches billing
    // unless the case manager finishes and submits the note.
    public const int DefaultMinutes = 15;

    public async Task<IReadOnlyList<WorkAgendaItem>> LoadAsync(int userId, DateTime date)
    {
        var rows = await notes.GetDayScheduleAsync(userId, date.Date);
        return rows
            .Where(note => note.Status == NoteStatus.Scheduled &&
                           note.EventDate?.Date == date.Date)
            .Select(note => new WorkAgendaItem(note))
            .OrderBy(item => item.Section)
            .ThenBy(item => item.Note.StartTime ?? int.MaxValue)
            .ThenBy(item => item.ClientName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Summary, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<WorkAgendaAddResult> AddFromDailyAgendaAsync(
        int userId,
        DateTime date,
        IReadOnlyList<DailyAgendaItem> selectedItems)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        if (selectedItems.Count == 0)
            return new WorkAgendaAddResult(0, 0);
        if (selectedItems.Any(item => item.PersonId <= 0 || item.FormType is null))
        {
            throw new InvalidOperationException(
                "Every selected agenda item must identify a client and form type.");
        }

        var agendaDate = date.Date;
        var day = await notes.GetDayScheduleAsync(userId, agendaDate);
        var added = 0;
        var existing = 0;

        foreach (var item in selectedItems)
        {
            var narrative = DailyAgendaText.FormatItem(item);
            if (day.Any(note => Matches(note, item, agendaDate, narrative)))
            {
                existing++;
                continue;
            }

            var note = Note.Create(
                narrative,
                agendaDate,
                NoteStatus.Scheduled,
                DefaultMinutes,
                item.PersonId,
                item.FormType,
                NoteType.Form);
            var saved = await notes.AddNoteAsync(note);
            day.Add(saved);
            added++;
        }

        return new WorkAgendaAddResult(added, existing);
    }

    private static bool Matches(
        Note note,
        DailyAgendaItem item,
        DateTime date,
        string narrative) =>
        note.Status == NoteStatus.Scheduled &&
        note.EventDate?.Date == date &&
        note.PersonId == item.PersonId &&
        note.NoteType == NoteType.Form &&
        note.FormType == item.FormType &&
        string.Equals(note.Narrative?.Trim(), narrative, StringComparison.Ordinal);
}
