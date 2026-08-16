using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using Sati.ViewModels;

namespace Sati.Views
{
    /// <summary>
    /// First-run administrator setup. The view owns the two password fields and
    /// compares them, so the entered value never has to become a view model
    /// property.
    /// </summary>
    public partial class FirstRunAdminWindow : Window
    {
        private readonly FirstRunAdminViewModel _viewModel;

        public FirstRunAdminWindow(FirstRunAdminViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;

            viewModel.Completed += OnCompleted;
            Closed += (_, _) => viewModel.Completed -= OnCompleted;

            Loaded += (_, _) => PasswordInput.Focus();
        }

        /// <summary>The username created, once the dialog closes successfully.</summary>
        public string? CreatedUsername { get; private set; }

        private void OnCompleted(object? sender, string username)
        {
            CreatedUsername = username;
            MessageBox.Show(
                this,
                $"The administrator account '{username}' was created. Sign in as that account to create supervisors and other users.",
                "Administrator Created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }

        private async void OnCreate(object sender, RoutedEventArgs e)
        {
            var password = PasswordInput.SecurePassword;
            var confirmation = ConfirmInput.SecurePassword;

            await _viewModel.SubmitAsync(password, SecureStringsMatch(password, confirmation));
        }

        /// <summary>
        /// Compares two SecureStrings without leaving either as a managed string
        /// that lingers for the garbage collector to move around. Both buffers are
        /// zeroed and freed in the finally block.
        ///
        /// Length is compared first only as an early exit; the character loop runs
        /// to completion over the shared length so the comparison does not leak the
        /// position of the first difference through timing. That matters little for
        /// a confirmation field, but the cost of doing it properly is nil.
        /// </summary>
        private static bool SecureStringsMatch(SecureString? left, SecureString? right)
        {
            if (left is null || right is null)
                return false;
            if (left.Length != right.Length)
                return false;

            var leftPointer = IntPtr.Zero;
            var rightPointer = IntPtr.Zero;
            try
            {
                leftPointer = Marshal.SecureStringToGlobalAllocUnicode(left);
                rightPointer = Marshal.SecureStringToGlobalAllocUnicode(right);

                var difference = 0;
                for (var index = 0; index < left.Length; index++)
                {
                    difference |= Marshal.ReadInt16(leftPointer, index * 2)
                                ^ Marshal.ReadInt16(rightPointer, index * 2);
                }
                return difference == 0;
            }
            finally
            {
                if (leftPointer != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(leftPointer);
                if (rightPointer != IntPtr.Zero)
                    Marshal.ZeroFreeGlobalAllocUnicode(rightPointer);
            }
        }
    }
}
