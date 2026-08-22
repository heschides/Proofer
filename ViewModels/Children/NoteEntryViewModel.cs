using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.Services.LocalAi;
using Sati.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private readonly IPersonService _personService;
        private readonly ISettingsService _settingsService;
        private readonly ISessionService _sessionService;
        private readonly IPersonContactService _personContactService;
        private readonly IClientAiContextService _clientAiContextService;
        private readonly ICaseNoteFormatter _caseNoteFormatter;
        private readonly Func<string, UserMessageDialog> _validationDialog;

        private Settings? _settings;
        private Note? _editingNote;
        private string? _aiSourceNarrative;
        private string? _aiSourceFingerprint;
        private bool _applyingAiDraft;
        private VisitDocumentation? _pendingVisitDocumentation;
        private int _attendeeLoadVersion;
        private bool _suppressVisitChangeNotifications;
        private readonly LatestRequestTracker _aiDraftRequests = new();
        private CancellationTokenSource? _aiDraftCancellation;

        // Time already claimed by this case manager's other notes on EventDate.
        // Reloaded whenever the date changes; the tracker keeps a slow load for a
        // date the user has already moved away from from publishing stale bands.
        private readonly LatestRequestTracker _dayScheduleLoad = new();
        private IReadOnlyList<ServiceBlock> _recordedDayBlocks = [];

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
            IPersonService personService,
            ISettingsService settingsService,
            ISessionService sessionService,
            IPersonContactService personContactService,
            IClientAiContextService clientAiContextService,
            ICaseNoteFormatter caseNoteFormatter,
            Func<string, UserMessageDialog> validationDialog)
        {
            _noteService = noteService;
            _personService = personService;
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

        // Awaited BEFORE a reminder is written, for the same reason
        // FormNoteSavedAsync is a Func and not an event: ordering is the point.
        // The host owns the client page whose journal text box writes the same
        // column, and that page saves on a debounce — so its pending edit has to
        // reach the database before the server prepends the entry, or the next
        // debounced save would replace the journal with pre-reminder text.
        public Func<int, Task>? JournalWriteStartingAsync { get; set; }

        // Fired after the entry is written, carrying the journal the WRITER
        // produced rather than a locally composed guess at it.
        public event EventHandler<JournalReminderAddedEventArgs>? ReminderAdded;

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
        [ObservableProperty] private ServiceStartOption? selectedStartTime;
        [ObservableProperty] private string serviceTimeMessage = NoStartTimeMessage;
        [ObservableProperty] private bool hasServiceTimeConflict;
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
        [ObservableProperty] private string aiContextSummary = "Verified inputs";

        // Structured visit facts. Exclusive observations use enum pickers; the
        // independent facts are checkboxes and may be combined.
        [ObservableProperty] private VisitSetting visitSetting;
        [ObservableProperty] private VisitAppearance visitAppearance;
        [ObservableProperty] private VisitParticipation visitParticipation;
        [ObservableProperty] private VisitSafetyObservation visitSafetyObservation;
        [ObservableProperty] private VisitPresence visitPresence;
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
        public string SaveActionLabel => IsReminderNote
            ? "Add Reminder"
            : Status switch
            {
                NoteStatus.Pending => IsEditing ? "Update Draft" : "Save as Draft",
                NoteStatus.Logged => "Submit for Supervisor Review",
                _ => IsEditing ? "Update Note" : "Save Note"
            };
        public string StatusGuidance => IsReminderNote ? ReminderGuidance : DescribeStatus(Status);

        // Says why the workflow fields are unavailable, so the disabled state is
        // explained rather than merely observed. The status guidance block is a
        // polite live region, so a screen reader announces this on selection.
        internal const string ReminderGuidance =
            "Reminder — adds a timestamped entry to the top of this client's journal. " +
            "It is not a service note: it has no status, minutes, or service date, and it " +
            "does not go to your supervisor or into billing.";

        internal static string DescribeStatus(NoteStatus? status) => status switch
        {
            NoteStatus.Scheduled => "Scheduled — saves this as planned work. It stays out of supervisor review and billing until the service occurs and the status is changed.",
            NoteStatus.Pending => "Draft — saves the note in your queue so you can finish it later. Your supervisor cannot review it, and it cannot be billed.",
            NoteStatus.Logged => "Submit for review — sends the completed note to your supervisor. After approval, it can enter the billing queue if all billing requirements pass.",
            NoteStatus.Cancelled => "Cancelled — records that the planned service did not occur. It will not be sent for approval or billing.",
            NoteStatus.Delayed => "Delayed — records that the planned service was postponed. It stays out of approval and billing until you update it.",
            _ => "Select a status to see where the note will go next."
        };
        public Array FormTypes => Enum.GetValues(typeof(FormType));
        public bool IsFormNote => SelectedNoteType == NoteType.Form;
        public bool IsVisitNote => SelectedNoteType == NoteType.Visit;

        // A reminder is not service documentation. It has no place in the review
        // workflow, no billable minutes, no service date, and no visit facts, so
        // the controls those drive are DISABLED rather than hidden: the form keeps
        // its shape and the case manager can see what a reminder does not carry.
        // Client and narrative are the only inputs.
        public bool IsReminderNote => SelectedNoteType == NoteType.Reminder;
        public bool AreNoteFieldsEnabled => !IsReminderNote;
        public string NarrativeLabel => IsReminderNote ? "REMINDER" : "NARRATIVE";

        partial void OnStatusChanged(NoteStatus? value)
        {
            OnPropertyChanged(nameof(SaveActionLabel));
            OnPropertyChanged(nameof(StatusGuidance));

            // Cancelled and Delayed release the time the draft was holding.
            RedrawServiceDay();
        }

        partial void OnIsEditingChanged(bool value) => OnPropertyChanged(nameof(SaveActionLabel));

        partial void OnEventDateChanged(DateTime? value)
        {
            OnPropertyChanged(nameof(ServiceDayHeading));
            _ = RefreshServiceDayAsync();
        }

        partial void OnSelectedStartTimeChanged(ServiceStartOption? value) => RedrawServiceDay();

        partial void OnMinutesChanged(int? value) => RedrawServiceDay();

        public bool IsLocalAiEnabled => _caseNoteFormatter.IsEnabled;
        public Array VisitSettings => Enum.GetValues(typeof(VisitSetting));
        public Array VisitAppearances => Enum.GetValues(typeof(VisitAppearance));
        public Array VisitParticipations => Enum.GetValues(typeof(VisitParticipation));
        public Array VisitSafetyObservations => Enum.GetValues(typeof(VisitSafetyObservation));
        public Array VisitPresences => Enum.GetValues(typeof(VisitPresence));
        public ObservableCollection<VisitAttendeeOptionViewModel> VisitAttendees { get; } = [];

        // -------------------------------------------------------------------------
        // Service day
        // -------------------------------------------------------------------------

        private const string NoStartTimeMessage =
            "Optional. Choose a start time to reserve this service on your day and to check it against your other notes.";

        private const string NoDateMessage =
            "Choose a date first — service time is checked against the other notes on that day.";

        public static IReadOnlyList<ServiceStartOption> StartTimeOptions { get; } = ServiceStartOption.BuildDay();

        /// <summary>Bands drawn on the service-day bar: recorded time, plus this draft.</summary>
        public ObservableCollection<ServiceDaySegment> ServiceDaySegments { get; } = [];

        public string ServiceDayHeading => EventDate is DateTime date
            ? $"YOUR DAY — {date:dddd, MMMM d}"
            : "YOUR DAY";

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

            InvalidateAiGeneration();

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
            SelectedStartTime = null;
            _pendingVisitDocumentation = null;
            ResetVisitDocumentation(clearAttendees: true);
            _ = LoadVisitAttendeesAsync(newValue);
        }

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            InvalidateAiGeneration();
            OnPropertyChanged(nameof(IsFormNote));
            OnPropertyChanged(nameof(IsVisitNote));
            OnPropertyChanged(nameof(IsReminderNote));
            OnPropertyChanged(nameof(AreNoteFieldsEnabled));
            OnPropertyChanged(nameof(NarrativeLabel));
            OnPropertyChanged(nameof(SaveActionLabel));
            OnPropertyChanged(nameof(StatusGuidance));
            FormatNarrativeWithAiCommand.NotifyCanExecuteChanged();

            if (value == NoteType.Reminder)
            {
                // Cleared, not merely disabled. A status or a set of minutes left
                // behind by a half-finished note must not travel with the journal
                // entry, and must not reappear if the case manager switches back to
                // a service note and saves without revisiting these fields.
                Status = null;
                Minutes = null;
                EventDate = null;
                SelectedStartTime = null;
                ClearAiReview();
                AiStatusMessage = string.Empty;
            }

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

            if (!_applyingAiDraft)
                InvalidateAiGeneration();

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
        partial void OnVisitPresenceChanged(VisitPresence value) => VisitFactsChanged();
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
            InvalidateAiGeneration();
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

            InvalidateAiGeneration();

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
                VisitPresence = VisitPresence.NotDocumented;
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
                VisitPresence = documentation.ConsumerPresent switch
                {
                    true => VisitPresence.Present,
                    false => VisitPresence.NotPresent,
                    null => VisitPresence.NotDocumented
                };
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
                ConsumerPresent = VisitPresence switch
                {
                    VisitPresence.Present => true,
                    VisitPresence.NotPresent => false,
                    _ => null
                },
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

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // -------------------------------------------------------------------------
        // Service day
        // -------------------------------------------------------------------------

        // Reloads the rest of the case manager's day for the selected date. The
        // query is scoped to the signed-in user across their whole caseload:
        // billing the same minute twice is a conflict no matter which two clients
        // the notes belong to.
        private async Task RefreshServiceDayAsync()
        {
            var request = _dayScheduleLoad.Begin();
            var date = EventDate;
            var userId = _sessionService.CurrentUser?.Id;

            if (date is null || userId is null)
            {
                _recordedDayBlocks = [];
                RedrawServiceDay();
                return;
            }

            IReadOnlyList<ServiceBlock> blocks;
            try
            {
                blocks = ToBlocks(await _noteService.GetDayScheduleAsync(userId.Value, date.Value));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Loading the service day failed: {ex.Message}");
                if (!_dayScheduleLoad.IsCurrent(request))
                    return;

                _recordedDayBlocks = [];
                RedrawServiceDay();
                ServiceTimeMessage =
                    "Sati could not load the rest of your day, so overlapping time cannot be shown here. " +
                    "The overlap check still runs when you save.";
                return;
            }

            if (!_dayScheduleLoad.IsCurrent(request))
                return;

            _recordedDayBlocks = blocks;
            RedrawServiceDay();
        }

        private static IReadOnlyList<ServiceBlock> ToBlocks(IEnumerable<Note> notes) => notes
            .Select(note => ServiceTimeline.TryCreateBlock(
                note.Id, note.StartTime, note.Minutes, note.Status?.ToString(), DescribeNoteOwner(note)))
            .OfType<ServiceBlock>()
            .OrderBy(block => block.StartMinutes)
            .ToList();

        private static string DescribeNoteOwner(Note note)
        {
            var name = note.Person is null
                ? null
                : $"{note.Person.FirstName} {note.Person.LastName}".Trim();
            return string.IsNullOrWhiteSpace(name) ? "another note" : $"a note for {name}";
        }

        // The block this draft would claim, or null when it claims nothing.
        private ServiceBlock? BuildCandidateBlock() => ServiceTimeline.TryCreateBlock(
            _editingNote?.Id ?? 0, SelectedStartTime?.Minutes, Minutes, Status?.ToString(), "this note");

        // Rebuilds the bar and the conflict verdict from state already in memory.
        // Cheap enough to run on every keystroke in Minutes.
        private void RedrawServiceDay()
        {
            ServiceDaySegments.Clear();

            // The note being edited is redrawn as the draft band, so it must not
            // also appear as recorded time underneath it.
            var editingId = _editingNote?.Id ?? 0;
            var recorded = _recordedDayBlocks
                .Where(block => block.NoteId != editingId)
                .ToList();
            foreach (var block in recorded)
                ServiceDaySegments.Add(ServiceDaySegment.FromBlock(block, ServiceDaySegmentKind.Recorded));

            var candidate = BuildCandidateBlock();
            if (candidate is null)
            {
                HasServiceTimeConflict = false;
                ServiceTimeMessage = DescribeIncompleteDraft();
                return;
            }

            var windowProblem = ServiceTimeline.DescribeWindowViolation(candidate.StartMinutes, candidate.Minutes);
            if (windowProblem is not null)
            {
                HasServiceTimeConflict = true;
                ServiceDaySegments.Add(ServiceDaySegment.FromBlock(candidate, ServiceDaySegmentKind.Conflict));
                ServiceTimeMessage = windowProblem;
                return;
            }

            var conflicts = ServiceTimeline.FindConflicts(candidate, recorded);
            HasServiceTimeConflict = conflicts.Count > 0;
            ServiceDaySegments.Add(ServiceDaySegment.FromBlock(
                candidate,
                HasServiceTimeConflict ? ServiceDaySegmentKind.Conflict : ServiceDaySegmentKind.Draft));

            ServiceTimeMessage = HasServiceTimeConflict
                ? "Overlapping service time. " + string.Join(" ", conflicts.Select(conflict => conflict.Reason))
                : $"{ServiceTimeline.DescribeRange(candidate)} is free on your day.";
        }

        private string DescribeIncompleteDraft()
        {
            if (EventDate is null)
                return NoDateMessage;
            if (SelectedStartTime is null)
                return NoStartTimeMessage;
            if (Minutes is null or <= 0)
                return "Enter the service minutes to reserve this time on your day.";
            return $"A {Status} note does not hold time on your day.";
        }

        // Re-checks against freshly loaded data immediately before persisting.
        // The screen's live verdict can be stale — another window, or another
        // device, may have claimed the time since the bar was last drawn.
        // Returns the blocking message, or null when the time is free.
        private async Task<string?> FindServiceTimeConflictAsync()
        {
            var candidate = BuildCandidateBlock();
            if (candidate is null)
                return null;

            var windowProblem = ServiceTimeline.DescribeWindowViolation(candidate.StartMinutes, candidate.Minutes);
            if (windowProblem is not null)
                return windowProblem;

            var userId = _sessionService.CurrentUser?.Id;
            if (EventDate is not DateTime date || userId is null)
                return null;

            var blocks = ToBlocks(await _noteService.GetDayScheduleAsync(userId.Value, date));
            _recordedDayBlocks = blocks;
            var conflicts = ServiceTimeline.FindConflicts(candidate, blocks);
            if (conflicts.Count == 0)
                return null;

            RedrawServiceDay();
            return "This note's service time overlaps time already recorded on " +
                   $"{date:MMMM d}. " +
                   string.Join(" ", conflicts.Select(conflict => conflict.Reason)) +
                   " Adjust the start time or the minutes before saving.";
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
            SelectedStartTime = FindStartOption(note.StartTime);
            Status = note.Status;
            SelectedNoteType = note.NoteType;
            SelectedFormType = note.FormType;
            ApplyVisitDocumentation(note.VisitDocumentation);
            _ = LoadVisitAttendeesAsync(SelectedPerson);
            _ = RefreshServiceDayAsync();
        }

        // Notes saved before start times existed, or saved off the 5-minute grid,
        // have no matching option. Snapping to the nearest slot would silently
        // move a recorded time, so those notes simply show no selection until the
        // user picks one.
        private static ServiceStartOption? FindStartOption(int? startMinutes) =>
            startMinutes is int minutes
                ? StartTimeOptions.FirstOrDefault(option => option.Minutes == minutes)
                : null;

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseNarrativeFont() => NarrativeFontSize = Math.Min(NarrativeFontSize + 2, 28);
        [RelayCommand] private void DecreaseNarrativeFont() => NarrativeFontSize = Math.Max(NarrativeFontSize - 2, 10);

        // Reminders are excluded at the command, not only in the view: the grounded
        // drafting contract is for case-note facts and the service-note review path.
        private bool CanFormatNarrativeWithAi() =>
            IsLocalAiEnabled && !IsAiBusy && !IsReminderNote && SelectedPerson is not null &&
            !string.IsNullOrWhiteSpace(Narrative);

        [RelayCommand(CanExecute = nameof(CanFormatNarrativeWithAi))]
        private async Task FormatNarrativeWithAi()
        {
            if (string.IsNullOrWhiteSpace(Narrative))
                return;

            var currentUser = _sessionService.CurrentUser
                ?? throw new InvalidOperationException("A signed-in user is required to use local AI.");
            var selectedPerson = SelectedPerson
                ?? throw new InvalidOperationException("Select a client before formatting a note.");
            var source = Narrative.Trim();
            var capturedNoteType = SelectedNoteType;
            var capturedFormType = SelectedFormType;
            var capturedVisit = BuildVisitDocumentation();
            var requestIdentity = _aiDraftRequests.Begin();
            var cancellation = new CancellationTokenSource();
            var previousCancellation = Interlocked.Exchange(ref _aiDraftCancellation, cancellation);
            previousCancellation?.Cancel();

            IsAiBusy = true;
            ClearAiReview();
            AiStatusMessage = "Preparing the local case-note assistant…";

            var progress = new Progress<CaseNoteFormattingProgress>(update =>
            {
                if (!_aiDraftRequests.IsCurrent(requestIdentity))
                    return;
                AiStatusMessage = update.Message;
                AiDownloadProgress = update.Percent;
            });

            try
            {
                AiStatusMessage = "Confirming the selected client boundary…";
                var clientContext = await _clientAiContextService.BuildAsync(
                    selectedPerson.Id,
                    cancellation.Token);

                if (!_aiDraftRequests.IsCurrent(requestIdentity) ||
                    SelectedPerson?.Id != selectedPerson.Id ||
                    clientContext.PersonId != selectedPerson.Id)
                    return;

                var snapshot = CaseNoteFactCompiler.Build(
                    selectedPerson.Id,
                    source,
                    capturedNoteType,
                    capturedFormType,
                    currentUser.DisplayName,
                    clientContext.ConsumerFirstName,
                    capturedVisit);

                AiContextSources = clientContext.Sources
                    .Concat(snapshot.Facts
                        .Where(fact => fact.Id != CaseNoteDraftRules.NoFollowUpFactId)
                        .GroupBy(fact => fact.Category)
                        .Select(group => new ClientAiContextSource(
                            group.Key,
                            $"{group.Count()} current-note fact{(group.Count() == 1 ? string.Empty : "s")}")))
                    .ToList();
                var requiredFactCount = snapshot.Facts.Count(fact => fact.Required);
                AiContextSummary = $"Verified inputs ({requiredFactCount} required facts)";

                var result = await _caseNoteFormatter.FormatAsync(
                    new CaseNoteFormattingRequest(
                        snapshot.PersonId,
                        snapshot.RawNarrative,
                        snapshot.NoteType,
                        snapshot.FormType,
                        snapshot.CaseManagerFullName,
                        snapshot.ConsumerFirstName,
                        snapshot.Fingerprint,
                        snapshot.Facts),
                    progress,
                    cancellation.Token);

                if (!_aiDraftRequests.IsCurrent(requestIdentity) ||
                    SelectedPerson?.Id != snapshot.PersonId ||
                    !CurrentAiInputsMatch(result.SourceFingerprint, currentUser))
                    return;

                _aiSourceNarrative = source;
                _aiSourceFingerprint = result.SourceFingerprint;
                AiDraftNarrative = result.DraftNarrative;
                AiWarnings = result.Warnings;
                IsAiReviewVisible = true;
                AiStatusMessage = $"All {requiredFactCount} required facts were accounted for. Compare the draft before accepting it.";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // A client, template, or source change invalidated this request. The change handler
                // already cleared the review surface; a cancellation is not an error dialog.
            }
            catch (Exception ex)
            {
                if (!_aiDraftRequests.IsCurrent(requestIdentity))
                    return;

                // Do not write exception messages here; grounding failures may quote draft content.
                Debug.WriteLine($"Local case-note formatting failed: {ex.GetType().Name}");
                AiStatusMessage = "The local assistant could not create a draft.";

                var dialog = _validationDialog(
                    "Sati could not create a local AI draft. Your original narrative has not changed.\n\n" +
                    GetFriendlyAiError(ex));
                dialog.Owner = Application.Current.MainWindow;
                dialog.ShowDialog();
            }
            finally
            {
                Interlocked.CompareExchange(ref _aiDraftCancellation, null, cancellation);
                cancellation.Dispose();

                if (_aiDraftRequests.IsCurrent(requestIdentity))
                {
                    IsAiBusy = false;
                    AiDownloadProgress = null;
                }
            }
        }

        [RelayCommand]
        private void AcceptAiDraft()
        {
            if (!IsAiReviewVisible || string.IsNullOrWhiteSpace(AiDraftNarrative))
                return;

            var currentUser = _sessionService.CurrentUser;
            if (currentUser is null || string.IsNullOrWhiteSpace(_aiSourceFingerprint) ||
                !CurrentAiInputsMatch(_aiSourceFingerprint, currentUser))
            {
                ClearAiReview();
                AiStatusMessage = "The client or note inputs changed, so the AI draft was discarded. Format the current facts again.";
                return;
            }

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
            // A reminder leaves the note pipeline entirely — no status, no
            // compliance gate, no billing window, and no Notes row. It is a journal
            // entry, so it goes down its own path before any of that runs.
            if (IsReminderNote)
            {
                await SubmitReminderAsync();
                return;
            }

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
            if (HasServiceTimeConflict)
                errors.Add($"• {ServiceTimeMessage}");

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

        // Client and reminder text are the only inputs, so they are the only
        // validation. The length bound is the contract's, so the desktop refuses
        // exactly what the server refuses instead of discovering it in a 400.
        private async Task SubmitReminderAsync()
        {
            var errors = new List<string>();
            if (SelectedPerson is null)
                errors.Add("• Please select a client.");
            if (string.IsNullOrWhiteSpace(Narrative))
                errors.Add("• Please enter the reminder.");
            else if (Narrative.Trim().Length > JournalEntry.MaxTextLength)
                errors.Add($"• A reminder is limited to {JournalEntry.MaxTextLength} characters.");

            if (errors.Count > 0)
            {
                var dialog = _validationDialog(string.Join("\n", errors));
                dialog.Owner = Application.Current.MainWindow;
                dialog.ShowDialog();
                return;
            }

            var personId = SelectedPerson!.Id;
            try
            {
                // Flush the client page's pending journal edit FIRST — see
                // JournalWriteStartingAsync. Then the writer prepends the stamped
                // entry and returns the journal it actually wrote.
                if (JournalWriteStartingAsync is not null)
                    await JournalWriteStartingAsync(personId);

                var result = await _personService.AddJournalReminderAsync(personId, Narrative!);

                // Note fields only: the client stays selected so several reminders
                // can be added in a row, matching how notes behave here.
                ClearNoteFields();
                ReminderAdded?.Invoke(this, new JournalReminderAddedEventArgs(
                    personId, result.Journal, result.UsedLegacyJournalWrite));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SubmitReminder failed: {ex.Message}");
                MessageBox.Show(
                    "Sati could not add the reminder to this client's journal. Your text remains on screen, so you can try again.",
                    "Reminder Not Added", MessageBoxButton.OK, MessageBoxImage.Error);
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
            // Last check before the record exists. The API repeats this as the
            // authoritative gate; this covers the desktop's direct database path
            // and catches time claimed since the bar was drawn.
            var timeConflict = await FindServiceTimeConflictAsync();
            if (timeConflict is not null)
            {
                var conflictDialog = _validationDialog(timeConflict);
                conflictDialog.Owner = Application.Current.MainWindow;
                conflictDialog.ShowDialog();
                return;
            }

            var wasEdit = IsEditing && _editingNote is not null;
            FormType? savedFormType = null;

            if (wasEdit)
            {
                var note = _editingNote!;
                note.Narrative = Narrative!;
                note.EventDate = EventDate;
                note.Minutes = Minutes ?? 0;
                note.StartTime = SelectedStartTime?.Minutes;
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
                note.StartTime = SelectedStartTime?.Minutes;
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
            if (draft.StartTime != latest.StartTime) fields.Add("service start time");
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
            SelectedStartTime = null;
            _pendingVisitDocumentation = null;
            ResetVisitDocumentation(clearAttendees: true);
            ClearAiReview();
            AiStatusMessage = string.Empty;
        }

        private void ClearAiReview()
        {
            _aiSourceNarrative = null;
            _aiSourceFingerprint = null;
            IsAiReviewVisible = false;
            AiDraftNarrative = string.Empty;
            AiWarnings = [];
            AiContextSources = [];
            AiContextSummary = "Verified inputs";
            AiDownloadProgress = null;
        }

        private void InvalidateAiGeneration()
        {
            _aiDraftRequests.Invalidate();
            Interlocked.Exchange(ref _aiDraftCancellation, null)?.Cancel();
            if (IsAiBusy)
            {
                IsAiBusy = false;
                AiDownloadProgress = null;
            }
        }

        private bool CurrentAiInputsMatch(string fingerprint, User currentUser)
        {
            if (SelectedPerson is null || string.IsNullOrWhiteSpace(Narrative))
                return false;

            var current = CaseNoteFactCompiler.Build(
                SelectedPerson.Id,
                Narrative,
                SelectedNoteType,
                SelectedFormType,
                currentUser.DisplayName,
                SelectedPerson.FirstName,
                BuildVisitDocumentation());
            return string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal);
        }

        private static string GetFriendlyAiError(Exception exception)
        {
            var root = exception;
            while (root.InnerException is not null)
                root = root.InnerException;

            return root switch
            {
                CaseNoteDraftRejectedException rejected =>
                    rejected.Message + "\n\n" + string.Join("\n", rejected.Errors.Take(6).Select(error => $"• {error}")),
                ArgumentException => root.Message,
                UnauthorizedAccessException => root.Message,
                OperationCanceledException => "The operation was canceled.",
                _ => "The model may still need to be downloaded, or the configured model may not be available. " +
                     "Check the development configuration and try again."
            };
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

    /// <summary>
    /// A stamped reminder reached the client's journal. Carries the journal text
    /// the writer produced so a host showing the same column can display what was
    /// actually stored rather than re-composing the entry itself.
    /// </summary>
    public sealed class JournalReminderAddedEventArgs(
        int personId,
        string? journal,
        bool usedLegacyJournalWrite = false) : EventArgs
    {
        public int PersonId { get; } = personId;
        public string? Journal { get; } = journal;

        /// <summary>
        /// The entry was written through the older whole-journal route because the
        /// server does not have the journal-entries route. Surfaced to the user
        /// rather than swallowed: a client running ahead of its server is a
        /// deployment fact worth stating, not a detail to hide.
        /// </summary>
        public bool UsedLegacyJournalWrite { get; } = usedLegacyJournalWrite;
    }
}
