using Sati.Models;
using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace Sati.Views
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
                    vm.Username = newUser.Username;
                }
            };

            vm.LoginSucceeded += (s, success) =>
            {
                DialogResult = success;
                Close();
            };

            vm.IncorrectPasswordRequested += (s, e) =>
            {
                var dialog = new IncorrectPasswordDialog { Owner = this };
                dialog.ShowDialog();
                PasswordInput.Clear();
                PasswordInput.Focus();
            };

            vm.SignInUnavailableRequested += (s, e) =>
            {
                MessageBox.Show(
                    this,
                    e.Message,
                    e.Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                PasswordInput.Focus();
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

        private void ForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "Contact your administrator to reset your password.",
                "Forgot Password",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && DataContext is LoginWindowViewModel vm)
                _ = vm.LoginAsync();
        }
    }
}
