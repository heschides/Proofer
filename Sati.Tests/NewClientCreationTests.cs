using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Xunit;

namespace Sati.Tests;

public sealed class NewClientCreationTests
{
    [Fact]
    public void OptionalEmailAllowsBlankValuesAndRejectsOnlyMalformedEntries()
    {
        var viewModel = CreateViewModel(new CountingPersonService(), new FixedSettingsService());
        viewModel.FirstName = "Jamie";
        viewModel.LastName = "River";
        viewModel.BirthDate = new DateTime(1990, 4, 3);
        viewModel.Bio = "Optional email validation regression test.";
        viewModel.Email = string.Empty;

        var blankResults = new List<ValidationResult>();
        var blankIsValid = Validator.TryValidateObject(
            viewModel,
            new ValidationContext(viewModel),
            blankResults,
            validateAllProperties: true);

        Assert.True(blankIsValid);
        Assert.DoesNotContain(blankResults, result =>
            result.ErrorMessage?.Contains("email", StringComparison.OrdinalIgnoreCase) == true);

        viewModel.Email = "not-an-email";
        var malformedResults = new List<ValidationResult>();
        var malformedIsValid = Validator.TryValidateObject(
            viewModel,
            new ValidationContext(viewModel),
            malformedResults,
            validateAllProperties: true);

        Assert.False(malformedIsValid);
        Assert.Contains(malformedResults, result =>
            result.ErrorMessage == "Enter a valid email address, or leave it blank.");
    }

    [Fact]
    public void RequiredFieldCompletionStateTracksMeaningfulInput()
    {
        var viewModel = CreateViewModel(new CountingPersonService(), new FixedSettingsService());
        var notifications = new HashSet<string>(StringComparer.Ordinal);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
                notifications.Add(args.PropertyName);
        };

        Assert.False(viewModel.IsFirstNameReady);
        Assert.False(viewModel.IsLastNameReady);
        Assert.False(viewModel.IsBirthDateReady);
        Assert.False(viewModel.IsBioReady);

        viewModel.FirstName = " ";
        Assert.False(viewModel.IsFirstNameReady);
        viewModel.FirstName = "Jamie";
        viewModel.LastName = "River";
        viewModel.BirthDate = new DateTime(1990, 4, 3);
        viewModel.Bio = "A short biography.";

        Assert.True(viewModel.IsFirstNameReady);
        Assert.True(viewModel.IsLastNameReady);
        Assert.True(viewModel.IsBirthDateReady);
        Assert.True(viewModel.IsBioReady);
        Assert.Contains(nameof(NewClientViewModel.IsFirstNameReady), notifications);
        Assert.Contains(nameof(NewClientViewModel.IsLastNameReady), notifications);
        Assert.Contains(nameof(NewClientViewModel.IsBirthDateReady), notifications);
        Assert.Contains(nameof(NewClientViewModel.IsBioReady), notifications);
    }

    [Fact]
    public async Task AdminCanMarkANewConsumerAsTestAndTheChoiceReachesTheSave()
    {
        var people = new CountingPersonService();
        var viewModel = CreateViewModel(people, new FixedSettingsService(), UserRole.Admin);
        viewModel.OpenEntryPanelCommand.Execute(null);
        viewModel.FirstName = "Synthetic";
        viewModel.LastName = "Consumer";
        viewModel.BirthDate = new DateTime(1990, 4, 3);
        viewModel.Bio = "Test-only consumer.";
        viewModel.IsTestData = true;

        Assert.True(viewModel.CanMarkNewConsumerAsTest);
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.True(people.LastAdded!.IsTestData);
    }

    [Fact]
    public async Task OrdinaryUserCannotMarkANewConsumerAsTestEvenIfThePropertyIsForged()
    {
        var people = new CountingPersonService();
        var viewModel = CreateViewModel(people, new FixedSettingsService(), UserRole.CaseManager);
        viewModel.OpenEntryPanelCommand.Execute(null);
        viewModel.FirstName = "Ordinary";
        viewModel.LastName = "Consumer";
        viewModel.BirthDate = new DateTime(1990, 4, 3);
        viewModel.Bio = "Ordinary consumer.";
        viewModel.IsTestData = true;

        Assert.False(viewModel.CanMarkNewConsumerAsTest);
        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.False(people.LastAdded!.IsTestData);
    }

    [Fact]
    public async Task PersonSaveFailureIsContainedAndExplainsThatSaveStatusMustBeChecked()
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            41,
            "case-manager",
            "Case Manager",
            "hash",
            "salt",
            UserRole.CaseManager,
            null,
            7));
        var viewModel = new NewClientViewModel(
            new ThrowingPersonService(),
            session,
            null!,
            null!,
            new FixedSettingsService(),
            null!,
            null!,
            null!,
            new RecordingIncidentReporter(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!)
        {
            FirstName = "Jamie",
            LastName = "River",
            BirthDate = new DateTime(1990, 4, 3),
            Bio = "New client creation regression test."
        };
        ClientSaveProblem? shownProblem = null;
        viewModel.ClientSaveProblemOccurred += (_, args) => shownProblem = args.Problem;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(shownProblem);
        Assert.True(shownProblem.SaveStatusUnknown);
        Assert.Contains("WHAT WAS SAVED", shownProblem.Message);
        Assert.Contains("WHAT WENT WRONG", shownProblem.Message);
        Assert.Contains("BEST FIX", shownProblem.Message);
        Assert.Contains("Refresh the client list", shownProblem.Message);
        Assert.True(viewModel.HasClientSaveError);
        Assert.Empty(viewModel.People);
    }

    [Fact]
    public async Task MissingRequiredFieldsExplainExactlyWhyNoSaveWasAttempted()
    {
        var people = new CountingPersonService();
        var viewModel = CreateViewModel(people, new FixedSettingsService());
        ClientSaveProblem? shownProblem = null;
        viewModel.ClientSaveProblemOccurred += (_, args) => shownProblem = args.Problem;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(0, people.AddCalls);
        Assert.NotNull(shownProblem);
        Assert.False(shownProblem.SaveStatusUnknown);
        Assert.Contains("First name is required", shownProblem.Message);
        Assert.Contains("Last name is required", shownProblem.Message);
        Assert.Contains("Birthdate is required", shownProblem.Message);
        Assert.Contains("No client record", shownProblem.Message);
    }

    [Fact]
    public async Task SettingsFailureStopsBeforeSaveAndOffersMigrationRecovery()
    {
        var settings = new FirstThenThrowSettingsService();
        var people = new CountingPersonService();
        var viewModel = CreateViewModel(people, settings);
        await settings.FirstLoadCompleted.Task;
        viewModel.FirstName = "Jamie";
        viewModel.LastName = "River";
        viewModel.BirthDate = new DateTime(1990, 4, 3);
        viewModel.Bio = "Settings failure regression test.";
        viewModel.Waiver = WaiverType.Section21;
        viewModel.EffectiveDateText = DateTime.Today.AddMonths(-2).ToString("MM/dd");
        ClientSaveProblem? shownProblem = null;
        viewModel.ClientSaveProblemOccurred += (_, args) => shownProblem = args.Problem;

        await viewModel.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(0, people.AddCalls);
        Assert.NotNull(shownProblem);
        Assert.False(shownProblem.SaveStatusUnknown);
        Assert.Contains("form-deadline settings", shownProblem.Message);
        Assert.Contains("Close and reopen Sati", shownProblem.Message);
    }

    [Fact]
    public async Task LocalCreationCommitsClientFormsLifecycleAndAuditTogether()
    {
        await using var fixture = await LocalPersonFixture.CreateAsync(seedActor: true);
        var effectiveDate = DateTime.Today.AddMonths(-2);
        var person = Person.CreatePerson(
            fixture.Actor.Id,
            "Jamie",
            "River",
            "A client saved by the local transaction test.",
            new DateTime(1990, 4, 3),
            effectiveDate,
            WaiverType.Section21,
            new Settings());
        person.AgencyId = 999; // The service must replace caller-supplied agency scope.

        var saved = await fixture.Service.AddPersonAsync(person);

        await using var db = fixture.Factory.CreateDbContext();
        var stored = await db.People.AsNoTracking().SingleAsync();
        Assert.Equal(saved.Id, stored.Id);
        Assert.Equal(fixture.Actor.Id, stored.UserId);
        Assert.Equal(fixture.Actor.AgencyId, stored.AgencyId);
        Assert.Equal(PersonSaveRules.FormTypes.Count, await db.Forms.CountAsync());
        Assert.Single(await db.PersonVersions.AsNoTracking().ToListAsync());
        Assert.Single(await db.AuditEvents.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task LocalAdminCanCreateAMarkedTestConsumerAndLifecycleRecordsTheDesignation()
    {
        await using var fixture = await LocalPersonFixture.CreateAsync(true, UserRole.Admin);
        var person = Person.CreatePerson(
            fixture.Actor.Id,
            "Synthetic",
            "Consumer",
            "Admin-created test record.",
            new DateTime(1990, 4, 3),
            null,
            WaiverType.None,
            new Settings());
        person.IsTestData = true;

        await fixture.Service.AddPersonAsync(person);

        await using var db = fixture.Factory.CreateDbContext();
        Assert.True((await db.People.AsNoTracking().SingleAsync()).IsTestData);
        var version = await db.PersonVersions.AsNoTracking().SingleAsync();
        var changes = PersonLifecycleLedger.ToDto(version).Changes;
        Assert.Contains(changes, change => change.Field == "isTestData" && change.NewValue == "Yes");
    }

    [Fact]
    public async Task LocalCaseManagerCannotForgeTheTestMarker()
    {
        await using var fixture = await LocalPersonFixture.CreateAsync(true);
        var person = Person.CreatePerson(
            fixture.Actor.Id,
            "Forged",
            "Marker",
            "Must not be saved.",
            new DateTime(1990, 4, 3),
            null,
            WaiverType.None,
            new Settings());
        person.IsTestData = true;

        var error = await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.AddPersonAsync(person));

        Assert.Contains("isTestData", error.Errors.Keys);
        await fixture.AssertCreationTablesEmptyAsync();
    }

    [Fact]
    public async Task TestDesignationCannotBeAddedDuringALaterProfileEdit()
    {
        await using var fixture = await LocalPersonFixture.CreateAsync(true, UserRole.Admin);
        var person = Person.CreatePerson(
            fixture.Actor.Id,
            "Ordinary",
            "Consumer",
            "Created without a test designation.",
            new DateTime(1990, 4, 3),
            null,
            WaiverType.None,
            new Settings());
        await fixture.Service.AddPersonAsync(person);
        person.IsTestData = true;

        var error = await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.EditPersonAsync(person));

        Assert.Contains("isTestData", error.Errors.Keys);
        await using var db = fixture.Factory.CreateDbContext();
        Assert.False((await db.People.AsNoTracking().SingleAsync()).IsTestData);
    }

    [Fact]
    public async Task LocalCreationRejectsCallerSuppliedOwnerBeforeWritingAnything()
    {
        await using var fixture = await LocalPersonFixture.CreateAsync(seedActor: true);
        var person = Person.CreatePerson(
            fixture.Actor.Id + 1,
            "Wrong",
            "Owner",
            "Ownership scope regression test.",
            new DateTime(1990, 4, 3),
            null,
            WaiverType.None,
            new Settings());

        var error = await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.AddPersonAsync(person));

        Assert.Contains("owner", error.Errors.Keys);
        await fixture.AssertCreationTablesEmptyAsync();
    }

    [Fact]
    public async Task LocalCreationUsesSharedFieldValidationBeforeWriting()
    {
        await using var fixture = await LocalPersonFixture.CreateAsync(seedActor: true);
        var person = Person.CreatePerson(
            fixture.Actor.Id,
            "Jamie",
            "River",
            "Field validation regression test.",
            new DateTime(1990, 4, 3),
            null,
            WaiverType.None,
            new Settings());
        person.PhoneNumber = new string('1', PersonSaveRules.PhoneMaxLength + 1);

        var error = await Assert.ThrowsAsync<PersonValidationException>(
            () => fixture.Service.AddPersonAsync(person));

        Assert.Contains("phoneNumber", error.Errors.Keys);
        await fixture.AssertCreationTablesEmptyAsync();
    }

    [Fact]
    public async Task DatabaseRejectionLeavesNoPartialClientGraph()
    {
        // The signed-in actor intentionally is not seeded as a User. SQLite's
        // foreign-key rejection happens during SaveChanges after EF has staged the
        // Person, forms, lifecycle version, and audit event.
        await using var fixture = await LocalPersonFixture.CreateAsync(seedActor: false);
        var person = Person.CreatePerson(
            fixture.Actor.Id,
            "Jamie",
            "River",
            "Atomic rollback regression test.",
            new DateTime(1990, 4, 3),
            DateTime.Today.AddMonths(-2),
            WaiverType.Section21,
            new Settings());

        await Assert.ThrowsAsync<PersonPersistenceException>(
            () => fixture.Service.AddPersonAsync(person));

        await fixture.AssertCreationTablesEmptyAsync();
    }

    [Fact]
    public void GeneratedAdmissionFormsNeverClaimCompletionInTheFutureOrWithoutADate()
    {
        var pastForms = Person.GenerateFormList(DateTime.Today.AddMonths(-1), new Settings());
        var futureForms = Person.GenerateFormList(DateTime.Today.AddMonths(1), new Settings());

        Assert.All(pastForms, form => Assert.Equal(form.IsCompliant, form.CompletedDate.HasValue));
        Assert.All(pastForms.Where(form => form.CompletedDate.HasValue), form =>
            Assert.True(form.CompletedDate!.Value.Date <= DateTime.Today));
        Assert.All(futureForms, form =>
        {
            Assert.False(form.IsCompliant);
            Assert.Null(form.CompletedDate);
        });
    }

    private sealed class FixedSettingsService : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(new Settings());
        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private static NewClientViewModel CreateViewModel(
        IPersonService personService,
        ISettingsService settingsService,
        UserRole role = UserRole.CaseManager)
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            41,
            "case-manager",
            "Case Manager",
            "hash",
            "salt",
            role,
            null,
            7));
        return new NewClientViewModel(
            personService,
            session,
            null!,
            null!,
            settingsService,
            null!,
            null!,
            null!,
            new RecordingIncidentReporter(),
            null!,
            null!,
            null!,
            null!,
            null!,
            null!);
    }

    private sealed class FirstThenThrowSettingsService : ISettingsService
    {
        private int _loads;
        public TaskCompletionSource FirstLoadCompleted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Settings> LoadAsync()
        {
            if (Interlocked.Increment(ref _loads) == 1)
            {
                FirstLoadCompleted.TrySetResult();
                return Task.FromResult(new Settings());
            }

            return Task.FromException<Settings>(
                new InvalidOperationException("simulated missing Settings column"));
        }

        public Task SaveAsync(Settings settings) => Task.CompletedTask;
    }

    private sealed class ThrowingPersonService : IPersonService
    {
        public Task<Person> AddPersonAsync(Person person) =>
            Task.FromException<Person>(new InvalidOperationException("simulated persistence failure"));

        public Task<Person> EditPersonAsync(Person person) => throw new NotSupportedException();
        public Task<List<Person>> GetAllPeopleAsync(int userId) => throw new NotSupportedException();
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) => throw new NotSupportedException();
        public Task<string?> GetJournalAsync(int personId) => throw new NotSupportedException();
        public Task SaveJournalAsync(int personId, string? journal) => throw new NotSupportedException();
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            throw new NotSupportedException();
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(int personId, int targetUserId, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CredibleClientMatchDto>> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds) =>
            Task.FromResult<IReadOnlyList<CredibleClientMatchDto>>([]);
    }

    private sealed class CountingPersonService : IPersonService
    {
        public int AddCalls { get; private set; }
        public Person? LastAdded { get; private set; }

        public Task<Person> AddPersonAsync(Person person)
        {
            AddCalls++;
            LastAdded = person;
            return Task.FromResult(person);
        }

        public Task<Person> EditPersonAsync(Person person) => throw new NotSupportedException();
        public Task<List<Person>> GetAllPeopleAsync(int userId) => throw new NotSupportedException();
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) => throw new NotSupportedException();
        public Task<string?> GetJournalAsync(int personId) => throw new NotSupportedException();
        public Task SaveJournalAsync(int personId, string? journal) => throw new NotSupportedException();
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            throw new NotSupportedException();
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(int personId, int targetUserId, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CredibleClientMatchDto>> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds) =>
            Task.FromResult<IReadOnlyList<CredibleClientMatchDto>>([]);
    }

    private sealed class RecordingIncidentReporter : IIncidentReporter
    {
        public Task ReportAsync(
            Exception exception,
            string operation,
            string reference,
            string severity = "Error",
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LocalPersonFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private LocalPersonFixture(
            SqliteConnection connection,
            IDbContextFactory<SatiContext> factory,
            User actor)
        {
            _connection = connection;
            Factory = factory;
            Actor = actor;
            var session = new SessionService();
            session.SetUser(actor);
            Service = new PersonService(factory, new FixedSettingsService(), session);
        }

        public IDbContextFactory<SatiContext> Factory { get; }
        public User Actor { get; }
        public PersonService Service { get; }

        public static async Task<LocalPersonFixture> CreateAsync(
            bool seedActor,
            UserRole role = UserRole.CaseManager)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<SatiContext>()
                .UseSqlite(connection)
                .Options;
            var factory = new LocalContextFactory(options);
            var actor = User.Create(
                41,
                "case-manager",
                "Case Manager",
                "hash",
                "salt",
                role,
                null,
                7);

            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.Add(new Agency { Id = 7, Name = "Agency Seven" });
            if (seedActor)
                db.Users.Add(actor);
            await db.SaveChangesAsync();
            return new LocalPersonFixture(connection, factory, actor);
        }

        public async Task AssertCreationTablesEmptyAsync()
        {
            await using var db = Factory.CreateDbContext();
            Assert.Empty(await db.People.AsNoTracking().ToListAsync());
            Assert.Empty(await db.Forms.AsNoTracking().ToListAsync());
            Assert.Empty(await db.PersonVersions.AsNoTracking().ToListAsync());
            Assert.Empty(await db.AuditEvents.AsNoTracking().ToListAsync());
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

        private sealed class LocalContextFactory(DbContextOptions<SatiContext> options)
            : IDbContextFactory<SatiContext>
        {
            public SatiContext CreateDbContext() => new(options);
        }
    }
}
