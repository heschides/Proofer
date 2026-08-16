using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels
{
    public partial class SchedulerViewModel : ObservableObject
    {

        //FIELDS
        private readonly ISessionService _sessionService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISettingsService _settingsService;
        private Incentive? _incentive;
        private readonly LatestRequestTracker _monthLoadRequests = new();


        //EVENTS

        //PROPERTIES
        [ObservableProperty] private int currentMonth;
        [ObservableProperty] private int currentYear;
        [ObservableProperty] private string monthLabel = string.Empty;
        [ObservableProperty] private bool isLoading;
        public int DaysScheduled => _incentive?.DaysScheduled ?? 0;

        public ObservableCollection<WorkdayTile> Tiles { get; } = [];


        //CONSTRUCTOR
        public SchedulerViewModel(IIncentiveService incentiveService, ISettingsService settingsService, ISessionService sessionService)
        {
            _incentiveService = incentiveService;
            _settingsService = settingsService;
            CurrentMonth = DateTime.Now.Month;
            CurrentYear = DateTime.Now.Year;
            _sessionService = sessionService;
        }

        //RELAY METHODS
        [RelayCommand]
        private async Task ToggleTile(WorkdayTile tile)
        {
            if (IsLoading) return;
            if (!tile.IsInteractable) return;
            if (_incentive is null) return;

            tile.IsExcluded = !tile.IsExcluded;

            // ExcludedDates getter deserializes; setter re-serializes.
            // We must read, mutate, then write back to trigger serialization.
            var excluded = _incentive.ExcludedDates;
            if (tile.IsExcluded)
                excluded.Add(tile.Date);
            else
                excluded.Remove(tile.Date);
            _incentive.ExcludedDates = excluded;

            _incentive.DaysScheduled = Tiles.Count(t => !t.IsExcluded);
            await _incentiveService.SaveAsync(_incentive);
            OnPropertyChanged(nameof(DaysScheduled));
        }

        [RelayCommand]
        private async Task PreviousMonth()
        {
            if (CurrentMonth == 1)
            {
                CurrentMonth = 12;
                CurrentYear--;
            }
            else
            {
                CurrentMonth--;
            }
            await LoadMonthAsync();
        }

        [RelayCommand]
        private async Task NextMonth()
        {
            if (CurrentMonth == 12)
            {
                CurrentMonth = 1;
                CurrentYear++;
            }
            else
            {
                CurrentMonth++;
            }
            await LoadMonthAsync();
        }



        //FUNCTIONS
        private async Task LoadMonthAsync()
        {
            var request = _monthLoadRequests.Begin();
            var month = CurrentMonth;
            var year = CurrentYear;
            IsLoading = true;
            try
            {
                var userId = _sessionService.CurrentUser!.Id;
                var settings = await _settingsService.LoadAsync();
                var (incentive, _) = await _incentiveService.GetOrCreateAsync(userId, month, year);
                var tiles = BuildTiles(incentive, settings, month, year);
                var computed = tiles.Count(tile => !tile.IsExcluded);
                if (computed != incentive.DaysScheduled)
                {
                    incentive.DaysScheduled = computed;
                    await _incentiveService.SaveAsync(incentive);
                }

                if (!_monthLoadRequests.IsCurrent(request) ||
                    CurrentMonth != month || CurrentYear != year)
                    return;

                _incentive = incentive;
                MonthLabel = new DateTime(year, month, 1).ToString("MMMM yyyy");
                Tiles.Clear();
                foreach (var tile in tiles)
                    Tiles.Add(tile);
                OnPropertyChanged(nameof(DaysScheduled));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadMonthAsync failed: {ex.Message}");
            }
            finally
            {
                if (_monthLoadRequests.IsCurrent(request))
                    IsLoading = false;
            }
        }

        private static List<WorkdayTile> BuildTiles(
            Incentive incentive,
            Settings settings,
            int month,
            int year)
        {
            var tiles = new List<WorkdayTile>();
            var daysInMonth = DateTime.DaysInMonth(year, month);

            // Normalize stored excluded dates to midnight for reliable comparison.
            var manuallyExcluded = incentive.ExcludedDates
                .Select(d => d.Date)
                .ToHashSet();

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(year, month, day);
                var dow = date.DayOfWeek;

                if (dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday)
                    continue;

                var letter = dow switch
                {
                    DayOfWeek.Monday => "M",
                    DayOfWeek.Tuesday => "T",
                    DayOfWeek.Wednesday => "W",
                    DayOfWeek.Thursday => "Th",
                    _ => "F"
                };

                // Always-excluded: weekday flags or federal holidays from Settings.
                // Non-interactable so the user cannot toggle them.
                var alwaysExcluded = WorkdayHelper.IsAlwaysExcludedWorkday(date, settings);

                tiles.Add(new WorkdayTile
                {
                    Date = date,
                    Letter = letter,
                    IsInteractable = !alwaysExcluded,
                    IsExcluded = alwaysExcluded || manuallyExcluded.Contains(date)
                });
            }

            return tiles;
        }

        public void Initialize()
        {
            _ = LoadMonthAsync();
        }

    }
}
