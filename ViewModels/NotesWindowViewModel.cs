using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace Sati.ViewModels
{
    public record StatusOption(NoteStatus? Value, string Display);

    public partial class NotesWindowViewModel : ObservableObject
    {
        // FIELDS
        private readonly IPersonService _personService;
        private readonly ISessionService _sessionService;
        private readonly INoteService _noteService;

        // True when the open dialog was triggered by a billing-window block rather
        // than a paperwork-gate failure. Same fork as the entry module: window
        // block => Hold saves ComplianceBlocked; paperwork gate => HeldForCompliance.
        private bool _dialogIsWindowBlock;

        // Set while the grid selection is being put back after the case manager
        // declined to discard a draft, so the restore does not re-enter the
        // selection handler and ask again.
        private bool _restoringSelection;

        private readonly ObservableCollection<Note> _allNotes = [];

        private static readonly Person AllPersonsSentinel = Person.CreateSentinel("All Persons");

        // FILTER OPTIONS
        public static IReadOnlyList<StatusOption> StatusOptions { get; } =
        [
            new(null, "All Statuses"),
            ..Enum.GetValues<NoteStatus>().Select(s => new StatusOption(s, s.ToString()))
        ];

        public ObservableCollection<Person?> FilterPeople { get; } = [AllPersonsSentinel];

        // PROPERTIES
        public NoteEntryViewModel NoteEntry { get; }
        public ICollectionView NotesView { get; }

        [ObservableProperty] private Person? _selectedFilterPerson = AllPersonsSentinel;
        [ObservableProperty] private StatusOption _selectedStatusOption = StatusOptions[0];
        [ObservableProperty] private Note? _selectedNote;
        [ObservableProperty] private string? _searchText;
        [ObservableProperty] private DateTime? _rangeStart;
        [ObservableProperty] private DateTime? _rangeEnd;
        [ObservableProperty] private bool _isComplianceDialogVisible;
        [ObservableProperty] private string _pendingJustification = string.Empty;
        [ObservableProperty] private IReadOnlyList<string> _complianceFailureReasons = [];

        public event EventHandler? NoteStatusChanged;

        // CALLBACKS
        partial void OnSelectedFilterPersonChanged(Person? value) => RefreshView();
        partial void OnSelectedStatusOptionChanged(StatusOption value) => NotesView.Refresh();
        partial void OnSearchTextChanged(string? value) => RefreshView();
        partial void OnRangeStartChanged(DateTime? value) => RefreshView();
        partial void OnRangeEndChanged(DateTime? value) => RefreshView();
        // The entry module IS the detail view now, so the grid selection drives
        // it: pick a row and that note appears in the panel, locked. An unsaved
        // draft is the case manager's work and is never replaced silently — if
        // they decline to discard it, the selection snaps back where it was.
        partial void OnSelectedNoteChanged(Note? oldValue, Note? newValue)
        {
            if (_restoringSelection)
                return;

            if (newValue is null)
            {
                // The row was dropped. Only a locked view follows it back to a
                // blank form; an open draft or edit is left exactly as it was, and
                // the client stays selected either way.
                if (NoteEntry.IsLocked)
                    NoteEntry.ReturnToNewNote();
                return;
            }

            if (!NoteEntry.TryReleaseDraft())
            {
                RestoreSelection(oldValue);
                return;
            }

            NoteEntry.EnterViewMode(newValue);
        }

        private void RestoreSelection(Note? previous)
        {
            _restoringSelection = true;
            try
            {
                SelectedNote = previous;
            }
            finally
            {
                _restoringSelection = false;
            }
        }

        // COMPUTED
        public int ReturnedCount => _allNotes.Count(n => n.Status == NoteStatus.Returned);
        public int HeldCount => _allNotes.Count(n => n.Status == NoteStatus.HeldForCompliance);
        public bool HasReturned => ReturnedCount > 0;
        public bool HasHeld => HeldCount > 0;
        public bool HasAttentionItems => HasReturned || HasHeld;

        // RANGE SUMMARY — scoped by person/search/date range, independent of the status combobox
        public int RangePendingUnits =>
            _allNotes.Where(n => MatchesScope(n) && n.Status == NoteStatus.Pending).Sum(n => n.Units ?? 0);
        public int RangeLoggedUnits =>
            _allNotes.Where(n => MatchesScope(n) && n.Status == NoteStatus.Logged).Sum(n => n.Units ?? 0);
        public int RangeTotalUnits => RangePendingUnits + RangeLoggedUnits;

        // CONSTRUCTOR
        public NotesWindowViewModel(
            IPersonService personService,
            ISessionService sessionService,
            INoteService noteService,
            NoteEntryViewModel noteEntryViewModel)
        {
            _personService = personService;
            _sessionService = sessionService;
            _noteService = noteService;
            NoteEntry = noteEntryViewModel;
            NotesView = CollectionViewSource.GetDefaultView(_allNotes);
            NotesView.Filter = FilterNotes;

            // Second host. Its own transient instance — drafts here never bleed
            // into the dashboard's copy. FormNoteSavedAsync stays null by intent:
            // form-completion side effects need the dashboard's SelectedPerson
            // context, so a form note edited from NotesLog does not touch form
            // state (matches pre-refactor behavior).
            //
            // On save: refresh this grid, then bubble to the dashboard via the
            // existing NoteStatusChanged seam so productivity and the board track.
            noteEntryViewModel.NoteSaved += async (s, e) =>
            {
                // The panel returns to New Note after a save, so the grid must not
                // keep a row highlighted as though it were still on display.
                SelectedNote = null;
                await ReloadAsync();
                NoteStatusChanged?.Invoke(this, EventArgs.Empty);
            };

            // New Note (button or Escape) resets the panel; the grid drops its
            // highlight to match, so the two never describe different notes.
            noteEntryViewModel.EditorCleared += (s, e) => SelectedNote = null;
        }

        // COMMANDS
        [RelayCommand]
        private void ShowReturned() =>
                   SelectedStatusOption = StatusOptions.First(s => s.Value == NoteStatus.Returned);

        [RelayCommand]
        private void ShowHeld() =>
            SelectedStatusOption = StatusOptions.First(s => s.Value == NoteStatus.HeldForCompliance);


        // Double-click opens the selected row for editing. The single click that
        // preceded it has normally already loaded the note, so this usually just
        // lifts the lock. The module owns that decision — see NoteEntryViewModel
        // .OpenForEdit — because the dashboard makes the same one.
        public void OpenSelectedNoteForEdit() => NoteEntry.OpenForEdit(SelectedNote);

        [RelayCommand]
        private async Task MarkNoteLogged()
        {
            if (SelectedNote is null) return;

            var (passed, reasons) = SelectedNote.Person.EvaluateComplianceGate(DateTime.Today,
                SelectedNote.NoteType == NoteType.Form ? SelectedNote.FormType : null);

            // Window check keyed to the note's date — previously missing here,
            // which let notes inside a missed-form window get Logged from the
            // context menu when the dashboard would have blocked them. Notes
            // without an EventDate skip the window check (nothing to key on).
            var windowReasons = SelectedNote.EventDate is DateTime eventDate
                ? SelectedNote.Person.EvaluateBillingWindow(eventDate)
                : [];

            if (!passed || windowReasons.Count > 0)
            {
                _dialogIsWindowBlock = windowReasons.Count > 0;
                ComplianceFailureReasons = reasons.Concat(windowReasons).ToList();
                PendingJustification = string.Empty;
                IsComplianceDialogVisible = true;
                return;
            }

            await LogNoteDirectlyAsync();
        }

        private async Task LogNoteDirectlyAsync()
        {
            if (SelectedNote is null) return;
            SelectedNote.Status = NoteStatus.Logged;
            if (!await TryUpdateNoteAsync(SelectedNote)) return;
            RefreshView();
            RefreshPanelForSelectedNote();
            NoteStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        // The panel copies a note's fields in when it loads rather than binding
        // through to the instance, so a status change made from the grid has to be
        // pushed back into it — otherwise a note just marked Logged still reads
        // Pending on screen. Only while it is showing that same note and locked;
        // an open edit is the case manager's and is never overwritten.
        private void RefreshPanelForSelectedNote()
        {
            if (SelectedNote is not null && NoteEntry.IsLocked && NoteEntry.IsShowing(SelectedNote))
                NoteEntry.EnterViewMode(SelectedNote);
        }

        [RelayCommand]
        private async Task HoldForCompliance()
        {
            if (SelectedNote is null) return;
            SelectedNote.Status = _dialogIsWindowBlock
                ? NoteStatus.ComplianceBlocked
                : NoteStatus.HeldForCompliance;
            _dialogIsWindowBlock = false;
            if (!await TryUpdateNoteAsync(SelectedNote)) return;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            RefreshView();
            RefreshPanelForSelectedNote();
            NoteStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private async Task SendToSupervisor()
        {
            if (SelectedNote is null) return;
            if (string.IsNullOrWhiteSpace(PendingJustification)) return;

            SelectedNote.Status = NoteStatus.Logged;
            SelectedNote.CaseManagerJustification = PendingJustification;
            if (!await TryUpdateNoteAsync(SelectedNote)) return;
            _dialogIsWindowBlock = false;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            RefreshView();
            RefreshPanelForSelectedNote();
        }

        [RelayCommand]
        private void CancelComplianceDialog()
        {
            _dialogIsWindowBlock = false;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            NotesView.Refresh();
            NoteStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        // METHODS
        private async Task LoadAsync()
        {
            var userId = _sessionService.CurrentUser!.Id;
            var people = await LoadPeopleWithFullNotesAsync(userId);

            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }
        }

        public async Task ReloadAsync()
        {
            _allNotes.Clear();
            FilterPeople.Clear();
            FilterPeople.Add(AllPersonsSentinel);

            var userId = _sessionService.CurrentUser!.Id;
            var people = await LoadPeopleWithFullNotesAsync(userId);

            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }

            // Real people only — the sentinel would render as a bogus "All Persons"
            // client in the module's combobox.
            NoteEntry.SetPeople(people);

            NotesView.Refresh();
            OnPropertyChanged(nameof(ReturnedCount));
            OnPropertyChanged(nameof(HeldCount));
            OnPropertyChanged(nameof(HasReturned));
            OnPropertyChanged(nameof(HasHeld));
            OnPropertyChanged(nameof(HasAttentionItems));
        }

        private async Task<List<Person>> LoadPeopleWithFullNotesAsync(int userId)
        {
            var people = await _personService.GetAllPeopleAsync(userId);
            await Task.WhenAll(people.Select(async person =>
            {
                var notes = await _noteService.GetAllByPersonAsync(person.Id);
                foreach (var note in notes)
                    note.Person = person;
                person.Notes = notes;
            }));
            return people;
        }

        private async Task<bool> TryUpdateNoteAsync(Note note)
        {
            try
            {
                await _noteService.UpdateNoteAsync(note);
                return true;
            }
            catch (NoteConcurrencyException)
            {
                await ReloadAsync();
                SelectedNote = null;
                MessageBox.Show(
                    "This note changed after the log was opened. The log has been refreshed; review the latest copy before changing its status.",
                    "Note Updated",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return false;
            }
        }

        private void RefreshView()
        {
            NotesView.Refresh();
            OnPropertyChanged(nameof(RangePendingUnits));
            OnPropertyChanged(nameof(RangeLoggedUnits));
            OnPropertyChanged(nameof(RangeTotalUnits));
        }

        // Scoping filters shared by the grid and the units summary.
        // Status is deliberately NOT here — the grid adds it; the summary fixes it to Pending/Logged.
        private bool MatchesScope(Note note)
        {
            var matchesPerson = ReferenceEquals(SelectedFilterPerson, AllPersonsSentinel)
                || note.PersonId == SelectedFilterPerson?.Id;

            var matchesSearch = string.IsNullOrWhiteSpace(SearchText)
                || note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            var noDateBounds = RangeStart is null && RangeEnd is null;
            var matchesRange = noDateBounds
                || (note.EventDate is DateTime date
                    && (RangeStart is null || date.Date >= RangeStart.Value.Date)
                    && (RangeEnd is null || date.Date <= RangeEnd.Value.Date));

            return matchesPerson && matchesSearch && matchesRange;
        }

        private bool FilterNotes(object obj)
        {
            if (obj is not Note note) return false;

            var matchesStatus = SelectedStatusOption.Value is null
                || note.Status == SelectedStatusOption.Value;

            return MatchesScope(note) && matchesStatus;
        }
    }
}
