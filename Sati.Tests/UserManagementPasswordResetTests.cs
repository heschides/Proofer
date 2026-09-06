using Sati.Contracts.V1;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels.Supervisor;
using Sati.Views;
using System.Security;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Xunit;

namespace Sati.Tests;

[Collection(WpfViewCollection.Name)]
public sealed class UserManagementPasswordResetTests
{
    [Fact]
    public async Task InvalidTemporaryPasswordExplainsWhyNothingWasChangedOnTheRenderedScreen()
    {
        var (viewModel, _) = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.SelectUserCommand.Execute(viewModel.Users.Single(user => user.Username == "amber"));
        viewModel.ResetPasswordValue = Secure("short");
        viewModel.ResetPasswordConfirmation = Secure("short");

        await viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.Equal("Enter a new password between 8 and 128 characters.", viewModel.StatusMessage);
        WpfUiHarness.Run(() =>
        {
            var view = new UserManagementView { DataContext = viewModel };
            WpfUiHarness.Realize(view, 900, 760);
            var status = WpfUiHarness.Descendants(view).OfType<TextBlock>()
                .Single(block => AutomationProperties.GetAutomationId(block) == "UserManagementStatus");
            Assert.Equal(Visibility.Visible, status.Visibility);
            Assert.Equal(viewModel.StatusMessage, AutomationProperties.GetName(status));
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        });
    }

    [Fact]
    public async Task ValidTemporaryPasswordReachesTheSelectedUserAndConfirmsCompletion()
    {
        var (viewModel, service) = CreateViewModel();
        await viewModel.InitializeAsync();
        var amber = viewModel.Users.Single(user => user.Username == "amber");
        viewModel.SelectUserCommand.Execute(amber);
        viewModel.ResetPasswordValue = Secure("Temporary-Password-42!");
        viewModel.ResetPasswordConfirmation = Secure("Temporary-Password-42!");

        await viewModel.ResetPasswordCommand.ExecuteAsync(null);

        Assert.Same(amber, service.ResetTarget);
        Assert.Equal("Temporary-Password-42!", service.ResetPassword);
        Assert.Equal("Password reset for Amber Example.", viewModel.StatusMessage);
    }

    private static (UserManagementViewModel ViewModel, CapturingUserService Service) CreateViewModel()
    {
        var admin = User.Create(1, "longchenpa", "Longchenpa", string.Empty, string.Empty,
            UserRole.Admin, null, 1);
        var amber = User.Create(2, "amber", "Amber Example", string.Empty, string.Empty,
            UserRole.CaseManager, null, 1);
        var session = new SessionService();
        session.SetUser(admin);
        var service = new CapturingUserService([admin, amber]);
        return (new UserManagementViewModel(
            service, session, () => throw new NotSupportedException()), service);
    }

    private static SecureString Secure(string value)
    {
        var secure = new SecureString();
        foreach (var character in value)
            secure.AppendChar(character);
        secure.MakeReadOnly();
        return secure;
    }

    private sealed class CapturingUserService(List<User> users) : IUserService
    {
        public User? ResetTarget { get; private set; }
        public string? ResetPassword { get; private set; }

        public Task<List<User>> GetAllAsync() => Task.FromResult(users);
        public Task<User> CreateAsync(AgencyActor actor, User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => throw new NotSupportedException();
        public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) =>
            throw new NotSupportedException();
        public Task UpdateAsync(AgencyActor actor, User user) => throw new NotSupportedException();
        public Task UpdateOwnContactDetailsAsync(AgencyActor actor, User user) =>
            throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) =>
            throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) => throw new NotSupportedException();

        public Task ResetPasswordAsync(AgencyActor actor, User user, SecureString newPassword)
        {
            ResetTarget = user;
            ResetPassword = new System.Net.NetworkCredential(string.Empty, newPassword).Password;
            return Task.CompletedTask;
        }
    }
}
