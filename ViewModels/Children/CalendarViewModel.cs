using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Diagnostics;
using System.Globalization;

namespace Sati.ViewModels.Children;

public partial class CalendarViewModel : ObservableObject
{
    internal const int MinimumYear = 2000;
    internal const int MaximumYear = 2200;

    private readonly IExemptDateService _exemptDateService;
    private readonly INoteService _noteService;
    private readonly ISessionService _sessionService;
    private readonly LatestRequestTracker _yearLoadRequests = new();

    private List<ExemptDate> _exemptDates = [];
    private List<Note> _yearNotes = [];

    // The dashboard refresh is part of the calendar operation, so it is a Task
    // rather than an async-void EventHandler. Each subscriber is awaited and
    // isolated below; a failed summary refresh must never reach WPF's dispatcher.
    public event Func<Task>? ExemptDateChanged;

    [ObservableProperty]
    private int currentYear = DateTime.Today.Year;

    [ObservableProperty]
    private CalendarDay? selectedDay;

    [ObservableProperty]
    private int selectedMonth = DateTime.Today.Month;

    [ObservableProperty]
    private List<CalendarMonth> months = [];

    [ObservableProperty]
    private bool isDayFocused;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private bool isUpdatingExemptDate;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    public IReadOnlyList<CalendarNoteItem> SelectedDayNotes =>
        SelectedDay?.Notes ?? [];

    public bool HasSelectedDay => SelectedDay is not null;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public int SelectedDayTotalMinutes =>
        SelectedDayNotes.Sum(note => note.Minutes ?? 0);

    public int SelectedDayTotalUnits =>
        SelectedDayNotes.Sum(note => note.Units ?? 0);

    public string SelectedDaySummary
    {
        get
        {
            var count = SelectedDayNotes.Count;
            if (count == 0)
                return "No service notes are dated this day.";

            var noteWord = count == 1 ? "note" : "notes";
            var minuteWord = SelectedDayTotalMinutes == 1 ? "minute" : "minutes";
            var unitWord = SelectedDayTotalUnits == 1 ? "unit" : "units";
            return $"{count} {noteWord} · {SelectedDayTotalMinutes} {minuteWord} · {SelectedDayTotalUnits} {unitWord}";
        }
    }

    public string SelectedDayExemptActionLabel =>
        SelectedDay?.IsExempt == true ? "Restore workday" : "Mark as exempt";

    public List<ExemptDate> ExemptDaysForSelectedMonth =>
        _exemptDates
            .Where(entry => entry.Date.Month == SelectedMonth && entry.Date.Year == CurrentYear)
            .OrderBy(entry => entry.Date)
            .ToList();

    public string SelectedMonthName => SelectedMonth is >= 1 and <= 12
        ? CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(SelectedMonth)
        : string.Empty;

    public CalendarViewModel(
        IExemptDateService exemptDateService,
        INoteService noteService,
        ISessionService sessionService)
    {
        _exemptDateService = exemptDateService;
        _noteService = noteService;
        _sessionService = sessionService;
    }

    public Task InitializeAsync() => LoadYearAsync();

    [RelayCommand]
    private Task Refresh() => LoadYearAsync();

    [RelayCommand]
    private void SelectDay(CalendarDay? day)
    {
        if (day is null || day.Date.Year != CurrentYear)
            return;

        SelectedDay = day;
        SelectedMonth = day.Date.Month;
    }

    [RelayCommand]
    private void OpenSelectedDay()
    {
        if (SelectedDay is not null)
            IsDayFocused = true;
    }

    [RelayCommand]
    private void ReturnToYear() => IsDayFocused = false;

    [RelayCommand]
    private async Task ToggleExempt(CalendarDay? day)
    {
        if (day is null || day.Date.Year != CurrentYear || IsUpdatingExemptDate)
            return;

        var user = _sessionService.CurrentUser;
        if (user is null)
        {
            StatusMessage = "Sign in again before changing an exempt day.";
            return;
        }

        IsUpdatingExemptDate = true;
        try
        {
            // Read the canonical loaded collection instead of trusting a CalendarDay
            // instance. BuildMonths replaces day objects, so an old command parameter
            // can otherwise invert the wrong state or create a duplicate exemption.
            var existing = _exemptDates.FirstOrDefault(
                entry => entry.Date.Date == day.Date.Date);
            if (existing is not null)
            {
                await _exemptDateService.RemoveAsync(existing.Id);
                _exemptDates.RemoveAll(entry => entry.Date.Date == day.Date.Date);
            }
            else
            {
                var exempt = await _exemptDateService.AddAsync(user.Id, day.Date.Date);
                _exemptDates.RemoveAll(entry => entry.Date.Date == day.Date.Date);
                _exemptDates.Add(exempt);
            }

            BuildMonths();
            StatusMessage = string.Empty;
            if (!await NotifyExemptDateChangedAsync())
            {
                StatusMessage =
                    "The calendar changed, but the dashboard summary could not be refreshed. Refresh the dashboard before relying on its totals.";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.ToggleExempt failed: {ex.Message}");
            StatusMessage = "The exempt-day change could not be saved. Please try again.";
        }
        finally
        {
            IsUpdatingExemptDate = false;
        }
    }

    [RelayCommand]
    private async Task PreviousYear()
    {
        if (IsLoading || CurrentYear <= MinimumYear)
            return;

        CurrentYear--;
        SelectedDay = null;
        IsDayFocused = false;
        await LoadYearAsync();
    }

    [RelayCommand]
    private async Task NextYear()
    {
        if (IsLoading || CurrentYear >= MaximumYear)
            return;

        CurrentYear++;
        SelectedDay = null;
        IsDayFocused = false;
        await LoadYearAsync();
    }

    private async Task LoadYearAsync()
    {
        var request = _yearLoadRequests.Begin();
        var year = CurrentYear;
        IsLoading = true;

        try
        {
            if (year is < MinimumYear or > MaximumYear)
                throw new ArgumentOutOfRangeException(nameof(CurrentYear));

            var user = _sessionService.CurrentUser;
            if (user is null)
                throw new UnauthorizedAccessException("A signed-in user is required.");

            var exemptDatesTask = _exemptDateService.GetByYearAsync(user.Id, year);
            var notesTask = _noteService.GetByYearAsync(user.Id, year);
            await Task.WhenAll(exemptDatesTask, notesTask);

            if (!_yearLoadRequests.IsCurrent(request) || CurrentYear != year)
                return;

            _exemptDates = await exemptDatesTask;
            _yearNotes = await notesTask;
            BuildMonths();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CalendarViewModel.LoadYearAsync failed: {ex.Message}");
            if (_yearLoadRequests.IsCurrent(request) && CurrentYear == year)
            {
                ClearLoadedYear();
                StatusMessage = _sessionService.CurrentUser is null
                    ? "Your calendar is unavailable because the session ended. Sign in again."
                    : "The calendar could not be loaded. Check the connection and choose Refresh.";
            }
        }
        finally
        {
            if (_yearLoadRequests.IsCurrent(request))
                IsLoading = false;
        }
    }

    private void BuildMonths()
    {
        var selectedDate = SelectedDay?.Date.Date;
        var notesByDate = _yearNotes
            .Where(note => note.EventDate.HasValue && note.EventDate.Value.Year == CurrentYear)
            .Select(note => new CalendarNoteItem(note))
            .GroupBy(note => note.EventDate.Date)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(note => note.StartTime ?? int.MaxValue)
                    .ThenBy(note => note.ClientName, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(note => note.Id)
                    .ToList());
        var exemptByDate = _exemptDates
            .Where(entry => entry.Date.Year == CurrentYear)
            .GroupBy(entry => entry.Date.Date)
            .ToDictionary(group => group.Key, group => group.First());

        var result = new List<CalendarMonth>();
        for (var month = 1; month <= 12; month++)
        {
            var firstDay = new DateTime(CurrentYear, month, 1);
            var daysInMonth = DateTime.DaysInMonth(CurrentYear, month);
            var cells = new List<CalendarDay?>();

            for (var index = 0; index < (int)firstDay.DayOfWeek; index++)
                cells.Add(null);

            for (var dayNumber = 1; dayNumber <= daysInMonth; dayNumber++)
            {
                var date = new DateTime(CurrentYear, month, dayNumber);
                exemptByDate.TryGetValue(date, out var exemptEntry);
                notesByDate.TryGetValue(date, out var notes);
                cells.Add(new CalendarDay
                {
                    Date = date,
                    IsExempt = exemptEntry is not null,
                    ExemptDateId = exemptEntry?.Id,
                    IsWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    Notes = notes ?? []
                });
            }

            result.Add(new CalendarMonth
            {
                Name = firstDay.ToString("MMMM", CultureInfo.CurrentCulture),
                Month = month,
                Year = CurrentYear,
                Cells = cells
            });
        }

        Months = result;
        SelectedDay = selectedDate.HasValue && selectedDate.Value.Year == CurrentYear
            ? FindDay(selectedDate.Value)
            : null;
        if (SelectedDay is null)
            IsDayFocused = false;

        NotifyCalendarComputedProperties();
    }

    private CalendarDay? FindDay(DateTime date) =>
        Months
            .Where(month => month.Month == date.Month)
            .SelectMany(month => month.Cells)
            .FirstOrDefault(day => day?.Date.Date == date.Date);

    private void ClearLoadedYear()
    {
        _exemptDates = [];
        _yearNotes = [];
        Months = [];
        SelectedDay = null;
        IsDayFocused = false;
        NotifyCalendarComputedProperties();
    }

    private async Task<bool> NotifyExemptDateChangedAsync()
    {
        var handlers = ExemptDateChanged;
        if (handlers is null)
            return true;

        var succeeded = true;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            try
            {
                await handler();
            }
            catch (Exception ex)
            {
                succeeded = false;
                Debug.WriteLine($"CalendarViewModel.ExemptDateChanged subscriber failed: {ex.Message}");
            }
        }

        return succeeded;
    }

    private void NotifyCalendarComputedProperties()
    {
        OnPropertyChanged(nameof(SelectedDayNotes));
        OnPropertyChanged(nameof(SelectedDayTotalMinutes));
        OnPropertyChanged(nameof(SelectedDayTotalUnits));
        OnPropertyChanged(nameof(SelectedDaySummary));
        OnPropertyChanged(nameof(SelectedDayExemptActionLabel));
        OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        OnPropertyChanged(nameof(SelectedMonthName));
        OnPropertyChanged(nameof(HasSelectedDay));
    }

    partial void OnSelectedDayChanged(CalendarDay? value) =>
        NotifyCalendarComputedProperties();

    partial void OnSelectedMonthChanged(int value)
    {
        OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        OnPropertyChanged(nameof(SelectedMonthName));
    }

    partial void OnCurrentYearChanged(int value)
    {
        OnPropertyChanged(nameof(ExemptDaysForSelectedMonth));
        OnPropertyChanged(nameof(SelectedMonthName));
    }

    partial void OnStatusMessageChanged(string value) =>
        OnPropertyChanged(nameof(HasStatusMessage));
}
