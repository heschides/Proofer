using CommunityToolkit.Mvvm.ComponentModel;
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

        public ReviewClientRowViewModel(Person person, DateTime today)
        {
            Person = person;
            CurrentQuarter = person.GetCurrentQuarter(today);
            Q1Selection = new ReviewCellSelection(this, 1);
            Q2Selection = new ReviewCellSelection(this, 2);
            Q3Selection = new ReviewCellSelection(this, 3);
            Q4Selection = new ReviewCellSelection(this, 4);
        }

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