using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

using Sati.Services;

namespace Sati.ViewModels.Children
{
    public partial class ScratchpadViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly IScratchpadService _scratchpadService;
        private readonly ISessionService _sessionService;
        private readonly IWorkAgendaService? _workAgendaService;

        private Scratchpad? _scratchpad;
        private Scratchpad? _tomorrowAgenda;
        private DispatcherTimer? _scratchpadTimer;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private readonly LatestRequestTracker _scheduledWorkLoads = new();
        private string _lastSavedScratchpadContent = string.Empty;
        private string _lastSavedTomorrowAgendaContent = string.Empty;
        private bool _sessionExpiredDuringSave;
        private int? _loadedUserId;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ScratchpadViewModel(
            IScratchpadService scratchpadService,
            ISessionService sessionService,
            IWorkAgendaService? workAgendaService = null)
        {
            _scratchpadService = scratchpadService;
            _sessionService = sessionService;
            _workAgendaService = workAgendaService;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler? OpenScratchpadHistoryRequested;

        // The shell supplies the host action so this child never reaches into a
        // parent ViewModel or creates a View. Awaiting it through an async command
        // also keeps navigation failures out of an async-void event handler.
        public Func<WorkAgendaItem, Task>? ScheduledWorkOpeningAsync { get; set; }

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private string scratchpadContent = string.Empty;
        [ObservableProperty] private string tomorrowAgendaContent = string.Empty;
        [ObservableProperty] private string tomorrowAgendaDateLabel = "Next workday";
        [ObservableProperty] private double scratchpadFontSize = 14;
        [ObservableProperty] private bool hasScratchpadConflict;
        [ObservableProperty] private string scratchpadConflictMessage = string.Empty;
        [ObservableProperty] private bool hasTomorrowAgendaConflict;
        [ObservableProperty] private string tomorrowAgendaConflictMessage = string.Empty;
        [ObservableProperty] private bool hasScratchpadLoadError;
        [ObservableProperty] private string scratchpadLoadErrorMessage = string.Empty;
        [ObservableProperty] private bool hasTomorrowAgendaLoadError;
        [ObservableProperty] private string tomorrowAgendaLoadErrorMessage = string.Empty;
        [ObservableProperty] private bool hasScratchpadSessionExpired;
        [ObservableProperty] private string scratchpadSessionExpiredMessage = string.Empty;
        [ObservableProperty] private bool hasScheduledWorkLoadError;
        [ObservableProperty] private string scheduledWorkLoadErrorMessage = string.Empty;
        [ObservableProperty] private bool isScheduledWorkBusy;

        public ObservableCollection<WorkAgendaItem> PaperworkItems { get; } = [];
        public ObservableCollection<WorkAgendaItem> VisitItems { get; } = [];
        public ObservableCollection<WorkAgendaItem> CallItems { get; } = [];
        public ObservableCollection<WorkAgendaItem> EmailItems { get; } = [];
        public ObservableCollection<WorkAgendaItem> FreeformItems { get; } = [];

        public bool HasPaperworkItems => PaperworkItems.Count > 0;
        public bool HasVisitItems => VisitItems.Count > 0;
        public bool HasCallItems => CallItems.Count > 0;
        public bool HasEmailItems => EmailItems.Count > 0;
        public bool HasFreeformItems => FreeformItems.Count > 0;
        public int ScheduledWorkCount => PaperworkItems.Count + VisitItems.Count +
            CallItems.Count + EmailItems.Count + FreeformItems.Count;
        public bool HasScheduledWorkItems => ScheduledWorkCount > 0;
        public string ScheduledWorkSummary => ScheduledWorkCount switch
        {
            0 => "No scheduled work for today.",
            1 => "1 scheduled item · double-click or choose Start.",
            _ => $"{ScheduledWorkCount} scheduled items · double-click or choose Start."
        };

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseScratchpadFont() => ScratchpadFontSize = Math.Min(ScratchpadFontSize + 2, 28);
        [RelayCommand]
        private async Task ReloadLatestScratchpadAsync()
        {
            if (_sessionService.CurrentUser is not { } user)
                return;

            var scratchpadLoaded = await TryLoadTodayAsync(user.Id);
            var workLoaded = await RefreshScheduledWorkAsync(user.Id);
            if (scratchpadLoaded || workLoaded)
                StartScratchpadTimer();
        }

        [RelayCommand]
        private async Task ReloadLatestTomorrowAgendaAsync()
        {
            if (_sessionService.CurrentUser is not { } user)
                return;

            if (await TryLoadTomorrowAsync(user.Id))
                StartScratchpadTimer();
        }

        [RelayCommand] private void DecreaseScratchpadFont() => ScratchpadFontSize = Math.Max(ScratchpadFontSize - 2, 10);
        [RelayCommand] private void OpenScratchpadHistory() => OpenScratchpadHistoryRequested?.Invoke(this, EventArgs.Empty);

        [RelayCommand]
        private async Task OpenScheduledWork(WorkAgendaItem? item)
        {
            if (item is not null && ScheduledWorkOpeningAsync is not null)
                await ScheduledWorkOpeningAsync(item);
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            if (_sessionService.CurrentUser is not { } user)
                return;

            // This ViewModel lives with the shell across account switches. Clear the
            // previous person's drafts before any request for the new person starts;
            // a failed load must never leave another user's scratchpad visible.
            if (_loadedUserId != user.Id)
                ResetForUser(user.Id);

            // Deliberately sequential and independently published. The previous
            // Task.WhenAll implementation made either request all-or-nothing: a
            // failure loading Tomorrow's Agenda discarded a successfully loaded
            // Today's Work result and made saved text look deleted. It also opened
            // two LocalDB readers during the cold-start path.
            var todayLoaded = await TryLoadTodayAsync(user.Id);
            var tomorrowLoaded = await TryLoadTomorrowAsync(user.Id);
            var workLoaded = await RefreshScheduledWorkAsync(user.Id);
            ClearExpiredSessionWarning();
            if (todayLoaded || tomorrowLoaded || workLoaded)
                StartScratchpadTimer();
        }

        // -------------------------------------------------------------------------
        // Public methods
        // -------------------------------------------------------------------------

        public async Task<bool> SaveScratchpadAsync(string content)
        {
            await _saveGate.WaitAsync();
            try
            {
                if (_scratchpad is null)
                    return true;

                ScratchpadContent = content;
                return await SaveTodayCoreAsync();
            }
            finally
            {
                _saveGate.Release();
            }
        }

        public async Task<bool> SaveAllScratchpadsAsync()
        {
            await _saveGate.WaitAsync();
            try
            {
                var todayDirty = IsTodayDirty;
                var tomorrowDirty = IsTomorrowDirty;
                if (!todayDirty && !tomorrowDirty)
                    return true;
                if (_sessionExpiredDuringSave)
                    return false;

                var todaySaved = !todayDirty ||
                    (!HasScratchpadConflict && await SaveTodayCoreAsync());
                if (_sessionExpiredDuringSave)
                    return false;

                var tomorrowSaved = !tomorrowDirty ||
                    (!HasTomorrowAgendaConflict && await SaveTomorrowCoreAsync());
                return todaySaved && tomorrowSaved;
            }
            finally
            {
                _saveGate.Release();
            }
        }

        public async Task<bool> RollForwardIfNeededAsync()
        {
            if (_scratchpad is null || _scratchpad.Date.Date == DateTime.Today)
                return true;

            // Keep the previous day's visible drafts recoverable before changing
            // which dated rows the two tabs display.
            if (!await SaveAllScratchpadsAsync())
                return false;

            await _saveGate.WaitAsync();
            try
            {
                var userId = _sessionService.CurrentUser!.Id;
                var todayTask = _scratchpadService.LoadTodayAsync(userId);
                var tomorrowTask = _scratchpadService.LoadTomorrowAsync(userId);
                await Task.WhenAll(todayTask, tomorrowTask);

                _scratchpad = await todayTask;
                _tomorrowAgenda = await tomorrowTask;
                _lastSavedScratchpadContent = _scratchpad.Content;
                _lastSavedTomorrowAgendaContent = _tomorrowAgenda.Content;
                ScratchpadContent = _scratchpad.Content;
                TomorrowAgendaContent = _tomorrowAgenda.Content;
                TomorrowAgendaDateLabel = FormatAgendaDate(_tomorrowAgenda.Date);
                HasScratchpadConflict = false;
                ScratchpadConflictMessage = string.Empty;
                HasTomorrowAgendaConflict = false;
                TomorrowAgendaConflictMessage = string.Empty;
                await RefreshScheduledWorkAsync(userId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Work agenda rollover failed: {ex.Message}");
                MessageBox.Show(
                    "Sati could not move the work agenda to the new day. Your previous drafts remain visible.",
                    "Agenda Rollover Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                _saveGate.Release();
            }
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

        public async Task<WorkAgendaAddResult> AddDailyAgendaItemsAsync(
            IReadOnlyList<DailyAgendaItem> selectedItems,
            DateOnly agendaDate)
        {
            if (_workAgendaService is null || _sessionService.CurrentUser is not { } user)
                throw new InvalidOperationException("The structured Work Agenda is unavailable.");

            try
            {
                return await _workAgendaService.AddFromDailyAgendaAsync(
                    user.Id,
                    agendaDate.ToDateTime(TimeOnly.MinValue),
                    selectedItems);
            }
            finally
            {
                // A multi-item save can fail after an earlier item committed. A
                // refresh in finally presents the server's actual result and keeps
                // a retry from looking as though nothing happened.
                await RefreshScheduledWorkAsync(user.Id);
            }
        }

        public Task<bool> RefreshScheduledWorkAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            return userId is int value
                ? RefreshScheduledWorkAsync(value)
                : Task.FromResult(false);
        }

        private async Task<bool> RefreshScheduledWorkAsync(int userId)
        {
            var request = _scheduledWorkLoads.Begin();
            if (_workAgendaService is null)
            {
                ReplaceScheduledWork([]);
                return true;
            }

            IsScheduledWorkBusy = true;
            try
            {
                var items = await _workAgendaService.LoadAsync(userId, DateTime.Today);
                if (!_scheduledWorkLoads.IsCurrent(request) ||
                    _sessionService.CurrentUser?.Id != userId)
                {
                    return false;
                }

                ReplaceScheduledWork(items);
                HasScheduledWorkLoadError = false;
                ScheduledWorkLoadErrorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Scheduled Work load failed: {ex.Message}");
                if (!_scheduledWorkLoads.IsCurrent(request) ||
                    _sessionService.CurrentUser?.Id != userId)
                {
                    return false;
                }

                var reference = AppErrorLog.Record(ex, "work-agenda.load.scheduled");
                HasScheduledWorkLoadError = true;
                ScheduledWorkLoadErrorMessage =
                    "Scheduled work could not be loaded. Your freeform text is still available. " +
                    $"Choose Retry. Support reference: {reference}.";
                return false;
            }
            finally
            {
                if (_scheduledWorkLoads.IsCurrent(request))
                    IsScheduledWorkBusy = false;
            }
        }

        private void ReplaceScheduledWork(IEnumerable<WorkAgendaItem> items)
        {
            PaperworkItems.Clear();
            VisitItems.Clear();
            CallItems.Clear();
            EmailItems.Clear();
            FreeformItems.Clear();

            foreach (var item in items)
            {
                CollectionFor(item.Section).Add(item);
            }

            OnPropertyChanged(nameof(HasPaperworkItems));
            OnPropertyChanged(nameof(HasVisitItems));
            OnPropertyChanged(nameof(HasCallItems));
            OnPropertyChanged(nameof(HasEmailItems));
            OnPropertyChanged(nameof(HasFreeformItems));
            OnPropertyChanged(nameof(ScheduledWorkCount));
            OnPropertyChanged(nameof(HasScheduledWorkItems));
            OnPropertyChanged(nameof(ScheduledWorkSummary));
        }

        private ObservableCollection<WorkAgendaItem> CollectionFor(WorkAgendaSection section) =>
            section switch
            {
                WorkAgendaSection.Paperwork => PaperworkItems,
                WorkAgendaSection.Visits => VisitItems,
                WorkAgendaSection.Calls => CallItems,
                WorkAgendaSection.Emails => EmailItems,
                _ => FreeformItems
            };

        private void StartScratchpadTimer()
        {
            _scratchpadTimer?.Stop();
            _scratchpadTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            _scratchpadTimer.Tick += async (s, e) =>
            {
                if (_scratchpad is null && _tomorrowAgenda is null) return;
                if (!await RollForwardIfNeededAsync()) return;
                await SaveAllScratchpadsAsync();
            };
            _scratchpadTimer.Start();
        }

        private async Task<bool> TryLoadTodayAsync(int userId)
        {
            try
            {
                var loaded = await _scratchpadService.LoadTodayAsync(userId);
                _scratchpad = loaded;
                _lastSavedScratchpadContent = loaded.Content;
                ScratchpadContent = loaded.Content;
                HasScratchpadConflict = false;
                ScratchpadConflictMessage = string.Empty;
                HasScratchpadLoadError = false;
                ScratchpadLoadErrorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Today's Work load failed: {ex.Message}");
                var reference = AppErrorLog.Record(ex, "scratchpad.load.today");
                HasScratchpadLoadError = true;
                ScratchpadLoadErrorMessage =
                    "Today's Work could not be loaded. Nothing was replaced or saved. " +
                    $"Choose Retry. Support reference: {reference}.";
                return false;
            }
        }

        private async Task<bool> TryLoadTomorrowAsync(int userId)
        {
            try
            {
                var loaded = await _scratchpadService.LoadTomorrowAsync(userId);
                _tomorrowAgenda = loaded;
                _lastSavedTomorrowAgendaContent = loaded.Content;
                TomorrowAgendaContent = loaded.Content;
                TomorrowAgendaDateLabel = FormatAgendaDate(loaded.Date);
                HasTomorrowAgendaConflict = false;
                TomorrowAgendaConflictMessage = string.Empty;
                HasTomorrowAgendaLoadError = false;
                TomorrowAgendaLoadErrorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tomorrow's Agenda load failed: {ex.Message}");
                var reference = AppErrorLog.Record(ex, "scratchpad.load.tomorrow");
                HasTomorrowAgendaLoadError = true;
                TomorrowAgendaLoadErrorMessage =
                    "Tomorrow's Agenda could not be loaded. Nothing was replaced or saved. " +
                    $"Choose Retry. Support reference: {reference}.";
                return false;
            }
        }

        private void ResetForUser(int userId)
        {
            _scratchpadTimer?.Stop();
            _loadedUserId = userId;
            _scratchpad = null;
            _tomorrowAgenda = null;
            _lastSavedScratchpadContent = string.Empty;
            _lastSavedTomorrowAgendaContent = string.Empty;
            ScratchpadContent = string.Empty;
            TomorrowAgendaContent = string.Empty;
            TomorrowAgendaDateLabel = "Next workday";
            HasScratchpadConflict = false;
            ScratchpadConflictMessage = string.Empty;
            HasTomorrowAgendaConflict = false;
            TomorrowAgendaConflictMessage = string.Empty;
            HasScratchpadLoadError = false;
            ScratchpadLoadErrorMessage = string.Empty;
            HasTomorrowAgendaLoadError = false;
            TomorrowAgendaLoadErrorMessage = string.Empty;
            _scheduledWorkLoads.Invalidate();
            ReplaceScheduledWork([]);
            HasScheduledWorkLoadError = false;
            ScheduledWorkLoadErrorMessage = string.Empty;
            IsScheduledWorkBusy = false;
        }

        private async Task<bool> SaveTodayCoreAsync()
        {
            if (_scratchpad is null)
                return true;
            if (!IsTodayDirty)
                return true;

            _scratchpad.Content = ScratchpadContent;
            try
            {
                await _scratchpadService.SaveAsync(_scratchpad);
                _lastSavedScratchpadContent = ScratchpadContent;
                HasScratchpadConflict = false;
                ScratchpadConflictMessage = string.Empty;
                return true;
            }
            catch (ScratchpadConcurrencyException ex)
            {
                _scratchpadTimer?.Stop();
                HasScratchpadConflict = true;
                ScratchpadConflictMessage = ConflictMessage;
                ShowConflict(ex, "Today's Work");
                return false;
            }
            catch (ScratchpadSessionExpiredException ex)
            {
                HandleExpiredSession(ex);
                return false;
            }
            catch (ScratchpadSaveException ex)
            {
                ShowSaveError(ex, "Today's Work", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                ShowSaveError(ex, "Today's Work");
                return false;
            }
        }

        private async Task<bool> SaveTomorrowCoreAsync()
        {
            if (_tomorrowAgenda is null)
                return true;
            if (!IsTomorrowDirty)
                return true;

            _tomorrowAgenda.Content = TomorrowAgendaContent;
            try
            {
                await _scratchpadService.SaveAsync(_tomorrowAgenda);
                _lastSavedTomorrowAgendaContent = TomorrowAgendaContent;
                HasTomorrowAgendaConflict = false;
                TomorrowAgendaConflictMessage = string.Empty;
                return true;
            }
            catch (ScratchpadConcurrencyException ex)
            {
                _scratchpadTimer?.Stop();
                HasTomorrowAgendaConflict = true;
                TomorrowAgendaConflictMessage = ConflictMessage;
                ShowConflict(ex, "Tomorrow's Agenda");
                return false;
            }
            catch (ScratchpadSessionExpiredException ex)
            {
                HandleExpiredSession(ex);
                return false;
            }
            catch (ScratchpadSaveException ex)
            {
                ShowSaveError(ex, "Tomorrow's Agenda", ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                ShowSaveError(ex, "Tomorrow's Agenda");
                return false;
            }
        }

        private static string FormatAgendaDate(DateTime date) =>
            $"Next workday · {date:ddd, MMM d}";

        private bool IsTodayDirty =>
            _scratchpad is not null && ScratchpadContent != _lastSavedScratchpadContent;

        private bool IsTomorrowDirty =>
            _tomorrowAgenda is not null && TomorrowAgendaContent != _lastSavedTomorrowAgendaContent;

        private void HandleExpiredSession(ScratchpadSessionExpiredException ex)
        {
            Debug.WriteLine($"Scratchpad save paused after session expiry: {ex.Message}");
            _scratchpadTimer?.Stop();
            _sessionExpiredDuringSave = true;
            HasScratchpadSessionExpired = true;
            ScratchpadSessionExpiredMessage =
                "Your Demo session expired. Your unsaved agenda text remains here. " +
                "Sign in again when prompted and it will save.";
        }

        /// <summary>
        /// Resumes autosave after the user signs back in as the same person.
        ///
        /// Deliberately reloads nothing. The visible drafts are the newer text — the
        /// whole reason the save was paused rather than abandoned — so replacing them
        /// with what the server last stored would discard exactly what was being
        /// protected. A different person signing in is an account switch, which
        /// reinitializes through its own path.
        /// </summary>
        public void ResumeAfterReauthentication()
        {
            ClearExpiredSessionWarning();
            StartScratchpadTimer();
        }

        private void ClearExpiredSessionWarning()
        {
            _sessionExpiredDuringSave = false;
            HasScratchpadSessionExpired = false;
            ScratchpadSessionExpiredMessage = string.Empty;
        }

        private static void ShowConflict(Exception ex, string agendaName)
        {
            Debug.WriteLine($"{agendaName} save conflict: {ex.Message}");
            MessageBox.Show(ex.Message, $"{agendaName} Changed Elsewhere",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void ShowSaveError(
            Exception ex,
            string agendaName,
            string? safeRecoveryDetail = null)
        {
            Debug.WriteLine($"{agendaName} save failed: {ex.Message}");
            var reference = AppErrorLog.Record(
                ex,
                agendaName == "Today's Work"
                    ? "scratchpad.save.today"
                    : "scratchpad.save.tomorrow");
            MessageBox.Show(
                $"Sati could not save {agendaName}. Your text is still visible, but Sati could not confirm the save.\n\n" +
                $"Technical code: {ex.GetType().Name} (0x{ex.HResult:X8})\n" +
                $"Support reference: {reference}\n\n" +
                (string.IsNullOrWhiteSpace(safeRecoveryDetail)
                    ? "Check the internet connection and try closing again. If it still fails, give the support reference to Sati support."
                    : $"{safeRecoveryDetail}\n\nTry closing again. If it still fails, give the support reference to Sati support."),
                "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private const string ConflictMessage =
            "A newer saved copy exists. Your text remains in this box. Copy anything you need, then choose Reload Latest.";
    }
}
