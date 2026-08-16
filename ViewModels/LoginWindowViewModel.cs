using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security;




namespace Sati.ViewModels 
{
    public partial class LoginWindowViewModel : ObservableObject
    {

        //FIELDS
        private readonly IAuthService _authService;
        private readonly DataEnvironmentInfo _environment;

        //EVENTS
        public event EventHandler<bool>? LoginSucceeded;
        public event EventHandler? IncorrectPasswordRequested;
        public event EventHandler<SignInUnavailableEventArgs>? SignInUnavailableRequested;

        //PROPERTIES
        [ObservableProperty] private string username = string.Empty;
        [ObservableProperty] private string signInStatus = string.Empty;
        [ObservableProperty] private bool isSigningIn;
        public User? SelectedUser { get; set; }
        // REMOVED 2026-08-15: self-service account creation from the sign-in screen.
        //
        // A CanCreateAccount flag used to be true for every local (non-API)
        // environment, which meant Production. Anyone who could launch Sati could
        // create themselves a CaseManager account — no credentials, no approval,
        // no record of who authorised it — and pick their own supervisor from a
        // dropdown built by enumerating every staff account in the database.
        //
        // The command and the button are both gone rather than hidden, because a
        // hidden control is not a control: a bound command still exists to be
        // invoked. Creating users now happens only where it was always meant to,
        // in User Management behind an authenticated Supervisor/Director/Admin
        // session. The one legitimate need this served — an installation with
        // nobody to sign in as — is served by first-run administrator setup, which
        // creates exactly one account and only while none exists. See
        // AdministratorBootstrap.
        public SecureString? SecurePassword { get; set; }

        //CONSTRUCTOR
        public LoginWindowViewModel(IAuthService authService, DataEnvironmentInfo environment)
        {
            _authService = authService;
            _environment = environment;
        }

        //COMMANDS
        [RelayCommand]
        public async Task LoginAsync()
        {
            if (IsSigningIn || string.IsNullOrWhiteSpace(Username) || SecurePassword == null)
                return;

            IsSigningIn = true;
            SignInStatus = "Signing in... The Demo service may need a moment to wake up.";
            try
            {
                var user = await _authService.AuthenticateAsync(Username, SecurePassword);
                if (user == null)
                {
                    IncorrectPasswordRequested?.Invoke(this, EventArgs.Empty);
                    return;
                }

                SelectedUser = user;
                LoginSucceeded?.Invoke(this, true);
            }
            catch (AuthenticationServiceException ex)
            {
                var title = ex.Issue switch
                {
                    AuthenticationServiceIssue.TooManyAttempts => "Please Wait",
                    AuthenticationServiceIssue.NetworkUnavailable => "Connection Problem",
                    _ => "Demo Service Unavailable"
                };
                SignInUnavailableRequested?.Invoke(this, new SignInUnavailableEventArgs(title, ex.Message));
            }
            catch (Exception ex)
            {
                var reference = AppErrorLog.Record(ex, "authentication.response");
                SignInUnavailableRequested?.Invoke(this, new SignInUnavailableEventArgs(
                    "Sign-in Could Not Be Completed",
                    "Sati received an unexpected sign-in response. No session was opened. " +
                    $"Please give support error reference {reference}."));
            }
            finally
            {
                SignInStatus = string.Empty;
                IsSigningIn = false;
            }
        }

    }

    public sealed class SignInUnavailableEventArgs(string title, string message) : EventArgs
    {
        public string Title { get; } = title;
        public string Message { get; } = message;
    }
}

