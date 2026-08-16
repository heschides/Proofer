using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Helpers;
using System.Security;

namespace Sati.ViewModels
{
    /// <summary>
    /// Backs the first-run gate: Sati refuses to start with no administrator, and this is
    /// the only way to create one. Deliberately narrow — it takes a name, a username and a
    /// password, and nothing else. Role is fixed to Admin and the agency is resolved by
    /// the service, so there is nothing here to get wrong at the one moment the user has
    /// no way to recover from a mistake.
    /// </summary>
    public partial class FirstRunAdminViewModel : ObservableObject
    {
        private const int MinimumPasswordLength = 8;

        private readonly IUserService _userService;

        public FirstRunAdminViewModel(IUserService userService)
        {
            _userService = userService;
        }

        // Fired once the administrator is persisted. The window subscribes and closes with
        // DialogResult = true; App.OnStartup treats anything else as a refusal and shuts
        // down rather than continuing without an admin.
        public event EventHandler? AdminCreated;

        [ObservableProperty] private string? displayName;
        [ObservableProperty] private string? username;
        [ObservableProperty] private SecureString? password;
        [ObservableProperty] private SecureString? passwordConfirm;
        [ObservableProperty] private string errorMessage = string.Empty;
        [ObservableProperty] private bool isBusy;

        [RelayCommand]
        public async Task CreateAdminAsync()
        {
            ErrorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(Username))
            {
                ErrorMessage = "Username is required.";
                return;
            }

            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                ErrorMessage = "Display name is required.";
                return;
            }

            if (Password is null || Password.Length == 0)
            {
                ErrorMessage = "Password is required.";
                return;
            }

            if (Password.Length < MinimumPasswordLength)
            {
                ErrorMessage = $"Password must be at least {MinimumPasswordLength} characters.";
                return;
            }

            // Compared by value, not by length. Two different passwords of equal length
            // must not pass as a match on the account that can never be locked out.
            if (!SecureStringHelper.Matches(Password, PasswordConfirm))
            {
                ErrorMessage = "Passwords do not match.";
                return;
            }

            try
            {
                IsBusy = true;
                await _userService.CreateFirstAdminAsync(
                    Username!.Trim(), DisplayName!.Trim(), Password);

                Password.Dispose();
                PasswordConfirm?.Dispose();
                Password = null;
                PasswordConfirm = null;

                AdminCreated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

    }
}
