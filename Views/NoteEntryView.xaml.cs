using Sati.ViewModels.Children;
using System.Windows;
using System.Windows.Controls;

namespace Sati.Views
{
    public partial class NoteEntryView : UserControl
    {
        private NoteEntryViewModel? _subscribedViewModel;

        public NoteEntryView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) =>
            Attach(DataContext as NoteEntryViewModel);

        private void OnUnloaded(object sender, RoutedEventArgs e) => Attach(null);

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsLoaded)
                Attach(e.NewValue as NoteEntryViewModel);
        }

        private void Attach(NoteEntryViewModel? viewModel)
        {
            if (ReferenceEquals(_subscribedViewModel, viewModel))
                return;

            if (_subscribedViewModel is not null)
                _subscribedViewModel.NoteReassignmentConfirmationRequested -= ConfirmReassignment;

            _subscribedViewModel = viewModel;
            if (_subscribedViewModel is not null)
                _subscribedViewModel.NoteReassignmentConfirmationRequested += ConfirmReassignment;
        }

        private void ConfirmReassignment(
            object? sender,
            NoteReassignmentConfirmationEventArgs e)
        {
            var owner = Window.GetWindow(this);
            var answer = owner is null
                ? MessageBox.Show(
                    e.Message,
                    "Reassign Note",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No)
                : MessageBox.Show(
                    owner,
                    e.Message,
                    "Reassign Note",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
            e.Confirmed = answer == MessageBoxResult.Yes;
        }
    }
}
