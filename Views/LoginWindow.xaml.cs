using Proofer.Models;
using Proofer.ViewModels;
using System.Windows;
using System.Windows.Controls;


namespace Proofer.Views
{

    public partial class LoginWindow : Window
    {

        private readonly Func<NewUserWindow> _newUserWindowFactory;

        public LoginWindow(LoginWindowViewModel vm, Func<NewUserWindow> newUserWindowFactory)
        {
             DataContext = vm;
             _newUserWindowFactory = newUserWindowFactory;
            InitializeComponent();

            vm.OpenNewUserRequested += (s, success) =>
            {
                var win = _newUserWindowFactory();
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
