using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sati.Data;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Data;
using System.Windows.Threading;
using static Sati.Enums;

namespace Sati
{
    public partial class MainWindowViewModel : ObservableObject
    {
        
        //FIELDS
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly ISettingsService _settingsService;
        private Settings? _settings;
        private readonly IScratchpadService _scratchpadService;
        private Scratchpad? _scratchpad;
        private readonly IIncentiveService _incentiveService;
        private Incentive? _incentive;

        //EVENTS
        public event EventHandler<bool>? OpenClientsWindowRequested;
        public event EventHandler<bool>? OpenSettingsWindowRequested;

        //PROPERTIES
        public ICollectionView NotesView { get; }

        [ObservableProperty] private Person? selectedPerson;
        partial void OnSelectedPersonChanged(Person? value)
        {
            LoadNotesForPersonAsync(value);
        }

        [ObservableProperty] private int? units;
        [ObservableProperty] private DateTime? eventDate;
        [ObservableProperty] private int? duration;
        [ObservableProperty] private string? narrative;
        [ObservableProperty] private NoteStatus? status;
        [ObservableProperty] private bool isEditing = false;
        [ObservableProperty] private Note? selectedNote = null;
        [ObservableProperty] private string? searchText;
        [ObservableProperty] private NoteStatus? filterStatus;
        [ObservableProperty] private User? loggedInUser;
        [ObservableProperty] private string scratchpadContent = string.Empty;
        [ObservableProperty] private NoteType? selectedNoteType; 
        [ObservableProperty] private int daysScheduled;
        partial  void OnFilterStatusChanged(NoteStatus? value) => NotesView.Refresh();
        public static Array NoteStatusOptions => Enum.GetValues(typeof(NoteStatus));
        public ObservableCollection<Note> Notes { get; } = [];
        public ObservableCollection<Person> People { get; set; } = [];
        public ObservableCollection<Event> UpcomingEvents { get; set; } = [];
        public int SafeThreshold => Threshold > 0 ? Threshold : 1;


        //Constructor
        public MainWindowViewModel(IServiceProvider services, IPersonService personService, INoteService noteService, ISettingsService settingsService, IScratchpadService scratchpadService, IIncentiveService incentiveService)
        {
            _personService = personService;
            _noteService = noteService;
            _settingsService = settingsService;
            _scratchpadService = scratchpadService;
            _incentiveService = incentiveService;
            NotesView = CollectionViewSource.GetDefaultView(Notes);
            NotesView.Filter = FilterNotes;

        }

        //Commands
        [RelayCommand]
        public void OpenClientList()
        {
            OpenClientsWindowRequested?.Invoke(this, true);
        }

        [RelayCommand]
        public void OpenSettingsWindow()
        {
            OpenSettingsWindowRequested?.Invoke(this, true);
        }

        [RelayCommand]
        public async Task SubmitNote()
        {
            if (string.IsNullOrWhiteSpace(Narrative) ||
                    SelectedPerson == null)
            {
                return;
            }

            if (!IsEditing)
            {
                var note = Note.Create(Narrative, EventDate, Status, Units, SelectedPerson.Id);
                var savedNote = await _noteService.AddNoteAsync(note);
                Notes.Insert(0, savedNote);
                _= LoadMonthlyNotesAsync();
                NotesView.Refresh();

                //SelectedPerson = null;
                Status = null;
                Narrative = string.Empty;
                EventDate = null;
                Units = null;
                Duration = null;
            }
            else
            {
                if (SelectedNote == null)
                    return;

                var note = SelectedNote!;
                note.Narrative = Narrative;
                note.EventDate = EventDate;
                note.Units = Units ?? 0;
                note.Status = Status;
                await _noteService.UpdateNoteAsync(note);
                _ = LoadMonthlyNotesAsync();
                NotesView.Refresh();


                IsEditing = false;
                Status = null;
                Narrative = string.Empty;
                EventDate = null;
                Units = null;
                Duration = null;
            }
        }

        [RelayCommand]
        private async Task DeleteNote()
        {
            if (SelectedNote != null)
            {
                await _noteService.DeleteNoteAsync(SelectedNote);
                Notes.Remove(SelectedNote);
                _= LoadMonthlyNotesAsync();
                SelectedNote = null;
            }
        }

        //Methods
        partial void OnSearchTextChanged(string? value)
        {
            NotesView.Refresh();
        }

        private async void LoadNotesForPersonAsync(Person? person)
        {
            if (person is null)
            {
                Notes.Clear();
                return;
            }
            var notes = await _noteService.GetAllByPersonAsync(person.Id);
            Notes.Clear();
            foreach (var note in notes)
                Notes.Add(note);
        }

        public bool FilterNotes(object obj)
        {
            if (obj is not Note note)
                return false;

            var matchesText = string.IsNullOrWhiteSpace(SearchText) ||
                              note.Narrative.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

            var matchesStatus = FilterStatus == null || note.Status == FilterStatus;

            return matchesText && matchesStatus;
        }

        public async Task LoadPeopleAsync()
        {
            try
            {
                People.Clear();
                var people = await _personService.GetAllPeopleAsync();
                foreach (var person in people)
                    People.Add(person);
            }
            catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load people: {ex.Message}");
                }
            }     

        public void Initialize(User user)
        {
            
            LoggedInUser = user;
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            _settings = await _settingsService.LoadAsync();
            await LoadPeopleAsync();
            await _noteService.UpdateAbandonedNotesAsync(_settings.AbandonedAfterDays);
            await LoadMonthlyNotesAsync();

            _scratchpad = await _scratchpadService.LoadTodayAsync(LoggedInUser!.Id);
            ScratchpadContent = _scratchpad!.Content;

            _incentive = await _incentiveService.GetOrCreateAsync(
                LoggedInUser!.Id,
                DateTime.Now.Month,
                DateTime.Now.Year);

            DaysScheduled = _incentive.DaysScheduled;

            StartAbandonmentTimer();
            StartScratchpadTimer();
        }

        private void StartScratchpadTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            timer.Tick += async (s, e) =>
            {
                if (_scratchpad is null) return;
                _scratchpad.Content = ScratchpadContent;
                await _scratchpadService.SaveAsync(_scratchpad);
            };
            timer.Start();
        }

        public async Task SaveScratchpadAsync()
        {
            if (_scratchpad is null) return;
            _scratchpad.Content = ScratchpadContent;
            await _scratchpadService.SaveAsync(_scratchpad);
        }

        public void EnterEditMode()
        {
            if (SelectedNote is null)
                return;

            IsEditing = true;

            Narrative = SelectedNote.Narrative;
            EventDate = SelectedNote.EventDate;
            Units = SelectedNote.Units;
            Status = SelectedNote.Status;
            SelectedPerson = People.First(p => p.Id == SelectedNote.PersonId);
        }

        private DateTime _lastAbandonmentCheck = DateTime.Now;
        private void StartAbandonmentTimer()
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            timer.Tick += async (s, e) =>
            {
                if ((DateTime.Now - _lastAbandonmentCheck).TotalHours >= 24)
                {
                    await _noteService.UpdateAbandonedNotesAsync(_settings?.AbandonedAfterDays ?? 7);
                    _lastAbandonmentCheck = DateTime.Now;
                }
            };
            timer.Start();
        }

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            if (value is null) return;
            if (!string.IsNullOrWhiteSpace(Narrative)) return;

            var noteType = value.Value;
            Narrative = noteType switch
            {
                Sati.Enums.NoteType.Visit => _settings?.VisitTemplate ?? string.Empty,
                Sati.Enums.NoteType.Contact => _settings?.ContactTemplate ?? string.Empty,
                Sati.Enums.NoteType.Documentation => _settings?.DocumentationTemplate ?? string.Empty,
                _ => string.Empty
            };
        }

        //COMPUTED PROPERTIES AND METHOD FOR UNIT LOGIC
        private List<Note> _monthlyNotes = [];
        public int? PendingUnits => _monthlyNotes
      .Where(n => n.Status == NoteStatus.Pending)
      .Sum(n => n.Units);

        public int? LoggedUnits => _monthlyNotes
            .Where(n => n.Status == NoteStatus.Logged)
            .Sum(n => n.Units);

        public int? AbandonedUnits => _monthlyNotes
            .Where(n => n.Status == NoteStatus.Abandoned)
            .Sum(n => n.Units);

        public decimal EstimatedIncentive => _incentive?.Calculate(LoggedUnits ?? 0) ?? 0;
        public int Threshold => _incentive?.Threshold ?? 0;

        private async Task LoadMonthlyNotesAsync()
        {
            _monthlyNotes = await _noteService.GetMonthlyNotesAsync();
            OnPropertyChanged(nameof(PendingUnits));
            OnPropertyChanged(nameof(LoggedUnits));
            OnPropertyChanged(nameof(AbandonedUnits));
            OnPropertyChanged(nameof(EstimatedIncentive));
            OnPropertyChanged(nameof(Threshold));
            OnPropertyChanged(nameof(SafeThreshold));
        }
    }
}
