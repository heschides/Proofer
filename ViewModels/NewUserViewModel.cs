using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Security;

namespace Sati.ViewModels
{
    public partial class NewUserViewModel : ObservableObject
    {
        private readonly IPasswordHasher _hasher;
        private readonly IUserService _userService;

        public NewUserViewModel(IPasswordHasher hasher, IUserService userService)
        {
            _userService = userService;
            _hasher = hasher;
        }

        public event EventHandler<bool>? CloseWindowRequested;

        [ObservableProperty] private string? username;
        [ObservableProperty] private string? displayName;
        [ObservableProperty] private SecureString? passwordInit;
        [ObservableProperty] private SecureString? passwordConfirm;
        [ObservableProperty] private User? selectedSupervisor;
        [ObservableProperty] private Agency? assignedAgency;

        public ObservableCollection<User> Supervisors { get; } = [];
        public User? CreatedUser { get; private set; }

        public async Task InitializeAsync()
        {
            var all = await _userService.GetAllAsync();
            Supervisors.Clear();
            foreach (var u in all.Where(u => u.Role == UserRole.Supervisor))
                Supervisors.Add(u);
        }

        [RelayCommand]
        public async Task CreateUser()
        {
            if (string.IsNullOrWhiteSpace(Username))
                throw new InvalidOperationException("Username is required.");

            if (PasswordInit == null || PasswordConfirm == null)
                throw new InvalidOperationException("Password fields are required.");

            if (PasswordInit.Length == 0)
                throw new InvalidOperationException("Password cannot be empty.");

            // Compared by value, not by length: equal-length mismatches must not pass.
            if (!SecureStringHelper.Matches(PasswordInit, PasswordConfirm))
                throw new InvalidOperationException("Passwords do not match.");

            var all = await _userService.GetAllAsync();
            if (all.Any(u => string.Equals(u.Username, Username, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A user with that username already exists.");

            var (hash, salt) = _hasher.HashPassword(PasswordInit);

            var user = User.Create(
                0,
                Username!,
                DisplayName ?? string.Empty,
                hash,
                salt,
                UserRole.CaseManager,
                SelectedSupervisor?.Id,
                AssignedAgency.Id);

            CreatedUser = await _userService.CreateAsync(user);

            PasswordInit.Dispose();
            PasswordConfirm.Dispose();
            PasswordInit = null;
            PasswordConfirm = null;

            CloseWindowRequested?.Invoke(this, true);
        }
    }
}