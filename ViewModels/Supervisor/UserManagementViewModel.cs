using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Sati.Views;
using System.Security;
using System.Runtime.InteropServices;
using Sati.Data.Cloud;

namespace Sati.ViewModels.Supervisor
{
    public partial class UserManagementViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly IUserService _userService;
        private readonly ISessionService _sessionService;
        private readonly Func<NewUserWindow> _newUserWindowFactory;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public UserManagementViewModel(IUserService userService, ISessionService sessionService,
            Func<NewUserWindow> newUserWindowFactory)
        {
            _userService = userService;
            _sessionService = sessionService;
            _newUserWindowFactory = newUserWindowFactory;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        // Raised after a user edit persists. Parent dashboard subscribes to rebuild
        // its sidebar — VM fires, parent handles, no child→parent reference.
        public event Action? UsersChanged;
        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private User? selectedUser;
        [ObservableProperty] private User? selectedSupervisor;
        [ObservableProperty] private string statusMessage = string.Empty;
        [ObservableProperty] private bool statusIsError;
        [ObservableProperty] private SecureString? resetPasswordValue;
        [ObservableProperty] private SecureString? resetPasswordConfirmation;

        public event Action? ResetPasswordInputsCleared;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public ObservableCollection<User> Users { get; } = [];
        public ObservableCollection<User> Supervisors { get; } = [];
        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasSelectedUser => SelectedUser is not null;
        public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

        // Presentation only. UserService enforces the same rule against the database, so
        // this decides what to offer rather than what is allowed.
        public bool CanAssignExpandedPermissions =>
            _sessionService.CurrentUser?.HasAdminPermissions == true;

        private Sati.Contracts.V1.AgencyActor CurrentActor() =>
            _sessionService.CurrentUser?.ToAgencyActor()
            ?? throw new UnauthorizedAccessException("A signed-in user is required to manage users.");
        public string SelectedSupervisorName => SelectedSupervisor?.DisplayName ??
            (SelectedUser?.SupervisorId is null ? "Not assigned" : "Supervisor unavailable");

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnSelectedUserChanged(User? value)
        {
            OnPropertyChanged(nameof(HasSelectedUser));
            StatusMessage = string.Empty;
            ClearResetPasswordInputs();

            if (value is null)
            {
                OnPropertyChanged(nameof(SelectedSupervisorName));
                return;
            }

            SelectedSupervisor = Supervisors.FirstOrDefault(s => s.Id == value.SupervisorId);
            OnPropertyChanged(nameof(SelectedSupervisorName));
        }

        partial void OnSelectedSupervisorChanged(User? value) =>
            OnPropertyChanged(nameof(SelectedSupervisorName));

        partial void OnStatusMessageChanged(string value) =>
            OnPropertyChanged(nameof(HasStatusMessage));

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task CreateUser()
        {
            var window = _newUserWindowFactory();
            if (window.ShowDialog() == true)
            {
                await RefreshAsync();
                UsersChanged?.Invoke();
            }
        }

        [RelayCommand]
        private async Task SaveChanges()
        {
            if (SelectedUser is null)
                return;

            try
            {
                // The rule itself is enforced in UserService.UpdateAsync against the database.
                // This only steers the supervisor case toward the assignment that will be
                // accepted, so the refusal is rare rather than the primary control.
                if (!CanAssignExpandedPermissions)
                    SelectedSupervisor = _sessionService.CurrentUser;
                SelectedUser.SupervisorId = SelectedSupervisor?.Id;
                await _userService.UpdateAsync(CurrentActor(), SelectedUser);
                SetStatus("Changes saved.");
                await RefreshAsync();
                UsersChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveChanges failed: {ex.Message}");
                SetStatus("Changes were not saved. Please try again.", isError: true);
            }
        }

        [RelayCommand]
        private async Task ResetPassword()
        {
            if (SelectedUser is null)
                return;

            try
            {
                if (ResetPasswordValue is null || ResetPasswordValue.Length is < 8 or > 128)
                {
                    SetStatus("Enter a new password between 8 and 128 characters.", isError: true);
                    return;
                }
                if (ResetPasswordConfirmation is null ||
                    !SecureStringsMatch(ResetPasswordValue, ResetPasswordConfirmation))
                {
                    SetStatus("The new passwords do not match.", isError: true);
                    return;
                }

                // Keep the target stable for the whole request. The selected row can change
                // while a hosted reset is awaiting its response, but the confirmation must name
                // the account the service actually received.
                var target = SelectedUser;
                SetStatus($"Resetting the password for {target.DisplayName}...");
                await _userService.ResetPasswordAsync(CurrentActor(), target, ResetPasswordValue);
                ClearResetPasswordInputs();
                SetStatus($"Password reset for {target.DisplayName}.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResetPassword failed: {ex.Message}");
                SetStatus(DescribeResetFailure(ex), isError: true);
            }
        }

        private void SetStatus(string message, bool isError = false)
        {
            StatusIsError = isError;
            StatusMessage = message;
        }

        private static string DescribeResetFailure(Exception exception) => exception switch
        {
            SessionExpiredException =>
                "The password was not reset because your session expired. Sign in again and retry.",
            CloudApiException cloud => $"The password was not reset. {cloud.Message}",
            CloudConnectivityException connectivity =>
                $"Sati could not confirm the password reset. {connectivity.Message} Retry with the same password after the connection is stable.",
            UnauthorizedAccessException unauthorized => $"The password was not reset. {unauthorized.Message}",
            InvalidOperationException invalid => $"The password was not reset. {invalid.Message}",
            _ => "Sati could not confirm the password reset. Retry with the same password; if it still fails, contact support."
        };

        private void ClearResetPasswordInputs()
        {
            ResetPasswordValue?.Dispose();
            ResetPasswordConfirmation?.Dispose();
            ResetPasswordValue = null;
            ResetPasswordConfirmation = null;
            ResetPasswordInputsCleared?.Invoke();
        }

        private static bool SecureStringsMatch(SecureString first, SecureString second)
        {
            if (first.Length != second.Length)
                return false;

            var firstPtr = IntPtr.Zero;
            var secondPtr = IntPtr.Zero;
            try
            {
                firstPtr = Marshal.SecureStringToGlobalAllocUnicode(first);
                secondPtr = Marshal.SecureStringToGlobalAllocUnicode(second);
                for (var index = 0; index < first.Length; index++)
                {
                    if (Marshal.ReadInt16(firstPtr, index * sizeof(char)) !=
                        Marshal.ReadInt16(secondPtr, index * sizeof(char)))
                        return false;
                }
                return true;
            }
            finally
            {
                if (firstPtr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(firstPtr);
                if (secondPtr != IntPtr.Zero) Marshal.ZeroFreeGlobalAllocUnicode(secondPtr);
            }
        }

        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            var all = await _userService.GetAllAsync();

            Users.Clear();
            var actor = _sessionService.CurrentUser;
            var visibleUsers = CanAssignExpandedPermissions
                ? all
                : all.Where(user => user.SupervisorId == actor?.Id && user.HasCaseManagerPermissions);
            foreach (var user in visibleUsers.OrderBy(u => u.DisplayName))
                Users.Add(user);

            Supervisors.Clear();
            foreach (var user in all.Where(u => u.HasSupervisorPermissions)
                .OrderBy(u => u.DisplayName))
                Supervisors.Add(user);

            // Re-select the same user after refresh
            if (SelectedUser is not null)
                SelectedUser = Users.FirstOrDefault(u => u.Id == SelectedUser.Id);
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void SelectUser(User user) => SelectedUser = user;
    }
}
