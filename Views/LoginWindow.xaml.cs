using Sati.Data;
using Sati.Models;
using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;


namespace Sati.Views
{

    public partial class LoginWindow : Window
    {

        private readonly Func<FirstRunAdminWindow> _firstRunAdminWindowFactory;
        private readonly IUserService _userService;
        private bool _loginCompletionHandled;

        public LoginWindow(
            LoginWindowViewModel vm,
            Func<FirstRunAdminWindow> firstRunAdminWindowFactory,
            IUserService userService)
        {
             DataContext = vm;
            _firstRunAdminWindowFactory = firstRunAdminWindowFactory;
            _userService = userService;
            InitializeComponent();
            ReleaseVersionText.Text =
                $"Version {typeof(LoginWindow).Assembly.GetName().Version?.ToString(3)}";

            Loaded += async (_, _) => await OfferFirstRunSetupAsync();

            vm.LoginSucceeded += (s, success) =>
            {
                if (_loginCompletionHandled || !IsLoaded)
                    return;

                _loginCompletionHandled = true;
                DialogResult = success;
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
            // PasswordRevealBox forwards its masked box's SecurePassword, so this
            // is the same value it always was — the reveal toggle never becomes
            // the source of truth.
            if (DataContext is LoginWindowViewModel vm && sender is PasswordRevealBox box)
            {
                vm.SecurePassword = box.SecurePassword;
            }
        }

        /// <summary>
        /// Shows the first-run banner when the installation has no administrator.
        ///
        /// This only OFFERS setup. It does not block signing in, because an
        /// installation in this state usually already has working accounts — a case
        /// manager doing real work should not be held hostage by a prompt about
        /// administration they may not be the right person to perform.
        ///
        /// A failure here is swallowed on purpose: not being able to answer the
        /// question is not a reason to prevent someone signing in. The consequence
        /// of guessing wrong is only that a banner does or does not appear, and the
        /// service refuses the write regardless.
        /// </summary>
        private async Task OfferFirstRunSetupAsync()
        {
            try
            {
                if (await _userService.AnyAdministratorExistsAsync())
                    return;

                FirstRunAdminPrompt.Visibility = Visibility.Visible;
            }
            catch (Exception)
            {
                FirstRunAdminPrompt.Visibility = Visibility.Collapsed;
            }
        }

        private void CreateFirstAdmin_Click(object sender, RoutedEventArgs e)
        {
            var window = _firstRunAdminWindowFactory();
            window.Owner = this;

            if (window.ShowDialog() == true && window.CreatedUsername is string username)
            {
                FirstRunAdminPrompt.Visibility = Visibility.Collapsed;
                if (DataContext is LoginWindowViewModel vm)
                    vm.Username = username;
                PasswordInput.Focus();
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
