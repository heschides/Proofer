using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Diagnostics;
using System.Security;

namespace Sati.ViewModels
{
    // "My Account" — self-service profile + password management for the logged-in
    // user. Distinct from SwitchUserViewModel (which authenticates a switch TO
    // another user); this manages the CURRENT user, obtained from the session.
    //
    // Profile fields are read-only until Edit is pressed (fat-finger protection).
    // Editable: Email, Phone. Read-only-for-context: DisplayName, Username, Role,
    // Agency — identity/permission fields that must not be self-edited.
    //
    // Password change verifies the current password via AuthenticateAsync (the
    // identity gate) before calling UserService.ChangePasswordAsync. Every crypto
    // step routes through existing sanctioned methods; nothing is reinvented here.
    public partial class MyAccountViewModel : ObservableObject
    {
        private const int MinPasswordLength = 8;

        private readonly ISessionService _sessionService;
        private readonly IUserService _userService;
        private readonly IAuthService _authService;

        public MyAccountViewModel(ISessionService sessionService, IUserService userService, IAuthService authService)
        {
            _sessionService = sessionService;
            _userService = userService;
            _authService = authService;
            LoadFromSession();
        }

        // Raised when the user chooses to switch accounts. The window's code-behind
        // handles launching the existing switch-user flow — no View reference here.
        public event EventHandler? SwitchUserRequested;

        // Raised after a successful password change so the view can clear the
        // PasswordBoxes — the VM nulls its SecureStrings but can't touch the boxes
        // (no View reference), so the code-behind clears them on this signal.
        public event EventHandler? PasswordChanged;

        // The current user, straight from the session.
        private User? CurrentUser => _sessionService.CurrentUser;

        // ---- Read-only context (never self-editable) ----
        public string DisplayName => CurrentUser?.DisplayName ?? string.Empty;
        public string Username => CurrentUser?.Username ?? string.Empty;
        public string Role => CurrentUser?.Role.ToString() ?? string.Empty;
        public string AgencyName => CurrentUser?.Agency?.Name ?? string.Empty;

        // ---- Editable profile fields ----
        [ObservableProperty] private string? email;
        [ObservableProperty] private string? phone;

        // ---- View/edit toggle ----
        [ObservableProperty] private bool isEditing;

        // ---- Feedback ----
        [ObservableProperty] private string profileMessage = string.Empty;
        [ObservableProperty] private string passwordMessage = string.Empty;

        // ---- Password change: SecureStrings written by the code-behind bridge ----
        [ObservableProperty] private SecureString? currentPassword;
        [ObservableProperty] private SecureString? newPassword;
        [ObservableProperty] private SecureString? confirmPassword;

        // Pulls the editable fields from the session user into the VM. Called at
        // construction and after a save, so the form reflects persisted state.
        private void LoadFromSession()
        {
            Email = CurrentUser?.Email;
            Phone = CurrentUser?.Phone;
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Username));
            OnPropertyChanged(nameof(Role));
            OnPropertyChanged(nameof(AgencyName));
        }

        // ---- Profile editing ----

        [RelayCommand]
        private void BeginEdit()
        {
            ProfileMessage = string.Empty;
            IsEditing = true;
        }

        [RelayCommand]
        private void CancelEdit()
        {
            // Discard unsaved edits by reloading from the session.
            LoadFromSession();
            ProfileMessage = string.Empty;
            IsEditing = false;
        }

        [RelayCommand]
        private async Task SaveProfile()
        {
            if (CurrentUser is null)
                return;

            ProfileMessage = string.Empty;

            try
            {
                CurrentUser.Email = Email;
                CurrentUser.Phone = Phone;
                await _userService.UpdateAsync(CurrentUser);

                IsEditing = false;
                ProfileMessage = "Saved.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveProfile failed: {ex.Message}");
                ProfileMessage = "Couldn't save. Please try again.";
            }
        }

        // ---- Password change ----

        [RelayCommand]
        private async Task ChangePassword()
        {
            PasswordMessage = string.Empty;

            if (CurrentUser is null)
                return;

            if (CurrentPassword is null || CurrentPassword.Length == 0)
            {
                PasswordMessage = "Enter your current password.";
                return;
            }

            if (NewPassword is null || NewPassword.Length < MinPasswordLength)
            {
                PasswordMessage = $"New password must be at least {MinPasswordLength} characters.";
                return;
            }

            if (!SecureStringsMatch(NewPassword, ConfirmPassword))
            {
                PasswordMessage = "New passwords don't match.";
                return;
            }

            try
            {
                // Identity gate: verify the CURRENT password before allowing change.
                var verified = await _authService.AuthenticateAsync(CurrentUser.Username, CurrentPassword);
                if (verified is null)
                {
                    PasswordMessage = "Current password is incorrect.";
                    return;
                }

                await _userService.ChangePasswordAsync(CurrentUser, NewPassword);

                // Clear the secure inputs, then signal the view to clear the boxes.
                CurrentPassword = null;
                NewPassword = null;
                ConfirmPassword = null;
                PasswordMessage = "Password changed.";
                PasswordChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ChangePassword failed: {ex.Message}");
                PasswordMessage = "Couldn't change password. Please try again.";
            }
        }

        [RelayCommand]
        private void RequestSwitchUser() => SwitchUserRequested?.Invoke(this, EventArgs.Empty);

        // Compares two SecureStrings without materializing either as a managed
        // string (which would leave the plaintext in the heap). Decrypts to
        // unmanaged memory, compares char-by-char, always frees in finally.
        private static bool SecureStringsMatch(SecureString? a, SecureString? b)
        {
            if (a is null || b is null || a.Length != b.Length)
                return false;

            IntPtr bstrA = IntPtr.Zero, bstrB = IntPtr.Zero;
            try
            {
                bstrA = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(a);
                bstrB = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(b);

                var length = a.Length;
                for (var i = 0; i < length; i++)
                {
                    var charA = System.Runtime.InteropServices.Marshal.ReadInt16(bstrA, i * 2);
                    var charB = System.Runtime.InteropServices.Marshal.ReadInt16(bstrB, i * 2);
                    if (charA != charB)
                        return false;
                }
                return true;
            }
            finally
            {
                if (bstrA != IntPtr.Zero) System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstrA);
                if (bstrB != IntPtr.Zero) System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(bstrB);
            }
        }
    }
}