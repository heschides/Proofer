
using System.Windows.Controls;


namespace Sati.Views
{
    /// <summary>
    /// Interaction logic for PendingApprovalsView.xaml
    /// </summary>
    public partial class PendingApprovalsView : UserControl
    {
        private async void Queue_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Only deliberate downward scrolling loads another page; layout changes
            // must not turn incremental loading into an eager full-queue fetch.
            if (e.VerticalChange > 0 && sender is ScrollViewer viewer &&
                viewer.ScrollableHeight - viewer.VerticalOffset < 120 &&
                DataContext is Sati.ViewModels.Supervisor.PendingApprovalsViewModel vm &&
                vm.LoadMoreCommand.CanExecute(null))
                await vm.LoadMoreCommand.ExecuteAsync(null);
        }

        public PendingApprovalsView()
        {
            InitializeComponent();
            Unloaded += (_, _) =>
            {
                if (DataContext is Sati.ViewModels.Supervisor.PendingApprovalsViewModel vm)
                    vm.Deactivate();
            };
        }
    }
}
