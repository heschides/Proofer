namespace Sati.ViewModels.Children;

public sealed class CalendarDay
{
    public DateTime Date { get; init; }
    public bool IsExempt { get; set; }
    public int? ExemptDateId { get; set; }
    public bool IsWeekend { get; init; }
    public bool IsToday => Date.Date == DateTime.Today;
    public List<CalendarNoteItem> Notes { get; init; } = [];
    public int NoteCount => Notes.Count;
    public bool HasNotes => NoteCount > 0;

    public string AccessibleLabel
    {
        get
        {
            var noteText = NoteCount == 1 ? "1 note" : $"{NoteCount} notes";
            var exemptText = IsExempt ? ", exempt day" : string.Empty;
            return $"{Date:dddd, MMMM d, yyyy}, {noteText}{exemptText}";
        }
    }
}
