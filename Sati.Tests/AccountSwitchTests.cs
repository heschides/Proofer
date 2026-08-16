using System.Security;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using Sati.ViewModels;
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

    private sealed class UnusedAuthService : IAuthService
    {
        public Task<User?> AuthenticateAsync(string username, SecureString password) =>
            Task.FromResult<User?>(null);
    }

    private sealed class FailingUserService : IUserService
    {
        public Task<List<User>> GetAllAsync() =>
            throw new UnauthorizedAccessException("Directory enumeration forbidden.");

        public Task<User> CreateAsync(User user, SecureString initialPassword) => throw new NotSupportedException();
        public Task<bool> AnyAdministratorExistsAsync() => Task.FromResult(true);
        public Task<User> CreateFirstAdministratorAsync(User user, SecureString initialPassword) => throw new NotSupportedException();
        public Task UpdateAsync(User user) => throw new NotSupportedException();
        public Task ResetPasswordAsync(User user, SecureString newPassword) => throw new NotSupportedException();
        public Task ChangePasswordAsync(User user, SecureString currentPassword, SecureString newPassword) => throw new NotSupportedException();
        public Task<List<User>> GetSuperviseesAsync(int supervisorId) => throw new NotSupportedException();
    }
}
