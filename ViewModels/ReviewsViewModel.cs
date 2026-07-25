using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.ViewModels.Children;
using System.Collections.ObjectModel;

namespace Sati.ViewModels
{
    public partial class ReviewsViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly ISessionService _sessionService;
        private readonly IPersonService _personService;
        private readonly IReviewItemService _reviewItemService;

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty]
        private ReviewCellSelection? selectedCell;

        [ObservableProperty]
        private bool isLoading;

        // False (default) = relative mode: one column per client, their current
        // quarter only, with the explainer panel. True = absolute mode: all four
        // quarters. Not persisted — resets to the friendly default each launch.
        [ObservableProperty]
        private bool showAllQuarters;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public ObservableCollection<ReviewClientRowViewModel> Rows { get; } = [];

        // The items shown in the detail pane — one quarter of one client.
        public ObservableCollection<ReviewCellViewModel> DetailCells { get; } = [];

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasSelectedCell => SelectedCell is not null;

        public string DetailHeader => SelectedCell is null
            ? string.Empty
            : $"{SelectedCell.Row.Person.FullName} — Quarter {SelectedCell.Quarter}";

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnSelectedCellChanged(ReviewCellSelection? value)
        {
            OnPropertyChanged(nameof(HasSelectedCell));
            OnPropertyChanged(nameof(DetailHeader));

            DetailCells.Clear();
            if (value is null)
                return;

            foreach (var cell in value.Row.CellsForQuarter(value.Quarter))
                DetailCells.Add(cell);
        }

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ReviewsViewModel(ISessionService sessionService, IPersonService personService,
                                IReviewItemService reviewItemService)
        {
            _sessionService = sessionService;
            _personService = personService;
            _reviewItemService = reviewItemService;
        }

        // -------------------------------------------------------------------------
        // Public methods
        // -------------------------------------------------------------------------

        // Called when the tab opens. Generation runs first so a client whose
        // services changed since last visit gets their new items before the grid
        // is built. Generation is idempotent and the unique index backstops it,
        // so repeated tab visits cost a no-op query rather than duplicates.
        public async Task LoadAsync()
        {
            if (_sessionService.CurrentUser is null)
                return;

            IsLoading = true;

            try
            {
                var people = await _personService.GetAllPeopleAsync(_sessionService.CurrentUser.Id); var today = DateTime.Today;

                await _reviewItemService.EnsureCurrentCycleItemsAsync(people, today);

                var items = await _reviewItemService.GetForCaseloadAsync(_sessionService.CurrentUser.Id);

                BuildRows(people, items, today);
            }
            finally
            {
                IsLoading = false;
            }
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void SelectCell(ReviewCellSelection? selection)
        {
            SelectedCell = selection;
        }

        // Handed to every cell VM at construction. Writes one stage date through
        // the service, then refreshes the cell from the persisted entity so the
        // UI reflects what was stored rather than what was typed.
        private async Task PersistStageDateAsync(ReviewCellViewModel cell, ReviewStage stage, DateTime? date)
        {
            var updated = await _reviewItemService.SetStageDateAsync(cell.ReviewItemId, stage, date);

            cell.Refresh(updated);
            SelectedCell?.Row.NotifyCellsChanged();
        }

        [RelayCommand]
        private async Task Refresh()
        {
            var previous = SelectedCell;
            await LoadAsync();

            // Restore the selection by identity rather than reference — LoadAsync
            // rebuilt every row and cell VM, so the old references are stale.
            if (previous is null)
                return;

            var row = Rows.FirstOrDefault(r => r.Person.Id == previous.Row.Person.Id);
            if (row is not null)
                SelectedCell = new ReviewCellSelection(row, previous.Quarter);
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        // Rebuilds every row from scratch. Cheap at caseload scale and avoids the
        // whole class of bugs where an incremental update misses a case — a
        // client whose services changed, an item generated this session, a cycle
        // that rolled over between visits.
        private void BuildRows(List<Person> people, List<ReviewItem> items, DateTime today)
        {
            Rows.Clear();
            DetailCells.Clear();
            SelectedCell = null;

            // Group once rather than filtering the full list per person per
            // quarter — 25 clients × 4 quarters is 100 scans of several hundred
            // items otherwise.
            var byPerson = items.GroupBy(i => i.PersonId)
                                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var person in people)
            {
                var boundaries = person.GetCurrentCycleBoundaries(today);
                if (boundaries is null)
                    continue;

                var anchor = boundaries.Value.cycleStart.Date;
                var row = new ReviewClientRowViewModel(person, today);
                if (byPerson.TryGetValue(person.Id, out var personItems))
                {
                    var currentCycle = personItems
                                            .Where(i => i.CycleAnchor.Date == anchor)
                                            .OrderBy(i => TileSortKey(i.Category))
                                            .ThenBy(i => i.Category)
                                            .ThenBy(i => i.SlotIndex);

                    foreach (var item in currentCycle)
                            row.CellsForQuarter(item.Quarter).Add(
                                new ReviewCellViewModel(item, PersistStageDateAsync));
                }

                Rows.Add(row);
            }
        }

        // Display-order key, deliberately not the enum's own order. Employment
        // appears on only a handful of clients, so leading with it would shift
        // every following tile right on those rows and break the vertical
        // alignment that makes the grid scannable. Sorting it last keeps the
        // common categories in the same columns on every row.
        //
        // ReviewCategory is persisted as an int, so reordering the enum itself
        // would reinterpret existing rows. The key lives here instead.
        private static int TileSortKey(ReviewCategory category) => category switch
        {
            ReviewCategory.Employment => 1,
            _ => 0
        };
    }
}