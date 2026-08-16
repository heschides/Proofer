using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

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
        private DispatcherTimer? _scratchpadTimer;
        private readonly SemaphoreSlim _saveGate = new(1, 1);

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
        [ObservableProperty] private double scratchpadFontSize = 14;
        [ObservableProperty] private bool hasScratchpadConflict;
        [ObservableProperty] private string scratchpadConflictMessage = string.Empty;

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseScratchpadFont() => ScratchpadFontSize = Math.Min(ScratchpadFontSize + 2, 28);
        [RelayCommand]
        private async Task ReloadLatestScratchpadAsync()
        {
            var userId = _sessionService.CurrentUser!.Id;
            _scratchpad = await _scratchpadService.LoadTodayAsync(userId);
            ScratchpadContent = _scratchpad.Content;
            HasScratchpadConflict = false;
            ScratchpadConflictMessage = string.Empty;
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
                _scratchpad = await _scratchpadService.LoadTodayAsync(userId);
                ScratchpadContent = _scratchpad.Content;
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

                _scratchpad.Content = content;
                await _scratchpadService.SaveAsync(_scratchpad);
                HasScratchpadConflict = false;
                ScratchpadConflictMessage = string.Empty;
                return true;
            }
            catch (ScratchpadConcurrencyException ex)
            {
                _scratchpadTimer?.Stop();
                HasScratchpadConflict = true;
                ScratchpadConflictMessage =
                    "A newer saved copy exists. Your text remains in this box. Copy anything you need, then choose Reload Latest.";
                Debug.WriteLine($"SaveScratchpadAsync conflict: {ex.Message}");
                MessageBox.Show(ex.Message, "Scratchpad Changed Elsewhere",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveScratchpadAsync failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your scratchpad. Your work may not have been saved.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (_scratchpad is null || HasScratchpadConflict) return;
                await SaveScratchpadAsync(ScratchpadContent);
            };
            _scratchpadTimer.Start();
        }
    }
}
