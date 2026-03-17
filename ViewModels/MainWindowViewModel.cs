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
using static Sati.Enums;

namespace Sati
{
    public partial class MainWindowViewModel : ObservableObject
    {
        
        //FIELDS
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;

        //EVENTS
        public event EventHandler<bool>? OpenClientsWindowRequested;

        //PROPERTIES
        public ICollectionView NotesView { get; }

        [ObservableProperty]
        private Person? selectedPerson;
        partial void OnSelectedPersonChanged(Person? value)
        {
            LoadNotesForPersonAsync(value);
        }

        [ObservableProperty]
        private int? units;

        [ObservableProperty]
        private DateTime? eventDate;

        [ObservableProperty]
        private int? duration;

        [ObservableProperty]
        private string? narrative;

        [ObservableProperty]
        private NoteStatus? status;

        [ObservableProperty]
        private bool isEditing = false;

        [ObservableProperty]
        private Note? selectedNote = null;

        [ObservableProperty]
        private string? searchText;

        [ObservableProperty]
        private NoteStatus? filterStatus;
        partial  void OnFilterStatusChanged(NoteStatus? value) => NotesView.Refresh();

        [ObservableProperty]
        private User? loggedInUser;

        public static Array NoteStatusOptions => Enum.GetValues(typeof(NoteStatus));


        public ObservableCollection<Note> Notes { get; } = [];
        public ObservableCollection<Person> People { get; set; } = [];
        public ObservableCollection<Event> UpcomingEvents { get; set; } = [];


        //Constructor
        public MainWindowViewModel(IServiceProvider services, IPersonService personService, INoteService noteService)
        {
            _personService = personService;
            _noteService = noteService;
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
            _ = LoadPeopleAsync();
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

    }
}
