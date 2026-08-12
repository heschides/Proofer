using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Sati.ViewModels.Supervisor;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for UserManagementView.xaml
    /// </summary>
    public partial class UserManagementView : UserControl
    {
        public UserManagementView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is UserManagementViewModel oldViewModel)
                oldViewModel.ResetPasswordInputsCleared -= ClearPasswordInputs;
            if (e.NewValue is UserManagementViewModel newViewModel)
                newViewModel.ResetPasswordInputsCleared += ClearPasswordInputs;
            ClearPasswordInputs();
        }

        private void ResetPasswordInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is UserManagementViewModel viewModel && sender is PasswordBox box)
                viewModel.ResetPasswordValue = box.SecurePassword;
        }

        private void ResetPasswordConfirmationInput_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is UserManagementViewModel viewModel && sender is PasswordBox box)
                viewModel.ResetPasswordConfirmation = box.SecurePassword;
        }

        private void ClearPasswordInputs()
        {
            ResetPasswordInput.Clear();
            ResetPasswordConfirmationInput.Clear();
        }
    }
}
