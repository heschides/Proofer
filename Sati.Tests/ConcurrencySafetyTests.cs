using Sati.Data;
using Sati.Data.Billing;
using Sati.Models;
using Sati.Models.Billing;
using Sati.Services;
using Sati.ViewModels;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ConcurrencySafetyTests
{
    [Fact]
    public void LatestRequestTrackerInvalidatesOlderWork()
    {
        var tracker = new LatestRequestTracker();
        var first = tracker.Begin();
        var second = tracker.Begin();

        Assert.False(tracker.IsCurrent(first));
        Assert.True(tracker.IsCurrent(second));

        tracker.Invalidate();
        Assert.False(tracker.IsCurrent(second));
    }

    [Fact]
    public async Task ScratchpadSavesAreSerializedAndPreserveRequestOrder()
    {
        var service = new BlockingScratchpadService();
        var session = CreateSession(UserRole.CaseManager);
        var viewModel = new ScratchpadViewModel(service, session);
        await viewModel.InitializeAsync();

        var first = viewModel.SaveScratchpadAsync("first");
        await service.FirstSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = viewModel.SaveScratchpadAsync("second");

        await Task.Delay(50);
        Assert.Equal(1, service.MaximumConcurrentSaves);

        service.ReleaseFirstSave.TrySetResult();
        Assert.True(await first);
        Assert.True(await second);
        Assert.Equal(["first", "second"], service.SavedContents);
        Assert.Equal(1, service.MaximumConcurrentSaves);
    }

    [Fact]
    public async Task ScratchpadInitializationDoesNotOverlapDatabaseReads()
    {
        var service = new LoadTrackingScratchpadService();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));

        await viewModel.InitializeAsync();

        Assert.Equal(1, service.MaximumConcurrentLoads);
        Assert.Equal(["today", "tomorrow"], service.LoadOrder);
    }

    [Fact]
    public async Task TomorrowLoadFailureDoesNotHideSuccessfullyLoadedToday()
    {
        var service = new PartiallyFailingScratchpadService();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));

        await viewModel.InitializeAsync();

        Assert.Equal("saved work for today", viewModel.ScratchpadContent);
        Assert.False(viewModel.HasScratchpadLoadError);
        Assert.True(viewModel.HasTomorrowAgendaLoadError);
        Assert.Contains("Nothing was replaced or saved", viewModel.TomorrowAgendaLoadErrorMessage);
    }

    [Fact]
    public async Task NotesLogDoesNotFanOutOneDatabaseReadPerConsumer()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = new LoadTrackingNoteService();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var viewModel = new NotesWindowViewModel(
            fixture.PeopleAs(fixture.CaseManagerOne),
            session,
            notes,
            fixture.NoteEntry(notes: notes));

        await viewModel.ReloadAsync();

        Assert.Equal(1, notes.MaximumConcurrentLoads);
        Assert.Equal(2, notes.LoadCalls);
    }

    [Fact]
    public async Task NotesLogLoadFailureIsContainedForShellStartup()
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var notes = new FailingNoteService();
        var session = new SessionService();
        session.SetUser(fixture.CaseManagerOne);
        var viewModel = new NotesWindowViewModel(
            fixture.PeopleAs(fixture.CaseManagerOne),
            session,
            notes,
            fixture.NoteEntry(notes: notes));

        var failure = await Record.ExceptionAsync(viewModel.ReloadAsync);

        Assert.Null(failure);
        Assert.True(viewModel.HasLoadError);
        Assert.Contains("other workspaces are still available", viewModel.LoadErrorMessage);
    }

    [Fact]
    public async Task ScratchpadFlushSavesTodayAndTomorrow()
    {
        var service = new BlockingScratchpadService();
        service.ReleaseFirstSave.TrySetResult();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));
        await viewModel.InitializeAsync();
        viewModel.ScratchpadContent = "finish today's calls";
        viewModel.TomorrowAgendaContent = "start with the annual review";

        var saved = await viewModel.SaveAllScratchpadsAsync();

        Assert.True(saved);
        Assert.Equal(
            ["finish today's calls", "start with the annual review"],
            service.SavedContents);
    }

    [Fact]
    public async Task ScratchpadFlushDoesNotResaveUnchangedDrafts()
    {
        var service = new BlockingScratchpadService();
        service.ReleaseFirstSave.TrySetResult();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));
        await viewModel.InitializeAsync();

        var saved = await viewModel.SaveAllScratchpadsAsync();

        Assert.True(saved);
        Assert.Empty(service.SavedContents);
    }

    [Fact]
    public async Task ScratchpadFlushOnlySavesTheDraftThatChanged()
    {
        var service = new BlockingScratchpadService();
        service.ReleaseFirstSave.TrySetResult();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));
        await viewModel.InitializeAsync();
        viewModel.TomorrowAgendaContent = "prepare the annual review";

        var saved = await viewModel.SaveAllScratchpadsAsync();

        Assert.True(saved);
        Assert.Equal(["prepare the annual review"], service.SavedContents);
    }

    [Fact]
    public async Task SuccessfulScratchpadFlushBecomesTheNewNoOpBaseline()
    {
        var service = new BlockingScratchpadService();
        service.ReleaseFirstSave.TrySetResult();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));
        await viewModel.InitializeAsync();
        viewModel.ScratchpadContent = "call guardian";

        Assert.True(await viewModel.SaveAllScratchpadsAsync());
        Assert.True(await viewModel.SaveAllScratchpadsAsync());

        Assert.Equal(["call guardian"], service.SavedContents);
    }

    [Fact]
    public async Task ExpiredScratchpadSessionStopsTheSecondWriteAndFurtherRetries()
    {
        var service = new ExpiredScratchpadService();
        var viewModel = new ScratchpadViewModel(
            service,
            CreateSession(UserRole.CaseManager));
        await viewModel.InitializeAsync();
        viewModel.ScratchpadContent = "unsaved today";
        viewModel.TomorrowAgendaContent = "unsaved tomorrow";

        Assert.False(await viewModel.SaveAllScratchpadsAsync());
        Assert.True(viewModel.HasScratchpadSessionExpired);
        Assert.Equal(1, service.SaveCalls);

        Assert.False(await viewModel.SaveAllScratchpadsAsync());
        Assert.Equal(1, service.SaveCalls);
    }

    [Fact]
    public async Task BillingQueueCollapsesOverlappingLoads()
    {
        var service = new BlockingBillingService();
        var viewModel = new BillingQueueViewModel(service);

        var first = viewModel.LoadAsync();
        await service.ConfigurationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = viewModel.LoadAsync();
        await duplicate;

        Assert.Equal(1, service.ConfigurationCalls);
        service.ReleaseConfiguration.TrySetResult();
        await first;
        Assert.True(viewModel.HasLoaded);
    }

    [Fact]
    public async Task SchedulerPublishesOnlyTheNewestMonthResponse()
    {
        var incentives = new OutOfOrderIncentiveService();
        var viewModel = new SchedulerViewModel(
            incentives,
            new StaticSettingsService(),
            CreateSession(UserRole.CaseManager))
        {
            CurrentMonth = 1,
            CurrentYear = 2026
        };

        var older = viewModel.PreviousMonthCommand.ExecuteAsync(null);
        await incentives.DecemberRequestEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newer = viewModel.NextMonthCommand.ExecuteAsync(null);
        await newer;

        incentives.ReleaseDecember.TrySetResult();
        await older;

        Assert.Equal("January 2026", viewModel.MonthLabel);
        Assert.NotEmpty(viewModel.Tiles);
        Assert.All(viewModel.Tiles, tile =>
        {
            Assert.Equal(1, tile.Date.Month);
            Assert.Equal(2026, tile.Date.Year);
        });
    }

    private static SessionService CreateSession(UserRole role)
    {
        var session = new SessionService();
        session.SetUser(User.Create(7, "race-user", "Race User", "hash", "salt", role, null, 1));
        return session;
    }

    private sealed class BlockingScratchpadService : IScratchpadService
    {
        private int _concurrent;
        public int MaximumConcurrentSaves { get; private set; }
        public List<string> SavedContents { get; } = [];
        public TaskCompletionSource FirstSaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Scratchpad> LoadTodayAsync(int userId) => Task.FromResult(new Scratchpad
        {
            Id = 1,
            UserId = userId,
            Date = DateTime.Today,
            Revision = 1
        });

        public Task<Scratchpad> LoadTomorrowAsync(int userId) => Task.FromResult(new Scratchpad
        {
            Id = 2,
            UserId = userId,
            Date = DateTime.Today.AddDays(1),
            Revision = 1
        });

        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => Task.FromResult(new List<Scratchpad>());
        public Task<ScratchpadComment> AddCommentAsync(int scratchpadId, int userId, string authorDisplayName, string content) =>
            throw new NotSupportedException();

        public async Task SaveAsync(Scratchpad scratchpad)
        {
            var concurrent = Interlocked.Increment(ref _concurrent);
            MaximumConcurrentSaves = Math.Max(MaximumConcurrentSaves, concurrent);
            try
            {
                var content = scratchpad.Content;
                if (SavedContents.Count == 0)
                {
                    FirstSaveEntered.TrySetResult();
                    await ReleaseFirstSave.Task;
                }
                SavedContents.Add(content);
                scratchpad.Revision++;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }
    }

    private sealed class LoadTrackingScratchpadService : IScratchpadService
    {
        private int _concurrentLoads;
        public int MaximumConcurrentLoads { get; private set; }
        public List<string> LoadOrder { get; } = [];

        public Task<Scratchpad> LoadTodayAsync(int userId) => LoadAsync(userId, "today", 1, DateTime.Today);

        public Task<Scratchpad> LoadTomorrowAsync(int userId) =>
            LoadAsync(userId, "tomorrow", 2, DateTime.Today.AddDays(1));

        private async Task<Scratchpad> LoadAsync(int userId, string name, int id, DateTime date)
        {
            var concurrent = Interlocked.Increment(ref _concurrentLoads);
            MaximumConcurrentLoads = Math.Max(MaximumConcurrentLoads, concurrent);
            LoadOrder.Add(name);
            try
            {
                await Task.Delay(50);
                return new Scratchpad { Id = id, UserId = userId, Date = date, Revision = 1 };
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentLoads);
            }
        }

        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => Task.FromResult(new List<Scratchpad>());
        public Task<ScratchpadComment> AddCommentAsync(
            int scratchpadId, int userId, string authorDisplayName, string content) =>
            throw new NotSupportedException();
        public Task SaveAsync(Scratchpad scratchpad) => Task.CompletedTask;
    }

    private sealed class LoadTrackingNoteService : INoteService
    {
        private int _concurrentLoads;
        public int MaximumConcurrentLoads { get; private set; }
        public int LoadCalls { get; private set; }

        public async Task<List<Note>> GetAllByPersonAsync(int personId)
        {
            LoadCalls++;
            var concurrent = Interlocked.Increment(ref _concurrentLoads);
            MaximumConcurrentLoads = Math.Max(MaximumConcurrentLoads, concurrent);
            try
            {
                await Task.Delay(50);
                return [];
            }
            finally
            {
                Interlocked.Decrement(ref _concurrentLoads);
            }
        }

        public Task<Note> AddNoteAsync(Note note) => throw new NotSupportedException();
        public Task DeleteNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetByYearAsync(int userId, int year) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }

    private sealed class PartiallyFailingScratchpadService : IScratchpadService
    {
        public Task<Scratchpad> LoadTodayAsync(int userId) => Task.FromResult(new Scratchpad
        {
            Id = 1,
            UserId = userId,
            Date = DateTime.Today,
            Content = "saved work for today",
            Revision = 1
        });

        public Task<Scratchpad> LoadTomorrowAsync(int userId) =>
            Task.FromException<Scratchpad>(new InvalidOperationException("Simulated database startup failure."));

        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => Task.FromResult(new List<Scratchpad>());
        public Task<ScratchpadComment> AddCommentAsync(
            int scratchpadId, int userId, string authorDisplayName, string content) =>
            throw new NotSupportedException();
        public Task SaveAsync(Scratchpad scratchpad) => Task.CompletedTask;
    }

    private sealed class FailingNoteService : INoteService
    {
        public Task<List<Note>> GetAllByPersonAsync(int personId) =>
            Task.FromException<List<Note>>(new InvalidOperationException("Simulated database startup failure."));
        public Task<Note> AddNoteAsync(Note note) => throw new NotSupportedException();
        public Task DeleteNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateNoteAsync(Note note) => throw new NotSupportedException();
        public Task UpdateAbandonedNotesAsync(int abandonedAfterDays) => throw new NotSupportedException();
        public Task<List<Note>> GetMonthlyNotesAsync(int userId) => throw new NotSupportedException();
        public Task<List<Note>> GetByYearAsync(int userId, int year) => throw new NotSupportedException();
        public Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date) => throw new NotSupportedException();
    }

    private sealed class ExpiredScratchpadService : IScratchpadService
    {
        public int SaveCalls { get; private set; }

        public Task<Scratchpad> LoadTodayAsync(int userId) => Task.FromResult(new Scratchpad
        {
            Id = 1,
            UserId = userId,
            Date = DateTime.Today,
            Revision = 1
        });

        public Task<Scratchpad> LoadTomorrowAsync(int userId) => Task.FromResult(new Scratchpad
        {
            Id = 2,
            UserId = userId,
            Date = DateTime.Today.AddDays(1),
            Revision = 1
        });

        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => Task.FromResult(new List<Scratchpad>());
        public Task<ScratchpadComment> AddCommentAsync(int scratchpadId, int userId, string authorDisplayName, string content) =>
            throw new NotSupportedException();

        public Task SaveAsync(Scratchpad scratchpad)
        {
            SaveCalls++;
            throw new ScratchpadSessionExpiredException(new InvalidOperationException("401"));
        }
    }

    private sealed class BlockingBillingService : IBillingService
    {
        public int ConfigurationCalls { get; private set; }
        public TaskCompletionSource ConfigurationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseConfiguration { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BillingConfiguration> GetBillingConfigurationAsync()
        {
            ConfigurationCalls++;
            ConfigurationEntered.TrySetResult();
            await ReleaseConfiguration.Task;
            return new BillingConfiguration("T1016", null, 1m, "SUBMITTER", "Payer", "PAYER", "Contact", "2075550100");
        }

        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync() => Task.FromResult<IEnumerable<Note>>([]);
        public BillingValidationResult ValidateNoteForBilling(Note note) => new(true, note, []);
        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync() => throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(int noteId, bool isComplianceException = false, string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId) => throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(int billingPeriodId) => throw new NotSupportedException();
        public Task SaveBillingConfigurationAsync(BillingConfiguration configuration) => throw new NotSupportedException();
    }

    private sealed class OutOfOrderIncentiveService : IIncentiveService
    {
        public TaskCompletionSource DecemberRequestEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDecember { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year)
        {
            if (month == 12 && year == 2025)
            {
                DecemberRequestEntered.TrySetResult();
                await ReleaseDecember.Task;
            }

            return (new Incentive { Id = month, UserId = userId, Month = month, Year = year }, false);
        }

        public Task SaveAsync(Incentive incentive) => Task.CompletedTask;
        public Task<int> GetRemainingEligibleDaysAsync(int month, int year, HashSet<DateTime> daysAlreadyWorked, HashSet<DateTime> exemptDates) => throw new NotSupportedException();
        public Task<int> GetEligibleDaysAsync(DateTime startInclusive, DateTime endInclusive) => throw new NotSupportedException();
        public Task<List<Incentive>> GetHistoryAsync(int userId) => throw new NotSupportedException();
    }

    private sealed class StaticSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }
}
