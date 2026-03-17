using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using Sati.Data;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using System.Security;
using Sati.Models;
using System.Windows.Media;
using Windows.UI.WindowManagement;

namespace Sati.ViewModels
{
    public partial class NewUserViewModel : ObservableObject
    {
        //services
        
        private readonly IPasswordHasher _hasher;
        private readonly IUserService _userService;

        //constructor
        
        public NewUserViewModel(IPasswordHasher hasher, IUserService userService)
        {
            _userService = userService;
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

            // check for duplicate username
            var all = await _userService.GetAllAsync();
            if (all.Any(u => string.Equals(u.Username, Username, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("A user with that username already exists.");

            // hash the password
            var (hash, salt) = _hasher.HashPassword(PasswordInit);

            // create user entity (id 0 so EF will assign it)
            var user = Models.User.Create(0, Username!, DisplayName ?? string.Empty, hash, salt);

            // persist
            CreatedUser = await _userService.CreateAsync(user);

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
