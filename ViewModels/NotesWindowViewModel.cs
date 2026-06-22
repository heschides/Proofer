using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
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
        partial void OnSelectedNoteChanged(Note? value) =>
            OnPropertyChanged(nameof(IsSelectedNoteReturned));

        // COMPUTED
        public int ReturnedCount => _allNotes.Count(n => n.Status == NoteStatus.Returned);
        public int HeldCount => _allNotes.Count(n => n.Status == NoteStatus.HeldForCompliance);
        public bool HasReturned => ReturnedCount > 0;
        public bool HasHeld => HeldCount > 0;
        public bool HasAttentionItems => HasReturned || HasHeld;
        public bool IsSelectedNoteReturned => SelectedNote?.Status == NoteStatus.Returned;

        // RANGE SUMMARY — scoped by person/search/date range, independent of the status combobox
        public int RangePendingUnits =>
            _allNotes.Where(n => MatchesScope(n) && n.Status == NoteStatus.Pending).Sum(n => n.Units ?? 0);
        public int RangeLoggedUnits =>
            _allNotes.Where(n => MatchesScope(n) && n.Status == NoteStatus.Logged).Sum(n => n.Units ?? 0);
        public int RangeTotalUnits => RangePendingUnits + RangeLoggedUnits;

        // CONSTRUCTOR
        public NotesWindowViewModel(IPersonService personService, ISessionService sessionService, INoteService noteService)
        {
            _personService = personService;
            _sessionService = sessionService;
            _noteService = noteService;
            NotesView = CollectionViewSource.GetDefaultView(_allNotes);
            NotesView.Filter = FilterNotes;
        }

        // COMMANDS
        [RelayCommand]
        private void ShowReturned() =>
                   SelectedStatusOption = StatusOptions.First(s => s.Value == NoteStatus.Returned);

        [RelayCommand]
        private void ShowHeld() =>
            SelectedStatusOption = StatusOptions.First(s => s.Value == NoteStatus.HeldForCompliance);


        [RelayCommand]
        private void MarkNoteLogged()
        {
            if (SelectedNote is null) return;

            var (passed, reasons) = SelectedNote.Person.EvaluateComplianceGate(DateTime.Today,
                            SelectedNote.NoteType == NoteType.Form ? SelectedNote.FormType : null); if (!passed)
            {
                ComplianceFailureReasons = reasons;
                PendingJustification = string.Empty;
                IsComplianceDialogVisible = true;
                return;
            }

            _ = LogNoteDirectlyAsync();
        }

        private async Task LogNoteDirectlyAsync()
        {
            if (SelectedNote is null) return;
            SelectedNote.Status = NoteStatus.Logged;
            await _noteService.UpdateNoteAsync(SelectedNote);
            RefreshView();
            NoteStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private async Task HoldForCompliance()
        {
            if (SelectedNote is null) return;
            SelectedNote.Status = NoteStatus.HeldForCompliance;
            await _noteService.UpdateNoteAsync(SelectedNote);
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            RefreshView();
            NoteStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private async Task SendToSupervisor()
        {
            if (SelectedNote is null) return;
            if (string.IsNullOrWhiteSpace(PendingJustification)) return;

            SelectedNote.Status = NoteStatus.Logged;
            SelectedNote.CaseManagerJustification = PendingJustification;
            await _noteService.UpdateNoteAsync(SelectedNote);
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            RefreshView();
        }

        [RelayCommand]
        private void CancelComplianceDialog()
        {
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            NotesView.Refresh();
            NoteStatusChanged?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void CopyNarrative()
        {
            var text = SelectedNote?.Narrative;
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                Clipboard.SetText(text);
            }
            catch (ExternalException)
            {
                //NO CATCH NEEDED
            }
        }

        // METHODS
        private async Task LoadAsync()
        {
            var userId = _sessionService.CurrentUser!.Id;
            var people = await _personService.GetAllPeopleAsync(userId);

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
            var people = await _personService.GetAllPeopleAsync(userId);

            foreach (var person in people)
            {
                FilterPeople.Add(person);
                foreach (var note in person.Notes)
                    _allNotes.Add(note);
            }

            NotesView.Refresh();
            OnPropertyChanged(nameof(ReturnedCount));
            OnPropertyChanged(nameof(HeldCount));
            OnPropertyChanged(nameof(HasReturned));
            OnPropertyChanged(nameof(HasHeld));
            OnPropertyChanged(nameof(HasAttentionItems));
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