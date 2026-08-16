using Sati.Models;
using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views
{
    public partial class SwitchUserWindow : Window
    {
        private readonly SwitchUserViewModel _viewModel;
        public User? NewUser { get; private set; }

        public SwitchUserWindow(SwitchUserViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            viewModel.SwitchSucceeded += (s, user) =>
            {
                NewUser = user;
                DialogResult = true;
                Close();
            };

            viewModel.IncorrectPasswordRequested += (s, e) =>
            {
                var dialog = new IncorrectPasswordDialog { Owner = this };
                dialog.ShowDialog();
                PasswordBox.Clear();
                PasswordBox.Focus();
            };

            Loaded += async (s, e) => await viewModel.InitializeAsync();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // PasswordRevealBox forwards its masked box's SecurePassword, so this
            // is the same value it always was — the reveal toggle never becomes
            // the source of truth.
            if (sender is PasswordRevealBox box)
                _viewModel.Password = box.SecurePassword;
        }
    }
}
