using Sati.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class NotesPanelView : UserControl
    {
        public NotesPanelView()
        {
            InitializeComponent();
        }

        // Moved here with the grid itself when the notes panel was extracted from
        // CaseManagerDashboardContentView, so double-click-to-edit keeps working in
        // whichever position the panel currently occupies.
        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is CaseManagerDashboardViewModel vm && vm.SelectedNote is not null)
                vm.EnterEditMode();
        }
    }
}
