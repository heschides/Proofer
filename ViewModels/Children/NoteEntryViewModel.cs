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
using System.Reflection;
using System.Runtime.InteropServices;
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
    // note persistence. Hosts learn about successful saves through NoteSaved.
    // A form-tagged note is evidence only and never changes compliance state.
    public partial class NoteEntryViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly INoteService _noteService;
        private readonly IPersonService _personService;
        private readonly ISettingsService _settingsService;
        private readonly IUpcomingEventService _upcomingEventService;
        private readonly ISessionService _sessionService;
        private readonly IPersonContactService _personContactService;
        private readonly IClientAiContextService _clientAiContextService;
        private readonly ICaseNoteFormatter _caseNoteFormatter;
        private readonly Func<string, UserMessageDialog> _validationDialog;
        private readonly DiscardChangesPrompt _confirmDiscard;

        private Settings? _settings;
        public BillingComplianceRequirements ComplianceRequirements =>
            _settings?.BillingComplianceRequirements ?? BillingComplianceGate.DefaultRequirements;
        private Note? _editingNote;
        private string? _aiSourceNarrative;
        private string? _aiSourceFingerprint;
        private bool _applyingAiDraft;
        private VisitDocumentation? _pendingVisitDocumentation;
        private int _attendeeLoadVersion;
        private bool _suppressVisitChangeNotifications;
        private bool _synchronizingVisitChoices;
        private bool _suppressDirtyTracking;
        private bool _suppressPersonSelectionEffects;
        private bool _applyingSchedulingPolicy;
        private readonly LatestRequestTracker _aiDraftRequests = new();
        private CancellationTokenSource? _aiDraftCancellation;

        // Time already claimed by this case manager's other notes on EventDate.
        // Reloaded whenever the date changes; the tracker keeps a slow load for a
        // date the user has already moved away from from publishing stale bands.
        private readonly LatestRequestTracker _dayScheduleLoad = new();

        // Guards the freshness read taken when a note is unlocked, so a slow reply
        // for a note the panel has already moved off cannot publish over it.
        private readonly LatestRequestTracker _freshnessChecks = new();
        private readonly LatestRequestTracker _suggestedFollowUpRequests = new();
        private UpcomingEvent? _suggestedFollowUp;
        private bool _suggestedFollowUpAccepted;
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
            IUpcomingEventService upcomingEventService,
            ISessionService sessionService,
            IPersonContactService personContactService,
            IClientAiContextService clientAiContextService,
            ICaseNoteFormatter caseNoteFormatter,
            Func<string, UserMessageDialog> validationDialog,
            DiscardChangesPrompt confirmDiscard)
        {
            _noteService = noteService;
            _personService = personService;
            _settingsService = settingsService;
            _upcomingEventService = upcomingEventService;
            _sessionService = sessionService;
            _personContactService = personContactService;
            _clientAiContextService = clientAiContextService;
            _caseNoteFormatter = caseNoteFormatter;
            _validationDialog = validationDialog;
            _confirmDiscard = confirmDiscard;

            VisitSettingOptions = CreateVisitOptions<VisitSetting>(VisitSetting.NotDocumented);
            VisitAppearanceOptions = CreateVisitOptions<VisitAppearance>(VisitAppearance.NotDocumented);
            VisitParticipationOptions = CreateVisitOptions<VisitParticipation>(VisitParticipation.NotDocumented);
            VisitSafetyObservationOptions = CreateVisitOptions<VisitSafetyObservation>(VisitSafetyObservation.NotDocumented);

            SubscribeToVisitOptions(VisitSettingOptions);
            SubscribeToVisitOptions(VisitAppearanceOptions);
            SubscribeToVisitOptions(VisitParticipationOptions);
            SubscribeToVisitOptions(VisitSafetyObservationOptions);
        }

        // -------------------------------------------------------------------------
        // Host integration
        // -------------------------------------------------------------------------

        // Awaited before NoteSaved when a Form-type note lands as Pending/Logged.
        // Args: the form type, and whether this was an edit (hosts may route new
        // vs. edited form notes differently).
        public event EventHandler? NoteSaved;

        // The view owns the confirmation window. The ViewModel supplies the exact
        // old/new client names and treats an absent handler as a refusal, so a
        // background or test caller cannot bypass the human confirmation by
        // changing SelectedPerson directly.
        public event EventHandler<NoteReassignmentConfirmationEventArgs>?
            NoteReassignmentConfirmationRequested;

        // The panel went back to a blank New Note. Hosts use this to drop the row
        // their grid still has highlighted, so the highlight and the panel never
        // describe different things.
        public event EventHandler? EditorCleared;

        // Awaited BEFORE a reminder is written, for the same reason
        // JournalWriteStartingAsync is a Func and not an event: ordering is the point.
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

        // A loaded note starts LOCKED: the panel opens as a reader of the record
        // and only becomes an editor when the case manager says so. Locked is not
        // a permission — the API is still the authority on who may change a note —
        // it is protection against editing a clinical record by accident.
        [ObservableProperty] private bool isLocked;

        // True once the case manager has changed something that is not on disk.
        // Tracked with an explicit flag rather than by diffing against the saved
        // note: loading a note writes every field, and the visit attendees arrive
        // asynchronously, so a diff would report changes the user never made.
        [ObservableProperty] private bool hasUnsavedChanges;

        // The supervisor's reason, carried by the loaded note. Displayed rather
        // than edited — the case manager answers it in the narrative.
        [ObservableProperty] private string? returnReason;

        // Says that the note on screen is no longer what the server holds, and what
        // was done about it. Null whenever the panel is showing a copy it has no
        // reason to doubt.
        [ObservableProperty] private string? staleNoteMessage;
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

        public string SuggestedFollowUpText => _suggestedFollowUp is null
            ? string.Empty
            : $"{_suggestedFollowUp.Title}, due {_suggestedFollowUp.Date:M/d/yy}";

        public bool IsSuggestedFollowUpVisible =>
            _suggestedFollowUp is not null && !IsReminderNote;

        public string SuggestedFollowUpAutomationName => _suggestedFollowUp is null
            ? "Accept suggested follow-up"
            : $"Add suggested follow-up: {SuggestedFollowUpText}";

        public string SuggestedFollowUpToolTip
        {
            get
            {
                if (_suggestedFollowUpAccepted)
                    return "This suggestion has already been added to the note.";
                if (CaseNoteFactCompiler.HasFollowUpSignal(Narrative))
                    return "A follow-up is already documented in this note.";
                if (IsLocked)
                    return "Unlock the note before adding the suggested follow-up.";
                return "Add this item to the note as an editable follow-up.";
            }
        }

        // Structured Visit facts. The legacy singular enum values remain so old
        // note JSON still opens; the option collections are the current UI state.
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

        public ObservableCollection<VisitChoiceOptionViewModel<VisitSetting>> VisitSettingOptions { get; }
        public ObservableCollection<VisitChoiceOptionViewModel<VisitAppearance>> VisitAppearanceOptions { get; }
        public ObservableCollection<VisitChoiceOptionViewModel<VisitParticipation>> VisitParticipationOptions { get; }
        public ObservableCollection<VisitChoiceOptionViewModel<VisitSafetyObservation>> VisitSafetyObservationOptions { get; }

        public static IReadOnlyList<NoteStatus> NoteStatusOptions { get; } =
        [
            NoteStatus.Scheduled,
            NoteStatus.Pending,
            NoteStatus.Logged,
            NoteStatus.Cancelled,
            NoteStatus.Delayed
        ];
        public string SaveActionLabel => IsCalendarReminder
            ? "Add to Calendar"
            : IsReminderNote
            ? "Add Reminder"
            : Status switch
            {
                NoteStatus.Pending => IsEditing ? "Update Draft" : "Save as Draft",
                NoteStatus.Logged => "Submit for Supervisor Review",
                _ => IsEditing ? "Update Note" : "Save Note"
            };

        // Three modes, one panel. New Note is the resting state; selecting a note
        // in a host's grid shows it as View Note; the lock toggle turns that into
        // Edit Note. The heading is the primary cue — the lock glyph is a second
        // one, never the only one.
        public string EditorHeading => !IsEditing
            ? "New Note"
            : IsLocked ? "View Note" : "Edit Note";

        public bool IsUnlocked => !IsLocked;

        // Segoe MDL2: E72E Lock, E785 Unlock. The glyph shows the CURRENT state,
        // and the tooltip and automation name say what clicking it will do.
        public string LockGlyph => IsLocked ? "\uE72E" : "\uE785";

        public string LockToggleLabel => IsLocked
            ? "Unlock this note for editing"
            : "Lock this note and return to viewing it";

        public string LockToggleTooltip => IsLocked
            ? "This note is locked. Unlock it to change the saved record."
            : "Lock this note. Any unsaved changes are discarded and the saved record is shown again.";

        public string MinutesLabel => Note.CalculateUnits(Minutes) is int units
            ? $"MINUTES — {units} UNIT{(units == 1 ? string.Empty : "S")}"
            : "MINUTES";

        public bool HasReturnReason => !string.IsNullOrWhiteSpace(ReturnReason);

        public bool HasStaleNoteMessage => !string.IsNullOrWhiteSpace(StaleNoteMessage);
        public string StatusGuidance => IsCalendarReminder
            ? CalendarReminderGuidance
            : IsReminderNote
                ? ReminderGuidance
                : DescribeStatus(Status);

        // Says why the workflow fields are unavailable, so the disabled state is
        // explained rather than merely observed. The status guidance block is a
        // polite live region, so a screen reader announces this on selection.
        internal const string ReminderGuidance =
            "Reminder — adds a timestamped entry to the top of this client's journal. " +
            "To put it on the calendar instead, choose a future date. It is not service " +
            "documentation: it has no status, minutes, or service date, and it " +
            "does not go to your supervisor or into billing.";

        internal const string CalendarReminderGuidance =
            "Calendar reminder — the future date changed this entry to a scheduled Reminder. " +
            "It will appear on that calendar day. It carries no service time and cannot enter " +
            "supervisor review, productivity totals, or billing.";

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
        public bool IsCalendarReminder => IsReminderNote && EventDate.HasValue;
        public bool IsJournalReminder => IsReminderNote && EventDate is null;
        // Reminder disables the service-note fields; a lock disables everything
        // that would change the record. Both funnel through this one property so
        // a control never has to know which of the two is currently in force.
        public bool AreNoteFieldsEnabled => !IsReminderNote && !IsLocked;
        public bool IsDateEnabled => !IsLocked;
        public string NarrativeLabel => IsReminderNote ? "REMINDER" : "NARRATIVE";

        partial void OnStatusChanged(NoteStatus? value)
        {
            OnPropertyChanged(nameof(SaveActionLabel));
            OnPropertyChanged(nameof(StatusGuidance));
            MarkDirty();

            // Cancelled and Delayed release the time the draft was holding.
            RedrawServiceDay();
        }

        partial void OnIsEditingChanged(bool value)
        {
            OnPropertyChanged(nameof(SaveActionLabel));
            OnPropertyChanged(nameof(EditorHeading));
            ToggleLockCommand.NotifyCanExecuteChanged();
            StartNewNoteCommand.NotifyCanExecuteChanged();
        }

        partial void OnHasUnsavedChangesChanged(bool value) =>
            StartNewNoteCommand.NotifyCanExecuteChanged();

        partial void OnIsComplianceDialogVisibleChanged(bool value) =>
            StartNewNoteCommand.NotifyCanExecuteChanged();

        partial void OnIsLockedChanged(bool value)
        {
            OnPropertyChanged(nameof(IsUnlocked));
            OnPropertyChanged(nameof(AreNoteFieldsEnabled));
            OnPropertyChanged(nameof(EditorHeading));
            OnPropertyChanged(nameof(LockGlyph));
            OnPropertyChanged(nameof(LockToggleLabel));
            OnPropertyChanged(nameof(LockToggleTooltip));
            OnPropertyChanged(nameof(IsDateEnabled));
            SubmitNoteCommand.NotifyCanExecuteChanged();
            FormatNarrativeWithAiCommand.NotifyCanExecuteChanged();
            BuildCaseNoteTemplateCommand.NotifyCanExecuteChanged();
            AcceptSuggestedFollowUpCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SuggestedFollowUpToolTip));
        }

        partial void OnReturnReasonChanged(string? value) =>
            OnPropertyChanged(nameof(HasReturnReason));

        partial void OnStaleNoteMessageChanged(string? value) =>
            OnPropertyChanged(nameof(HasStaleNoteMessage));

        partial void OnEventDateChanged(DateTime? value)
        {
            if (!_applyingSchedulingPolicy &&
                NoteSchedulingPolicy.IsFutureDate(value, DateTime.Today))
            {
                ApplyFutureReminderPolicy();
            }

            OnPropertyChanged(nameof(ServiceDayHeading));
            NotifyReminderModeChanged();
            MarkDirty();
            _ = RefreshServiceDayAsync();
        }

        partial void OnSelectedStartTimeChanged(ServiceStartOption? value)
        {
            MarkDirty();
            RedrawServiceDay();
        }

        partial void OnMinutesChanged(int? value)
        {
            OnPropertyChanged(nameof(MinutesLabel));
            MarkDirty();
            RedrawServiceDay();
        }

        // One place decides that the panel now holds work the database does not.
        // Suppressed while a note is being loaded into the fields, which is a
        // read, not an edit.
        private void MarkDirty()
        {
            if (_suppressDirtyTracking)
                return;
            HasUnsavedChanges = true;
        }

        public bool IsLocalAiEnabled => _caseNoteFormatter.IsEnabled;
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
                RefreshSuggestedFollowUp(newValue, resetAcceptance: false);
                return;
            }

            if (_suppressPersonSelectionEffects)
                return;

            InvalidateAiGeneration();

            // A client change while editing is now a reassignment of the existing
            // record. Confirm it immediately, while the old and new names are both
            // visible in context, and leave every other note field untouched.
            if (IsEditing && !IsLocked && _editingNote is Note editingNote)
            {
                var originalPerson = People.FirstOrDefault(person =>
                    person.Id == editingNote.PersonId) ?? editingNote.Person;
                if (newValue is null)
                {
                    SetSelectedPersonWithoutEffects(originalPerson);
                    return;
                }

                if (newValue.Id != editingNote.PersonId)
                {
                    var confirmation = new NoteReassignmentConfirmationEventArgs(
                        originalPerson?.FullName ?? "the current client",
                        newValue.FullName);
                    NoteReassignmentConfirmationRequested?.Invoke(this, confirmation);
                    if (!confirmation.Confirmed)
                    {
                        SetSelectedPersonWithoutEffects(originalPerson);
                        return;
                    }
                }

                MarkDirty();
                _ = LoadVisitAttendeesAsync(newValue);
                RefreshSuggestedFollowUp(newValue, resetAcceptance: true);
                return;
            }

            // Genuine client switch: reset the draft; edit mode ends because the
            // note being edited belongs to the previous client. Editing client
            // changes return above; this branch is the New Note workflow.
            Status = null;
            Narrative = string.Empty;
            EventDate = null;
            SelectedNoteType = null;
            SelectedFormType = null;
            Minutes = null;
            SelectedStartTime = null;
            _pendingVisitDocumentation = null;
            ResetVisitDocumentation(clearAttendees: true);

            // The draft this flag was tracking no longer exists — it was just
            // wiped by the client switch, which is itself the deliberate act.
            HasUnsavedChanges = false;
            _ = LoadVisitAttendeesAsync(newValue);
            RefreshSuggestedFollowUp(newValue, resetAcceptance: true);
        }

        private void SetSelectedPersonWithoutEffects(Person? person)
        {
            _suppressPersonSelectionEffects = true;
            try
            {
                SelectedPerson = person;
            }
            finally
            {
                _suppressPersonSelectionEffects = false;
            }
        }

        partial void OnSelectedNoteTypeChanged(NoteType? value)
        {
            InvalidateAiGeneration();
            MarkDirty();
            OnPropertyChanged(nameof(IsFormNote));
            OnPropertyChanged(nameof(IsVisitNote));
            OnPropertyChanged(nameof(IsReminderNote));
            OnPropertyChanged(nameof(IsCalendarReminder));
            OnPropertyChanged(nameof(IsJournalReminder));
            OnPropertyChanged(nameof(AreNoteFieldsEnabled));
            OnPropertyChanged(nameof(NarrativeLabel));
            OnPropertyChanged(nameof(IsSuggestedFollowUpVisible));
            OnPropertyChanged(nameof(SuggestedFollowUpToolTip));
            OnPropertyChanged(nameof(SaveActionLabel));
            OnPropertyChanged(nameof(StatusGuidance));
            FormatNarrativeWithAiCommand.NotifyCanExecuteChanged();
            BuildCaseNoteTemplateCommand.NotifyCanExecuteChanged();
            AcceptSuggestedFollowUpCommand.NotifyCanExecuteChanged();

            // A caller can change fields programmatically and a user can click a
            // type after choosing the date. Either way, a future date remains a
            // Reminder; the API and local service repeat the same policy on save.
            if (!_applyingSchedulingPolicy &&
                value != NoteType.Reminder &&
                NoteSchedulingPolicy.IsFutureDate(EventDate, DateTime.Today))
            {
                ApplyFutureReminderPolicy();
                return;
            }

            if (value == NoteType.Reminder)
            {
                // Cleared, not merely disabled. Service details left behind by a
                // half-finished note must not travel with either kind of reminder.
                // A future date stays attached and receives the only workflow
                // status a calendar reminder may carry; an undated Reminder keeps
                // the established journal-entry behavior.
                var isCalendarReminder =
                    NoteSchedulingPolicy.IsFutureDate(EventDate, DateTime.Today) ||
                    (_suppressDirtyTracking && EventDate.HasValue);
                Status = isCalendarReminder ? NoteStatus.Scheduled : null;
                Minutes = null;
                if (!isCalendarReminder)
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

        private void ApplyFutureReminderPolicy()
        {
            if (!NoteSchedulingPolicy.IsFutureDate(EventDate, DateTime.Today))
                return;

            _applyingSchedulingPolicy = true;
            try
            {
                SelectedNoteType = NoteType.Reminder;
                Status = NoteStatus.Scheduled;
                Minutes = null;
                SelectedStartTime = null;
                SelectedFormType = null;
                _pendingVisitDocumentation = null;
                ResetVisitDocumentation(clearAttendees: false);
            }
            finally
            {
                _applyingSchedulingPolicy = false;
            }

            NotifyReminderModeChanged();
        }

        private void NotifyReminderModeChanged()
        {
            OnPropertyChanged(nameof(IsCalendarReminder));
            OnPropertyChanged(nameof(IsJournalReminder));
            OnPropertyChanged(nameof(StatusGuidance));
            OnPropertyChanged(nameof(SaveActionLabel));
        }

        partial void OnNarrativeChanged(string? value)
        {
            FormatNarrativeWithAiCommand.NotifyCanExecuteChanged();
            BuildCaseNoteTemplateCommand.NotifyCanExecuteChanged();
            AcceptSuggestedFollowUpCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SuggestedFollowUpToolTip));
            MarkDirty();

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

        partial void OnVisitSettingChanged(VisitSetting value)
        {
            if (!_synchronizingVisitChoices)
                SelectOnly(VisitSettingOptions, value, VisitSetting.NotDocumented);
            VisitFactsChanged();
        }

        partial void OnVisitAppearanceChanged(VisitAppearance value)
        {
            if (!_synchronizingVisitChoices)
                SelectOnly(VisitAppearanceOptions, value, VisitAppearance.NotDocumented);
            VisitFactsChanged();
        }

        partial void OnVisitParticipationChanged(VisitParticipation value)
        {
            if (!_synchronizingVisitChoices)
                SelectOnly(VisitParticipationOptions, value, VisitParticipation.NotDocumented);
            VisitFactsChanged();
        }

        partial void OnVisitSafetyObservationChanged(VisitSafetyObservation value)
        {
            if (!_synchronizingVisitChoices)
                SelectOnly(VisitSafetyObservationOptions, value, VisitSafetyObservation.NotDocumented);
            VisitFactsChanged();
        }
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
            MarkDirty();
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
            RefreshSuggestedFollowUp(SelectedPerson, resetAcceptance: false);
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

        private void RefreshSuggestedFollowUp(Person? person, bool resetAcceptance)
        {
            var request = _suggestedFollowUpRequests.Begin();
            UpcomingEvent? suggestion = null;

            if (person is not null && _settings is not null)
            {
                try
                {
                    suggestion = _upcomingEventService
                        .GenerateEvents([person], _settings)
                        .OrderBy(item => item.Date)
                        .FirstOrDefault()
                        // Without this fallback the row is blank for most of every
                        // cycle: GenerateEvents only reports a form inside its open
                        // window, and the default review window is zero days wide.
                        ?? _upcomingEventService.NextFormSuggestion(person, _settings);
                }
                catch (Exception ex)
                {
                    var reference = AppErrorLog.Record(ex, "note-entry.suggested-follow-up");
                    Debug.WriteLine($"Suggested follow-up unavailable. Support reference: {reference}.");
                }
            }

            if (!_suggestedFollowUpRequests.IsCurrent(request) ||
                SelectedPerson?.Id != person?.Id)
                return;

            _suggestedFollowUp = suggestion;
            if (resetAcceptance)
                _suggestedFollowUpAccepted = false;
            NotifySuggestedFollowUpChanged();
        }

        private void NotifySuggestedFollowUpChanged()
        {
            OnPropertyChanged(nameof(SuggestedFollowUpText));
            OnPropertyChanged(nameof(IsSuggestedFollowUpVisible));
            OnPropertyChanged(nameof(SuggestedFollowUpAutomationName));
            OnPropertyChanged(nameof(SuggestedFollowUpToolTip));
            AcceptSuggestedFollowUpCommand.NotifyCanExecuteChanged();
        }

        private bool CanAcceptSuggestedFollowUp() =>
            _suggestedFollowUp is not null &&
            !_suggestedFollowUpAccepted &&
            !IsReminderNote &&
            !IsLocked &&
            !CaseNoteFactCompiler.HasFollowUpSignal(Narrative);

        [RelayCommand(CanExecute = nameof(CanAcceptSuggestedFollowUp))]
        private void AcceptSuggestedFollowUp()
        {
            if (!CanAcceptSuggestedFollowUp() || _suggestedFollowUp is null)
                return;

            var item = _suggestedFollowUp.Title.Trim().TrimEnd('.');
            var line = $"Follow-up: {item} due {_suggestedFollowUp.Date:M/d/yy}.";
            Narrative = string.IsNullOrWhiteSpace(Narrative)
                ? line
                : $"{Narrative.TrimEnd()}{Environment.NewLine}{line}";

            _suggestedFollowUpAccepted = true;
            NotifySuggestedFollowUpChanged();
        }

        // The template reads the ticked meeting controls, so its availability
        // has to be re-checked whenever one of them changes.
        private bool CanBuildCaseNoteTemplate() =>
            IsVisitNote &&
            !IsLocked &&
            AreNoteFieldsEnabled &&
            CaseNoteTemplateComposer.HasContent(BuildVisitDocumentation());

        /// <summary>
        /// Writes a structured case note from the ticked meeting controls and moves
        /// whatever was already in the box below a Meeting Narrative header. Nothing
        /// is removed or rewritten; the existing text is preserved verbatim.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanBuildCaseNoteTemplate))]
        private void BuildCaseNoteTemplate()
        {
            var template = CaseNoteTemplateComposer.Compose(BuildVisitDocumentation());
            if (string.IsNullOrEmpty(template))
                return;

            Narrative = CaseNoteTemplateComposer.Merge(template, Narrative);
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

        private static ObservableCollection<VisitChoiceOptionViewModel<T>> CreateVisitOptions<T>(T notDocumented)
            where T : struct, Enum =>
            new(Enum.GetValues<T>()
                .Where(value => !EqualityComparer<T>.Default.Equals(value, notDocumented))
                .Select(value => new VisitChoiceOptionViewModel<T>(value, DescribeEnum(value))));

        private static string DescribeEnum<T>(T value) where T : struct, Enum
        {
            var field = typeof(T).GetField(value.ToString());
            return field?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();
        }

        private void SubscribeToVisitOptions<T>(IEnumerable<VisitChoiceOptionViewModel<T>> options)
            where T : struct, Enum
        {
            foreach (var option in options)
                option.PropertyChanged += OnVisitChoicePropertyChanged;
        }

        private void OnVisitChoicePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(VisitChoiceOptionViewModel<VisitSetting>.IsSelected) ||
                _synchronizingVisitChoices)
            {
                return;
            }

            _synchronizingVisitChoices = true;
            try
            {
                switch (sender)
                {
                    case VisitChoiceOptionViewModel<VisitSetting>:
                        VisitSetting = FirstSelected(VisitSettingOptions, VisitSetting.NotDocumented);
                        break;

                    case VisitChoiceOptionViewModel<VisitAppearance> changed:
                        EnforceExclusiveChoice(
                            VisitAppearanceOptions,
                            changed,
                            [VisitAppearance.NotObserved]);
                        VisitAppearance = FirstSelected(VisitAppearanceOptions, VisitAppearance.NotDocumented);
                        break;

                    case VisitChoiceOptionViewModel<VisitParticipation> changed:
                        EnforceExclusiveChoice(
                            VisitParticipationOptions,
                            changed,
                            [VisitParticipation.NotAssessed]);
                        VisitParticipation = FirstSelected(VisitParticipationOptions, VisitParticipation.NotDocumented);
                        break;

                    case VisitChoiceOptionViewModel<VisitSafetyObservation> changed:
                        // Every safety choice describes an alternative state. The
                        // checkbox presentation is consistent with the rest of the
                        // Visit panel, while this guard prevents contradictory facts.
                        if (changed.IsSelected)
                        {
                            foreach (var option in VisitSafetyObservationOptions.Where(option => !ReferenceEquals(option, changed)))
                                option.IsSelected = false;
                        }
                        VisitSafetyObservation = FirstSelected(
                            VisitSafetyObservationOptions,
                            VisitSafetyObservation.NotDocumented);
                        break;
                }
            }
            finally
            {
                _synchronizingVisitChoices = false;
            }

            VisitFactsChanged();
        }

        private static void EnforceExclusiveChoice<T>(
            IEnumerable<VisitChoiceOptionViewModel<T>> options,
            VisitChoiceOptionViewModel<T> changed,
            IReadOnlyCollection<T> exclusiveValues)
            where T : struct, Enum
        {
            if (!changed.IsSelected)
                return;

            var changedIsExclusive = exclusiveValues.Contains(changed.Value);
            foreach (var option in options.Where(option => !ReferenceEquals(option, changed)))
            {
                if (changedIsExclusive || exclusiveValues.Contains(option.Value))
                    option.IsSelected = false;
            }
        }

        private static void SelectOnly<T>(
            IEnumerable<VisitChoiceOptionViewModel<T>> options,
            T selected,
            T notDocumented)
            where T : struct, Enum
        {
            foreach (var option in options)
                option.IsSelected = !EqualityComparer<T>.Default.Equals(selected, notDocumented) &&
                                    EqualityComparer<T>.Default.Equals(option.Value, selected);
        }

        private static void SelectMany<T>(
            IEnumerable<VisitChoiceOptionViewModel<T>> options,
            IEnumerable<T> selected)
            where T : struct, Enum
        {
            var values = selected.ToHashSet();
            foreach (var option in options)
                option.IsSelected = values.Contains(option.Value);
        }

        private static T FirstSelected<T>(
            IEnumerable<VisitChoiceOptionViewModel<T>> options,
            T notDocumented)
            where T : struct, Enum =>
            options.FirstOrDefault(option => option.IsSelected)?.Value ?? notDocumented;

        private static List<T> SelectedValues<T>(IEnumerable<VisitChoiceOptionViewModel<T>> options)
            where T : struct, Enum =>
            options.Where(option => option.IsSelected).Select(option => option.Value).ToList();

        private void VisitFactsChanged()
        {
            if (_suppressVisitChangeNotifications)
                return;

            MarkDirty();
            BuildCaseNoteTemplateCommand.NotifyCanExecuteChanged();
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
                SelectMany(VisitSettingOptions, []);
                SelectMany(VisitAppearanceOptions, []);
                SelectMany(VisitParticipationOptions, []);
                SelectMany(VisitSafetyObservationOptions, []);
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
                SelectMany(VisitSettingOptions, documentation.EffectiveSettings());
                SelectMany(VisitAppearanceOptions, documentation.EffectiveAppearances());
                SelectMany(VisitParticipationOptions, documentation.EffectiveParticipations());
                SelectMany(VisitSafetyObservationOptions, documentation.EffectiveSafetyObservations());
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

            var settings = SelectedValues(VisitSettingOptions);
            var appearances = SelectedValues(VisitAppearanceOptions);
            var participations = SelectedValues(VisitParticipationOptions);
            var safetyObservations = SelectedValues(VisitSafetyObservationOptions);

            return new VisitDocumentation
            {
                Setting = settings.FirstOrDefault(VisitSetting.NotDocumented),
                Appearance = appearances.FirstOrDefault(VisitAppearance.NotDocumented),
                Participation = participations.FirstOrDefault(VisitParticipation.NotDocumented),
                SafetyObservation = safetyObservations.FirstOrDefault(VisitSafetyObservation.NotDocumented),
                Settings = settings,
                Appearances = appearances,
                Participations = participations,
                SafetyObservations = safetyObservations,
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
        // View and edit modes
        // -------------------------------------------------------------------------

        /// <summary>Shows a saved note in the panel, locked against editing.</summary>
        public void EnterViewMode(Note note) => LoadNote(note, locked: true);

        /// <summary>Shows a saved note in the panel, open for editing.</summary>
        public void EnterEditMode(Note note) => LoadNote(note, locked: false);

        /// <summary>
        /// Lifts the lock on the note already loaded. Used by the double-click
        /// gesture, where the preceding selection has already loaded the note.
        /// </summary>
        public void UnlockForEdit()
        {
            if (IsEditing)
                IsLocked = false;
        }

        /// <summary>
        /// Opens a note for editing on behalf of a host's grid gesture. Unlocks in
        /// place when the panel is already showing that note; otherwise loads it,
        /// asking first if that would throw away unsaved work.
        /// </summary>
        /// <remarks>
        /// Both hosts route their double-click through here rather than each
        /// writing the same three branches. Two copies of this decision would be
        /// two chances to forget the guard, and the notes log and the dashboard
        /// already differed on it once.
        /// </remarks>
        public void OpenForEdit(Note? note)
        {
            if (note is null)
                return;

            if (IsShowing(note))
            {
                UnlockForEdit();
                return;
            }

            if (!TryReleaseDraft())
                return;

            EnterEditMode(note);
        }

        /// <summary>True when this panel is currently showing that exact note.</summary>
        public bool IsShowing(Note note) => ReferenceEquals(_editingNote, note);

        /// <summary>
        /// Asks whether the panel's contents may be replaced. True when there is
        /// nothing to lose, or the case manager agreed to lose it. Hosts must call
        /// this BEFORE loading something else, never after.
        /// </summary>
        public bool TryReleaseDraft() =>
            !HasUnsavedChanges ||
            _confirmDiscard(
                "Discard this note?",
                "The note in the panel has changes that are not saved. " +
                "Opening another note will discard them.");

        private void LoadNote(Note note, bool locked)
        {
            // Whatever the panel was showing is being replaced, so a freshness read
            // still in flight for it has nothing left to say.
            _freshnessChecks.Invalidate();
            StaleNoteMessage = null;

            // Select the person without treating navigation between saved records
            // as a user-requested reassignment. The note is attached only after
            // the selection points at the record's stored client.
            SetSelectedPersonWithoutEffects(People.FirstOrDefault(p => p.Id == note.PersonId));
            _editingNote = note;

            // Filling the fields from the record is a read, not the case manager's
            // work, so it must not register as unsaved changes.
            _suppressDirtyTracking = true;
            try
            {
                IsEditing = true;
                IsLocked = locked;
                ReturnReason = note.ReturnReason;
                Narrative = note.Narrative;
                EventDate = note.EventDate;
                Minutes = note.Minutes;
                SelectedStartTime = FindStartOption(note.StartTime);
                Status = note.Status;
                SelectedNoteType = note.NoteType;
                SelectedFormType = note.FormType;
                ApplyVisitDocumentation(note.VisitDocumentation);
            }
            finally
            {
                _suppressDirtyTracking = false;
            }

            HasUnsavedChanges = false;
            _ = LoadVisitAttendeesAsync(SelectedPerson);
            RefreshSuggestedFollowUp(SelectedPerson, resetAcceptance: true);
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
            IsLocalAiEnabled && !IsAiBusy && !IsReminderNote && IsUnlocked &&
            SelectedPerson is not null && !string.IsNullOrWhiteSpace(Narrative);

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

        /// <summary>
        /// Full reset, client included. Hosts wanting "another note for the same
        /// client" want <see cref="ReturnToNewNote"/> instead.
        /// </summary>
        [RelayCommand]
        public void Clear()
        {
            SetSelectedPersonWithoutEffects(null);
            RefreshSuggestedFollowUp(null, resetAcceptance: true);
            ReturnToNewNote();
        }

        /// <summary>
        /// Puts the panel back to a blank New Note, keeping the selected client.
        /// </summary>
        /// <remarks>
        /// The client deliberately stays. On the dashboard this module's
        /// SelectedPerson is mirrored onto the page and scopes the notes grid,
        /// compliance checkboxes, and forms — clearing the note there must not
        /// blank the page around it. On both hosts the next thing a case manager
        /// usually does is write another note for the same person, which is why
        /// saving already leaves the client in place.
        /// </remarks>
        public void ReturnToNewNote()
        {
            _editingNote = null;
            IsEditing = false;
            IsLocked = false;
            ReturnReason = null;
            ClearNoteFields();
        }

        // Offered whenever it would do something: a note is loaded, or there is
        // typing to drop. Withheld while the compliance dialog is up, so Escape
        // cannot reset the note out from under a decision the case manager is
        // being asked to make.
        private bool CanStartNewNote() =>
            (IsEditing || HasUnsavedChanges) && !IsComplianceDialogVisible;

        /// <summary>
        /// The one way back to a blank New Note, from a button and from Escape in
        /// both hosts. Hosts clear their own grid selection off
        /// <see cref="EditorCleared"/> — the module does not know about grids.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanStartNewNote))]
        private void StartNewNote()
        {
            if (!TryReleaseDraft())
                return;

            ReturnToNewNote();
            EditorCleared?.Invoke(this, EventArgs.Empty);
        }

        private bool CanToggleLock() => IsEditing;

        // Unlock is free. Lock is not: it puts the SAVED record back on screen, so
        // anything typed since the unlock would vanish behind a panel that then
        // claims to be showing what is stored. Confirm before that happens.
        [RelayCommand(CanExecute = nameof(CanToggleLock))]
        private void ToggleLock()
        {
            if (IsLocked)
            {
                IsLocked = false;

                // Unlocking is the moment a stale copy starts to cost something.
                // Reading an old version is a nuisance; editing one means either
                // overwriting somebody else's change or losing a finished narrative
                // to a conflict at the very end. So the check happens here, when
                // the case manager commits to editing, rather than on a timer that
                // would poll the server for an uncommon event. It runs BEHIND the
                // unlock: a Demo round trip can take seconds and the panel must not
                // sit frozen waiting for one.
                _ = VerifyLoadedNoteIsCurrentAsync();
                return;
            }

            if (!TryReleaseDraft())
                return;

            if (_editingNote is Note note)
                LoadNote(note, locked: true);
        }

        [RelayCommand]
        private void CopyNarrative()
        {
            if (string.IsNullOrEmpty(Narrative))
                return;

            try
            {
                Clipboard.SetText(Narrative);
            }
            catch (ExternalException)
            {
                // The clipboard is held by another process. Nothing to recover.
            }
        }

        // Locked is a read-only view of a saved record, so the save path is closed
        // at the command as well as hidden in the view — the button is not the gate.
        private bool CanSubmitNote() => IsUnlocked;

        [RelayCommand(CanExecute = nameof(CanSubmitNote))]
        private async Task SubmitNote()
        {
            // An undated Reminder keeps the established journal-entry path. A
            // future-dated Reminder is deliberately a scheduled Notes row so the
            // calendar can retrieve it; it continues through the normal save path.
            if (IsJournalReminder)
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
                (VisitAppearanceOptions.Any(option =>
                     option.IsSelected && option.Value == VisitAppearance.ConcernObserved) ||
                 VisitSafetyObservationOptions.Any(option =>
                     option.IsSelected && option.Value == VisitSafetyObservation.ConcernObserved)) &&
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
                        SelectedNoteType == NoteType.Form ? SelectedFormType : null,
                        ComplianceRequirements);

                    // Window check is keyed to the NOTE's date, not today.
                    // EventDate is non-null here — validated above.
                    var windowReasons = SelectedPerson!.EvaluateBillingWindow(
                        EventDate!.Value, ComplianceRequirements);

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
                    personId, result.Journal));
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
            if (wasEdit)
            {
                var note = _editingNote!;
                var selectedPerson = SelectedPerson!;
                var previousPersonId = note.PersonId;
                var previousPerson = note.Person;
                note.Narrative = Narrative!;
                note.EventDate = EventDate;
                note.Minutes = Minutes ?? 0;
                note.StartTime = SelectedStartTime?.Minutes;
                note.Status = Status;
                note.NoteType = SelectedNoteType;
                note.FormType = SelectedFormType;
                note.VisitDocumentation = BuildVisitDocumentation();
                note.PersonId = selectedPerson.Id;
                if (caseManagerJustification is not null)
                    note.CaseManagerJustification = caseManagerJustification;

                try
                {
                    await _noteService.UpdateNoteAsync(note);
                }
                catch (NoteConcurrencyException)
                {
                    note.PersonId = previousPersonId;
                    note.Person = previousPerson;
                    await ReconcileNoteConflictAsync(note);
                    return;
                }
                catch
                {
                    note.PersonId = previousPersonId;
                    note.Person = previousPerson;
                    throw;
                }
                note.Person = selectedPerson;
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
            }

            // Same shape as the New Note button: the note goes, the client stays,
            // so the next note for this person needs no re-selection.
            ReturnToNewNote();

            NoteSaved?.Invoke(this, EventArgs.Empty);
        }

        // There is no single-note read on INoteService, so the caseload read is
        // filtered — the same route the save-time reconcile has always used.
        private async Task<Note?> FindLatestAsync(Note note) =>
            (await _noteService.GetAllByPersonAsync(note.PersonId))
                .SingleOrDefault(candidate => candidate.Id == note.Id);

        /// <summary>
        /// Confirms that the note on screen is still what the server holds, and
        /// says so if it is not. Fire-and-forget from the unlock; awaited directly
        /// by tests.
        /// </summary>
        /// <remarks>
        /// This does not replace the concurrency check on save — that one is
        /// authoritative and catches a change made in the seconds after this ran.
        /// This exists so the case manager finds out BEFORE writing a narrative
        /// against a copy that is already out of date, rather than after.
        /// </remarks>
        internal async Task VerifyLoadedNoteIsCurrentAsync()
        {
            if (_editingNote is not Note shown)
                return;

            var identity = _freshnessChecks.Begin();
            Note? latest;
            try
            {
                latest = await FindLatestAsync(shown);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Swallowed on purpose: a background check that cannot reach the
                // server is not a reason to interrupt the edit, and the save path
                // still refuses a stale write. The message carries no exception
                // detail — nothing about a note belongs in one.
                if (IsStillShowing(shown, identity))
                    StaleNoteMessage =
                        "Sati could not check whether this is still the current version of this note. " +
                        "It will be checked again when you save.";
                return;
            }

            if (!IsStillShowing(shown, identity))
                return;

            if (latest is null)
            {
                StaleNoteMessage =
                    "This note is no longer on the server — it may have been removed. " +
                    "Copy anything you still need; saving will not bring it back.";
                return;
            }

            if (latest.Revision == shown.Revision)
                return;

            var differences = GetDifferingNoteFields(shown, latest);
            var summary = differences.Count == 0
                ? "Only its revision moved; the editable fields still match what you were shown."
                : $"It differs in: {string.Join(", ", differences)}.";

            // Nothing has been typed yet, so showing the current version costs the
            // case manager nothing and starts the edit from the record as it now
            // stands — including a supervisor's return reason, which is the most
            // likely thing to have changed under a note being opened for editing.
            if (!HasUnsavedChanges)
            {
                LoadNote(latest, locked: false);
                StaleNoteMessage =
                    $"This note changed after you opened it. {summary} " +
                    "The panel now shows the current version.";
                return;
            }

            StaleNoteMessage =
                $"This note changed after you opened it. {summary} " +
                "Your unsaved changes are still on screen and were not replaced — " +
                "review them against the saved copy before saving.";
        }

        // A slow reply for a note the panel has since moved off must not publish
        // into shared UI state.
        private bool IsStillShowing(Note note, int request) =>
            _freshnessChecks.IsCurrent(request) && ReferenceEquals(_editingNote, note);

        private async Task ReconcileNoteConflictAsync(Note draft)
        {
            var latest = await FindLatestAsync(draft);
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
            _suggestedFollowUpAccepted = false;
            NotifySuggestedFollowUpChanged();

            _freshnessChecks.Invalidate();
            StaleNoteMessage = null;

            // Last, because the assignments above each mark the panel dirty.
            HasUnsavedChanges = false;
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
            _editingNote = null;
            _pendingVisitDocumentation = null;
            IsEditing = false;
            IsLocked = false;
            ReturnReason = null;
            SetSelectedPersonWithoutEffects(null);
            RefreshSuggestedFollowUp(null, resetAcceptance: true);
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
        string? journal) : EventArgs
    {
        public int PersonId { get; } = personId;
        public string? Journal { get; } = journal;
    }

    public sealed class NoteReassignmentConfirmationEventArgs(
        string previousClientName,
        string newClientName) : EventArgs
    {
        public string PreviousClientName { get; } = previousClientName;
        public string NewClientName { get; } = newClientName;
        public string Message { get; } =
            $"Are you sure you want to reassign this note from {previousClientName} to {newClientName}?";
        public bool Confirmed { get; set; }
    }
}
