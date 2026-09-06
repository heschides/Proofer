using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class DailyAgendaViewModelTests
{
    [Fact]
    public void NothingIsPreselectedAndSkipWritesNothing()
    {
        var scratchpad = Scratchpad("Existing work");
        var viewModel = ViewModel(scratchpad, Result(upcoming: [Item("One")]));
        var closed = false;
        viewModel.CloseRequested += (_, _) => closed = true;

        Assert.All(viewModel.AllItems, item => Assert.False(item.IsSelected));
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));

        viewModel.SkipCommand.Execute(null);

        Assert.True(closed);
        Assert.Equal("Existing work", scratchpad.ScratchpadContent);
    }

    [Fact]
    public async Task ConfirmCreatesOneStructuredItemPerSelectionAndCannotRepeat()
    {
        var workAgenda = new RecordingWorkAgendaService();
        var scratchpad = Scratchpad("Call provider", workAgenda);
        var viewModel = ViewModel(
            scratchpad,
            Result(upcoming: [Item("First"), Item("Second")]));
        viewModel.UpcomingItems[0].IsSelected = true;
        viewModel.UpcomingItems[1].IsSelected = true;

        await viewModel.ConfirmCommand.ExecuteAsync(null);
        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("Call provider", scratchpad.ScratchpadContent);
        Assert.Equal(["First", "Second"], workAgenda.Added.Select(item => item.Title));
        Assert.Equal(new DateTime(2026, 9, 1), workAgenda.Date);
        Assert.Equal(1, workAgenda.AddCalls);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task AddingStructuredWorkLeavesAnEmptyFreeformDraftEmpty()
    {
        var workAgenda = new RecordingWorkAgendaService();
        var scratchpad = Scratchpad(string.Empty, workAgenda);
        var viewModel = ViewModel(scratchpad, Result(upcoming: [Item("First")]));
        viewModel.UpcomingItems[0].IsSelected = true;

        await viewModel.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, scratchpad.ScratchpadContent);
        Assert.Equal("First", Assert.Single(workAgenda.Added).Title);
    }

    [Fact]
    public void GreetingAndDemoIndicatorStateRemainStable()
    {
        var viewModel = ViewModel(
            Scratchpad(string.Empty),
            Result(upcoming: [Item("First")]),
            isDemo: true,
            greetingIndex: 2);

        var first = viewModel.Greeting;

        Assert.Equal(first, viewModel.Greeting);
        Assert.True(viewModel.IsDemo);
        Assert.Equal("DEMO", viewModel.EnvironmentLabel);
    }

    [Fact]
    public void OpenRaisesTheExactSelectedAgendaItem()
    {
        var source = Item("First");
        var viewModel = ViewModel(Scratchpad(string.Empty), Result(upcoming: [source]));
        DailyAgendaItem? opened = null;
        viewModel.OpenRequested += (_, item) => opened = item;

        viewModel.OpenItemCommand.Execute(viewModel.UpcomingItems[0]);

        Assert.Same(source, opened);
    }

    private static DailyAgendaViewModel ViewModel(
        ScratchpadViewModel scratchpad,
        DailyAgendaBuildResult result,
        bool isDemo = false,
        int greetingIndex = 0) => new(
        result,
        scratchpad,
        "Case Manager One",
        isDemo ? "DEMO" : "PRODUCTION",
        isDemo,
        "Not started",
        new DateOnly(2026, 9, 1),
        greetingIndex);

    private static DailyAgendaBuildResult Result(
        IReadOnlyList<DailyAgendaItem>? overdue = null,
        IReadOnlyList<DailyAgendaItem>? upcoming = null,
        DailyAgendaItem? assessment = null) => new(
        1,
        30,
        overdue?.Count ?? 0,
        overdue ?? [],
        upcoming ?? [],
        assessment);

    private static DailyAgendaItem Item(string title) => new(
        title,
        1,
        "Alex Person",
        title,
        new DateTime(2026, 9, 12),
        DailyAgendaItemKind.UpcomingWork,
        false);

    private static ScratchpadViewModel Scratchpad(
        string content,
        IWorkAgendaService? workAgenda = null)
    {
        var session = new SessionService();
        session.SetUser(Sati.Models.User.Create(
            41, "case.manager", "Case Manager", "hash", "salt",
            UserRole.CaseManager, null, 3));
        return new(new StubScratchpadService(), session, workAgenda)
        {
            ScratchpadContent = content
        };
    }

    private sealed class RecordingWorkAgendaService : IWorkAgendaService
    {
        public List<DailyAgendaItem> Added { get; } = [];
        public DateTime? Date { get; private set; }
        public int AddCalls { get; private set; }

        public Task<IReadOnlyList<WorkAgendaItem>> LoadAsync(int userId, DateTime date) =>
            Task.FromResult<IReadOnlyList<WorkAgendaItem>>([]);

        public Task<WorkAgendaAddResult> AddFromDailyAgendaAsync(
            int userId,
            DateTime date,
            IReadOnlyList<DailyAgendaItem> selectedItems)
        {
            AddCalls++;
            Date = date;
            Added.AddRange(selectedItems);
            return Task.FromResult(new WorkAgendaAddResult(selectedItems.Count, 0));
        }
    }

    private sealed class StubScratchpadService : IScratchpadService
    {
        public Task<Scratchpad> LoadTodayAsync(int userId) => throw new NotSupportedException();
        public Task<Scratchpad> LoadTomorrowAsync(int userId) => throw new NotSupportedException();
        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => throw new NotSupportedException();
        public Task<ScratchpadComment> AddCommentAsync(
            int scratchpadId,
            int userId,
            string authorDisplayName,
            string content) => throw new NotSupportedException();
        public Task SaveAsync(Scratchpad scratchpad) => throw new NotSupportedException();
    }
}
