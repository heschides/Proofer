using Sati.ViewModels;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private bool _closeAfterSuccessfulSave;

        public SettingsWindow(SettingsViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;

            Closing += SaveBeforeClosing;
        }

        private async void SaveBeforeClosing(object? sender, CancelEventArgs e)
        {
            if (_closeAfterSuccessfulSave)
                return;

            e.Cancel = true;
            if (DataContext is not SettingsViewModel vm)
                return;

            if (!await vm.TrySaveSettingsAsync())
            {
                var discard = MessageBox.Show(
                    "Close Settings without saving your attempted changes?",
                    "Discard Unsaved Settings",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (discard != MessageBoxResult.Yes)
                    return;
            }

            _closeAfterSuccessfulSave = true;
            Close();
        }
    }


}
