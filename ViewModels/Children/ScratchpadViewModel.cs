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

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void IncreaseScratchpadFont() => ScratchpadFontSize = Math.Min(ScratchpadFontSize + 2, 28);
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

        public async Task SaveScratchpadAsync(string content)
        {
            try
            {
                if (_scratchpad is null)
                    return;

                _scratchpad.Content = content;
                await _scratchpadService.SaveAsync(_scratchpad);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveScratchpadAsync failed: {ex.Message}");
                MessageBox.Show(
                    "Sati encountered an error saving your scratchpad. Your work may not have been saved.",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // -------------------------------------------------------------------------
        // Private methods
        // -------------------------------------------------------------------------

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
    }
}
