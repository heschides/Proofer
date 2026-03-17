using Sati.Models;
using Sati.ViewModels;
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
        private readonly Func<NewClientWindow> _newClientWindowFactory;

        public MainWindow(MainWindowViewModel vm, Func<NewClientWindow> newClientWindowFactory)
        {
            InitializeComponent();
            var screenHeight = SystemParameters.PrimaryScreenHeight;
            var screenWidth = SystemParameters.PrimaryScreenWidth;

            Height = Math.Min(900, screenHeight * 0.9);
            Width = Math.Min(1100, screenWidth * 0.9);

            DataContext = vm;
            _newClientWindowFactory = newClientWindowFactory;

            vm.OpenClientsWindowRequested += (s, success) =>
            {
                var win = _newClientWindowFactory();
                var result = win.ShowDialog();

                _= vm.LoadPeopleAsync();
            };
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
