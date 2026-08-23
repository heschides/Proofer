using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
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

        private Scratchpad? _scratchpad;
        private Scratchpad? _tomorrowAgenda;
        private DispatcherTimer? _scratchpadTimer;
        private readonly SemaphoreSlim _saveGate = new(1, 1);
        private string _lastSavedScratchpadContent = string.Empty;
        private string _lastSavedTomorrowAgendaContent = string.Empty;
        private bool _sessionExpiredDuringSave;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ScratchpadViewModel(IScratchpadService scratchpadService, ISessionService sessionService)
        {
            _scratchpadService = scratchpadService;
            _sessionService = sessionService;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler? OpenScratchpadHistoryRequested;

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
        [ObservableProperty] private bool hasScratchpadSessionExpired;
        [ObservableProperty] private string scratchpadSessionExpiredMessage = string.Empty;

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseScratchpadFont() => ScratchpadFontSize = Math.Min(ScratchpadFontSize + 2, 28);
        [RelayCommand]
        private async Task ReloadLatestScratchpadAsync()
        {
            var userId = _sessionService.CurrentUser!.Id;
            _scratchpad = await _scratchpadService.LoadTodayAsync(userId);
            _lastSavedScratchpadContent = _scratchpad.Content;
            ScratchpadContent = _scratchpad.Content;
            HasScratchpadConflict = false;
            ScratchpadConflictMessage = string.Empty;
            StartScratchpadTimer();
        }

        [RelayCommand]
        private async Task ReloadLatestTomorrowAgendaAsync()
        {
            var userId = _sessionService.CurrentUser!.Id;
            _tomorrowAgenda = await _scratchpadService.LoadTomorrowAsync(userId);
            _lastSavedTomorrowAgendaContent = _tomorrowAgenda.Content;
            TomorrowAgendaContent = _tomorrowAgenda.Content;
            TomorrowAgendaDateLabel = FormatAgendaDate(_tomorrowAgenda.Date);
            HasTomorrowAgendaConflict = false;
            TomorrowAgendaConflictMessage = string.Empty;
            StartScratchpadTimer();
        }

        [RelayCommand] private void DecreaseScratchpadFont() => ScratchpadFontSize = Math.Max(ScratchpadFontSize - 2, 10);
        [RelayCommand] private void OpenScratchpadHistory() => OpenScratchpadHistoryRequested?.Invoke(this, EventArgs.Empty);

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
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
                ClearExpiredSessionWarning();
                StartScratchpadTimer();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScratchpadViewModel.InitializeAsync failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error loading your scratchpad.",
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
