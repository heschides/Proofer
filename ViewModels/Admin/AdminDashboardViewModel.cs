using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;

namespace Sati.ViewModels.Admin;

public partial class AdminDashboardViewModel(
    IAdminService adminService,
    ISessionService sessionService,
    IPersonService? personService = null) : ObservableObject
{
    private CancellationTokenSource? _historyCancellation;

    public ObservableCollection<AdminActivityRow> RecentActivity { get; } = [];
    public ObservableCollection<AdminPersonListItemDto> People { get; } = [];
    public ObservableCollection<PersonVersionDto> PersonHistory { get; } = [];
    public ObservableCollection<IncidentGroupDto> Incidents { get; } = [];
    public ObservableCollection<IncidentGroupDto> FilteredIncidents { get; } = [];
    public IReadOnlyList<string> IncidentStatusFilters { get; } = ["All statuses", "Open", "Reopened", "Investigating", "Resolved"];
    public IReadOnlyList<string> IncidentSeverityFilters { get; } = ["All severities", "Critical", "Error", "Warning"];
    public IReadOnlyList<string> IncidentStatuses { get; } = ["Open", "Investigating", "Resolved"];

    [ObservableProperty] private AdminOverviewDto? overview;
    [ObservableProperty] private AdminOperationsDto? operations;
    [ObservableProperty] private IncidentHealthScoreDto? health;
    [ObservableProperty] private string auditExportReason = "Internal compliance review";
    [ObservableProperty] private string incidentSearch = string.Empty;
    [ObservableProperty] private string incidentStatusFilter = "All statuses";
    [ObservableProperty] private string incidentSeverityFilter = "All severities";
    [ObservableProperty] private IncidentGroupDto? selectedIncident;
    [ObservableProperty] private string selectedIncidentStatus = "Investigating";
    [ObservableProperty] private AdminPersonListItemDto? selectedPerson;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string noticeMessage = string.Empty;
    [ObservableProperty] private DateTime? lastRefreshedAt;
    [ObservableProperty] private string consumerDeletionReason = string.Empty;
    [ObservableProperty] private string selectedTargetStatus = PersonStatusRules.NoLongerServed;
    [ObservableProperty] private string statusChangeNote = string.Empty;

    public IReadOnlyList<string> StatusChoices { get; } = PersonStatusRules.AllStatuses;

    public bool HasError => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool HasNotice => !string.IsNullOrWhiteSpace(NoticeMessage);
    public bool HasSelectedPerson => SelectedPerson is not null;
    public bool HasHistory => PersonHistory.Count > 0;
    public string LastRefreshedLabel => LastRefreshedAt is null
        ? "Not loaded"
        : $"Updated {LastRefreshedAt:MMM d, h:mm tt}";

    /// <summary>
    /// Explains why neither destructive-deletion action is available for the selected Person,
    /// so an Admin never has to guess why a delete button won't respond. Null when at least one
    /// path applies — the corresponding section explains itself in that case.
    /// </summary>
    public string? NoDeletionPathAvailableReason
    {
        get
        {
            if (SelectedPerson is null)
                return null;
            if (SelectedPerson.IsTestData || IsSelectedPersonWithinDeletionWindow)
                return null;

            // CreatedAtUtc == default means this record predates Person.CreatedAtUtc tracking —
            // backfilled far in the past by design (HANDOFF_CLIENT_DELETION_POLICY.md: never a
            // guessed real date), not a consumer that was genuinely created that long ago. Waiting
            // does not change this outcome the way it would for a record whose window merely
            // expired; the two read very differently to an Admin trying to understand what to do.
            var windowExplanation = SelectedPerson.CreatedAtUtc == default
                ? "was created before Sati started tracking consumer creation dates, so it can never " +
                  "be evaluated against the 20-day window"
                : $"was created more than {ConsumerDeletionRules.DeletionWindowDays} days ago";
            return $"This consumer is not marked as test data and {windowExplanation}, so neither " +
                   "delete tool on this screen applies — see HANDOFF_CLIENT_DELETION_POLICY.md. " +
                   "Use the Status control above to archive it instead: that takes it off the " +
                   "active caseload without deleting anything, and has no window restriction.";
        }
    }

    // Purely a display hint — CanDeleteConsumerInWindow re-derives the same check, and the
    // service re-derives it again server-side. See CLAUDE.md: UI visibility is not security.
    public bool IsSelectedPersonWithinDeletionWindow =>
        SelectedPerson is not null &&
        ConsumerDeletionRules.IsWithinDeletionWindow(SelectedPerson.CreatedAtUtc, DateTime.UtcNow);

    public event EventHandler<AdminPdfReadyEventArgs>? PdfReady;
    public event EventHandler<AdminCsvReadyEventArgs>? CsvReady;
    public event EventHandler<AdminTestConsumerDeletionConfirmationEventArgs>? TestConsumerDeletionConfirmationRequested;
    public event EventHandler<AdminConsumerDeletionConfirmationEventArgs>? ConsumerDeletionConfirmationRequested;

    public async Task InitializeAsync()
    {
        if (sessionService.CurrentUser?.HasAdminPermissions != true)
        {
            StatusMessage = "Only an Admin can open this dashboard.";
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = string.Empty;
        NoticeMessage = string.Empty;
        var selectedId = SelectedPerson?.PersonId;
        try
        {
            var overviewTask = adminService.GetOverviewAsync();
            var peopleTask = adminService.GetPeopleAsync();
            var activityTask = adminService.GetActivityAsync(30, 150);
            var operationsTask = adminService.GetOperationsAsync();
            var incidentsTask = adminService.GetIncidentsAsync();
            await Task.WhenAll(overviewTask, peopleTask, activityTask, operationsTask, incidentsTask);

            Overview = await overviewTask;
            Operations = await operationsTask;
            var incidentDashboard = await incidentsTask;
            Health = incidentDashboard.Health;
            Replace(Incidents, incidentDashboard.Incidents);
            ApplyIncidentFilter();
            Replace(People, await peopleTask);
            Replace(RecentActivity, (await activityTask).Select(item => new AdminActivityRow(item)));
            LastRefreshedAt = DateTime.Now;

            SelectedPerson = selectedId is int id
                ? People.FirstOrDefault(person => person.PersonId == id)
                : People.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"The Admin dashboard could not be loaded. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedPersonChanged(AdminPersonListItemDto? value)
    {
        OnPropertyChanged(nameof(HasSelectedPerson));
        OnPropertyChanged(nameof(IsSelectedPersonWithinDeletionWindow));
        OnPropertyChanged(nameof(NoDeletionPathAvailableReason));
        ExportPersonAuditPdfCommand.NotifyCanExecuteChanged();
        DeleteTestConsumerCommand.NotifyCanExecuteChanged();
        DeleteConsumerInWindowCommand.NotifyCanExecuteChanged();
        SetPersonStatusCommand.NotifyCanExecuteChanged();
        ConsumerDeletionReason = string.Empty;
        _historyCancellation?.Cancel();
        _historyCancellation?.Dispose();
        _historyCancellation = new CancellationTokenSource();
        _ = LoadHistoryAsync(value, _historyCancellation.Token);
    }

    partial void OnIsBusyChanged(bool value)
    {
        ExportPersonAuditPdfCommand.NotifyCanExecuteChanged();
        ExportAuditCsvCommand.NotifyCanExecuteChanged();
        UpdateIncidentStatusCommand.NotifyCanExecuteChanged();
        DeleteTestConsumerCommand.NotifyCanExecuteChanged();
        DeleteConsumerInWindowCommand.NotifyCanExecuteChanged();
        SetPersonStatusCommand.NotifyCanExecuteChanged();
    }

    partial void OnConsumerDeletionReasonChanged(string value) =>
        DeleteConsumerInWindowCommand.NotifyCanExecuteChanged();

    partial void OnAuditExportReasonChanged(string value) =>
        ExportAuditCsvCommand.NotifyCanExecuteChanged();
    partial void OnIncidentSearchChanged(string value) => ApplyIncidentFilter();
    partial void OnIncidentStatusFilterChanged(string value) => ApplyIncidentFilter();
    partial void OnIncidentSeverityFilterChanged(string value) => ApplyIncidentFilter();
    partial void OnSelectedIncidentChanged(IncidentGroupDto? value)
    {
        if (value is not null)
            SelectedIncidentStatus = value.Status == "Reopened" ? "Investigating" : value.Status;
        UpdateIncidentStatusCommand.NotifyCanExecuteChanged();
    }
    partial void OnSelectedIncidentStatusChanged(string value) =>
        UpdateIncidentStatusCommand.NotifyCanExecuteChanged();
    partial void OnStatusMessageChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnNoticeMessageChanged(string value) => OnPropertyChanged(nameof(HasNotice));
    partial void OnLastRefreshedAtChanged(DateTime? value) => OnPropertyChanged(nameof(LastRefreshedLabel));

    private bool CanUpdateIncidentStatus() =>
        !IsBusy && SelectedIncident is not null &&
        IncidentStatuses.Contains(SelectedIncidentStatus) &&
        SelectedIncident.Status != SelectedIncidentStatus;

    [RelayCommand(CanExecute = nameof(CanUpdateIncidentStatus))]
    private async Task UpdateIncidentStatusAsync()
    {
        if (SelectedIncident is null)
            return;
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var selectedId = SelectedIncident.Id;
            await adminService.UpdateIncidentStatusAsync(selectedId, SelectedIncidentStatus);
            var dashboard = await adminService.GetIncidentsAsync();
            Health = dashboard.Health;
            Replace(Incidents, dashboard.Incidents);
            ApplyIncidentFilter();
            SelectedIncident = FilteredIncidents.FirstOrDefault(item => item.Id == selectedId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"The incident status could not be updated. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyIncidentFilter()
    {
        var search = IncidentSearch?.Trim() ?? string.Empty;
        var filtered = Incidents.Where(item =>
            (IncidentStatusFilter == "All statuses" || item.Status == IncidentStatusFilter) &&
            (IncidentSeverityFilter == "All severities" || item.Severity == IncidentSeverityFilter) &&
            (search.Length == 0 ||
             item.Operation.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             item.LastReference.Contains(search, StringComparison.OrdinalIgnoreCase) ||
             item.LastRelease.Contains(search, StringComparison.OrdinalIgnoreCase)));
        Replace(FilteredIncidents, filtered);
    }

    private async Task LoadHistoryAsync(
        AdminPersonListItemDto? person,
        CancellationToken cancellationToken)
    {
        PersonHistory.Clear();
        OnPropertyChanged(nameof(HasHistory));
        if (person is null)
            return;

        try
        {
            var history = await adminService.GetPersonHistoryAsync(person.PersonId, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;
            Replace(PersonHistory, history);
            OnPropertyChanged(nameof(HasHistory));
        }
        catch (OperationCanceledException)
        {
            // A different Person was selected before this request completed.
        }
        catch (Exception ex)
        {
            StatusMessage = $"The Person history could not be loaded. {ex.Message}";
        }
    }

    private bool CanExportPersonAuditPdf() => SelectedPerson is not null && !IsBusy;

    private bool CanDeleteTestConsumer() =>
        SelectedPerson?.IsTestData == true && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanDeleteTestConsumer))]
    private async Task DeleteTestConsumerAsync()
    {
        var person = SelectedPerson;
        if (person is null)
            return;

        var confirmation = new AdminTestConsumerDeletionConfirmationEventArgs(
            person.PersonId,
            person.DisplayName,
            TestDataDeletionRules.ConsumerConfirmationText);
        TestConsumerDeletionConfirmationRequested?.Invoke(this, confirmation);
        if (!confirmation.Confirmed)
            return;

        IsBusy = true;
        StatusMessage = string.Empty;
        NoticeMessage = string.Empty;
        TestConsumerDeletionResultDto? result = null;
        try
        {
            _historyCancellation?.Cancel();
            result = await adminService.DeleteTestConsumerAsync(
                person.PersonId,
                person.Revision,
                TestDataDeletionRules.ConsumerAttestation);
        }
        catch (Exception ex)
        {
            StatusMessage = $"The consumer was not deleted. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (result is null)
            return;

        await RefreshAsync();
        if (!HasError)
        {
            NoticeMessage = result.RelatedRecordsDeleted == 1
                ? $"Deleted test consumer {person.DisplayName} and 1 related test record. The audit event was retained."
                : $"Deleted test consumer {person.DisplayName} and {result.RelatedRecordsDeleted} related test records. The audit event was retained.";
        }
    }

    private bool CanDeleteConsumerInWindow() =>
        SelectedPerson is not null && !IsBusy &&
        ConsumerDeletionRules.IsWithinDeletionWindow(SelectedPerson.CreatedAtUtc, DateTime.UtcNow);

    /// <summary>
    /// Rule-3 deletion: permanently deletes an ordinary consumer created within the window.
    /// Unlike <see cref="DeleteTestConsumerAsync"/>, this needs no creation-time marker — it is
    /// bounded by time and by the billing-integrity and legal-hold gates the service enforces.
    /// See HANDOFF_CLIENT_DELETION_POLICY.md.
    /// </summary>
    /// <remarks>
    /// The reason requirement is enforced here, on click, rather than through CanExecute. A
    /// button that silently disables itself when a text field is empty looks broken with no
    /// way to tell why — CLAUDE.md's "Working with Josh" asks for direct, legible feedback, not
    /// a dead control. Clicking with no reason yielded exactly a report of "the delete button
    /// wasn't working" the first time this shipped.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanDeleteConsumerInWindow))]
    private async Task DeleteConsumerInWindowAsync()
    {
        var person = SelectedPerson;
        if (person is null)
            return;

        if (string.IsNullOrWhiteSpace(ConsumerDeletionReason))
        {
            StatusMessage = "Enter a reason before deleting this consumer.";
            return;
        }

        var confirmation = new AdminConsumerDeletionConfirmationEventArgs(
            person.PersonId,
            person.DisplayName,
            "This permanently deletes this consumer and everything attached to their record — " +
            "notes, forms, reviews, assessments, AT requests, contacts, and any draft billing. " +
            "This cannot be undone.",
            $"Type \"{person.DisplayName}\" to confirm.",
            person.DisplayName);
        ConsumerDeletionConfirmationRequested?.Invoke(this, confirmation);
        if (!confirmation.Confirmed)
            return;

        IsBusy = true;
        StatusMessage = string.Empty;
        NoticeMessage = string.Empty;
        ConsumerDeletionResultDto? result = null;
        try
        {
            _historyCancellation?.Cancel();
            result = await adminService.DeleteConsumerInWindowAsync(
                person.PersonId,
                person.Revision,
                ConsumerDeletionRules.ConsumerAttestation,
                ConsumerDeletionReason);
        }
        catch (Exception ex)
        {
            StatusMessage = $"The consumer was not deleted. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (result is null)
            return;

        ConsumerDeletionReason = string.Empty;
        await RefreshAsync();
        if (!HasError)
        {
            NoticeMessage = result.RelatedRecordsDeleted == 1
                ? $"Deleted {person.DisplayName} and 1 related record. The audit event was retained."
                : $"Deleted {person.DisplayName} and {result.RelatedRecordsDeleted} related records. The audit event was retained.";
        }
    }

    // An Admin may set any status on any consumer in the agency — PersonStatusRules grants that
    // regardless of caseload ownership. This is the only path that reaches a consumer outside the
    // rule-3 window and not marked test data: neither delete tool applies once a record predates
    // Person.CreatedAtUtc tracking (backfilled far in the past, permanently outside any window),
    // but archiving it — taking it off the active caseload — has never needed the window at all.
    private bool CanSetPersonStatus() =>
        personService is not null && SelectedPerson is not null && !IsBusy &&
        !string.Equals(SelectedPerson.Status, SelectedTargetStatus, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanSetPersonStatus))]
    private async Task SetPersonStatusAsync()
    {
        var person = SelectedPerson;
        if (person is null || personService is null)
            return;

        IsBusy = true;
        StatusMessage = string.Empty;
        NoticeMessage = string.Empty;
        var fromStatus = person.Status;
        var toStatus = SelectedTargetStatus;
        try
        {
            _historyCancellation?.Cancel();
            await personService.SetPersonStatusAsync(
                person.PersonId,
                toStatus,
                string.IsNullOrWhiteSpace(StatusChangeNote) ? null : StatusChangeNote,
                person.Revision);
        }
        catch (Exception ex)
        {
            StatusMessage = $"The status was not changed. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (HasError)
            return;

        StatusChangeNote = string.Empty;
        await RefreshAsync();
        if (!HasError)
            NoticeMessage = $"{person.DisplayName} moved from {fromStatus} to {toStatus}.";
    }

    partial void OnSelectedTargetStatusChanged(string value) =>
        SetPersonStatusCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanExportPersonAuditPdf))]
    private async Task ExportPersonAuditPdfAsync()
    {
        if (SelectedPerson is null)
            return;

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var pdf = await adminService.ExportPersonHistoryPdfAsync(SelectedPerson.PersonId);
            var safeName = SafeFileName(SelectedPerson.DisplayName);
            PdfReady?.Invoke(this, new AdminPdfReadyEventArgs(
                pdf,
                $"person-{SelectedPerson.PersonId}-{safeName}-lifecycle-audit.pdf"));
            Replace(RecentActivity,
                (await adminService.GetActivityAsync(30, 150)).Select(item => new AdminActivityRow(item)));
            Overview = await adminService.GetOverviewAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"The audit PDF could not be created. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExportAuditCsv() =>
        !IsBusy && (AuditExportReason?.Trim().Length ?? 0) is >= 10 and <= 250;

    [RelayCommand(CanExecute = nameof(CanExportAuditCsv))]
    private async Task ExportAuditCsvAsync()
    {
        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var toUtc = DateTime.UtcNow;
            var fromUtc = toUtc.AddDays(-30);
            var csv = await adminService.ExportAuditCsvAsync(
                fromUtc,
                toUtc,
                AuditExportReason.Trim());
            CsvReady?.Invoke(this, new AdminCsvReadyEventArgs(
                csv,
                $"sati-audit-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv"));
            var activityTask = adminService.GetActivityAsync(30, 150);
            var overviewTask = adminService.GetOverviewAsync();
            var operationsTask = adminService.GetOperationsAsync();
            await Task.WhenAll(activityTask, overviewTask, operationsTask);
            Replace(RecentActivity, (await activityTask).Select(item => new AdminActivityRow(item)));
            Overview = await overviewTask;
            Operations = await operationsTask;
        }
        catch (Exception ex)
        {
            StatusMessage = $"The audit activity export could not be created. {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
    private static string SafeFileName(string value)
    {
        var safe = new string(value.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-').ToArray());
        while (safe.Contains("--", StringComparison.Ordinal))
            safe = safe.Replace("--", "-", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(safe.Trim('-')) ? "person" : safe.Trim('-');
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
            target.Add(value);
    }
}

public sealed class AdminActivityRow(AdminActivityDto activity)
{
    public long Id => activity.Id;
    public string ActorDisplayName => activity.ActorDisplayName;
    public string ActionLabel => activity.Action switch
    {
        "authentication.succeeded" => "Signed in",
        "person.created" => "Created Person",
        "person.updated" => "Updated Person",
        "person.journal-updated" => "Updated Person journal",
        "person-history.viewed" => "Viewed Person history",
        "person-history-pdf.generated" => "Exported Person audit PDF",
        "test-data.consumer-deleted" => "Deleted test consumer",
        "audit.exported" => "Exported audit activity",
        "user.created" => "Created user",
        "user.updated" => "Updated user",
        "user.password-reset" => "Reset user password",
        "user.password-changed" => "Changed password",
        "note.approved" => "Approved note",
        "note.approval-overridden" => "Approved note with override",
        "note.returned" => "Returned note",
        "assessment.created" => "Created assessment",
        "assessment.updated" => "Updated assessment",
        "assessment.submitted" => "Submitted assessment",
        "settings.updated" => "Updated settings",
        "scratchpad.updated" => "Updated scratchpad",
        "billing-period.submitted" => "Submitted billing period",
        "billing-claim-line.created" => "Created claim line",
        "billing-edi.generated" => "Generated EDI file",
        _ => activity.Action.Replace('-', ' ').Replace('.', ' ')
    };
    public string ResourceLabel => string.IsNullOrWhiteSpace(activity.ResourceId)
        ? activity.ResourceType
        : $"{activity.ResourceType} #{activity.ResourceId}";
    public DateTime OccurredAtLocal => activity.OccurredAtUtc.ToLocalTime();
    public string CorrelationId => activity.CorrelationId;
}

public sealed class AdminCsvReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}
public sealed class AdminPdfReadyEventArgs(byte[] content, string suggestedFileName) : EventArgs
{
    public byte[] Content { get; } = content;
    public string SuggestedFileName { get; } = suggestedFileName;
}

public sealed class AdminTestConsumerDeletionConfirmationEventArgs(
    int personId,
    string displayName,
    string message) : EventArgs
{
    public int PersonId { get; } = personId;
    public string DisplayName { get; } = displayName;
    public string Message { get; } = message;
    public bool Confirmed { get; set; }
}

public sealed class AdminConsumerDeletionConfirmationEventArgs(
    int personId,
    string displayName,
    string message,
    string prompt,
    string requiredConfirmationText) : EventArgs
{
    public int PersonId { get; } = personId;
    public string DisplayName { get; } = displayName;
    public string Message { get; } = message;
    public string Prompt { get; } = prompt;
    public string RequiredConfirmationText { get; } = requiredConfirmationText;
    public bool Confirmed { get; set; }
}
