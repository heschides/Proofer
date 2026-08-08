using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security;




namespace Sati.ViewModels 
{
    public partial class LoginWindowViewModel : ObservableObject
    {

        //FIELDS
        private readonly IAuthService _authService;

        //EVENTS
        public event EventHandler<bool>? OpenNewUserRequested;
        public event EventHandler<bool>? LoginSucceeded;
        public event EventHandler? IncorrectPasswordRequested;

        //PROPERTIES
        [ObservableProperty] private string username = string.Empty;
        public User? SelectedUser { get; set; }
        public SecureString? SecurePassword { get; set; }

        //CONSTRUCTOR
        public LoginWindowViewModel(IAuthService authService)
        {
            _authService = authService;
            
        }

        //COMMANDS
        [RelayCommand]
        public async Task LoginAsync()
        {
            if (string.IsNullOrWhiteSpace(Username) || SecurePassword == null)
                return;

            var user = await _authService.AuthenticateAsync(Username, SecurePassword);
            if (user == null)
            {
                IncorrectPasswordRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            SelectedUser = user;
            LoginSucceeded?.Invoke(this, true);
        }

        [RelayCommand]
        public void OpenNewUserWindow()
        {
            OpenNewUserRequested?.Invoke(this, true);
        }
    }
}

