using Sati.Models;
using Sati.ViewModels;
using Sati.Views;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Func<NewClientWindow> _newClientWindow;
        private readonly Func<SettingsWindow> _newSettingsWindow;
        private SchedulerViewModel _schedulerVm;

        public MainWindow(MainWindowViewModel vm, Func<NewClientWindow> newClientWindowFactory, Func<SettingsWindow> newSettingsWindowFactory, SchedulerViewModel schedulerVm)
        {
            InitializeComponent();
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var screenWidth = SystemParameters.PrimaryScreenWidth;

            Height = Math.Min(900, screenHeight * 0.9);
            Width = Math.Min(1100, screenWidth * 0.9);

            DataContext = vm;
            _newClientWindow = newClientWindowFactory;

            vm.OpenClientsWindowRequested += (s, success) =>
            {
                var win = _newClientWindow();
                var result = win.ShowDialog();

                _ = vm.LoadPeopleAsync();
            };

            vm.PromptSchedulerRequested += (s, e) =>
            {
                var prompt = new PromptWindow(vm.LoggedInUser?.DisplayName ?? "there");
                var result = prompt.ShowDialog();
                if (result == true)
                    vm.IsSchedulerOpen = true;
            };


            _newSettingsWindow = newSettingsWindowFactory;
            vm.OpenSettingsWindowRequested += (s, success) =>
            {
                var win = _newSettingsWindow();
                var result = win.ShowDialog();
            };

            Closing += async (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    await vm.SaveScratchpadAsync();
            };
            _schedulerVm = schedulerVm;
            SchedulerPopup.DataContext = schedulerVm;
            SchedulerPopup.Opened += (s, e) =>
            {
                Debug.WriteLine("Popup opened");
                _schedulerVm.Initialize();
            };

            Mouse.AddPreviewMouseDownOutsideCapturedElementHandler(SchedulerPopup, (s, e) =>
            {
                if (DataContext is MainWindowViewModel vm)
                    vm.IsSchedulerOpen = false;
            });
        }


        private void DataGrid_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm && vm.SelectedNote is not null)
            {
                vm.EnterEditMode();
            }
        }
  
    }
}
