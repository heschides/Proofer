using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Data;
using Sati.Models;
using Sati.Services.LocalAi;
using Sati.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
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
        private readonly IPersonContactService _personContactService;
        private readonly IClientAiContextService _clientAiContextService;
        private readonly ICaseNoteFormatter _caseNoteFormatter;
        private readonly Func<string, UserMessageDialog> _validationDialog;

        private Settings? _settings;
        private Note? _editingNote;
        private string? _aiSourceNarrative;
        private bool _applyingAiDraft;
        private VisitDocumentation? _pendingVisitDocumentation;
        private int _attendeeLoadVersion;
        private bool _suppressVisitChangeNotifications;

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
            IPersonContactService personContactService,
            IClientAiContextService clientAiContextService,
            ICaseNoteFormatter caseNoteFormatter,
            Func<string, UserMessageDialog> validationDialog)
        {
            _noteService = noteService;
            _settingsService = settingsService;
            _sessionService = sessionService;
            _personContactService = personContactService;
            _clientAiContextService = clientAiContextService;
            _caseNoteFormatter = caseNoteFormatter;
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
        [ObservableProperty] private bool isAiBusy;
        [ObservableProperty] private bool isAiReviewVisible;
        [ObservableProperty] private string aiDraftNarrative = string.Empty;
        [ObservableProperty] private string aiStatusMessage = string.Empty;
        [ObservableProperty] private double? aiDownloadProgress;
        [ObservableProperty] private IReadOnlyList<string> aiWarnings = [];
        [ObservableProperty] private IReadOnlyList<ClientAiContextSource> aiContextSources = [];
        [ObservableProperty] private string aiContextSummary = "Context used";

        // Structured visit facts. Exclusive observations use enum pickers; the
        // independent facts are checkboxes and may be combined.
        [ObservableProperty] private VisitSetting visitSetting;
        [ObservableProperty] private VisitAppearance visitAppearance;
        [ObservableProperty] private VisitParticipation visitParticipation;
        [ObservableProperty] private VisitSafetyObservation visitSafetyObservation;
        [ObservableProperty] private bool visitConsumerPresent = true;
        [ObservableProperty] private bool visitExpressedPreferences;
        [ObservableProperty] private bool visitAskedQuestions;
        [ObservableProperty] private bool visitMadeChoices;
        [ObservableProperty] private bool visitCommunicationSupportUsed;
        [ObservableProperty] private bool visitGoalsReviewed;
        [ObservableProperty] private bool visitServicesDiscussed;
        [ObservableProperty] private bool visitDocumentsReviewed;
        [ObservableProperty] private string? visitSettingDetails;
        [ObservableProperty] private string? visitObservationDetails;
        [ObservableProperty] private string? visitAdditionalAttendees;

        public static IReadOnlyList<NoteStatus> NoteStatusOptions { get; } =
        [
            NoteStatus.Scheduled,
            NoteStatus.Pending,
            NoteStatus.Logged,
            NoteStatus.Cancelled,
            NoteStatus.Delayed
        ];
        public Array FormTypes => Enum.GetValues(typeof(FormType));
        public bool IsFormNote => SelectedNoteType == NoteType.Form;
        public bool IsVisitNote => SelectedNoteType == NoteType.Visit;
        public bool IsLocalAiEnabled => _caseNoteFormatter.IsEnabled;
        public Array VisitSettings => Enum.GetValues(typeof(VisitSetting));
        public Array VisitAppearances => Enum.GetValues(typeof(VisitAppearance));
        public Array VisitParticipations => Enum.GetValues(typeof(VisitParticipation));
        public Array VisitSafetyObservations => Enum.GetValues(typeof(VisitSafetyObservation));
        public ObservableCollection<VisitAttendeeOptionViewModel> VisitAttendees { get; } = [];

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
            {
                _ = LoadVisitAttendeesAsync(newValue);
                return;
            }

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
            _pendingVisitDocumentation = null;
            ResetVisitDocumentation(clearAttendees: true);
            _ = LoadVisitAttendeesAsync(newValue);
        }

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            OnPropertyChanged(nameof(IsFormNote));
            OnPropertyChanged(nameof(IsVisitNote));

            if (value != NoteType.Form)
                SelectedFormType = null;

            if (value != NoteType.Visit)
                ResetVisitDocumentation(clearAttendees: false);
            else
                _ = LoadVisitAttendeesAsync(SelectedPerson);

            if (value is null || !string.IsNullOrWhiteSpace(Narrative))
                return;

            Narrative = value.Value switch
            {
                NoteType.Visit => _settings?.VisitTemplate ?? string.Empty,
                NoteType.Contact => _settings?.ContactTemplate ?? string.Empty,
                _ => string.Empty
            };
        }

        partial void OnNarrativeChanged(string? value)
        {
            FormatNarrativeWithAiCommand.NotifyCanExecuteChanged();

            if (!_applyingAiDraft && IsAiReviewVisible &&
                !string.Equals(value, _aiSourceNarrative, StringComparison.Ordinal))
            {
                ClearAiReview();
                AiStatusMessage = "The rough narrative changed, so the previous AI draft was discarded.";
            }
        }

        partial void OnIsAiBusyChanged(bool value) =>
            FormatNarrativeWithAiCommand.NotifyCanExecuteChanged();

        partial void OnVisitSettingChanged(VisitSetting value) => VisitFactsChanged();
        partial void OnVisitAppearanceChanged(VisitAppearance value) => VisitFactsChanged();
        partial void OnVisitParticipationChanged(VisitParticipation value) => VisitFactsChanged();
        partial void OnVisitSafetyObservationChanged(VisitSafetyObservation value) => VisitFactsChanged();
        partial void OnVisitConsumerPresentChanged(bool value) => VisitFactsChanged();
        partial void OnVisitExpressedPreferencesChanged(bool value) => VisitFactsChanged();
        partial void OnVisitAskedQuestionsChanged(bool value) => VisitFactsChanged();
        partial void OnVisitMadeChoicesChanged(bool value) => VisitFactsChanged();
        partial void OnVisitCommunicationSupportUsedChanged(bool value) => VisitFactsChanged();
        partial void OnVisitGoalsReviewedChanged(bool value) => VisitFactsChanged();
        partial void OnVisitServicesDiscussedChanged(bool value) => VisitFactsChanged();
        partial void OnVisitDocumentsReviewedChanged(bool value) => VisitFactsChanged();
        partial void OnVisitSettingDetailsChanged(string? value) => VisitFactsChanged();
        partial void OnVisitObservationDetailsChanged(string? value) => VisitFactsChanged();
        partial void OnVisitAdditionalAttendeesChanged(string? value) => VisitFactsChanged();

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

        private async Task LoadVisitAttendeesAsync(Person? person)
        {
            var loadVersion = ++_attendeeLoadVersion;
            var selectedIds = VisitAttendees
                .Where(option => option.IsSelected)
                .Select(option => option.ContactId)
                .ToHashSet();

            foreach (var option in VisitAttendees)
                option.PropertyChanged -= OnVisitAttendeePropertyChanged;
            VisitAttendees.Clear();

            if (person is null)
                return;

            var contacts = await _personContactService.GetActiveByPersonAsync(person.Id);
            if (loadVersion != _attendeeLoadVersion || SelectedPerson?.Id != person.Id)
                return;

            if (_pendingVisitDocumentation is not null)
            {
                selectedIds.UnionWith(_pendingVisitDocumentation.Attendees
                    .Where(attendee => attendee.SourceContactId.HasValue)
                    .Select(attendee => attendee.SourceContactId!.Value));
            }

            foreach (var contact in contacts)
            {
                var option = new VisitAttendeeOptionViewModel(contact)
                {
                    IsSelected = selectedIds.Contains(contact.Id)
                };
                option.PropertyChanged += OnVisitAttendeePropertyChanged;
                VisitAttendees.Add(option);
            }
        }

        private void OnVisitAttendeePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VisitAttendeeOptionViewModel.IsSelected))
                VisitFactsChanged();
        }

        private void VisitFactsChanged()
        {
            if (_suppressVisitChangeNotifications)
                return;

            if (IsAiReviewVisible)
            {
                ClearAiReview();
                AiStatusMessage = "The meeting details changed, so the previous AI draft was discarded.";
            }
            else if (!string.IsNullOrWhiteSpace(Narrative) && IsVisitNote)
            {
                AiStatusMessage = "Meeting details changed. Format again if you want them reflected in the narrative.";
            }
        }

        private void ResetVisitDocumentation(bool clearAttendees)
        {
            _suppressVisitChangeNotifications = true;
            try
            {
                VisitSetting = VisitSetting.NotDocumented;
                VisitAppearance = VisitAppearance.NotDocumented;
                VisitParticipation = VisitParticipation.NotDocumented;
                VisitSafetyObservation = VisitSafetyObservation.NotDocumented;
                VisitConsumerPresent = true;
                VisitExpressedPreferences = false;
                VisitAskedQuestions = false;
                VisitMadeChoices = false;
                VisitCommunicationSupportUsed = false;
                VisitGoalsReviewed = false;
                VisitServicesDiscussed = false;
                VisitDocumentsReviewed = false;
                VisitSettingDetails = string.Empty;
                VisitObservationDetails = string.Empty;
                VisitAdditionalAttendees = string.Empty;

                if (clearAttendees)
                {
                    foreach (var option in VisitAttendees)
                        option.IsSelected = false;
                }
            }
            finally
            {
                _suppressVisitChangeNotifications = false;
            }
        }

        private void ApplyVisitDocumentation(VisitDocumentation? documentation)
        {
            _pendingVisitDocumentation = documentation;
            ResetVisitDocumentation(clearAttendees: true);
            if (documentation is null)
                return;

            _suppressVisitChangeNotifications = true;
            try
            {
                VisitSetting = documentation.Setting;
                VisitAppearance = documentation.Appearance;
                VisitParticipation = documentation.Participation;
                VisitSafetyObservation = documentation.SafetyObservation;
                VisitConsumerPresent = documentation.ConsumerPresent;
                VisitExpressedPreferences = documentation.ExpressedPreferences;
                VisitAskedQuestions = documentation.AskedQuestions;
                VisitMadeChoices = documentation.MadeChoices;
                VisitCommunicationSupportUsed = documentation.CommunicationSupportUsed;
                VisitGoalsReviewed = documentation.GoalsReviewed;
                VisitServicesDiscussed = documentation.ServicesDiscussed;
                VisitDocumentsReviewed = documentation.DocumentsReviewed;
                VisitSettingDetails = documentation.SettingDetails;
                VisitObservationDetails = documentation.ObservationDetails;
                VisitAdditionalAttendees = documentation.AdditionalAttendees;

                var selectedIds = documentation.Attendees
                    .Where(attendee => attendee.SourceContactId.HasValue)
                    .Select(attendee => attendee.SourceContactId!.Value)
                    .ToHashSet();
                foreach (var option in VisitAttendees)
                    option.IsSelected = selectedIds.Contains(option.ContactId);
            }
            finally
            {
                _suppressVisitChangeNotifications = false;
            }
        }

        private VisitDocumentation? BuildVisitDocumentation()
        {
            if (!IsVisitNote)
                return null;

            var selected = VisitAttendees
                .Where(option => option.IsSelected)
                .Select(option => new VisitAttendeeSnapshot
                {
                    SourceContactId = option.ContactId,
                    FullName = option.FullName,
                    Role = option.Role,
                    Organization = option.Organization
                })
                .ToList();

            // Preserve snapshots for contacts that have since been archived. They
            // are no longer selectable, but editing unrelated note text must not
            // erase their historical attendance.
            var currentContactIds = VisitAttendees.Select(option => option.ContactId).ToHashSet();
            if (_pendingVisitDocumentation is not null)
            {
                selected.AddRange(_pendingVisitDocumentation.Attendees.Where(attendee =>
                    !attendee.SourceContactId.HasValue ||
                    !currentContactIds.Contains(attendee.SourceContactId.Value)));
            }

            return new VisitDocumentation
            {
                Setting = VisitSetting,
                Appearance = VisitAppearance,
                Participation = VisitParticipation,
                SafetyObservation = VisitSafetyObservation,
                ConsumerPresent = VisitConsumerPresent,
                ExpressedPreferences = VisitExpressedPreferences,
                AskedQuestions = VisitAskedQuestions,
                MadeChoices = VisitMadeChoices,
                CommunicationSupportUsed = VisitCommunicationSupportUsed,
                GoalsReviewed = VisitGoalsReviewed,
                ServicesDiscussed = VisitServicesDiscussed,
                DocumentsReviewed = VisitDocumentsReviewed,
                SettingDetails = Normalize(VisitSettingDetails),
                ObservationDetails = Normalize(VisitObservationDetails),
                AdditionalAttendees = Normalize(VisitAdditionalAttendees),
                Attendees = selected
            };
        }

        private string BuildStructuredVisitFacts()
        {
            var documentation = BuildVisitDocumentation();
            if (documentation is null)
                return "Not a Visit note; no structured in-person observations were selected.";

            var builder = new StringBuilder();
            builder.AppendLine($"Case manager present: {_sessionService.CurrentUser?.DisplayName ?? "Signed-in case manager"}");
            builder.AppendLine($"Consumer present: {(documentation.ConsumerPresent ? "Yes" : "No")}");
            builder.AppendLine($"Meeting setting: {GetDescription(documentation.Setting)}");
            builder.AppendLine($"Appearance: {GetDescription(documentation.Appearance)}");
            builder.AppendLine($"Participation: {GetDescription(documentation.Participation)}");
            builder.AppendLine($"Health/safety observation: {GetDescription(documentation.SafetyObservation)}");

            if (!string.IsNullOrWhiteSpace(documentation.SettingDetails))
                builder.AppendLine($"Setting details: {documentation.SettingDetails}");

            if (documentation.Attendees.Count == 0)
                builder.AppendLine("Selected profile contacts in attendance: None");
            else
            {
                builder.AppendLine("Selected profile contacts in attendance:");
                foreach (var attendee in documentation.Attendees)
                {
                    var role = string.IsNullOrWhiteSpace(attendee.Role) ? string.Empty : $", {attendee.Role}";
                    var organization = string.IsNullOrWhiteSpace(attendee.Organization)
                        ? string.Empty
                        : $" ({attendee.Organization})";
                    builder.AppendLine($"- {attendee.FullName}{role}{organization}");
                }
            }

            if (!string.IsNullOrWhiteSpace(documentation.AdditionalAttendees))
                builder.AppendLine($"Additional attendees entered by the case manager: {documentation.AdditionalAttendees}");

            AppendSelectedFact(builder, documentation.ExpressedPreferences, "Consumer expressed preferences");
            AppendSelectedFact(builder, documentation.AskedQuestions, "Consumer asked questions");
            AppendSelectedFact(builder, documentation.MadeChoices, "Consumer made choices");
            AppendSelectedFact(builder, documentation.CommunicationSupportUsed, "Communication support was used");
            AppendSelectedFact(builder, documentation.GoalsReviewed, "Goals were reviewed");
            AppendSelectedFact(builder, documentation.ServicesDiscussed, "Services were discussed");
            AppendSelectedFact(builder, documentation.DocumentsReviewed, "Documents were reviewed");

            if (!string.IsNullOrWhiteSpace(documentation.ObservationDetails))
                builder.AppendLine($"Case-manager observation details: {documentation.ObservationDetails}");

            return builder.ToString().Trim();
        }

        private static void AppendSelectedFact(StringBuilder builder, bool selected, string fact)
        {
            if (selected)
                builder.AppendLine($"Selected fact: {fact}");
        }

        private static string GetDescription<T>(T value) where T : struct, Enum
        {
            var member = typeof(T).GetMember(value.ToString()).FirstOrDefault();
            return member?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                       .OfType<DescriptionAttribute>()
                       .FirstOrDefault()?.Description
                   ?? value.ToString();
        }

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
            ApplyVisitDocumentation(note.VisitDocumentation);
            _ = LoadVisitAttendeesAsync(SelectedPerson);
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseNarrativeFont() => NarrativeFontSize = Math.Min(NarrativeFontSize + 2, 28);
        [RelayCommand] private void DecreaseNarrativeFont() => NarrativeFontSize = Math.Max(NarrativeFontSize - 2, 10);

        private bool CanFormatNarrativeWithAi() =>
            IsLocalAiEnabled && !IsAiBusy && SelectedPerson is not null &&
            !string.IsNullOrWhiteSpace(Narrative);

        [RelayCommand(CanExecute = nameof(CanFormatNarrativeWithAi))]
        private async Task FormatNarrativeWithAi()
        {
            if (string.IsNullOrWhiteSpace(Narrative))
                return;

            var source = Narrative.Trim();
            IsAiBusy = true;
            ClearAiReview();
            AiStatusMessage = "Preparing the local case-note assistant…";

            var progress = new Progress<CaseNoteFormattingProgress>(update =>
            {
                AiStatusMessage = update.Message;
                AiDownloadProgress = update.Percent;
            });

            try
            {
                var currentUser = _sessionService.CurrentUser
                    ?? throw new InvalidOperationException("A signed-in user is required to use client-aware local AI.");
                var selectedPerson = SelectedPerson
                    ?? throw new InvalidOperationException("Select a client before formatting a note.");

                AiStatusMessage = "Gathering permitted client context locally…";
                var clientContext = await _clientAiContextService.BuildAsync(
                    selectedPerson.Id,
                    currentUser.Id,
                    source,
                    IsEditing ? _editingNote?.Id : null);

                AiContextSources = clientContext.Sources;
                AiContextSummary = $"Context used ({clientContext.Sources.Count} sources)";

                var result = await _caseNoteFormatter.FormatAsync(
                    new CaseNoteFormattingRequest(
                        source,
                        SelectedNoteType,
                        SelectedFormType,
                        currentUser.DisplayName,
                        selectedPerson.FirstName,
                        BuildFallbackFollowUp(selectedPerson),
                        clientContext.PromptText,
                        BuildStructuredVisitFacts()),
                    progress);

                _aiSourceNarrative = source;
                AiDraftNarrative = result.DraftNarrative;
                AiWarnings = result.Warnings;
                IsAiReviewVisible = true;
                AiStatusMessage = "Compare the draft with your original. Nothing changes until you accept it.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Local case-note formatting failed: {ex}");
                AiStatusMessage = "The local assistant could not create a draft.";

                var dialog = _validationDialog(
                    "Sati could not create a local AI draft. Your original narrative has not changed.\n\n" +
                    GetFriendlyAiError(ex));
                dialog.Owner = Application.Current.MainWindow;
                dialog.ShowDialog();
            }
            finally
            {
                IsAiBusy = false;
                AiDownloadProgress = null;
            }
        }

        [RelayCommand]
        private void AcceptAiDraft()
        {
            if (!IsAiReviewVisible || string.IsNullOrWhiteSpace(AiDraftNarrative))
                return;

            _applyingAiDraft = true;
            try
            {
                Narrative = AiDraftNarrative;
            }
            finally
            {
                _applyingAiDraft = false;
            }

            ClearAiReview();
            AiStatusMessage = "AI draft accepted. Review and edit the narrative before submitting the note.";
        }

        [RelayCommand]
        private void DiscardAiDraft()
        {
            ClearAiReview();
            AiStatusMessage = "AI draft discarded. Your original narrative was preserved.";
        }

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
            if (SelectedNoteType == NoteType.Visit &&
                (VisitAppearance == VisitAppearance.ConcernObserved ||
                 VisitSafetyObservation == VisitSafetyObservation.ConcernObserved) &&
                string.IsNullOrWhiteSpace(VisitObservationDetails))
            {
                errors.Add("• Describe the appearance, health, or safety concern selected for this visit.");
            }

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
                note.VisitDocumentation = BuildVisitDocumentation();
                if (caseManagerJustification is not null)
                    note.CaseManagerJustification = caseManagerJustification;

                try
                {
                    await _noteService.UpdateNoteAsync(note);
                }
                catch (NoteConcurrencyException)
                {
                    await ReconcileNoteConflictAsync(note);
                    return;
                }
                savedFormType = note.FormType;
            }
            else
            {
                var note = Note.Create(Narrative!, EventDate, Status, Minutes,
                    SelectedPerson!.Id, SelectedFormType, SelectedNoteType);
                note.VisitDocumentation = BuildVisitDocumentation();
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

        private async Task ReconcileNoteConflictAsync(Note draft)
        {
            var latest = (await _noteService.GetAllByPersonAsync(draft.PersonId))
                .SingleOrDefault(note => note.Id == draft.Id);
            if (latest is null)
            {
                MessageBox.Show(
                    "This note was removed or is no longer available. Your draft remains on screen so you can copy it before cancelling the edit.",
                    "Note No Longer Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var differingFields = GetDifferingNoteFields(draft, latest);
            _editingNote = latest;
            var differenceSummary = differingFields.Count == 0
                ? "Only its revision changed; the editable fields now match your draft."
                : $"Your draft differs from the latest saved copy in: {string.Join(", ", differingFields)}.";
            MessageBox.Show(
                $"This note changed after you opened it. {differenceSummary} " +
                "Your draft remains on screen and is now attached to the latest revision. " +
                "Review those areas, then save again only if your draft should replace the saved values.",
                "Newer Note Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        private static IReadOnlyList<string> GetDifferingNoteFields(Note draft, Note latest)
        {
            var fields = new List<string>();
            if (draft.Narrative != latest.Narrative) fields.Add("narrative");
            if (draft.EventDate != latest.EventDate) fields.Add("date");
            if (draft.Minutes != latest.Minutes) fields.Add("minutes");
            if (draft.Status != latest.Status) fields.Add("status");
            if (draft.NoteType != latest.NoteType) fields.Add("note type");
            if (draft.FormType != latest.FormType) fields.Add("form type");
            if (draft.CaseManagerJustification != latest.CaseManagerJustification) fields.Add("justification");
            if (draft.VisitDocumentationJson != latest.VisitDocumentationJson) fields.Add("visit documentation");
            return fields;
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
            _pendingVisitDocumentation = null;
            ResetVisitDocumentation(clearAttendees: true);
            ClearAiReview();
            AiStatusMessage = string.Empty;
        }

        private void ClearAiReview()
        {
            _aiSourceNarrative = null;
            IsAiReviewVisible = false;
            AiDraftNarrative = string.Empty;
            AiWarnings = [];
            AiContextSources = [];
            AiContextSummary = "Context used";
            AiDownloadProgress = null;
        }

        private static string GetFriendlyAiError(Exception exception)
        {
            var root = exception;
            while (root.InnerException is not null)
                root = root.InnerException;

            return root switch
            {
                ArgumentException => root.Message,
                UnauthorizedAccessException => root.Message,
                OperationCanceledException => "The operation was canceled.",
                _ => "The model may still need to be downloaded, or the configured model may not be available. " +
                     "Check the development configuration and try again."
            };
        }

        private static string BuildFallbackFollowUp(Person? person)
        {
            if (person is null)
                return "review the consumer's upcoming case-management deadlines";

            var majorTypes = new[]
            {
                FormType.Q1R, FormType.Q2R, FormType.Q3R, FormType.Q4R,
                FormType.ComprehensiveAssessment,
                FormType.PCP,
                FormType.Reclassification
            };

            var incomplete = person.Forms
                .Where(form => majorTypes.Contains(form.Type) && !form.IsCompliant)
                .ToList();

            var today = DateTime.Today;
            var target = incomplete
                .Where(form => form.DueDate.Date < today)
                .OrderByDescending(form => form.DueDate)
                .FirstOrDefault()
                ?? incomplete
                    .Where(form => form.DueDate.Date >= today)
                    .OrderBy(form => form.DueDate)
                    .FirstOrDefault();

            if (target is null)
                return "review the consumer's upcoming case-management deadlines";

            var formName = target.Type switch
            {
                FormType.Q1R => "Q1 90-Day Review",
                FormType.Q2R => "Q2 90-Day Review",
                FormType.Q3R => "Q3 90-Day Review",
                FormType.Q4R => "Q4 90-Day Review",
                FormType.PCP => "Person-Centered Plan",
                _ => Person.FormDisplayName(target.Type)
            };

            return target.DueDate.Date < today
                ? $"complete the overdue {formName}, which was due {target.DueDate:MMMM d, yyyy}"
                : $"prepare for the {formName} due {target.DueDate:MMMM d, yyyy}";
        }

        public void Reset()
        {
            SelectedPerson = null;
            _editingNote = null;
            _pendingVisitDocumentation = null;
            IsEditing = false;
            ClearNoteFields();
            People.Clear();
        }
    }
}
