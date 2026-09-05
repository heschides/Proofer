using Sati.ViewModels.Children;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sati.Views
{
    public partial class NoteEntryView : UserControl
    {
        private const double ShortEditorHeight = 840;
        private NoteEntryViewModel? _subscribedViewModel;
        private readonly DispatcherTimer _resizeTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        private NoteEditorSection _selectedSection = NoteEditorSection.Details;
        private bool _isShortEditor;

        public NoteEntryView()
        {
            InitializeComponent();
            _resizeTimer.Tick += (_, _) =>
            {
                _resizeTimer.Stop();
                ApplyEditorLayout();
            };
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        internal bool IsShortEditor => _isShortEditor;
        internal string ActiveSectionName => _selectedSection.ToString();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Attach(DataContext as NoteEntryViewModel);
            ApplyEditorLayout();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _resizeTimer.Stop();
            Attach(null);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            _selectedSection = e.NewValue is NoteEntryViewModel { IsEditing: true }
                ? NoteEditorSection.Write
                : NoteEditorSection.Details;
            if (IsLoaded)
                Attach(e.NewValue as NoteEntryViewModel);
            ApplyEditorLayout();
        }

        private void Attach(NoteEntryViewModel? viewModel)
        {
            if (ReferenceEquals(_subscribedViewModel, viewModel))
                return;

            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.NoteReassignmentConfirmationRequested -= ConfirmReassignment;
                _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _subscribedViewModel = viewModel;
            if (_subscribedViewModel is not null)
            {
                _subscribedViewModel.NoteReassignmentConfirmationRequested += ConfirmReassignment;
                _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(NoteEntryViewModel.IsEditing))
                return;

            _selectedSection = _subscribedViewModel?.IsEditing == true
                ? NoteEditorSection.Write
                : NoteEditorSection.Details;
            ApplyEditorLayout();
        }

        private void NoteEntryView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!double.IsFinite(e.NewSize.Height) || e.NewSize.Height <= 0)
                return;

            if (!IsLoaded)
            {
                ApplyEditorLayout();
                return;
            }

            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void ApplyEditorLayout()
        {
            var height = ActualHeight;
            if (!double.IsFinite(height) || height <= 0)
                return;

            var useShortEditor = height < ShortEditorHeight;
            if (useShortEditor && !_isShortEditor)
            {
                _selectedSection = NarrativeTextBox.IsKeyboardFocusWithin ||
                    DataContext is NoteEntryViewModel { IsEditing: true }
                        ? NoteEditorSection.Write
                        : NoteEditorSection.Details;
            }

            _isShortEditor = useShortEditor;
            ShortEditorHeader.Visibility = useShortEditor
                ? Visibility.Visible
                : Visibility.Collapsed;
            TallEditorHeadingRow.Height = useShortEditor ? Pixel(0) : GridLength.Auto;
            NarrativeTextBox.MinHeight = useShortEditor ? 200 : 240;

            if (!useShortEditor)
            {
                SetRows(FormFieldsGrid, 1, 6, show: true);
                SetRows(NoteBodyGrid, 0, 10, show: true);
                WriteSectionButton.IsEnabled = true;
                DetailsSectionButton.IsEnabled = true;
                return;
            }

            var showDetails = _selectedSection == NoteEditorSection.Details;
            SetRows(FormFieldsGrid, 1, 6, showDetails);
            SetRows(NoteBodyGrid, 0, 5, showDetails);
            SetRows(NoteBodyGrid, 6, 10, !showDetails);
            WriteSectionButton.IsEnabled = showDetails;
            DetailsSectionButton.IsEnabled = !showDetails;
            FormScrollViewer.ScrollToTop();
        }

        private void WriteSection_Click(object sender, RoutedEventArgs e)
        {
            _selectedSection = NoteEditorSection.Write;
            ApplyEditorLayout();
            NarrativeTextBox.Focus();
            Keyboard.Focus(NarrativeTextBox);
        }

        private void DetailsSection_Click(object sender, RoutedEventArgs e)
        {
            _selectedSection = NoteEditorSection.Details;
            ApplyEditorLayout();
            PersonSelector.Focus();
            Keyboard.Focus(PersonSelector);
        }

        private static void SetRows(Grid grid, int first, int last, bool show)
        {
            var height = show ? GridLength.Auto : Pixel(0);
            for (var index = first; index <= last; index++)
                grid.RowDefinitions[index].Height = height;
        }

        private static GridLength Pixel(double value) => new(value, GridUnitType.Pixel);

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

        private enum NoteEditorSection
        {
            Write,
            Details
        }
    }
}
