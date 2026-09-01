using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Supervisor;
using System.Security;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The caseload distribution screen: the second half of the Credible import flow, where a
/// supervisor hands out the batch they imported onto their own account.
///
/// <para>
/// These are presentation tests. Nothing here proves a transfer is authorized — that is
/// <c>CaseloadTransferRules</c>, covered by <c>CaseloadTransferServiceTests</c> and
/// <c>CaseloadTransferApiTests</c> against real databases. What matters here is that the screen
/// asks for the right moves, reports each one honestly, and does not quietly drop a failure.
/// </para>
/// </summary>
public sealed class CaseloadDistributionViewModelTests
{
    private const int AgencyOne = 1;
    private const int AgencyTwo = 2;
    private const int SupervisorId = 10;
    private const int SuperviseeAId = 11;
    private const int SuperviseeBId = 12;

    [Fact]
    public async Task ItLoadsOnlyTheSignedInSupervisorsOwnCaseload()
    {
        var people = new FakePersonService();
        var model = Build(people, Supervisor());

        await model.InitializeAsync();

        Assert.Equal(SupervisorId, people.RequestedUserId);
        Assert.Equal(["Alpha Consumer", "Beta Consumer"], model.Consumers.Select(c => c.FullName));
    }

    // Presentation only, but getting it wrong offers a supervisor choices the server will
    // refuse, which reads as the feature being broken rather than as a permission boundary.
    [Fact]
    public async Task TheTargetListOffersOnlyReachableCaseManagers()
    {
        var model = Build(new FakePersonService(), Supervisor());

        await model.InitializeAsync();

        Assert.Equal(["Supervisee A", "Supervisee B"], model.Targets.Select(t => t.DisplayName));
    }

    [Fact]
    public async Task ADirectorSeesEveryCaseManagerInTheAgency()
    {
        var director = User.Create(
            SupervisorId, "director", "Director", "h", "s", UserRole.Director, null, AgencyOne);
        var model = Build(new FakePersonService(), director);

        await model.InitializeAsync();

        Assert.Contains(model.Targets, target => target.DisplayName == "Unsupervised Manager");
    }

    [Fact]
    public async Task NothingCanBeDistributedUntilBothATargetAndAConsumerAreChosen()
    {
        var model = Build(new FakePersonService(), Supervisor());
        await model.InitializeAsync();

        Assert.False(model.CanDistribute);

        model.Consumers[0].IsSelected = true;
        Assert.False(model.CanDistribute);

        model.SelectedTarget = model.Targets[0];
        Assert.True(model.CanDistribute);
    }

    [Fact]
    public async Task ItMovesOnlyTheSelectedConsumersAndAssertsTheRevisionItLoaded()
    {
        var people = new FakePersonService();
        var model = Build(people, Supervisor());
        await model.InitializeAsync();

        model.Consumers[1].IsSelected = true;
        model.SelectedTarget = model.Targets.Single(t => t.Id == SuperviseeBId);
        await model.DistributeCommand.ExecuteAsync(null);

        var transfer = Assert.Single(people.Transfers);
        Assert.Equal(902, transfer.PersonId);
        Assert.Equal(SuperviseeBId, transfer.TargetUserId);

        // The revision from the list load, not one re-read moments before the call. A token
        // fetched immediately beforehand would make the concurrency check meaningless.
        Assert.Equal(7, transfer.ExpectedRevision);
    }

    // The reason distribution is per-record rather than a batch: one consumer edited elsewhere
    // must not take the rest of the batch down with it, and must not vanish silently either.
    [Fact]
    public async Task AFailureOnOneConsumerLeavesTheOthersMovedAndIsReportedAgainstThatRow()
    {
        var people = new FakePersonService();
        people.FailFor(901, "This Person was changed after you opened it.");
        var model = Build(people, Supervisor());
        await model.InitializeAsync();

        model.SelectAllCommand.Execute(null);
        model.SelectedTarget = model.Targets[0];
        await model.DistributeCommand.ExecuteAsync(null);

        // The healthy consumer still moved; the failure did not abort the batch.
        var moved = Assert.Single(people.Transfers);
        Assert.Equal(902, moved.PersonId);

        var failedRow = model.Consumers.Single(c => c.PersonId == 901);
        Assert.True(failedRow.Failed);
        Assert.Contains("changed after you opened it", failedRow.Outcome);

        Assert.Contains("1 moved", model.StatusMessage);
        Assert.Contains("1 could not be moved", model.StatusMessage);
    }

    [Fact]
    public async Task AWhollySuccessfulDistributionSaysSoWithoutMentioningFailures()
    {
        var model = Build(new FakePersonService(), Supervisor());
        await model.InitializeAsync();

        model.SelectAllCommand.Execute(null);
        model.SelectedTarget = model.Targets.Single(t => t.Id == SuperviseeAId);
        await model.DistributeCommand.ExecuteAsync(null);

        Assert.Equal("2 consumers moved to Supervisee A.", model.StatusMessage);
    }

    // The moved consumers are no longer this supervisor's, so the list has to be re-read rather
    // than patched. The outcome messages must survive that reload or the screen reports nothing.
    [Fact]
    public async Task TheListIsReloadedAfterASuccessfulMoveAndOutcomesSurvive()
    {
        var people = new FakePersonService();
        people.FailFor(901, "That consumer or case manager is not on your team.");
        var model = Build(people, Supervisor());
        await model.InitializeAsync();

        model.SelectAllCommand.Execute(null);
        model.SelectedTarget = model.Targets[0];
        await model.DistributeCommand.ExecuteAsync(null);

        Assert.Equal(2, people.LoadCount);
        var failedRow = model.Consumers.Single(c => c.PersonId == 901);
        Assert.True(failedRow.Failed);
        Assert.False(string.IsNullOrWhiteSpace(failedRow.Outcome));
    }

    [Fact]
    public async Task AnEmptyCaseloadSaysSoRatherThanShowingAnEmptyList()
    {
        var people = new FakePersonService { People = [] };
        var model = Build(people, Supervisor());

        await model.InitializeAsync();

        Assert.False(model.HasConsumers);
        Assert.Contains("not holding any consumers", model.StatusMessage);
    }

    // ---- Helpers ----

    private static User Supervisor() => User.Create(
        SupervisorId, "supervisor", "Supervisor", "h", "s", UserRole.Supervisor, null, AgencyOne);

    private static CaseloadDistributionViewModel Build(FakePersonService people, User actor)
    {
        var session = new SessionService();
        session.SetUser(actor);
        return new CaseloadDistributionViewModel(people, new FakeUserService(), session);
    }

    private sealed record TransferCall(int PersonId, int TargetUserId, int ExpectedRevision);

    private sealed class FakePersonService : IPersonService
    {
        private readonly Dictionary<int, string> _failures = [];

        public List<PersonSummary> People { get; set; } =
        [
            new() { Id = 901, UserId = SupervisorId, FirstName = "Alpha", LastName = "Consumer", Revision = 3 },
            new() { Id = 902, UserId = SupervisorId, FirstName = "Beta", LastName = "Consumer", Revision = 7 }
        ];

        public List<TransferCall> Transfers { get; } = [];
        public int? RequestedUserId { get; private set; }
        public int LoadCount { get; private set; }

        public void FailFor(int personId, string message) => _failures[personId] = message;

        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId)
        {
            RequestedUserId = userId;
            LoadCount++;
            // Moved consumers leave this caseload, exactly as the real read would report.
            var moved = Transfers.Select(transfer => transfer.PersonId).ToHashSet();
            return Task.FromResult(People.Where(person => !moved.Contains(person.Id)).ToList());
        }

        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(
            int personId, int targetUserId, int expectedRevision)
        {
            if (_failures.TryGetValue(personId, out var message))
                return Task.FromException<CaseloadOwnershipDto>(new InvalidOperationException(message));

            Transfers.Add(new TransferCall(personId, targetUserId, expectedRevision));
            return Task.FromResult(new CaseloadOwnershipDto(personId, targetUserId, expectedRevision + 1));
        }

        public Task<Person> AddPersonAsync(Person person) => throw new NotSupportedException();
        public Task<Person> EditPersonAsync(Person person) => throw new NotSupportedException();
        public Task<List<Person>> GetAllPeopleAsync(int userId) => throw new NotSupportedException();
        public Task<string?> GetJournalAsync(int personId) => throw new NotSupportedException();
        public Task SaveJournalAsync(int personId, string? journal) => throw new NotSupportedException();
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUserService : IUserService
    {
        public Task<List<User>> GetAllAsync() => Task.FromResult(new List<User>
        {
            Supervisor(),
            User.Create(SuperviseeAId, "a", "Supervisee A", "h", "s",
                UserRole.CaseManager, SupervisorId, AgencyOne),
            User.Create(SuperviseeBId, "b", "Supervisee B", "h", "s",
                UserRole.CaseManager, SupervisorId, AgencyOne),
            // Same agency, real case manager, reports to nobody. A plain supervisor must not
            // see them; a Director must.
            User.Create(13, "c", "Unsupervised Manager", "h", "s",
                UserRole.CaseManager, null, AgencyOne),
            // Right supervisor link, wrong agency. Tenant isolation, not just reporting lines.
            User.Create(14, "d", "Other Agency Manager", "h", "s",
                UserRole.CaseManager, SupervisorId, AgencyTwo),
            // Reports to this supervisor but cannot work a caseload.
            BillingOnly()
        });

        private static User BillingOnly()
        {
            var user = User.Create(15, "e", "Billing Only", "h", "s",
                UserRole.CaseManager, SupervisorId, AgencyOne);
            user.Permissions = UserPermissions.Billing;
            return user;
        }

        public Task<User> CreateAsync(AgencyActor actor, User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => throw new NotSupportedException();
        public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task UpdateAsync(AgencyActor actor, User user) => throw new NotSupportedException();
        public Task UpdateOwnContactDetailsAsync(AgencyActor actor, User user) =>
            throw new NotSupportedException();
        public Task ResetPasswordAsync(AgencyActor actor, User user, SecureString newPassword) =>
            throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) =>
            throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) => throw new NotSupportedException();
    }
}
