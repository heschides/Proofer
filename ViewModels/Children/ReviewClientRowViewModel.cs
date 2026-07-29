using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Models;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    // Identifies which quarter cell of which client row was clicked. A record
    // because it's a value, not an entity — value equality and a compact
    // declaration are both free, and the caseload grid passes it as a
    // CommandParameter without any converter.
    public record ReviewCellSelection(ReviewClientRowViewModel Row, int Quarter);

    // One client's row in the caseload grid: four quarters, each holding the
    // blocks for that quarter's review items.
    public partial class ReviewClientRowViewModel : ObservableObject
    {
        public Person Person { get; }

        // Which cycle quarter the client is currently in, 1-4, or null if they
        // have no effective date. Captured at construction from the same 'today'
        // BuildRows threads through everything else — the row does not reach for
        // DateTime.Today itself, so a row built for a given date stays consistent
        // and remains testable.
        public int? CurrentQuarter { get; }

        private readonly DateTime _today;
        private readonly Settings _settings;

        public ReviewClientRowViewModel(Person person, DateTime today, Settings settings)
        {
            Person = person;
            _today = today.Date;
            _settings = settings;
            CurrentQuarter = person.GetCurrentQuarter(today);
            Q1Selection = new ReviewCellSelection(this, 1);
            Q2Selection = new ReviewCellSelection(this, 2);
            Q3Selection = new ReviewCellSelection(this, 3);
            Q4Selection = new ReviewCellSelection(this, 4);
        }

        // The Q_R form for the client's current quarter. Read from the stored
        // Form records rather than recomputed — FormDueDateCalculator is the
        // single owner of due-date math, and Q4R in particular is anchored to
        // nextAnniversary − Q4RDaysBeforeAnniversary, not a flat 360 days.
        private Form? CurrentQuarterForm =>
            CurrentQuarter is int q ? Person.GetCurrentCycleForm(QuarterFormType(q), _today) : null;

        public DateTime? DueDate => CurrentQuarterForm?.DueDate;

        // The window opens ReviewOpenDaysBefore ahead of the due date and closes
        // on the due date itself.
        public DateTime? WindowStart =>
            DueDate?.AddDays(-Person.GetOpenDaysBefore(FormType.Q1R, _settings));

        // Empty string when the client has no cycle — reads as a blank cell,
        // which is correct for a client with no effective date.
        public string WindowDisplay =>
            WindowStart is DateTime start && DueDate is DateTime due
                ? $"{start:MM/dd} – {due:MM/dd}"
                : string.Empty;

        // Today falls inside the window: the review is actionable now.
        public bool IsWindowOpen =>
            WindowStart is DateTime start && DueDate is DateTime due
            && _today >= start.Date && _today <= due.Date;

        // Past the due date and not yet completed.
        public bool IsOverdue =>
            CurrentQuarterForm is Form form
            && _today > form.DueDate.Date
            && form.CompletedDate is null;

        // The window opens later than today — nothing to do yet.
        public bool IsWindowUpcoming =>
            WindowStart is DateTime start && _today < start.Date;

        // The due date has passed. Purely temporal: says nothing about whether
        // the review was completed.
        public bool IsWindowClosed =>
            DueDate is DateTime due && _today > due.Date;

        private static FormType QuarterFormType(int quarter) => quarter switch
        {
            1 => FormType.Q1R,
            2 => FormType.Q2R,
            3 => FormType.Q3R,
            4 => FormType.Q4R,
            _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "Quarter must be 1 through 4.")
        };
        // One flag per quarter rather than binding the int and comparing in XAML.
        // A DataTrigger can only test equality against a literal, so the
        // alternative is four converters or a converter with a parameter; four
        // bools are cheaper to read and cost nothing.
        public bool IsQ1Current => CurrentQuarter == 1;
        public bool IsQ2Current => CurrentQuarter == 2;
        public bool IsQ3Current => CurrentQuarter == 3;
        public bool IsQ4Current => CurrentQuarter == 4;
        public ObservableCollection<ReviewCellViewModel> Q1Cells { get; } = [];
        public ObservableCollection<ReviewCellViewModel> Q2Cells { get; } = [];
        public ObservableCollection<ReviewCellViewModel> Q3Cells { get; } = [];
        public ObservableCollection<ReviewCellViewModel> Q4Cells { get; } = [];
        public ReviewCellSelection Q1Selection { get; }
        public ReviewCellSelection Q2Selection { get; }
        public ReviewCellSelection Q3Selection { get; }
        public ReviewCellSelection Q4Selection { get; }

        // The single column shown in relative mode: the cells for whichever
        // quarter the client is currently in. Empty when CurrentQuarter is null
        // (no effective date) — reads as a blank row, which is correct for a
        // client with no cycle rather than a reason to hide them.
        public ObservableCollection<ReviewCellViewModel> CurrentQuarterCells =>
            CurrentQuarter is int q ? CellsForQuarter(q) : [];

        // Null-cycle clients aren't clickable in relative mode — there's no
        // quarter to open. The XAML binds the button's command parameter to this;
        // a null parameter is a harmless no-op through SelectCell.
        public ReviewCellSelection? CurrentQuarterSelection =>
            CurrentQuarter is int q ? new ReviewCellSelection(this, q) : null;

        // More support arrangements than the four quarterly review slots can
        // cover. Compliance is unaffected — any four satisfy the requirement —
        // but the asterisk records that not every arrangement gets reviewed.
        public bool HasSurplusArrangements =>
            Person.HomeNoteSlots + Person.CommunityNoteSlots > 4;

        public string DisplayName =>
            HasSurplusArrangements ? $"{Person.FullName} *" : Person.FullName;

        public string SurplusTooltip =>
            "This client has more support arrangements than quarterly review "
            + "slots. Any four reviews satisfy the requirement.";

        public ObservableCollection<ReviewCellViewModel> CellsForQuarter(int quarter) => quarter switch
        {
            1 => Q1Cells,
            2 => Q2Cells,
            3 => Q3Cells,
            4 => Q4Cells,
            _ => throw new ArgumentOutOfRangeException(nameof(quarter), quarter, "Quarter must be 1 through 4.")
        };

        public void ClearCells()
        {
            Q1Cells.Clear();
            Q2Cells.Clear();
            Q3Cells.Clear();
            Q4Cells.Clear();
        }

        // Called after a cell's dates change so the row's own derived state
        // (currently none beyond the cells themselves) stays honest. Present as
        // the hook point for a future per-row completion summary.
        public void NotifyCellsChanged()
        {
            OnPropertyChanged(nameof(Q1Cells));
            OnPropertyChanged(nameof(Q2Cells));
            OnPropertyChanged(nameof(Q3Cells));
            OnPropertyChanged(nameof(Q4Cells));
        }
    }
}