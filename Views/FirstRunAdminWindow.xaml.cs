using Sati.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class FirstRunAdminWindow : Window
    {
        private bool _adminCreated;

        public FirstRunAdminWindow(FirstRunAdminViewModel vm)
        {
            DataContext = vm;
            InitializeComponent();

            vm.AdminCreated += (s, e) =>
            {
                _adminCreated = true;
                DialogResult = true;
                Close();
            };

            Loaded += (s, e) => DisplayNameInput.Focus();
        }

        // Closing without creating an administrator is a refusal, not a cancel: App.OnStartup
        // shuts down rather than continue. Confirm it, so a stray Alt+F4 during setup does
        // not read as the app crashing.
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_adminCreated)
            {
                var answer = MessageBox.Show(
                    "Sati cannot run without an administrator account. Close Sati?",
                    "Setup Incomplete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }

        private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is FirstRunAdminViewModel vm && sender is PasswordBox box)
                vm.Password = box.SecurePassword;
        }

        private void PasswordConfirmInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is FirstRunAdminViewModel vm && sender is PasswordBox box)
                vm.PasswordConfirm = box.SecurePassword;
        }

        private void PasswordConfirmInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Return && DataContext is FirstRunAdminViewModel vm)
                _ = vm.CreateAdminAsync();
        }
    }
}
