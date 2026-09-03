using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Admin;
using Xunit;

namespace Sati.Tests;

public sealed class AdminTestDataDeletionViewModelTests
{
    [Fact]
    public async Task DeleteFailsClosedWhenNoConfirmationViewIsAttached()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        SelectTestConsumer(viewModel, service.Person);

        await viewModel.DeleteTestConsumerCommand.ExecuteAsync(null);

        Assert.Equal(0, service.DeleteCalls);
        Assert.True(viewModel.HasSelectedPerson);
    }

    [Fact]
    public async Task ConfirmationNamesTheConsumerUsesExactWarningAndCancelWritesNothing()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        SelectTestConsumer(viewModel, service.Person);
        AdminTestConsumerDeletionConfirmationEventArgs? shown = null;
        viewModel.TestConsumerDeletionConfirmationRequested += (_, args) => shown = args;

        await viewModel.DeleteTestConsumerCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal(service.Person.PersonId, shown.PersonId);
        Assert.Equal(service.Person.DisplayName, shown.DisplayName);
        Assert.Equal(TestDataDeletionRules.ConsumerConfirmationText, shown.Message);
        Assert.False(shown.Confirmed);
        Assert.Equal(0, service.DeleteCalls);
    }

    [Fact]
    public async Task ExplicitConfirmationDeletesThenRefreshesAndReportsSuccess()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        SelectTestConsumer(viewModel, service.Person);
        viewModel.TestConsumerDeletionConfirmationRequested += (_, args) => args.Confirmed = true;

        await viewModel.DeleteTestConsumerCommand.ExecuteAsync(null);

        Assert.Equal(1, service.DeleteCalls);
        Assert.Equal(service.Person.PersonId, service.DeletedPersonId);
        Assert.Equal(service.Person.Revision, service.DeletedRevision);
        Assert.Equal(TestDataDeletionRules.ConsumerAttestation, service.Attestation);
        Assert.Empty(viewModel.People);
        Assert.Null(viewModel.SelectedPerson);
        Assert.True(viewModel.HasNotice);
        Assert.Contains("Deleted test consumer", viewModel.NoticeMessage);
        Assert.Contains("2 related test records", viewModel.NoticeMessage);
        Assert.Contains("audit event was retained", viewModel.NoticeMessage);
        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task ServiceFailureKeepsSelectionAndShowsWhatWasNotDeleted()
    {
        var service = new RecordingAdminService { DeleteFailure = TestDataDeletionRules.ConsumerHasClaimsMessage };
        var viewModel = CreateViewModel(service);
        SelectTestConsumer(viewModel, service.Person);
        viewModel.TestConsumerDeletionConfirmationRequested += (_, args) => args.Confirmed = true;

        await viewModel.DeleteTestConsumerCommand.ExecuteAsync(null);

        Assert.Equal(1, service.DeleteCalls);
        Assert.Equal(service.Person.PersonId, viewModel.SelectedPerson?.PersonId);
        Assert.True(viewModel.HasError);
        Assert.Contains("The consumer was not deleted", viewModel.StatusMessage);
        Assert.Contains("billing claim records", viewModel.StatusMessage);
        Assert.False(viewModel.HasNotice);
    }

    [Fact]
    public async Task UnmarkedConsumerCannotRunTheDeletionCommand()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        var ordinary = service.Person with { IsTestData = false };
        SelectTestConsumer(viewModel, ordinary);

        Assert.False(viewModel.DeleteTestConsumerCommand.CanExecute(null));
        await viewModel.DeleteTestConsumerCommand.ExecuteAsync(null);
        Assert.Equal(0, service.DeleteCalls);
    }

    // ---- Rule-3 deletion: DeleteConsumerInWindowCommand ----

    [Fact]
    public void ARecentlyCreatedConsumerWithNoReasonCannotRunTheCommand()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        var recent = service.Person with { CreatedAtUtc = DateTime.UtcNow.AddDays(-5) };
        SelectTestConsumer(viewModel, recent);

        Assert.True(viewModel.IsSelectedPersonWithinDeletionWindow);
        Assert.False(viewModel.DeleteConsumerInWindowCommand.CanExecute(null));
    }

    [Fact]
    public void AConsumerOutsideTheWindowCannotRunTheCommandEvenWithAReason()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        var stale = service.Person with { CreatedAtUtc = DateTime.UtcNow.AddDays(-25) };
        SelectTestConsumer(viewModel, stale);
        viewModel.ConsumerDeletionReason = "Duplicate from a batch import.";

        Assert.False(viewModel.IsSelectedPersonWithinDeletionWindow);
        Assert.False(viewModel.DeleteConsumerInWindowCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmationPromptsForTheExactDisplayNameAndCancelWritesNothing()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        var recent = service.Person with { CreatedAtUtc = DateTime.UtcNow.AddDays(-5) };
        SelectTestConsumer(viewModel, recent);
        viewModel.ConsumerDeletionReason = "Duplicate from a batch import.";
        AdminConsumerDeletionConfirmationEventArgs? shown = null;
        viewModel.ConsumerDeletionConfirmationRequested += (_, args) => shown = args;

        await viewModel.DeleteConsumerInWindowCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Equal(recent.PersonId, shown.PersonId);
        Assert.Equal(recent.DisplayName, shown.RequiredConfirmationText);
        Assert.Contains("cannot be undone", shown.Message);
        Assert.False(shown.Confirmed);
        Assert.Equal(0, service.WindowDeleteCalls);
    }

    [Fact]
    public async Task ExplicitConfirmationDeletesWithTheTypedReasonThenRefreshesAndReportsSuccess()
    {
        var service = new RecordingAdminService();
        var viewModel = CreateViewModel(service);
        var recent = service.Person with { CreatedAtUtc = DateTime.UtcNow.AddDays(-5) };
        SelectTestConsumer(viewModel, recent);
        viewModel.ConsumerDeletionReason = "Duplicate from a batch import.";
        viewModel.ConsumerDeletionConfirmationRequested += (_, args) => args.Confirmed = true;

        await viewModel.DeleteConsumerInWindowCommand.ExecuteAsync(null);

        Assert.Equal(1, service.WindowDeleteCalls);
        Assert.Equal(recent.PersonId, service.WindowDeletedPersonId);
        Assert.Equal(recent.Revision, service.WindowDeletedRevision);
        Assert.Equal(ConsumerDeletionRules.ConsumerAttestation, service.WindowAttestation);
        Assert.Equal("Duplicate from a batch import.", service.WindowReason);
        Assert.Empty(viewModel.People);
        Assert.Null(viewModel.SelectedPerson);
        Assert.True(viewModel.HasNotice);
        Assert.Contains("Deleted", viewModel.NoticeMessage);
        Assert.Contains("audit event was retained", viewModel.NoticeMessage);
        Assert.False(viewModel.HasError);
        Assert.Equal(string.Empty, viewModel.ConsumerDeletionReason);
    }

    [Fact]
    public async Task ServiceRefusalKeepsSelectionAndShowsWhatWasNotDeleted()
    {
        var service = new RecordingAdminService
        {
            WindowDeleteFailure = ConsumerDeletionRules.TransmittedBillingMessage
        };
        var viewModel = CreateViewModel(service);
        var recent = service.Person with { CreatedAtUtc = DateTime.UtcNow.AddDays(-5) };
        SelectTestConsumer(viewModel, recent);
        viewModel.ConsumerDeletionReason = "Duplicate from a batch import.";
        viewModel.ConsumerDeletionConfirmationRequested += (_, args) => args.Confirmed = true;

        await viewModel.DeleteConsumerInWindowCommand.ExecuteAsync(null);

        Assert.Equal(1, service.WindowDeleteCalls);
        Assert.Equal(recent.PersonId, viewModel.SelectedPerson?.PersonId);
        Assert.True(viewModel.HasError);
        Assert.Contains("The consumer was not deleted", viewModel.StatusMessage);
        Assert.Contains("reached a payer", viewModel.StatusMessage);
        Assert.False(viewModel.HasNotice);
    }

    private static AdminDashboardViewModel CreateViewModel(RecordingAdminService service)
    {
        var session = new SessionService();
        session.SetUser(User.Create(
            91,
            "admin-view-model",
            "Admin",
            "hash",
            "salt",
            UserRole.Admin,
            null,
            7));
        return new AdminDashboardViewModel(service, session);
    }

    private static void SelectTestConsumer(
        AdminDashboardViewModel viewModel,
        AdminPersonListItemDto person)
    {
        viewModel.People.Add(person);
        viewModel.SelectedPerson = person;
    }

    private sealed class RecordingAdminService : IAdminService
    {
        public AdminPersonListItemDto Person { get; } =
            new(41, "River, Jamie", 7, 12, "Case Manager", true);
        public int DeleteCalls { get; private set; }
        public int DeletedPersonId { get; private set; }
        public int DeletedRevision { get; private set; }
        public string? Attestation { get; private set; }
        public string? DeleteFailure { get; init; }
        private bool Deleted { get; set; }

        public int WindowDeleteCalls { get; private set; }
        public int WindowDeletedPersonId { get; private set; }
        public int WindowDeletedRevision { get; private set; }
        public string? WindowAttestation { get; private set; }
        public string? WindowReason { get; private set; }
        public string? WindowDeleteFailure { get; init; }

        public Task<TestConsumerDeletionResultDto> DeleteTestConsumerAsync(
            int personId,
            int expectedRevision,
            string attestation,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            DeletedPersonId = personId;
            DeletedRevision = expectedRevision;
            Attestation = attestation;
            if (DeleteFailure is not null)
                throw new InvalidOperationException(DeleteFailure);
            Deleted = true;
            return Task.FromResult(new TestConsumerDeletionResultDto(
                personId, 1, 1, 0, 0, 0, 0, 0, 0, 0));
        }

        public Task<AdminOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminOverviewDto(7, "Agency", 1, 1, Deleted ? 0 : 1, 0, 0, 0, 0, 0, null));

        public Task<AdminOperationsDto> GetOperationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new AdminOperationsDto(
                DateTime.UtcNow,
                "Healthy",
                "PolicyOnly",
                OperationalPolicyDefaults.AuditRetentionDays,
                OperationalPolicyDefaults.EdiReplayRetentionDays,
                0,
                0,
                0,
                null,
                null));

        public Task<AdminIncidentDashboardDto> GetIncidentsAsync(
            int days = 30,
            int take = 250,
            CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            return Task.FromResult(new AdminIncidentDashboardDto(
                now,
                IncidentHealthScoring.Calculate([], now, days),
                []));
        }

        public Task<IncidentGroupDto> UpdateIncidentStatusAsync(
            long incidentId,
            string status,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<byte[]> ExportAuditCsvAsync(
            DateTime fromUtc,
            DateTime toUtc,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<AdminPersonListItemDto>> GetPeopleAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Deleted ? new List<AdminPersonListItemDto>() : [Person]);

        public Task<List<AdminActivityDto>> GetActivityAsync(
            int days = 30,
            int take = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<AdminActivityDto>());

        public Task<List<PersonVersionDto>> GetPersonHistoryAsync(
            int personId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<PersonVersionDto>());

        public Task<byte[]> ExportPersonHistoryPdfAsync(
            int personId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LegalHoldDto> PlaceLegalHoldAsync(
            PlaceLegalHoldRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LegalHoldDto> ReleaseLegalHoldAsync(
            int legalHoldId, string? releaseNote, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<List<LegalHoldDto>> GetLegalHoldsAsync(
            int personId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConsumerDeletionResultDto> DeleteConsumerInWindowAsync(
            int personId, int expectedRevision, string attestation, string reason,
            CancellationToken cancellationToken = default)
        {
            WindowDeleteCalls++;
            WindowDeletedPersonId = personId;
            WindowDeletedRevision = expectedRevision;
            WindowAttestation = attestation;
            WindowReason = reason;
            if (WindowDeleteFailure is not null)
                throw new InvalidOperationException(WindowDeleteFailure);
            Deleted = true;
            return Task.FromResult(new ConsumerDeletionResultDto(
                personId, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        }
    }
}
