using Sati.ViewModels;
using Sati.Views;
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

namespace Sati
{
    /// <summary>
    /// Interaction logic for NewClientWindow.xaml
    /// </summary>
    public partial class NewClientWindow : Window
    {


        public NewClientWindow(NewClientViewModel vm)
        {
            DataContext = vm;
            InitializeComponent();

            vm.ComplianceReviewRequested += (forms) =>
            {
                var reviewVm = new ComplianceReviewViewModel
                {
                    ClientName = $"{vm.FirstName} {vm.LastName}",
                    Forms = forms
                };

                var dialog = new ComplianceReviewWindow(reviewVm);
                return dialog.ShowDialog() == true;
            };

        }

        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is NewClientViewModel vm && vm.SelectedPerson is Person person)
            {
                vm.LoadPersonForEdit(person);
                vm.IsEditMode = true;
            }
        }
    }
}