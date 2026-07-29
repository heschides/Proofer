using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Data;
using Sati.Models;
using Sati.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;

namespace Sati.ViewModels.Children
{
    // Shared note entry/edit module, hosted by CaseManagerDashboardViewModel and
    // NotesWindowViewModel. Registered TRANSIENT and injected into two singleton
    // hosts — each host captures its own instance for app life, so the two copies
    // never share draft state. (Transient-into-singleton is usually a lifetime
    // smell; here the two stable isolated instances are exactly the intent.)
    //
    // The module owns the full submit pipeline: validation, compliance gate,
    // billing-window check, the ComplianceBlocked/HeldForCompliance fork, and
    // note persistence. Hosts learn about outcomes two ways:
    //
    //   FormNoteSavedAsync — a Func<> callback property, not an event, because
    //     the host's form side effects must be AWAITED before NoteSaved fires;
    //     otherwise the host's refresh races the form update and misses it.
    //     One host per instance, so a single-assignment Func is sufficient.
    //   NoteSaved — plain event, "refresh whatever you own." Fired last.
    public partial class NoteEntryViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly INoteService _noteService;
        private readonly ISettingsService _settingsService;
        private readonly ISessionService _sessionService;
        private readonly Func<string, UserMessageDialog> _validationDialog;

        private Settings? _settings;
        private Note? _editingNote;

        // True when the open dialog was triggered by a billing-window block (note
        // date inside a missed-form window) rather than a current-cycle paperwork
        // gate failure. Drives the Hold outcome: window block => ComplianceBlocked;
        // paperwork gate => HeldForCompliance.
        private bool _dialogIsWindowBlock;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public NoteEntryViewModel(
            INoteService noteService,
            ISettingsService settingsService,
            ISessionService sessionService,
            Func<string, UserMessageDialog> validationDialog)
        {
            _noteService = noteService;
            _settingsService = settingsService;
            _sessionService = sessionService;
            _validationDialog = validationDialog;
        }

        // -------------------------------------------------------------------------
        // Host integration
        // -------------------------------------------------------------------------

        // Awaited before NoteSaved when a Form-type note lands as Pending/Logged.
        // Args: the form type, and whether this was an edit (hosts may route new
        // vs. edited form notes differently).
        public Func<FormType, bool, Task>? FormNoteSavedAsync { get; set; }

        public event EventHandler? NoteSaved;

        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public ObservableCollection<Person> People { get; } = [];

        [ObservableProperty] private Person? selectedPerson;
        [ObservableProperty] private NoteStatus? status;
        [ObservableProperty] private NoteType? selectedNoteType;
        [ObservableProperty] private FormType? selectedFormType;
        [ObservableProperty] private string? narrative;
        [ObservableProperty] private int? minutes;
        [ObservableProperty] private DateTime? eventDate;
        [ObservableProperty] private bool isEditing;
        [ObservableProperty] private double narrativeFontSize = 14;
        [ObservableProperty] private bool isComplianceDialogVisible;
        [ObservableProperty] private string pendingJustification = string.Empty;
        [ObservableProperty] private IReadOnlyList<string> complianceFailureReasons = [];

        public static Array NoteStatusOptions => Enum.GetValues(typeof(NoteStatus));
        public Array FormTypes => Enum.GetValues(typeof(FormType));
        public bool IsFormNote => SelectedNoteType == NoteType.Form;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        // Two-parameter overload of the generated partial callback — the toolkit
        // emits both a (newValue) and an (oldValue, newValue) hook; implementing
        // the latter lets us tell an instance swap from a real client change.
        // SetPeople re-selects by Id after every host reload, handing us a fresh
        // instance of the SAME client — clearing the draft there would wipe
        // in-progress narratives every time anything else refreshed the caseload.
        partial void OnSelectedPersonChanged(Person? oldValue, Person? newValue)
        {
            if (oldValue?.Id == newValue?.Id)
                return;

            // Genuine client switch: reset the draft; edit mode ends because the
            // note being edited belongs to the previous client.
            if (IsEditing)
            {
                IsEditing = false;
                _editingNote = null;
            }
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            SelectedNoteType = null;
            SelectedFormType = null;
            Minutes = null;
        }

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            OnPropertyChanged(nameof(IsFormNote));

            if (value != NoteType.Form)
                SelectedFormType = null;

            if (value is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            Narrative = value.Value switch
            {
                NoteType.Visit => _settings?.VisitTemplate ?? string.Empty,
                NoteType.Contact => _settings?.ContactTemplate ?? string.Empty,
                _ => string.Empty
            };
        }

        partial void OnSelectedFormTypeChanged(FormType? value)
        {
            if (value is null || SelectedPerson is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            var user = _sessionService.CurrentUser?.DisplayName ?? "Case Manager";
            var client = SelectedPerson.FullName;

            Narrative = value switch
            {
                FormType.Q1R => $"{user} completed the Q1 90-Day Review for {client}.",
                FormType.Q2R => $"{user} completed the Q2 90-Day Review for {client}.",
                FormType.Q3R => $"{user} completed the Q3 90-Day Review for {client}.",
                FormType.Q4R => $"{user} completed the Q4 90-Day Review for {client}.",
                FormType.PCP => $"{user} attached the signed PCP and set the plan's status to \"Complete.\"",
                FormType.ComprehensiveAssessment => $"{user} completed the Comprehensive Assessment for {client}.",
                FormType.Reclassification => $"{user} completed the Reclassification for {client}.",
                FormType.SafetyPlan => $"{user} received the signed Safety Plan from {client} and filed it with annual documentation.",
                FormType.PrivacyPractices => $"{user} received the signed Privacy Practices form from {client} and filed it with annual documentation.",
                FormType.Release_Agency => $"{user} received the signed Agency Release from {client} and filed it with annual documentation.",
                FormType.Release_DHHS => $"{user} received the signed DHHS Release from {client} and filed it with annual documentation.",
                FormType.Release_Medical => $"{user} received the signed Medical Release from {client} and filed it with annual documentation.",
                _ => string.Empty
            };
        }

        // -------------------------------------------------------------------------
        // Initialization & host-supplied data
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            _settings = await _settingsService.LoadAsync();
        }

        // Hosts own people loading (the module never queries the caseload itself —
        // adding a fourth GetAllPeopleAsync call would deepen the triple-load debt).
        // Selection is preserved by Id across the rebuild, since the incoming list
        // contains fresh instances.
        public void SetPeople(IEnumerable<Person> people)
        {
            var keepId = SelectedPerson?.Id;
            People.Clear();
            foreach (var person in people)
                People.Add(person);

            if (keepId is int id)
                SelectedPerson = People.FirstOrDefault(p => p.Id == id);
        }

        // -------------------------------------------------------------------------
        // Edit mode
        // -------------------------------------------------------------------------

        public void EnterEditMode(Note note)
        {
            _editingNote = note;

            // Select the person FIRST — OnSelectedPersonChanged clears the draft
            // fields, so populating them before selection would wipe them.
            SelectedPerson = People.FirstOrDefault(p => p.Id == note.PersonId);

            IsEditing = true;
            Narrative = note.Narrative;
            EventDate = note.EventDate;
            Minutes = note.Minutes;
            Status = note.Status;
            SelectedNoteType = note.NoteType;
            SelectedFormType = note.FormType;
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseNarrativeFont() => NarrativeFontSize = Math.Min(NarrativeFontSize + 2, 28);
        [RelayCommand] private void DecreaseNarrativeFont() => NarrativeFontSize = Math.Max(NarrativeFontSize - 2, 10);

        [RelayCommand]
        private void Clear()
        {
            SelectedPerson = null;
            _editingNote = null;
            IsEditing = false;
            ClearNoteFields();
        }

        [RelayCommand]
        private async Task SubmitNote()
        {
            var errors = new List<string>();

            if (SelectedPerson is null) errors.Add("• Please select a client.");
            if (Status is null) errors.Add("• Please select a status.");
            if (EventDate is null) errors.Add("• Please enter a date.");
            if (string.IsNullOrWhiteSpace(Narrative)) errors.Add("• Please enter a narrative.");
            if (SelectedNoteType is null) errors.Add("• Please select a note type.");

            if (errors.Count > 0)
            {
                var dialog = _validationDialog(string.Join("\n", errors));
                dialog.Owner = Application.Current.MainWindow;
                dialog.ShowDialog();
                return;
            }

            try
            {
                if (Status == NoteStatus.Logged)
                {
                    var (passed, reasons) = SelectedPerson!.EvaluateComplianceGate(DateTime.Today,
                        SelectedNoteType == NoteType.Form ? SelectedFormType : null);

                    // Window check is keyed to the NOTE's date, not today.
                    // EventDate is non-null here — validated above.
                    var windowReasons = SelectedPerson!.EvaluateBillingWindow(EventDate!.Value);

                    if (!passed || windowReasons.Count > 0)
                    {
                        _dialogIsWindowBlock = windowReasons.Count > 0;
                        ComplianceFailureReasons = reasons.Concat(windowReasons).ToList();
                        PendingJustification = string.Empty;
                        IsComplianceDialogVisible = true;
                        return;
                    }
                }

                await SaveAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SubmitNote failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your note. Please try again.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task HoldForCompliance()
        {
            Status = _dialogIsWindowBlock
                ? NoteStatus.ComplianceBlocked
                : NoteStatus.HeldForCompliance;
            _dialogIsWindowBlock = false;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            await SaveAsync();
        }

        [RelayCommand]
        private async Task SendToSupervisor()
        {
            if (string.IsNullOrWhiteSpace(PendingJustification)) return;
            var justification = PendingJustification;
            _dialogIsWindowBlock = false;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
            await SaveAsync(justification);
        }

        [RelayCommand]
        private void CancelComplianceDialog()
        {
            _dialogIsWindowBlock = false;
            IsComplianceDialogVisible = false;
            PendingJustification = string.Empty;
        }

        // -------------------------------------------------------------------------
        // Persistence
        // -------------------------------------------------------------------------

        // One save path for new and edited notes — the fork the old dashboard code
        // expressed as SubmitNewNoteAsync/SubmitEditedNoteAsync collapses to a
        // branch on _editingNote.
        private async Task SaveAsync(string? caseManagerJustification = null)
        {
            var wasEdit = IsEditing && _editingNote is not null;
            FormType? savedFormType = null;

            if (wasEdit)
            {
                var note = _editingNote!;
                note.Narrative = Narrative!;
                note.EventDate = EventDate;
                note.Minutes = Minutes ?? 0;
                note.Status = Status;
                note.NoteType = SelectedNoteType;
                note.FormType = SelectedFormType;
                if (caseManagerJustification is not null)
                    note.CaseManagerJustification = caseManagerJustification;

                await _noteService.UpdateNoteAsync(note);
                savedFormType = note.FormType;
            }
            else
            {
                var note = Note.Create(Narrative!, EventDate, Status, Minutes,
                    SelectedPerson!.Id, SelectedFormType, SelectedNoteType);
                if (caseManagerJustification is not null)
                    note.CaseManagerJustification = caseManagerJustification;

                await _noteService.AddNoteAsync(note);
                savedFormType = note.FormType;
            }

            // Form side effects run and complete BEFORE hosts refresh, so the
            // reload sees the updated form state.
            if (savedFormType.HasValue &&
                Status is NoteStatus.Pending or NoteStatus.Logged &&
                FormNoteSavedAsync is not null)
                await FormNoteSavedAsync(savedFormType.Value, wasEdit);

            IsEditing = false;
            _editingNote = null;
            ClearNoteFields();

            NoteSaved?.Invoke(this, EventArgs.Empty);
        }

        // Leaves SelectedPerson in place so several notes can be logged for the
        // same client in a row.
        private void ClearNoteFields()
        {
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            SelectedFormType = null;
            SelectedNoteType = null;
            Minutes = null;
        }

        public void Reset()
        {
            SelectedPerson = null;
            _editingNote = null;
            IsEditing = false;
            ClearNoteFields();
            People.Clear();
        }
    }
}