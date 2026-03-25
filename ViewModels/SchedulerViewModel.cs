using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace Sati.ViewModels
{
    public partial class SchedulerViewModel : ObservableObject
    {

        //FIELDS
        private readonly ISessionService _sessionService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISettingsService _settingsService;
        private Incentive? _incentive;
        private Settings? _settings;


        //EVENTS

        //PROPERTIES
        [ObservableProperty] private int currentMonth;
        [ObservableProperty] private int currentYear;
        [ObservableProperty] private string monthLabel = string.Empty;
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
            if (!tile.IsInteractable) return;

            tile.IsExcluded = !tile.IsExcluded;

            var dates = _incentive!.ExcludedDates;
            if (tile.IsExcluded)
                dates.Add(tile.Date);
            else
                dates.Remove(tile.Date);

            _incentive.ExcludedDates = dates;
            _incentive.DaysScheduled = Tiles.Count(t => t.IsInteractable && !t.IsExcluded);
            await _incentiveService.SaveAsync(_incentive);
            OnPropertyChanged(nameof(DaysScheduled));
        }

        [RelayCommand]
        private void PreviousMonth()
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
            _ = LoadMonthAsync();
        }

        [RelayCommand]
        private void NextMonth()
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
            _ = LoadMonthAsync();
        }

        //FUNCTIONS
        private async Task LoadMonthAsync()
        {
            try
            {
                var userId = _sessionService.CurrentUser!.Id;
                _settings = await _settingsService.LoadAsync();
                var (incentive, _) = await _incentiveService.GetOrCreateAsync(userId, CurrentMonth, CurrentYear);
                MonthLabel = new DateTime(CurrentYear, CurrentMonth, 1).ToString("MMMM yyyy");
                _incentive = incentive;
                BuildTiles();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadMonthAsync failed: {ex.Message}");
            }
        }

        private void BuildTiles()
        {
            Tiles.Clear();
            var daysInMonth = DateTime.DaysInMonth(CurrentYear, CurrentMonth);

            for (int day = 1; day <= daysInMonth; day++)
            {
                var date = new DateTime(CurrentYear, CurrentMonth, day);
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

                var isInteractable = !IsAlwaysExcluded(dow, _settings!);
                var isExcluded = !isInteractable ||
                                 (_incentive?.ExcludedDates.Contains(date) ?? false);

                Tiles.Add(new WorkdayTile
                {
                    Date = date,
                    Letter = letter,
                    IsInteractable = isInteractable,
                    IsExcluded = isExcluded
                });
            }
        }

        private bool IsAlwaysExcluded(DayOfWeek dow, Settings settings)
        {
            return dow switch
            {
                DayOfWeek.Monday => settings.ExcludeMonday,
                DayOfWeek.Tuesday => settings.ExcludeTuesday,
                DayOfWeek.Wednesday => settings.ExcludeWednesday,
                DayOfWeek.Thursday => settings.ExcludeThursday,
                DayOfWeek.Friday => settings.ExcludeFriday,
                _ => false
            };
        }

        public void Initialize()
        {
            _ = LoadMonthAsync();
        }

    }
}
