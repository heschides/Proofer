using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Proofer.Data;
using Proofer.Models;
using Proofer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Eventing.Reader;
using System.Drawing.Text;
using System.Security;
using System.Security.RightsManagement;
using System.Text;



namespace Proofer.ViewModels 
{
    public partial class LoginWindowViewModel : ObservableObject
    {

        private readonly IAuthService _authService;
        private readonly IServiceProvider _service;

        public User? SelectedUser { get; set; }
        public SecureString? SecurePassword { get; set; }

        public ObservableCollection<User> Users { get; set; } = new ObservableCollection<User>();

        //constructor
        public LoginWindowViewModel(IAuthService authService, IServiceProvider service)
        {
            _service = service;
            _authService = authService;
            InitializeUsers();
            Users.Add(User.Create(0, "Default", "Default", "hashish", "12354"));

        }

        public event EventHandler<bool>? OpenNewUserRequested;
        public event EventHandler<bool>? LoginSucceeded;

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


        private async void InitializeUsers()
        {

            var _userService = _service.GetService<IUserService>();

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

