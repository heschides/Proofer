using CommunityToolkit.Mvvm.ComponentModel;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Security;

namespace Sati.ViewModels
{
    /// <summary>
    /// First-run administrator setup.
    ///
    /// See <see cref="AdministratorBootstrap"/> for why this exists and why it
    /// never invents a password. This view model validates and collects; the
    /// service performs the guarded write and resolves the agency.
    ///
    /// Passwords arrive as <see cref="SecureString"/> from the view and are never
    /// copied into a bindable property. A view model field would put the
    /// credential somewhere the binding system — and anything that serialises view
    /// model state — could reach it.
    /// </summary>
    public partial class FirstRunAdminViewModel : ObservableObject
    {
        private readonly IUserService _userService;

        public FirstRunAdminViewModel(IUserService userService)
        {
            _userService = userService;
        }

        [ObservableProperty] private string? username;
        [ObservableProperty] private string? displayName;
        [ObservableProperty] private string? email;
        [ObservableProperty] private string? problemMessage;
        [ObservableProperty] private bool isWorking;

        public bool HasProblem => !string.IsNullOrWhiteSpace(ProblemMessage);

        partial void OnProblemMessageChanged(string? value) => OnPropertyChanged(nameof(HasProblem));

        public string PasswordRequirement =>
            $"At least {AdministratorBootstrap.MinimumPasswordLength} characters. This account can create " +
            "every other account, so it is held to a higher bar than an ordinary sign-in.";

        /// <summary>Raised with the new administrator's username once created.</summary>
        public event EventHandler<string>? Completed;

        /// <summary>
        /// Creates the administrator. The passwords stay in the view, which owns
        /// the entry controls; this receives the value to hash and the result of
        /// comparing it with the confirmation.
        /// </summary>
        public async Task SubmitAsync(SecureString? password, bool passwordsMatch)
        {
            if (IsWorking)
                return;

            var problems = AdministratorBootstrap.FindProblems(
                Username, DisplayName, password?.Length ?? 0, passwordsMatch);

            if (problems.Count > 0)
            {
                ProblemMessage = string.Join(" ", problems);
                return;
            }

            IsWorking = true;
            ProblemMessage = null;
            try
            {
                // Role and agency are both set by the service — see
                // CreateFirstAdministratorAsync. Passing the role here is
                // documentation, not authority.
                var user = User.Create(
                    0, Username!.Trim(), DisplayName!.Trim(),
                    string.Empty, string.Empty, UserRole.Admin, null, 0);
                user.Email = string.IsNullOrWhiteSpace(Email) ? null : Email!.Trim();

                await _userService.CreateFirstAdministratorAsync(user, password!);
                Completed?.Invoke(this, user.Username);
            }
            catch (AdministratorAlreadyExistsException ex)
            {
                // Somebody created one while this window was open. Report it; the
                // installation now has what it needed.
                ProblemMessage = ex.Message;
            }
            catch (Exception ex)
            {
                // Reports what went wrong without echoing back what was entered.
                ProblemMessage = ex.Message;
            }
            finally
            {
                IsWorking = false;
            }
        }
    }
}
