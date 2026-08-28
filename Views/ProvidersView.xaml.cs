using System.Windows.Controls;

namespace Sati.Views
{
    public partial class ProvidersView : UserControl
    {
        public ProvidersView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not Sati.ViewModels.Children.ProvidersViewModel viewModel)
                return;
            viewModel.MergeConfirmationRequested -= ConfirmMerge;
            viewModel.MergeConfirmationRequested += ConfirmMerge;
        }

        private void OnDataContextChanged(
            object sender,
            System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is Sati.ViewModels.Children.ProvidersViewModel oldViewModel)
                oldViewModel.MergeConfirmationRequested -= ConfirmMerge;
            if (e.NewValue is Sati.ViewModels.Children.ProvidersViewModel newViewModel)
                newViewModel.MergeConfirmationRequested += ConfirmMerge;
        }

        private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is Sati.ViewModels.Children.ProvidersViewModel viewModel)
                viewModel.MergeConfirmationRequested -= ConfirmMerge;
        }

        private void ConfirmMerge(
            object? sender,
            Sati.ViewModels.Children.ProviderMergeConfirmationEventArgs e)
        {
            var dialog = new ConfirmationDialog(
                "Merge provider entries?",
                e.Message,
                "Merge",
                isDestructive: true)
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            e.Confirmed = dialog.ShowDialog() == true;
        }
    }
}
