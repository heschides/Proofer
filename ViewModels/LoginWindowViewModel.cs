using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Sati.Data;
using Sati.Models;
using Sati.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.Drawing.Text;
using System.Security;
using System.Security.RightsManagement;
using System.Text;



namespace Sati.ViewModels 
{
    public partial class LoginWindowViewModel : ObservableObject
    {

        //FIELDS
        private readonly IAuthService _authService;
        private readonly IUserService _userService;

        //EVENTS
        public event EventHandler<bool>? OpenNewUserRequested;
        public event EventHandler<bool>? LoginSucceeded;

        //PROPERTIES
        public User? SelectedUser { get; set; }
        public SecureString? SecurePassword { get; set; }
        public ObservableCollection<User> Users { get; set; } = new ObservableCollection<User>();

        //CONSTRUCTOR
        public LoginWindowViewModel(IAuthService authService, IUserService userService)
        {
            _userService = userService;
            _authService = authService;
            InitializeUsers();
            
        }

        //COMMANDS
        [RelayCommand]
        public async Task LoginAsync()
        {
            var selectedUser = SelectedUser;
            var password = SecurePassword;
            if (selectedUser == null ||string.IsNullOrWhiteSpace(selectedUser.Username) || password == null)
            {
                return;
            }
            var user = await _authService.AuthenticateAsync(selectedUser.Username, password);
            if (user == null)
                return;

            SelectedUser = user;
            LoginSucceeded?.Invoke(this, true);
        }

        [RelayCommand]
        public void OpenNewUserWindow()
        {
            OpenNewUserRequested?.Invoke(this, true);
        }

        //METHODS
        private async void InitializeUsers()
        {
            if (_userService == null)
                return;
            var users = await _userService.GetAllAsync();
            foreach (var user in users)
            {
                Users.Add(user);
            }
        }
    }
}

