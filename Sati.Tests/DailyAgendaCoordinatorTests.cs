using Sati.Data;
using Sati.Models;
using Sati.Models.Assessments;
using Sati.Services;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class DailyAgendaCoordinatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sati-agenda-coordinator-{Guid.NewGuid():N}");
    private static readonly DateOnly Today = new(2026, 9, 1);

    [Fact]
    public async Task DisabledPreferenceSkipsEveryAgendaDataSource()
    {
        var settings = new StubSettingsService();
        var upcoming = new StubUpcomingEventService();
        var assessments = new StubAssessmentService();
        var preferences = Preferences();
        await preferences.SetShowAtSignInAsync(41, false);

        var result = await Coordinator(preferences, settings, upcoming, assessments)
            .TryCreateAsync(User(), [], Scratchpad(), Today);

        Assert.Null(result);
        Assert.Equal(0, settings.LoadCount);
        Assert.Equal(0, upcoming.GenerateCount);
        Assert.Equal(0, assessments.LoadCount);
    }

    [Fact]
    public async Task AlreadyShownTodaySkipsEveryAgendaDataSource()
    {
        var settings = new StubSettingsService();
        var upcoming = new StubUpcomingEventService();
        var assessments = new StubAssessmentService();
        var preferences = Preferences();
        await preferences.MarkShownAsync(41, Today);

        var result = await Coordinator(preferences, settings, upcoming, assessments)
            .TryCreateAsync(User(), [], Scratchpad(), Today);

        Assert.Null(result);
        Assert.Equal(0, settings.LoadCount);
        Assert.Equal(0, upcoming.GenerateCount);
        Assert.Equal(0, assessments.LoadCount);
    }

    [Fact]
    public async Task SuccessfulCreationMarksAgendaShownForTheCalendarDay()
    {
        var preferences = Preferences();
        var coordinator = Coordinator(
            preferences,
            new StubSettingsService(),
            new StubUpcomingEventService(),
            new StubAssessmentService());

        var first = await coordinator.TryCreateAsync(User(), [], Scratchpad(), Today);
        var second = await coordinator.TryCreateAsync(User(), [], Scratchpad(), Today);

        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(Today, (await preferences.LoadForUserAsync(41)).LastShownDate);
    }

    [Fact]
    public async Task UpcomingFailureDoesNotEscapeIntoSignIn()
    {
        var upcoming = new StubUpcomingEventService { Exception = new InvalidOperationException("offline") };

        var exception = await Record.ExceptionAsync(async () =>
        {
            var result = await Coordinator(
                    Preferences(),
                    new StubSettingsService(),
                    upcoming,
                    new StubAssessmentService())
                .TryCreateAsync(User(), [], Scratchpad(), Today);
            Assert.Null(result);
        });

        Assert.Null(exception);
        Assert.Equal(1, upcoming.GenerateCount);
    }

    [Fact]
    public async Task ApprovedAssessmentIsSuggestedWhenItsFormRemainsUnattested()
    {
        var assessment = new ComprehensiveAssessment
        {
            PersonId = 7,
            Status = AssessmentStatus.Approved,
            DocumentJson = "{}"
        };
        var assessments = new StubAssessmentService { Result = assessment };
        var person = Person.Rehydrate(7, 41);
        person.FirstName = "Alex";
        person.LastName = "Person";
        person.Forms.Add(new Form(
            FormType.ComprehensiveAssessment,
            new DateTime(2027, 4, 1),
            isCompliant: false));

        var result = await Coordinator(
                Preferences(),
                new StubSettingsService(),
                new StubUpcomingEventService(),
                assessments)
            .TryCreateAsync(User(), [person], Scratchpad(), Today);

        Assert.NotNull(result);
        Assert.True(result.HasAssessmentSuggestion);
        Assert.Contains("Approved", result.AssessmentProgressText, StringComparison.Ordinal);
        Assert.Equal(1, assessments.LoadCount);
    }

    [Fact]
    public async Task MissingAssessmentRowIsReportedAsNotStarted()
    {
        var person = Person.Rehydrate(7, 41);
        person.FirstName = "Alex";
        person.LastName = "Person";
        person.Forms.Add(new Form(
            FormType.ComprehensiveAssessment,
            new DateTime(2027, 4, 1),
            isCompliant: false));

        var result = await Coordinator(
                Preferences(),
                new StubSettingsService(),
                new StubUpcomingEventService(),
                new StubAssessmentService())
            .TryCreateAsync(User(), [person], Scratchpad(), Today);

        Assert.NotNull(result);
        Assert.Equal("Not started.", result.AssessmentProgressText);
    }

    private DailyAgendaCoordinator Coordinator(
        DailyAgendaPreferenceService preferences,
        StubSettingsService settings,
        StubUpcomingEventService upcoming,
        StubAssessmentService assessments) => new(
        preferences,
        settings,
        new DailyAgendaBuilder(upcoming),
        assessments,
        EnvironmentInfo());

    private DailyAgendaPreferenceService Preferences() => new(
        EnvironmentInfo(),
        Path.Combine(_directory, "preferences.json"));

    private static User User() => Sati.Models.User.Create(
        41,
        "case.manager",
        "Case Manager",
        "hash",
        "salt",
        UserRole.CaseManager,
        null,
        3);

    private static ScratchpadViewModel Scratchpad() =>
        new(new StubScratchpadService(), new SessionService());

    private static DataEnvironmentInfo EnvironmentInfo() => new(
        SatiDataEnvironment.Demo,
        "SatiDemo",
        ApiBaseAddress: new Uri("https://demo.invalid"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public int LoadCount { get; private set; }

        public Task<Settings> LoadAsync()
        {
            LoadCount++;
            return Task.FromResult(new Settings());
        }

        public Task SaveAsync(Settings settings) => throw new NotSupportedException();
    }

    private sealed class StubUpcomingEventService : IUpcomingEventService
    {
        public int GenerateCount { get; private set; }
        public Exception? Exception { get; init; }

        public List<UpcomingEvent> GenerateEvents(
            IEnumerable<IEventSource> people,
            Settings settings,
            DateTime? asOf = null)
        {
            GenerateCount++;
            if (Exception is not null)
                throw Exception;
            return [];
        }
    }

    private sealed class StubAssessmentService : IComprehensiveAssessmentService
    {
        public int LoadCount { get; private set; }
        public ComprehensiveAssessment? Result { get; init; }

        public Task<ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId)
        {
            LoadCount++;
            return Task.FromResult(Result);
        }

        public Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId) =>
            throw new NotSupportedException();
        public Task SaveDocumentAsync(ComprehensiveAssessment assessment, AssessmentDocument document) =>
            throw new NotSupportedException();
        public Task SubmitForReviewAsync(ComprehensiveAssessment assessment) =>
            throw new NotSupportedException();
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
