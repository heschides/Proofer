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

        // Same gesture as the dashboard grid: double-click opens the selected note
        // for editing. Single click already showed it in the entry module, locked,
        // so this normally just lifts that lock. Thin event-to-VM relay only — no
        // logic lives here.
        private void DataGrid_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (DataContext is NotesWindowViewModel vm)
                vm.OpenSelectedNoteForEdit();
        }
    }
}