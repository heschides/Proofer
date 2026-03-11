using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using Proofer.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Security;
using Proofer.Models;
using System.Windows.Media;
using Windows.UI.WindowManagement;

namespace Proofer.ViewModels
{
    public partial class NewUserViewModel : ObservableObject
    {
        //services
        
        private readonly IPasswordHasher _hasher;
        private readonly IServiceProvider _service;

        //constructor
        
        public NewUserViewModel(IPasswordHasher hasher, IServiceProvider service)
        {
            _service = service;
            _hasher = hasher;
        }

        //events
        public event EventHandler<bool>? CloseRequested;
        public event EventHandler<bool>? CloseWindowRequested;

        //properies

        [ObservableProperty]
        private string? username;

        [ObservableProperty]
        private string? displayName;

        [ObservableProperty]
        SecureString? passwordInit; 

        [ObservableProperty]
        public SecureString? passwordConfirm;

        public User? CreatedUser { get; private set; }


        

       //commands
        
        [RelayCommand]
        public async Task CreateUser()
        {
            // basic validation
            if (string.IsNullOrWhiteSpace(Username))
                throw new InvalidOperationException("Username is required.");

            if (PasswordInit == null || PasswordConfirm == null)
                throw new InvalidOperationException("Password fields are required.");

            if (PasswordInit.Length == 0)
                throw new InvalidOperationException("Password cannot be empty.");

            if (PasswordInit.Length != PasswordConfirm.Length)
                throw new InvalidOperationException("Passwords do not match.");

            // resolve the user service from the provided service provider
            var userService = _service.GetService(typeof(IUserService)) as IUserService
                              ?? _service.GetRequiredService<IUserService>();

            // check for duplicate username
            var all = await userService.GetAllAsync();
            if (all.Any(u => string.Equals(u.Username, Username, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A user with that username already exists.");

            // hash the password
            var (hash, salt) = _hasher.HashPassword(PasswordInit);

            // create user entity (id 0 so EF will assign it)
            var user = Models.User.Create(0, Username!, DisplayName ?? string.Empty, hash, salt);

            // persist
            CreatedUser = await userService.CreateAsync(user);

            // clear sensitive data
            PasswordInit.Dispose();
            PasswordConfirm.Dispose();
            PasswordInit = null;
            PasswordConfirm = null;

            CloseWindowRequested?.Invoke(this, true);
        }

        //methods



    }
}
