using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class CalendarViewModelTests
{
    [Fact]
    public async Task InitializeBuildsTheWholeYearAndGroupsNotesByServiceDate()
    {
        var notes = new StubNoteService();
        notes.Notes.Add(CreateNote(
            "later note",
            new DateTime(2024, 2, 29, 16, 30, 0),
            startTime: null,
            minutes: 15,
            personName: "Zulu Person"));
        notes.Notes.Add(CreateNote(
            "earlier note",
            new DateTime(2024, 2, 29, 8, 15, 0),
            startTime: 75,
            minutes: 30,
            personName: "Alpha Person"));
        notes.Notes.Add(CreateNote(
            "adjacent day",
            new DateTime(2024, 3, 1),
            startTime: 120,
            minutes: 45));
        // A defensive check: a malformed service response must not leak a note
        // from another year into the displayed year.
        notes.Notes.Add(CreateNote(
            "wrong year",
            new DateTime(2025, 2, 28),
            startTime: 0,
            minutes: 15));
        var exemptDates = new StubExemptDateService();
        exemptDates.Dates.Add(new ExemptDate
        {
            Id = 41,
            UserId = 7,
            Date = new DateTime(2024, 2, 29)
        });
        var viewModel = CreateViewModel(notes, exemptDates);
        viewModel.CurrentYear = 2024;

        await viewModel.InitializeAsync();

        Assert.Equal(12, viewModel.Months.Count);
        Assert.Equal(29, viewModel.Months.Single(month => month.Month == 2)
            .Cells.OfType<CalendarDay>().Count());
        var leapDay = FindDay(viewModel, new DateTime(2024, 2, 29));
        Assert.True(leapDay.IsExempt);
        Assert.Equal(["earlier note", "later note"],
            leapDay.Notes.Select(note => note.Narrative).ToList());
        Assert.Equal("8:15 AM–8:45 AM", leapDay.Notes[0].ServiceTimeLabel);
        Assert.Equal("30 min · 2 units", leapDay.Notes[0].DurationLabel);
        Assert.Contains("2 notes", leapDay.AccessibleLabel);
        Assert.DoesNotContain(
            viewModel.Months.SelectMany(month => month.Cells).OfType<CalendarDay>()
                .SelectMany(day => day.Notes),
            note => note.Narrative == "wrong year");
    }

    [Fact]
    public async Task SelectedDayCanOpenAFocusedSummaryAndReturnToTheYear()
    {
        var notes = new StubNoteService();
        var date = new DateTime(2026, 8, 12);
        notes.Notes.Add(CreateNote("first", date, 60, 30));
        notes.Notes.Add(CreateNote("second", date, 150, 15));
        var viewModel = CreateViewModel(notes, new StubExemptDateService());
        viewModel.CurrentYear = date.Year;
        await viewModel.InitializeAsync();
        var day = FindDay(viewModel, date);

        viewModel.SelectDayCommand.Execute(day);
        viewModel.OpenSelectedDayCommand.Execute(null);

        Assert.Same(day, viewModel.SelectedDay);
        Assert.Equal(8, viewModel.SelectedMonth);
        Assert.True(viewModel.IsDayFocused);
        Assert.Equal(2, viewModel.SelectedDayNotes.Count);
        Assert.Equal(45, viewModel.SelectedDayTotalMinutes);
        Assert.Equal(3, viewModel.SelectedDayTotalUnits);
        Assert.Equal("2 notes · 45 minutes · 3 units", viewModel.SelectedDaySummary);

        viewModel.ReturnToYearCommand.Execute(null);
        Assert.False(viewModel.IsDayFocused);
    }

    [Fact]
    public async Task RefreshPreservesTheFocusedDateAndPublishesFreshNotes()
    {
        var notes = new StubNoteService();
        var date = new DateTime(2026, 8, 12);
        notes.Notes.Add(CreateNote("original", date, 60, 15));
        var viewModel = CreateViewModel(notes, new StubExemptDateService());
        viewModel.CurrentYear = date.Year;
        await viewModel.InitializeAsync();
        var originalDay = FindDay(viewModel, date);
        viewModel.SelectDayCommand.Execute(originalDay);
        viewModel.OpenSelectedDayCommand.Execute(null);
        notes.Notes.Add(CreateNote("newly loaded", date, 120, 15));

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.NotSame(originalDay, viewModel.SelectedDay);
        Assert.Equal(date, viewModel.SelectedDay?.Date);
        Assert.True(viewModel.IsDayFocused);
        Assert.Equal(["original", "newly loaded"],
            viewModel.SelectedDayNotes.Select(note => note.Narrative).ToList());
    }

    [Fact]
    public async Task AddingAndRemovingAnExemptDatePreservesTheSelectedDay()
    {
        var exemptDates = new StubExemptDateService();
        var viewModel = CreateViewModel(new StubNoteService(), exemptDates);
        var date = new DateTime(viewModel.CurrentYear, 8, 12);
        await viewModel.InitializeAsync();
        var originalDay = FindDay(viewModel, date);
        viewModel.SelectDayCommand.Execute(originalDay);
        var subscriberCalls = 0;
        viewModel.ExemptDateChanged += () =>
        {
            subscriberCalls++;
            return Task.CompletedTask;
        };

        await viewModel.ToggleExemptCommand.ExecuteAsync(originalDay);

        Assert.True(viewModel.SelectedDay?.IsExempt);
        Assert.Equal(date, viewModel.SelectedDay?.Date);
        Assert.Equal("Restore workday", viewModel.SelectedDayExemptActionLabel);
        Assert.Single(exemptDates.AddedDates);
        Assert.Equal(1, subscriberCalls);

        // Deliberately pass the replaced, stale day object. The command must use
        // the canonical exemption collection and remove rather than add again.
        await viewModel.ToggleExemptCommand.ExecuteAsync(originalDay);

        Assert.False(viewModel.SelectedDay?.IsExempt);
        Assert.Single(exemptDates.AddedDates);
        Assert.Single(exemptDates.RemovedIds);
        Assert.Equal(2, subscriberCalls);
    }

    [Fact]
    public async Task LoadFailureLeavesAStableRetryableState()
    {
        var notes = new StubNoteService
        {
            GetFailure = new InvalidOperationException("simulated load failure")
        };
        var viewModel = CreateViewModel(notes, new StubExemptDateService());

        var failure = await Record.ExceptionAsync(viewModel.InitializeAsync);

        Assert.Null(failure);
        Assert.Empty(viewModel.Months);
        Assert.Null(viewModel.SelectedDay);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasStatusMessage);
        Assert.Contains("Refresh", viewModel.StatusMessage);
    }

    [Fact]
    public async Task MissingSessionDoesNotCallCalendarServices()
    {
        var notes = new StubNoteService();
        var exemptDates = new StubExemptDateService();
        var viewModel = new CalendarViewModel(exemptDates, notes, new SessionService());

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.Months);
        Assert.Empty(notes.RequestedYears);
        Assert.Equal(0, exemptDates.GetCalls);
        Assert.Contains("session ended", viewModel.StatusMessage);
    }

    [Fact]
    public async Task YearNavigationStopsAtTheApiSupportedBoundaries()
    {
        var notes = new StubNoteService();
        var viewModel = CreateViewModel(notes, new StubExemptDateService());
        viewModel.CurrentYear = CalendarViewModel.MinimumYear;
        await viewModel.InitializeAsync();

        await viewModel.PreviousYearCommand.ExecuteAsync(null);

        Assert.Equal(CalendarViewModel.MinimumYear, viewModel.CurrentYear);
        Assert.Equal([CalendarViewModel.MinimumYear], notes.RequestedYears);

        viewModel.CurrentYear = CalendarViewModel.MaximumYear;
        await viewModel.InitializeAsync();
        await viewModel.NextYearCommand.ExecuteAsync(null);

        Assert.Equal(CalendarViewModel.MaximumYear, viewModel.CurrentYear);
        Assert.Equal(
            [CalendarViewModel.MinimumYear, CalendarViewModel.MaximumYear],
            notes.RequestedYears);
    }

    [Fact]
    public async Task YearLoadsPublishOnlyTheNewestResponse()
    {
        var notes = new OutOfOrderYearNoteService();
        var viewModel = CreateViewModel(notes, new StubExemptDateService());
        viewModel.CurrentYear = 2025;

        var older = viewModel.InitializeAsync();
        await notes.OlderRequestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.CurrentYear = 2026;
        await viewModel.InitializeAsync();

        notes.ReleaseOlderRequest.TrySetResult();
        await older;

        var augustDay = FindDay(viewModel, new DateTime(2026, 8, 12));
        Assert.Single(augustDay.Notes);
        Assert.Equal("newer response", augustDay.Notes[0].Narrative);
    }

    [Fact]
    public async Task ExemptDateServiceFailureDoesNotEscapeTheCommand()
    {
        var exemptDates = new StubExemptDateService
        {
            AddFailure = new InvalidOperationException("simulated save failure")
        };
        var viewModel = CreateViewModel(new StubNoteService(), exemptDates);
        await viewModel.InitializeAsync();
        var day = FindDay(viewModel, new DateTime(viewModel.CurrentYear, 8, 12));

        var failure = await Record.ExceptionAsync(
            () => viewModel.ToggleExemptCommand.ExecuteAsync(day));

        Assert.Null(failure);
        Assert.False(day.IsExempt);
        Assert.False(viewModel.IsUpdatingExemptDate);
        Assert.Contains("could not be saved", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExemptDateSubscriberFailureDoesNotEscapeTheCommand()
    {
        var viewModel = CreateViewModel(new StubNoteService(), new StubExemptDateService());
        await viewModel.InitializeAsync();
        var day = FindDay(viewModel, new DateTime(viewModel.CurrentYear, 8, 12));
        viewModel.ExemptDateChanged += () =>
            Task.FromException(new InvalidOperationException("simulated refresh failure"));

        var failure = await Record.ExceptionAsync(
            () => viewModel.ToggleExemptCommand.ExecuteAsync(day));

        Assert.Null(failure);
        Assert.True(FindDay(viewModel, day.Date).IsExempt);
        Assert.Contains("dashboard summary", viewModel.StatusMessage);
    }

    private static CalendarViewModel CreateViewModel(
        INoteService notes,
        IExemptDateService exemptDates)
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            7,
            "calendar-user",
            "Calendar User",
            "hash",
            "salt",
            UserRole.CaseManager,
            null,
            1));
        return new CalendarViewModel(exemptDates, notes, session);
    }

    private static Note CreateNote(
        string narrative,
        DateTime date,
        int? startTime,
        int? minutes,
        string personName = "Test Person")
    {
        var names = personName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var person = Person.Rehydrate(101, 7);
        person.FirstName = names.ElementAtOrDefault(0) ?? "Test";
        person.LastName = names.ElementAtOrDefault(1) ?? "Person";
        var note = Note.Create(
            narrative,
            date,
            NoteStatus.Logged,
            minutes,
            person.Id,
            noteType: NoteType.Contact);
        note.StartTime = startTime;
        note.Person = person;
        return note;
    }

    private static CalendarDay FindDay(CalendarViewModel viewModel, DateTime date) =>
        viewModel.Months
            .Single(month => month.Month == date.Month)
            .Cells
            .OfType<CalendarDay>()
            .Single(day => day.Date.Date == date.Date);

    private class StubNoteService : INoteService
    {
        public List<Note> Notes { get; } = [];
        public List<int> RequestedYears { get; } = [];
        public Exception? GetFailure { get; init; }

        public virtual Task<List<Note>> GetByYearAsync(int userId, int year)
        {
            RequestedYears.Add(year);
            return GetFailure is null
                ? Task.FromResult(Notes.ToList())
                : Task.FromException<List<Note>>(GetFailure);
        }

        public Task<Note> AddNoteAsync(Note note) => throw new NotSupportedException();
        public Task DeleteNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note note) => throw new NotSupportedException();
        public Task<List<Note>> GetAllByPersonAsync(int personId) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }

    private sealed class OutOfOrderYearNoteService : StubNoteService
    {
        public TaskCompletionSource OlderRequestEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOlderRequest { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task<List<Note>> GetByYearAsync(int userId, int year)
        {
            if (year == 2025)
            {
                OlderRequestEntered.TrySetResult();
                await ReleaseOlderRequest.Task;
                return [CalendarViewModelTests.CreateNote(
                    "older response", new DateTime(2025, 8, 12), null, 30)];
            }

            return [CalendarViewModelTests.CreateNote(
                "newer response", new DateTime(2026, 8, 12), null, 30)];
        }
    }

    private sealed class StubExemptDateService : IExemptDateService
    {
        private int nextId = 1;

        public Exception? AddFailure { get; init; }
        public List<ExemptDate> Dates { get; } = [];
        public List<DateTime> AddedDates { get; } = [];
        public List<int> RemovedIds { get; } = [];
        public int GetCalls { get; private set; }

        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year)
        {
            GetCalls++;
            return Task.FromResult(Dates
                .Where(date => date.UserId == userId && date.Date.Year == year)
                .ToList());
        }

        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null)
        {
            if (AddFailure is not null)
                return Task.FromException<ExemptDate>(AddFailure);

            AddedDates.Add(date.Date);
            var result = new ExemptDate
            {
                Id = nextId++,
                UserId = userId,
                Date = date.Date,
                Reason = reason
            };
            Dates.Add(result);
            return Task.FromResult(result);
        }

        public Task RemoveAsync(int id)
        {
            RemovedIds.Add(id);
            Dates.RemoveAll(date => date.Id == id);
            return Task.CompletedTask;
        }
    }
}
