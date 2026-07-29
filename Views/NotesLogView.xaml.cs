using Sati.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Sati.Views
{
    public partial class NotesLogView : UserControl
    {
        public NotesLogView()
        {
            InitializeComponent();
        }

        // Same gesture as the dashboard grid: double-click pushes the selected
        // note into the entry module for editing. Thin event-to-VM relay only —
        // no logic lives here.
        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is NotesWindowViewModel vm && vm.SelectedNote is not null)
                vm.NoteEntry.EnterEditMode(vm.SelectedNote);
        }
    }
}