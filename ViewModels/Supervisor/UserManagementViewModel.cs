using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Sati.ViewModels.Supervisor
{
    public partial class UserManagementViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly IUserService _userService;
        private readonly ISessionService _sessionService;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public UserManagementViewModel(IUserService userService, ISessionService sessionService)
        {
            _userService = userService;
            _sessionService = sessionService;
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
        [ObservableProperty] private UserRole selectedRole;
        [ObservableProperty] private string statusMessage = string.Empty;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public ObservableCollection<User> Users { get; } = [];
        public ObservableCollection<User> Supervisors { get; } = [];
        public Array Roles => Enum.GetValues(typeof(UserRole));

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public bool HasSelectedUser => SelectedUser is not null;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnSelectedUserChanged(User? value)
        {
            OnPropertyChanged(nameof(HasSelectedUser));
            StatusMessage = string.Empty;

            if (value is null)
                return;

            SelectedRole = value.Role;
            SelectedSupervisor = Supervisors.FirstOrDefault(s => s.Id == value.SupervisorId);
        }

        // -------------------------------------------------------------------------
        // Commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private async Task SaveChanges()
        {
            if (SelectedUser is null)
                return;

            // The startup gate guarantees an Admin exists; this keeps it that way. Demoting
            // the last one would leave a database no one can administer, and no in-app path
            // back — the first-run window only opens when there are zero admins, and by then
            // the app has already refused to start.
            if (SelectedUser.Role == UserRole.Admin
                && SelectedRole != UserRole.Admin
                && await _userService.AdminCountAsync() <= 1)
            {
                StatusMessage = "Sati requires at least one administrator. "
                    + "Promote another user to Admin before changing this role.";
                SelectedRole = UserRole.Admin;
                return;
            }

            try
            {
                SelectedUser.Role = SelectedRole;
                SelectedUser.SupervisorId = SelectedSupervisor?.Id;
                await _userService.UpdateAsync(SelectedUser);
                StatusMessage = "Changes saved.";
                await RefreshAsync();
                UsersChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveChanges failed: {ex.Message}");
                StatusMessage = "Failed to save changes.";
            }
        }

        [RelayCommand]
        private async Task ResetPassword()
        {
            if (SelectedUser is null)
                return;

            try
            {
                await _userService.ResetPasswordAsync(SelectedUser, "defaultpassword");
                StatusMessage = $"Password reset to 'defaultpassword' for {SelectedUser.DisplayName}.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ResetPassword failed: {ex.Message}");
                StatusMessage = "Failed to reset password.";
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
            foreach (var user in all.OrderBy(u => u.DisplayName))
                Users.Add(user);

            Supervisors.Clear();
            foreach (var user in all.Where(u =>
                u.Role is UserRole.Supervisor or UserRole.Admin)
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