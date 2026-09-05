using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class DashboardFormComplianceTests
{
    [Theory]
    [InlineData(CompletionPath.DashboardToggle)]
    [InlineData(CompletionPath.TaskBoard)]
    [InlineData(CompletionPath.ClientOverview)]
    public async Task EveryCompletionPathRefreshesMatrixAndRemovesLateReviewEvent(
        CompletionPath path)
    {
        await using var fixture = await NoteEntryFixture.CreateAsync();
        var harness = await DashboardHarness.CreateAsync(fixture);
        var (person, form) = harness.AddOverdueQuarterlyReview(FormType.Q3R);

        Assert.Equal(FormCellStatus.Overdue, harness.Dashboard.Matrix!.Rows.Single().Q3R.Status);
        Assert.Contains(harness.Dashboard.UpcomingEvents,
            item => item.Kind == UpcomingEventKind.LateReview && item.Title.StartsWith("Q3 Review"));

        switch (path)
        {
            case CompletionPath.DashboardToggle:
                harness.Dashboard.SelectedPerson = person;
                await harness.Dashboard.ToggleFormCommand.ExecuteAsync(FormType.Q3R);
                break;
            case CompletionPath.TaskBoard:
                await harness.Dashboard.MarkFormCompletedCommand.ExecuteAsync(new FormTaskRow(
                    form,
                    person.FullName,
                    "Q3 Review",
                    form.DueDate.AddDays(-30),
                    DateTime.Today));
                break;
            case CompletionPath.ClientOverview:
                await harness.Dashboard.Clients.ToggleFormForAsync(person, FormType.Q3R);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }

        var attestation = path == CompletionPath.ClientOverview
            ? harness.Dashboard.Clients.Attestation
            : harness.Dashboard.Attestation;
        var explicitCompletion = DateTime.Today.AddDays(-2);
        Assert.Null(form.CompletedDate);
        Assert.True(attestation.IsVisible);
        Assert.Null(attestation.CompletionDate);
        attestation.CompletionDate = explicitCompletion;
        await attestation.CompleteAttestationCommand.ExecuteAsync(null);

        Assert.Equal(explicitCompletion, form.CompletedDate);
        Assert.Equal(FormCellStatus.Complete, harness.Dashboard.Matrix.Rows.Single().Q3R.Status);
        Assert.DoesNotContain(harness.Dashboard.UpcomingEvents,
            item => item.Kind == UpcomingEventKind.LateReview && item.Title.StartsWith("Q3 Review"));
    }

    private sealed class DashboardHarness
    {
        private readonly Settings settings;
        private readonly UpcomingEventService upcomingEvents;
        private readonly MutablePersonService people;

        private DashboardHarness(
            CaseManagerDashboardViewModel dashboard,
            Settings settings,
            UpcomingEventService upcomingEvents,
            MutablePersonService people)
        {
            Dashboard = dashboard;
            this.settings = settings;
            this.upcomingEvents = upcomingEvents;
            this.people = people;
        }

        public CaseManagerDashboardViewModel Dashboard { get; }

        public static async Task<DashboardHarness> CreateAsync(NoteEntryFixture fixture)
        {
            var session = new SessionService();
            session.SetUser(fixture.CaseManagerOne);
            var people = new MutablePersonService();
            var notes = fixture.NotesFromAnotherSession();
            var settings = new Settings { ReviewDaysAfterDue = 30 };
            var settingsService = new FixedSettingsService(settings);
            var forms = new RecordingFormService();
            var exemptDates = new EmptyExemptDateService();
            var upcomingEvents = new UpcomingEventService();
            var noteEntry = fixture.NoteEntry(people: people, notes: notes);
            var notesLog = new NotesWindowViewModel(
                people, session, notes, fixture.NoteEntry(people: people, notes: notes));
            var clients = new NewClientViewModel(
                people,
                session,
                notes,
                forms,
                settingsService,
                null!,
                new StubPersonContactService(),
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!,
                null!);
            var calendar = new CalendarViewModel(exemptDates, notes, session);
            var reviews = new ReviewsViewModel(session, people, null!, settingsService, forms);
            var dashboard = new CaseManagerDashboardViewModel(
                people,
                notes,
                settingsService,
                new StubIncentiveService(),
                session,
                upcomingEvents,
                forms,
                noteEntry,
                notesLog,
                clients,
                calendar,
                exemptDates,
                null!,
                reviews,
                null!,
                null!,
                new GuidanceViewModel(),
                new HelperReferenceViewModel());

            await dashboard.InitializeAsync();
            return new DashboardHarness(dashboard, settings, upcomingEvents, people);
        }

        public (Person Person, Form Form) AddOverdueQuarterlyReview(FormType type)
        {
            var person = Person.CreatePerson(
                Dashboard.LoggedInUser!.Id,
                "Quarterly",
                "Review",
                string.Empty,
                new DateTime(1990, 1, 1),
                DateTime.Today.AddMonths(-6),
                WaiverType.Section21,
                settings);
            var form = person.GetCurrentCycleForm(type, DateTime.Today)!;
            form.DueDate = DateTime.Today.AddDays(-1);
            form.SetInitialCompletion(null);

            people.Items.Clear();
            people.Items.Add(person);
            Dashboard.People.Clear();
            Dashboard.People.Add(person);
            Dashboard.Matrix!.Rebuild(Dashboard.People, DateTime.Today);
            Dashboard.UpcomingEvents.Clear();
            foreach (var item in upcomingEvents.GenerateEvents(Dashboard.People, settings))
                Dashboard.UpcomingEvents.Add(item);

            return (person, form);
        }
    }

    public enum CompletionPath
    {
        DashboardToggle,
        TaskBoard,
        ClientOverview
    }

    private sealed class MutablePersonService : IPersonService
    {
        public List<Person> Items { get; } = [];

        public Task<Person> AddPersonAsync(Person person) => Task.FromResult(person);
        public Task<List<Person>> GetAllPeopleAsync(int userId) => Task.FromResult(Items.ToList());
        public Task<Person> EditPersonAsync(Person person) => Task.FromResult(person);
        public Task<string?> GetJournalAsync(int personId) => Task.FromResult<string?>(null);
        public Task SaveJournalAsync(int personId, string? journal) => Task.CompletedTask;
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            Task.FromResult(new JournalReminderResult(text));
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(int personId, int targetUserId, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<PersonStatusDto> SetPersonStatusAsync(
            int personId, string status, string? note, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<CredibleMatchLookupResult> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds,
            IReadOnlyList<string>? maineCareIds = null,
            IReadOnlyList<PersonNameBirthDate>? nameBirthDates = null) =>
            Task.FromResult(CredibleMatchLookupResult.Empty);
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
            Task.FromResult<List<PersonSummary>>([]);
    }

    private sealed class RecordingFormService : IFormService
    {
        public Task UpdateFormAsync(Form form) => Task.CompletedTask;
        public Task AttestAsync(Form form, DateTime completedOn, int? evidenceNoteId = null)
        {
            form.Attest(FormAttestation.Attested(
                completedOn, AttestationActorKind.CaseManager, 31, DateTime.UtcNow));
            return Task.CompletedTask;
        }
        public Task RevokeAttestationAsync(Form form, string reason)
        {
            form.RevokeAttestation(FormAttestation.Revoked(
                AttestationActorKind.CaseManager, 31, DateTime.UtcNow, reason));
            return Task.CompletedTask;
        }
        public Task OpenFormAsync(Form form) => Task.CompletedTask;
        public Task DeleteFormsAsync(IEnumerable<Form> forms) => Task.CompletedTask;
    }

    private sealed class FixedSettingsService(Settings settings) : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(settings);
        public Task SaveAsync(Settings value) => Task.CompletedTask;
    }

    private sealed class EmptyExemptDateService : IExemptDateService
    {
        public Task<List<ExemptDate>> GetByYearAsync(int userId, int year) => Task.FromResult<List<ExemptDate>>([]);
        public Task<ExemptDate> AddAsync(int userId, DateTime date, string? reason = null) =>
            throw new NotSupportedException();
        public Task RemoveAsync(int id) => throw new NotSupportedException();
    }

    private sealed class StubIncentiveService : IIncentiveService
    {
        public Task<(Incentive incentive, bool wasCreated)> GetOrCreateAsync(int userId, int month, int year) =>
            Task.FromResult((new Incentive
            {
                UserId = userId,
                Month = month,
                Year = year
            }, false));

        public Task SaveAsync(Incentive incentive) => Task.CompletedTask;
        public Task<int> GetRemainingEligibleDaysAsync(
            int month,
            int year,
            HashSet<DateTime> daysAlreadyWorked,
            HashSet<DateTime> exemptDates) => Task.FromResult(0);
        public Task<int> GetEligibleDaysAsync(DateTime startInclusive, DateTime endInclusive) => Task.FromResult(0);
        public Task<List<Incentive>> GetHistoryAsync(int userId) => Task.FromResult<List<Incentive>>([]);
    }
}
