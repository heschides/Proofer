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
        public event EventHandler<bool>? OpenNewUserRequested;
        public event EventHandler<bool>? LoginSucceeded;
        public event EventHandler? IncorrectPasswordRequested;
        public event EventHandler<SignInUnavailableEventArgs>? SignInUnavailableRequested;

        //PROPERTIES
        [ObservableProperty] private string username = string.Empty;
        [ObservableProperty] private string signInStatus = string.Empty;
        [ObservableProperty] private bool isSigningIn;
        public User? SelectedUser { get; set; }
        public bool CanCreateAccount => !_environment.UsesCloudApi;
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

        [RelayCommand]
        public void OpenNewUserWindow()
        {
            OpenNewUserRequested?.Invoke(this, true);
        }
    }

    public sealed class SignInUnavailableEventArgs(string title, string message) : EventArgs
    {
        public string Title { get; } = title;
        public string Message { get; } = message;
    }
}

