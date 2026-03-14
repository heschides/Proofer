using Proofer.Models;
using Proofer.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Proofer
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
            DataContext = vm;
            _newClientWindowFactory = newClientWindowFactory;

            vm.OpenClientsWindowRequested += (s, success) =>
            {
                var win = _newClientWindowFactory();
                var result = win.ShowDialog();
            };
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
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
