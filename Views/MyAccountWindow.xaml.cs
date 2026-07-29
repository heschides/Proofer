using System.Windows;
using Sati.ViewModels;

namespace Sati.Views
{
    public partial class MyAccountWindow : Window
    {
        private MyAccountViewModel? ViewModel => DataContext as MyAccountViewModel;

        public MyAccountWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        // DataContext is assigned after construction, so wire the VM event here
        // rather than in the constructor. Unsubscribe from any prior VM to avoid
        // a dangling handler if the context is ever reassigned.
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is MyAccountViewModel oldVm)
                oldVm.PasswordChanged -= OnPasswordChanged;
            if (e.NewValue is MyAccountViewModel newVm)
                newVm.PasswordChanged += OnPasswordChanged;
        }

        private void OnPasswordChanged(object? sender, System.EventArgs e)
        {
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmPasswordBox.Clear();
        }

        // PasswordBox.Password isn't a DependencyProperty (WPF won't let the
        // plaintext sit in a binding), so each box hands its SecurePassword — a
        // SecureString WPF maintains for us — to the VM on change. Same bridge as
        // SwitchUserWindow, one per box.
        private void CurrentPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
                ViewModel.CurrentPassword = CurrentPasswordBox.SecurePassword;
        }

        private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
                ViewModel.NewPassword = NewPasswordBox.SecurePassword;
        }

        private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (ViewModel is not null)
                ViewModel.ConfirmPassword = ConfirmPasswordBox.SecurePassword;
        }
    }
}