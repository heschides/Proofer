using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Security;
using System.Runtime.InteropServices;
using Sati.Contracts.V1;

namespace Sati.ViewModels
{
    public partial class NewUserViewModel : ObservableObject
    {
        private readonly IUserService _userService;
        private readonly ISessionService _sessionService;

        public NewUserViewModel(IUserService userService, ISessionService sessionService)
        {
            _userService = userService;
            _sessionService = sessionService;
        }

        public event EventHandler<bool>? CloseWindowRequested;

        [ObservableProperty] private string? username;
        [ObservableProperty] private string? displayName;
        [ObservableProperty] private SecureString? passwordInit;
        [ObservableProperty] private SecureString? passwordConfirm;
        [ObservableProperty] private User? selectedSupervisor;
        [ObservableProperty] private Agency? assignedAgency;
        [ObservableProperty] private bool hasCaseManagerPermissions = true;
        [ObservableProperty] private bool hasSupervisorPermissions;
        [ObservableProperty] private bool hasAgencyWideSupervision;
        [ObservableProperty] private bool hasAdminPermissions;
        [ObservableProperty] private bool hasBillingPermissions;

        public ObservableCollection<User> Supervisors { get; } = [];
        public User? CreatedUser { get; private set; }

        // Presentation only. The same rule is enforced inside UserService.CreateAsync,
        // which is what actually stops a supervisor granting more than they hold — this
        // just avoids offering a control that would be refused.
        public bool CanAssignExpandedPermissions =>
            _sessionService.CurrentUser?.HasAdminPermissions == true;

        private AgencyActor CurrentActor() =>
            _sessionService.CurrentUser?.ToAgencyActor()
            ?? throw new UnauthorizedAccessException("A signed-in user is required to create users.");

        public async Task InitializeAsync()
        {
            var all = await _userService.GetAllAsync();
            Supervisors.Clear();
            foreach (var u in all.Where(u => u.HasSupervisorPermissions))
                Supervisors.Add(u);
            if (!CanAssignExpandedPermissions)
                SelectedSupervisor = _sessionService.CurrentUser;
        }

        [RelayCommand]
        public async Task CreateUser()
        {
            if (string.IsNullOrWhiteSpace(Username))
                throw new InvalidOperationException("Username is required.");

            if (PasswordInit == null || PasswordConfirm == null)
                throw new InvalidOperationException("Password fields are required.");

            if (PasswordInit.Length is < 8 or > 128)
                throw new InvalidOperationException("Password must be between 8 and 128 characters.");

            if (!SecureStringsMatch(PasswordInit, PasswordConfirm))
                throw new InvalidOperationException("Passwords do not match.");

            var all = await _userService.GetAllAsync();
            if (all.Any(u => string.Equals(u.Username, Username, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A user with that username already exists.");

            var agencyId = AssignedAgency?.Id ?? SelectedSupervisor?.AgencyId ?? _sessionService.CurrentUser?.AgencyId
                ?? throw new InvalidOperationException("An agency assignment is required.");
            var permissions = UserPermissions.None;
            if (CanAssignExpandedPermissions)
            {
                if (HasCaseManagerPermissions) permissions |= UserPermissions.CaseManagement;
                if (HasSupervisorPermissions) permissions |= UserPermissions.Supervision;
                if (HasAgencyWideSupervision) permissions |= UserPermissions.AgencyWideSupervision;
                if (HasAdminPermissions) permissions |= UserPermissions.Administration;
                if (HasBillingPermissions) permissions |= UserPermissions.Billing;
            }
            else
            {
                permissions = UserPermissions.CaseManagement;
                SelectedSupervisor = _sessionService.CurrentUser;
            }
            if (permissions == UserPermissions.None)
                throw new InvalidOperationException("Choose at least one permission.");
            var user = User.Create(
                0,
                Username!,
                DisplayName ?? string.Empty,
                string.Empty,
                string.Empty,
                UserRole.CaseManager,
                SelectedSupervisor?.Id,
                agencyId);
            user.Permissions = permissions;
            user.Role = Enum.Parse<UserRole>(UserPermissionRules.LegacyLabel(permissions));

            CreatedUser = await _userService.CreateAsync(CurrentActor(), user, PasswordInit);

            PasswordInit.Dispose();
            PasswordConfirm.Dispose();
            PasswordInit = null;
            PasswordConfirm = null;

            CloseWindowRequested?.Invoke(this, true);
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
    }
}
