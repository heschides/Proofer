using Sati.Helpers;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

public sealed class ReviewAttestationTests
{
    [Fact]
    public void LoggedLegendDoesNotClaimTheQuarterlyAttestationIsComplete()
    {
        var xaml = File.ReadAllText(Path.Combine(RepositoryRoot(), "Views", "ReviewsView.xaml"));

        Assert.DoesNotContain(
            "this quarter's 90-day review is complete",
            xaml,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("QnR attestation", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewRowUsesTheMatrixStatusOwnerForItsCurrentQuarter()
    {
        var today = new DateTime(2026, 8, 31);
        var settings = new Settings();
        var person = Person.CreatePerson(
            31,
            "Quarterly",
            "Review",
            string.Empty,
            new DateTime(1990, 1, 1),
            today.AddMonths(-6),
            WaiverType.Section21,
            settings);
        var row = new ReviewClientRowViewModel(person, today, settings);
        var form = person.GetCurrentCycleForm(QuarterType(row.CurrentQuarter!.Value), today);
        var statusProperty = typeof(ReviewClientRowViewModel).GetProperty("CurrentQuarterStatus");

        Assert.NotNull(form);
        Assert.NotNull(statusProperty);
        Assert.Equal(
            FormCellStatusCalculator.Compute(form, today),
            statusProperty.GetValue(row));
    }

    [Fact]
    public void LoggingReviewEvidenceDoesNotCompleteQuarterAttestationByDesign()
    {
        var today = new DateTime(2026, 8, 31);
        var settings = new Settings();
        var person = Person.CreatePerson(
            31,
            "Quarterly",
            "Review",
            string.Empty,
            new DateTime(1990, 1, 1),
            today.AddMonths(-6),
            WaiverType.Section21,
            settings);
        var quarter = person.GetCurrentQuarter(today)!.Value;
        var form = person.GetCurrentCycleForm(QuarterType(quarter), today)!;
        var evidence = new ReviewItem(
            person.Id,
            person.GetCurrentCycleBoundaries(today)!.Value.cycleStart,
            quarter,
            ReviewCategory.GoalReview);

        evidence.MarkLogged(today.AddDays(-1));

        Assert.Equal(ReviewStage.Logged, evidence.Stage);
        Assert.False(form.IsCompliant);
        Assert.Null(form.CompletedDate);
    }

    [Fact]
    public async Task ReviewsTabRequiresAndStoresTheExplicitLateCompletionDate()
    {
        var today = DateTime.Today;
        var settings = new Settings();
        var person = Person.CreatePerson(
            31,
            "Quarterly",
            "Review",
            string.Empty,
            new DateTime(1990, 1, 1),
            today.AddMonths(-6),
            WaiverType.Section21,
            settings);
        var quarter = person.GetCurrentQuarter(today)!.Value;
        var form = person.GetCurrentCycleForm(QuarterType(quarter), today)!;
        form.DueDate = today.AddDays(-10);
        form.Reset();
        var people = new FixedPersonService(person);
        var session = new SessionService();
        session.SetUser(User.Create(
            31,
            "case-manager",
            "Case Manager",
            "hash",
            "salt",
            UserRole.CaseManager,
            null,
            201));
        var formService = new RecordingFormService();
        var viewModel = new ReviewsViewModel(
            session,
            people,
            new EmptyReviewItemService(),
            new FixedSettingsService(settings),
            formService);
        var refreshes = 0;
        viewModel.FormComplianceChangedAsync = () =>
        {
            refreshes++;
            return Task.CompletedTask;
        };
        await viewModel.LoadAsync();
        var row = Assert.Single(viewModel.Rows);
        viewModel.SelectCellCommand.Execute(SelectionFor(row, quarter));

        Assert.Null(viewModel.AttestationCompletionDate);
        Assert.False(viewModel.CompleteSelectedQuarterCommand.CanExecute(null));

        viewModel.AttestationCompletionDate = today.AddDays(1);
        Assert.False(viewModel.CompleteSelectedQuarterCommand.CanExecute(null));
        Assert.Equal(FormCompletionRules.FutureDateMessage, viewModel.AttestationDateError);

        var explicitLateDate = today.AddDays(-2);
        viewModel.AttestationCompletionDate = explicitLateDate;
        Assert.True(viewModel.CompleteSelectedQuarterCommand.CanExecute(null));
        await viewModel.CompleteSelectedQuarterCommand.ExecuteAsync(null);

        Assert.Same(form, Assert.Single(formService.Saved));
        Assert.Equal(explicitLateDate, form.CompletedDate);
        Assert.Equal(FormCellStatus.Complete, row.StatusForQuarter(quarter));
        Assert.Contains("attestation complete", viewModel.DetailAttestationStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, refreshes);
        Assert.True(BillingComplianceGate.IsBillingWindowBlocked(
            form.Type.ToString(),
            form.DueDate,
            form.CompletedDate,
            form.DueDate.AddDays(1)));
    }

    private static FormType QuarterType(int quarter) => quarter switch
    {
        1 => FormType.Q1R,
        2 => FormType.Q2R,
        3 => FormType.Q3R,
        4 => FormType.Q4R,
        _ => throw new ArgumentOutOfRangeException(nameof(quarter))
    };

    private static ReviewCellSelection SelectionFor(ReviewClientRowViewModel row, int quarter) =>
        quarter switch
        {
            1 => row.Q1Selection,
            2 => row.Q2Selection,
            3 => row.Q3Selection,
            4 => row.Q4Selection,
            _ => throw new ArgumentOutOfRangeException(nameof(quarter))
        };

    private sealed class FixedPersonService(params Person[] people) : IPersonService
    {
        public Task<Person> AddPersonAsync(Person person) => Task.FromResult(person);
        public Task<List<Person>> GetAllPeopleAsync(int userId) => Task.FromResult(people.ToList());
        public Task<Person> EditPersonAsync(Person person) => Task.FromResult(person);
        public Task<string?> GetJournalAsync(int personId) => Task.FromResult<string?>(null);
        public Task SaveJournalAsync(int personId, string? journal) => Task.CompletedTask;
        public Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text) =>
            Task.FromResult(new JournalReminderResult(text));
        public Task<CaseloadOwnershipDto> TransferOwnershipAsync(int personId, int targetUserId, int expectedRevision) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<CredibleClientMatchDto>> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds) =>
            Task.FromResult<IReadOnlyList<CredibleClientMatchDto>>([]);
        public Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId) =>
            Task.FromResult<List<PersonSummary>>([]);
    }

    private sealed class EmptyReviewItemService : IReviewItemService
    {
        public Task<List<ReviewItem>> GetForCaseloadAsync(int userId) =>
            Task.FromResult<List<ReviewItem>>([]);
        public Task<List<ReviewItem>> GetForPersonAsync(int personId) =>
            Task.FromResult<List<ReviewItem>>([]);
        public Task<int> EnsureCurrentCycleItemsAsync(IEnumerable<Person> people, DateTime today) =>
            Task.FromResult(0);
        public Task<ReviewItem> SetStageDateAsync(
            int reviewItemId,
            ReviewStage stage,
            DateTime? date) => throw new NotSupportedException();
        public Task<ReviewItem> SetAppointmentAsync(
            int reviewItemId,
            DateTime? date,
            string? providerName) => throw new NotSupportedException();
        public Task<(Appointment? Medical, Appointment? Dental)> GetLatestAppointmentsAsync(int personId) =>
            Task.FromResult<(Appointment?, Appointment?)>((null, null));
    }

    private sealed class FixedSettingsService(Settings settings) : ISettingsService
    {
        public Task<Settings> LoadAsync() => Task.FromResult(settings);
        public Task SaveAsync(Settings value) => Task.CompletedTask;
    }

    private sealed class RecordingFormService : IFormService
    {
        public List<Form> Saved { get; } = [];

        public Task UpdateFormAsync(Form form)
        {
            Saved.Add(form);
            return Task.CompletedTask;
        }

        public Task OpenFormAsync(Form form) => throw new NotSupportedException();
        public Task DeleteFormsAsync(IEnumerable<Form> forms) => throw new NotSupportedException();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.csproj")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Sati repository root.");
    }
}
