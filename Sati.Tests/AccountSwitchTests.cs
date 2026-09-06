using Sati.Contracts.V1;
using System.Security;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.Data.Billing;
using Sati.Models.Billing;
using Xunit;

namespace Sati.Tests;

public sealed class AccountSwitchTests
{
    [Fact]
    public void PlatformOperatorUsesDirectCredentialEntryWithoutDirectoryEnumeration()
    {
        Assert.True(AccountSwitchPolicy.RequiresDirectSignIn(UserRole.PlatformOperator));
        Assert.False(AccountSwitchPolicy.RequiresDirectSignIn(UserRole.Admin));
        Assert.False(AccountSwitchPolicy.RequiresDirectSignIn(UserRole.CaseManager));
    }

    [Fact]
    public async Task DirectoryLoadFailureStaysInsideTheSwitchDialog()
    {
        var viewModel = new SwitchUserViewModel(new UnusedAuthService(), new FailingUserService());

        await viewModel.InitializeAsync();

        Assert.Empty(viewModel.Users);
        Assert.Contains("could not be loaded", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShellCoversClearsAndReloadsInPrivacyOrder()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "Views", "ShellWindow.xaml.cs"));
        var switchStart = source.IndexOf("private async Task OpenSwitchUserFlowAsync()", StringComparison.Ordinal);
        var switchEnd = source.IndexOf("private void ApplyScratchpadVisibility()", switchStart, StringComparison.Ordinal);
        var switchFlow = source[switchStart..switchEnd];

        var begin = switchFlow.IndexOf("_shellViewModel.BeginAccountTransition();", StringComparison.Ordinal);
        var render = switchFlow.IndexOf("DispatcherPriority.Render", StringComparison.Ordinal);
        var dialog = switchFlow.IndexOf(".ShowDialog()", StringComparison.Ordinal);
        var clear = switchFlow.IndexOf("_shellViewModel.ClearOutgoingAccountContent();", StringComparison.Ordinal);
        var install = switchFlow.IndexOf("_sessionService.SetUser(newUser);", StringComparison.Ordinal);
        var reload = switchFlow.IndexOf("await _shellViewModel.ReinitializeAsync();", StringComparison.Ordinal);
        var reveal = switchFlow.IndexOf("_shellViewModel.CompleteAccountTransition();", StringComparison.Ordinal);

        Assert.True(begin >= 0 && begin < render && render < dialog);
        Assert.True(clear > dialog && clear < install);
        Assert.True(reload > install && reload < reveal);

        var document = System.Xml.Linq.XDocument.Load(Path.Combine(root, "Views", "ShellWindow.xaml"));
        var shield = Assert.Single(document.Descendants(), element =>
            element.Name.LocalName == "Border" &&
            (string?)element.Attribute("AutomationProperties.Name") ==
                "Account information hidden while switching users");
        Assert.Equal("20", (string?)shield.Attribute("Panel.ZIndex"));
        Assert.Equal("{DynamicResource WindowBackgroundBrush}", (string?)shield.Attribute("Background"));
        Assert.Equal("True", (string?)shield.Attribute("IsHitTestVisible"));
        Assert.Contains("IsAccountTransitionActive", (string?)shield.Attribute("Visibility"));
    }

    [Fact]
    public async Task AnOldBillingLoadCannotPublishAfterTheAccountChanges()
    {
        var oldUser = User.Create(7, "old", "Old User", "hash", "salt", UserRole.Admin, null, 1);
        var newUser = User.Create(8, "new", "New User", "hash", "salt", UserRole.Admin, null, 1);
        var session = new SessionService();
        session.SetUser(oldUser);
        var service = new BlockingBillingService();
        var viewModel = new BillingOverviewViewModel(service, session);

        var oldLoad = viewModel.LoadAsync();
        await service.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.ClearForAccountSwitch();
        session.SetUser(newUser);
        var newLoad = viewModel.LoadAsync(waitForExisting: true);
        service.ReleaseFirst.SetResult();
        await service.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(string.Empty, viewModel.ProcedureCode);
        Assert.False(viewModel.HasLoaded);

        service.ReleaseSecond.SetResult();
        await Task.WhenAll(oldLoad, newLoad);

        Assert.Equal("NEW", viewModel.ProcedureCode);
        Assert.True(viewModel.HasLoaded);
    }

    [Fact]
    public async Task ACompletedOldScratchpadLoadCannotRestoreClearedDrafts()
    {
        var oldUser = User.Create(7, "old", "Old User", "hash", "salt", UserRole.CaseManager, null, 1);
        var newUser = User.Create(8, "new", "New User", "hash", "salt", UserRole.CaseManager, null, 1);
        var session = new SessionService();
        session.SetUser(oldUser);
        var service = new BlockingScratchpadLoadService();
        var viewModel = new ScratchpadViewModel(service, session);

        var oldLoad = viewModel.InitializeAsync();
        await service.OldTodayStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        viewModel.ClearForAccountSwitch();
        session.SetUser(newUser);
        service.ReleaseOldToday.SetResult();
        await oldLoad;

        Assert.Equal(string.Empty, viewModel.ScratchpadContent);
        Assert.Equal(string.Empty, viewModel.TomorrowAgendaContent);

        await viewModel.InitializeAsync();
        Assert.Equal("NEW TODAY", viewModel.ScratchpadContent);
        Assert.Equal("NEW TOMORROW", viewModel.TomorrowAgendaContent);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Sati repository root.");
    }

    private sealed class UnusedAuthService : IAuthService
    {
        public Task<User?> AuthenticateAsync(string username, SecureString password) =>
            Task.FromResult<User?>(null);
    }

    private sealed class FailingUserService : IUserService
    {
        public Task<List<User>> GetAllAsync() =>
            throw new UnauthorizedAccessException("Directory enumeration forbidden.");

        public Task<User> CreateAsync(AgencyActor actor, User user, SecureString initialPassword) => throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => Task.FromResult(true);
        public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) => throw new NotSupportedException();
        public Task UpdateAsync(AgencyActor actor, User user) => throw new NotSupportedException();
        public Task UpdateOwnContactDetailsAsync(AgencyActor actor, User user) => throw new NotSupportedException();
        public Task ResetPasswordAsync(AgencyActor actor, User user, SecureString newPassword) => throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) => throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) => throw new NotSupportedException();
    }

    private sealed class BlockingBillingService : IBillingService
    {
        private int _configurationCalls;
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor)
        {
            var call = Interlocked.Increment(ref _configurationCalls);
            if (call == 1)
            {
                FirstStarted.SetResult();
                await ReleaseFirst.Task;
                return new BillingConfiguration("OLD", null, 1m, "", "", "", "", "");
            }

            SecondStarted.SetResult();
            await ReleaseSecond.Task;
            return new BillingConfiguration("NEW", null, 1m, "", "", "", "", "");
        }

        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor actor, int userId, int month, int year) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(AgencyActor actor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId) => throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId) => throw new NotSupportedException();
        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor) => throw new NotSupportedException();
        public BillingValidationResult ValidateNoteForBilling(Note note) => throw new NotSupportedException();
        public Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration) => throw new NotSupportedException();
        public Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor) => throw new NotSupportedException();
        public Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor) => throw new NotSupportedException();
    }

    private sealed class BlockingScratchpadLoadService : IScratchpadService
    {
        public TaskCompletionSource OldTodayStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseOldToday { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Scratchpad> LoadTodayAsync(int userId)
        {
            if (userId == 7)
            {
                OldTodayStarted.SetResult();
                await ReleaseOldToday.Task;
            }
            return NewScratchpad(userId, DateTime.Today, userId == 7 ? "OLD TODAY" : "NEW TODAY");
        }

        public Task<Scratchpad> LoadTomorrowAsync(int userId) => Task.FromResult(
            NewScratchpad(userId, DateTime.Today.AddDays(1), userId == 7 ? "OLD TOMORROW" : "NEW TOMORROW"));

        public Task<List<Scratchpad>> GetHistoryAsync(int userId) => Task.FromResult(new List<Scratchpad>());
        public Task<ScratchpadComment> AddCommentAsync(int scratchpadId, int userId, string authorDisplayName, string content) => throw new NotSupportedException();
        public Task SaveAsync(Scratchpad scratchpad) => Task.CompletedTask;

        private static Scratchpad NewScratchpad(int userId, DateTime date, string content) => new()
        {
            Id = userId,
            UserId = userId,
            Date = date,
            Content = content,
            Revision = 1
        };
    }
}
