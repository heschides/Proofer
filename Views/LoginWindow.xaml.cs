using Proofer.Data;
using Proofer.Models;
using Proofer.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;


namespace Proofer.Views
{

    public partial class LoginWindow : Window
    {
        public LoginWindow(LoginWindowViewModel vm)
        {
            var services = ((App)Application.Current).Services;
            DataContext = vm;

            InitializeComponent();

            vm.OpenNewUserRequested += (s, success) =>
            {
                var services = ((App)Application.Current).Services;
                var win = new NewUserWindow(services.GetRequiredService<NewUserViewModel>()
                    );
                var result = win.ShowDialog();
                
                if(result == true && win.CreatedUser is User newUser)
                {
                    vm.Users.Add(newUser);
                    vm.SelectedUser = newUser;
                }
            };

            vm.LoginSucceeded += (s, success) =>
            {
                DialogResult = success;
                Close();
            };
        }
        public User? LoggedInUser =>
            (DataContext as LoginWindowViewModel)?.SelectedUser;

        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginWindowViewModel vm && sender is PasswordBox box)
            {
                vm.SecurePassword = box.SecurePassword;
            }
        }
    }
}
