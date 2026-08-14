using System.Security;
using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using Sati.ViewModels.Supervisor;
using Xunit;

namespace Sati.Tests;

public sealed class UserProfileSupervisorTests
{
    private readonly User _director = User.Create(
        20, "director", "Taylor Director", string.Empty, string.Empty,
        UserRole.Director, null, 1);
    private readonly User _caseManager = User.Create(
        21, "case-manager", "Case Manager", string.Empty, string.Empty,
        UserRole.CaseManager, 20, 1);

    [Fact]
    public async Task AdministrativeUserProfileShowsTheAssignedSupervisorName()
    {
        var viewModel = new UserManagementViewModel(
            new ProfileUserService([_caseManager, _director]),
            new SessionService(),
            () => throw new NotSupportedException());

        await viewModel.InitializeAsync();
        viewModel.SelectUserCommand.Execute(_caseManager);

        Assert.Equal("Taylor Director", viewModel.SelectedSupervisorName);
        Assert.Contains(viewModel.Supervisors, user => user.Id == _director.Id);
    }

    [Fact]
    public async Task MyAccountProfileShowsTheSignedInUsersSupervisorName()
    {
        var session = new SessionService();
        session.SetUser(_caseManager);
        var viewModel = new MyAccountViewModel(
            session,
            new ProfileUserService([_caseManager, _director]),
            new UnusedAuthService());

        await viewModel.InitializeAsync();

        Assert.Equal("Taylor Director", viewModel.SupervisorName);
    }

    private sealed class ProfileUserService(List<User> users) : IUserService
    {
        public Task<List<User>> GetAllAsync() => Task.FromResult(users);
        public Task<User> CreateAsync(User user, SecureString initialPassword) => throw new NotSupportedException();
        public Task UpdateAsync(User user) => throw new NotSupportedException();
        public Task ResetPasswordAsync(User user, SecureString newPassword) => throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) => throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) => throw new NotSupportedException();
    }

    private sealed class UnusedAuthService : IAuthService
    {
        public Task<User?> AuthenticateAsync(string username, SecureString password) =>
            Task.FromResult<User?>(null);
    }
}
