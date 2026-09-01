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
    public void ConfirmAppendsOneLinePerSelectionUnderDatedHeaderAndCannotRepeat()
    {
        var scratchpad = Scratchpad("Call provider");
        var viewModel = ViewModel(
            scratchpad,
            Result(upcoming: [Item("First"), Item("Second")]));
        viewModel.UpcomingItems[0].IsSelected = true;
        viewModel.UpcomingItems[1].IsSelected = true;

        viewModel.ConfirmCommand.Execute(null);
        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal(
            $"Call provider{Environment.NewLine}{Environment.NewLine}" +
            $"Tuesday, September 1{Environment.NewLine}" +
            $"First — due September 12, 2026{Environment.NewLine}" +
            "Second — due September 12, 2026",
            scratchpad.ScratchpadContent);
        Assert.False(viewModel.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void EmptyScratchpadReceivesLinesWithoutAnUnnecessaryHeader()
    {
        var scratchpad = Scratchpad(string.Empty);
        var viewModel = ViewModel(scratchpad, Result(upcoming: [Item("First")]));
        viewModel.UpcomingItems[0].IsSelected = true;

        viewModel.ConfirmCommand.Execute(null);

        Assert.Equal("First — due September 12, 2026", scratchpad.ScratchpadContent);
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

    private static ScratchpadViewModel Scratchpad(string content) =>
        new(new StubScratchpadService(), new SessionService())
        {
            ScratchpadContent = content
        };

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
