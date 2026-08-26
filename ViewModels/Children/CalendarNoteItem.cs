using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.ViewModels.Children;

/// <summary>
/// Safe, display-only projection of a service note for the calendar. Calendar UI
/// formatting stays out of the persistence model, while the authoritative service
/// time meaning continues to come from ServiceTimeline.
/// </summary>
public sealed class CalendarNoteItem
{
    private readonly Note _note;

    public CalendarNoteItem(Note note)
    {
        _note = note ?? throw new ArgumentNullException(nameof(note));
    }

    public int Id => _note.Id;
    public DateTime EventDate => _note.EventDate?.Date ?? DateTime.MinValue;
    public string ClientName => string.IsNullOrWhiteSpace(_note.Person?.FullName)
        ? "Client unavailable"
        : _note.Person.FullName;
    public string Narrative => string.IsNullOrWhiteSpace(_note.Narrative)
        ? "No narrative recorded."
        : _note.Narrative;
    public int? Minutes => _note.Minutes;
    public int? Units => _note.Units;
    public int? StartTime => _note.StartTime;
    public bool IsReminder => _note.NoteType == NoteType.Reminder;
    public string NoteTypeLabel => _note.NoteType?.ToString() ?? "Unclassified note";
    public string StatusLabel => _note.Status switch
    {
        NoteStatus.HeldForCompliance => "Held for compliance",
        NoteStatus.ComplianceBlocked => "Compliance blocked",
        null => "Status not recorded",
        _ => _note.Status.Value.ToString()
    };

    public string ServiceTimeLabel
    {
        get
        {
            if (IsReminder)
                return "No service time — calendar reminder";

            if (_note.StartTime is not int start ||
                start is < 0 or > ServiceTimeline.WindowLengthMinutes)
            {
                return "Time not recorded";
            }

            if (_note.Minutes is > 0 and int minutes &&
                (long)start + minutes <= ServiceTimeline.WindowLengthMinutes)
            {
                return $"{ServiceTimeline.Describe(start)}–{ServiceTimeline.Describe(start + minutes)}";
            }

            return ServiceTimeline.Describe(start);
        }
    }

    public string DurationLabel
    {
        get
        {
            if (IsReminder)
                return "Reminder only · non-billable";

            if (_note.Minutes is not int minutes)
                return "Duration not recorded";

            if (_note.Units is not int units)
                return $"{minutes} min";

            return $"{minutes} min · {units} {(units == 1 ? "unit" : "units")}";
        }
    }
}
