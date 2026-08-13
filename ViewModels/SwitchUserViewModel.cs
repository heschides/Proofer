using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Diagnostics;
using System.Security;

namespace Sati.ViewModels
{
    public partial class SwitchUserViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly IAuthService _authService;
        private readonly IUserService _userService;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public SwitchUserViewModel(IAuthService authService, IUserService userService)
        {
            _authService = authService;
            _userService = userService;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler<User>? SwitchSucceeded;
        public event EventHandler? IncorrectPasswordRequested;

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private User? selectedUser;
        [ObservableProperty] private SecureString? password;
        [ObservableProperty] private string errorMessage = string.Empty;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public System.Collections.ObjectModel.ObservableCollection<User> Users { get; } = [];

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task SwitchUser()
        {
            ErrorMessage = string.Empty;

            if (SelectedUser is null)
            {
                ErrorMessage = "Please select a user.";
                return;
            }

            if (Password is null || Password.Length == 0)
            {
                ErrorMessage = "Please enter a password.";
                return;
            }

            try
            {
                var user = await _authService.AuthenticateAsync(SelectedUser.Username, Password);
                if (user is null)
                {
                    IncorrectPasswordRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                SwitchSucceeded?.Invoke(this, user);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SwitchUser failed: {ex.Message}");
                ErrorMessage = "Something went wrong. Please try again.";
            }
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            ErrorMessage = string.Empty;
            try
            {
                var all = await _userService.GetAllAsync();
                Users.Clear();
                foreach (var user in all.OrderBy(u => u.DisplayName))
                    Users.Add(user);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Switch-user directory load failed: {ex.Message}");
                Users.Clear();
                ErrorMessage = "The user list could not be loaded. Close this window and sign in again.";
            }
        }
    }
}
