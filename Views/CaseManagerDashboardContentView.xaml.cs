using Sati.Services;
using Sati.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sati.Views
{
    public partial class CaseManagerDashboardContentView : UserControl
    {
        private readonly DispatcherTimer _resizeTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        private OverviewLayoutState? _layout;
        private OverviewWorkspace _selectedWorkspace = OverviewWorkspace.Agenda;
        private INotifyPropertyChanged? _subscribedViewModel;
        private bool _isFocusNote;
        private bool _focusShowsAgenda;
        private bool _updatingSelector;

        public CaseManagerDashboardContentView()
        {
            InitializeComponent();
            _resizeTimer.Tick += (_, _) =>
            {
                _resizeTimer.Stop();
                ApplyResponsiveLayout();
            };
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        internal ContentControl WorkAgendaHost => AgendaHost;
        internal OverviewLayoutState? CurrentLayout => _layout;
        internal bool IsFocusNote => _isFocusNote;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SubscribeToViewModel(DataContext as INotifyPropertyChanged);
            SetInitialWorkspace();

            if (Window.GetWindow(this) is ShellWindow shell)
                shell.RegisterOverviewAgendaHost(AgendaHost);
            else
                EnsureStandaloneAgenda();

            ApplyResponsiveLayout();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _resizeTimer.Stop();
            SubscribeToViewModel(null);
            if (Window.GetWindow(this) is ShellWindow shell)
                shell.UnregisterOverviewAgendaHost(AgendaHost);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            SubscribeToViewModel(e.NewValue as INotifyPropertyChanged);
            SetInitialWorkspace();
            EnsureStandaloneAgenda();
            ApplyResponsiveLayout();
        }

        private void SubscribeToViewModel(INotifyPropertyChanged? viewModel)
        {
            if (ReferenceEquals(_subscribedViewModel, viewModel))
                return;

            if (_subscribedViewModel is not null)
                _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribedViewModel = viewModel;
            if (_subscribedViewModel is not null)
                _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CaseManagerDashboardViewModel.IsScratchpadCentered))
                return;

            SetInitialWorkspace();
            ApplyResponsiveLayout();
        }

        private void SetInitialWorkspace()
        {
            if (DataContext is CaseManagerDashboardViewModel viewModel)
                _selectedWorkspace = viewModel.IsScratchpadCentered
                    ? OverviewWorkspace.Agenda
                    : OverviewWorkspace.Notes;
            SetSelector(_selectedWorkspace);
        }

        private void EnsureStandaloneAgenda()
        {
            if (AgendaHost.Content is not null ||
                DataContext is not CaseManagerDashboardViewModel viewModel ||
                Window.GetWindow(this) is ShellWindow)
            {
                return;
            }

            AgendaHost.Content = new ScratchpadView { DataContext = viewModel.Scratchpad };
        }

        private void AdaptiveGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0 ||
                !double.IsFinite(e.NewSize.Height) || e.NewSize.Height <= 0)
            {
                return;
            }

            // Apply the first measurable layout immediately. This prevents a
            // flash of the wide XAML defaults while the view is entering the
            // visual tree, and also keeps off-screen rendering deterministic.
            if (!IsLoaded)
            {
                ApplyResponsiveLayout();
                return;
            }

            _resizeTimer.Stop();
            _resizeTimer.Start();
        }

        private void ApplyResponsiveLayout()
        {
            var width = AdaptiveGrid.ActualWidth;
            var height = AdaptiveGrid.ActualHeight;
            if (!double.IsFinite(width) || width <= 0 ||
                !double.IsFinite(height) || height <= 0)
            {
                return;
            }

            var next = OverviewLayoutPolicy.Evaluate(width, height, _layout?.Tier);
            if (_layout is { } current &&
                next.Tier > current.Tier &&
                Keyboard.FocusedElement is TextBoxBase)
            {
                return;
            }

            _layout = next;
            ResetPlacement();

            if (_isFocusNote)
            {
                ApplyFocusLayout();
                return;
            }

            switch (next.Tier)
            {
                case OverviewLayoutTier.Wide:
                    ApplyWideLayout(next);
                    break;
                case OverviewLayoutTier.Balanced:
                    ApplyBalancedLayout(next);
                    break;
                case OverviewLayoutTier.CompactTwoPane:
                    ApplyCompactTwoPaneLayout();
                    break;
                default:
                    ApplyCompactOnePaneLayout();
                    break;
            }
        }

        private void ResetPlacement()
        {
            foreach (var element in WorkspaceElements())
            {
                element.Visibility = Visibility.Collapsed;
                Grid.SetRow(element, 1);
                Grid.SetRowSpan(element, 3);
                Grid.SetColumnSpan(element, 1);
            }

            OverviewToolbar.Visibility = Visibility.Visible;
            WorkspaceSelectorPanel.Visibility = Visibility.Collapsed;
            FocusNoteButton.Visibility = Visibility.Visible;
            ReturnToOverviewButton.Visibility = Visibility.Collapsed;
            ShowAgendaFromFocusButton.Visibility = Visibility.Collapsed;

            SetColumns(Px(0), Px(0), Star(), Px(0), Px(0), Px(0), Px(0));
            SetRows(Px(0), Star(), Px(0));
        }

        private void ApplyWideLayout(OverviewLayoutState state)
        {
            SetColumns(Px(520), Px(12), Star(), Px(12), Px(380), Px(12), Px(380));
            ShowAt(NoteHost, 0);

            var agendaCentered = IsAgendaCentered();
            var center = agendaCentered ? AgendaHost : NotesHost;
            var far = agendaCentered ? NotesHost : AgendaHost;
            ShowAt(center, 2);
            ShowAt(DeadlinesHost, 4);
            if (state.ShowsLowerSummaryBand)
            {
                ShowAt(far, 6);
                ApplySummaryBand(state, 2);
            }
            else
            {
                WorkspaceSelectorPanel.Visibility = Visibility.Visible;
                var principalWorkspace = agendaCentered
                    ? OverviewWorkspace.Agenda
                    : OverviewWorkspace.Notes;
                if (_selectedWorkspace is OverviewWorkspace.Note or OverviewWorkspace.Deadlines ||
                    _selectedWorkspace == principalWorkspace)
                {
                    _selectedWorkspace = agendaCentered
                        ? OverviewWorkspace.Notes
                        : OverviewWorkspace.Agenda;
                }
                ShowWorkspaceAt(_selectedWorkspace, 6);
                SetSelector(_selectedWorkspace);
            }
        }

        private void ApplyBalancedLayout(OverviewLayoutState state)
        {
            SetColumns(Px(440), Px(12), Star(), Px(12), Px(380), Px(0), Px(0));
            ShowAt(NoteHost, 0);

            var principal = IsAgendaCentered() ? OverviewWorkspace.Agenda : OverviewWorkspace.Notes;
            ShowAt(ElementFor(principal), 2);
            ApplySummaryBand(state, 2);

            WorkspaceSelectorPanel.Visibility = Visibility.Visible;
            if (_selectedWorkspace == principal || _selectedWorkspace == OverviewWorkspace.Note)
                _selectedWorkspace = principal == OverviewWorkspace.Agenda
                    ? OverviewWorkspace.Notes
                    : OverviewWorkspace.Agenda;
            ShowWorkspaceAt(_selectedWorkspace, 4);
            SetSelector(_selectedWorkspace);
        }

        private void ApplyCompactTwoPaneLayout()
        {
            SetColumns(Px(440), Px(12), Star(), Px(0), Px(0), Px(0), Px(0));
            ShowAt(NoteHost, 0);
            WorkspaceSelectorPanel.Visibility = Visibility.Visible;

            if (_selectedWorkspace == OverviewWorkspace.Note)
                _selectedWorkspace = IsAgendaCentered() ? OverviewWorkspace.Agenda : OverviewWorkspace.Notes;
            ShowWorkspaceAt(_selectedWorkspace, 2);
            SetSelector(_selectedWorkspace);
        }

        private void ApplyCompactOnePaneLayout()
        {
            SetColumns(Star(), Px(0), Px(0), Px(0), Px(0), Px(0), Px(0));
            WorkspaceSelectorPanel.Visibility = Visibility.Visible;
            ShowWorkspaceAt(_selectedWorkspace, 0);
            SetSelector(_selectedWorkspace);
        }

        private void ApplyFocusLayout()
        {
            SetColumns(Star(), Px(0), Px(0), Px(0), Px(0), Px(0), Px(0));
            FocusNoteButton.Visibility = Visibility.Collapsed;
            ReturnToOverviewButton.Visibility = Visibility.Visible;
            ShowAgendaFromFocusButton.Visibility = Visibility.Visible;
            ShowAgendaFromFocusButton.Content = _focusShowsAgenda ? "Return to note" : "Work Agenda";
            ShowAt(_focusShowsAgenda ? AgendaHost : NoteHost, 0);
        }

        private void ApplySummaryBand(OverviewLayoutState state, int column)
        {
            if (!state.ShowsLowerSummaryBand)
                return;

            var principal = IsAgendaCentered() ? AgendaHost : NotesHost;
            Grid.SetRowSpan(principal, 2);
            SummaryHost.Visibility = Visibility.Visible;
            Grid.SetRow(SummaryHost, 3);
            Grid.SetRowSpan(SummaryHost, 1);
            Grid.SetColumn(SummaryHost, column);
            AdaptiveGrid.RowDefinitions[3].Height = Px(280);
        }

        private void WorkspaceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingSelector ||
                WorkspaceSelector.SelectedItem is not ComboBoxItem item ||
                item.Tag is not string tag ||
                !Enum.TryParse(tag, out OverviewWorkspace selected))
            {
                return;
            }

            if (selected == OverviewWorkspace.Note &&
                _layout?.Tier == OverviewLayoutTier.CompactTwoPane)
            {
                _isFocusNote = true;
                _focusShowsAgenda = false;
            }
            else
            {
                _selectedWorkspace = selected;
            }

            ApplyResponsiveLayout();
        }

        private void FocusNote_Click(object sender, RoutedEventArgs e)
        {
            _isFocusNote = true;
            _focusShowsAgenda = false;
            ApplyResponsiveLayout();
        }

        private void ReturnToOverview_Click(object sender, RoutedEventArgs e)
        {
            _isFocusNote = false;
            _focusShowsAgenda = false;
            ApplyResponsiveLayout();
        }

        private void ShowAgendaFromFocus_Click(object sender, RoutedEventArgs e)
        {
            _focusShowsAgenda = !_focusShowsAgenda;
            ApplyResponsiveLayout();
        }

        private bool IsAgendaCentered() =>
            DataContext is not CaseManagerDashboardViewModel viewModel || viewModel.IsScratchpadCentered;

        private FrameworkElement ElementFor(OverviewWorkspace workspace) => workspace switch
        {
            OverviewWorkspace.Note => NoteHost,
            OverviewWorkspace.Notes => NotesHost,
            OverviewWorkspace.Deadlines => DeadlinesHost,
            OverviewWorkspace.Forms or OverviewWorkspace.Productivity => SummaryHost,
            _ => AgendaHost
        };

        private void ShowWorkspaceAt(OverviewWorkspace workspace, int column)
        {
            if (workspace is OverviewWorkspace.Forms or OverviewWorkspace.Productivity)
                SummaryTabs.SelectedIndex = workspace == OverviewWorkspace.Forms ? 0 : 1;
            ShowAt(ElementFor(workspace), column);
        }

        private static void ShowAt(FrameworkElement element, int column)
        {
            element.Visibility = Visibility.Visible;
            Grid.SetColumn(element, column);
        }

        private IEnumerable<FrameworkElement> WorkspaceElements()
        {
            yield return NoteHost;
            yield return AgendaHost;
            yield return NotesHost;
            yield return DeadlinesHost;
            yield return SummaryHost;
        }

        private void SetSelector(OverviewWorkspace workspace)
        {
            _updatingSelector = true;
            try
            {
                WorkspaceSelector.SelectedItem = WorkspaceSelector.Items
                    .OfType<ComboBoxItem>()
                    .FirstOrDefault(item => string.Equals(
                        item.Tag as string,
                        workspace.ToString(),
                        StringComparison.Ordinal));
            }
            finally
            {
                _updatingSelector = false;
            }
        }

        private void SetColumns(
            GridLength first,
            GridLength firstGap,
            GridLength second,
            GridLength secondGap,
            GridLength third,
            GridLength thirdGap,
            GridLength fourth)
        {
            var widths = new[] { first, firstGap, second, secondGap, third, thirdGap, fourth };
            for (var index = 0; index < widths.Length; index++)
            {
                AdaptiveGrid.ColumnDefinitions[index].MinWidth = 0;
                AdaptiveGrid.ColumnDefinitions[index].Width = widths[index];
            }
        }

        private void SetRows(GridLength firstContent, GridLength mainContent, GridLength summary)
        {
            AdaptiveGrid.RowDefinitions[1].Height = firstContent;
            AdaptiveGrid.RowDefinitions[2].Height = mainContent;
            AdaptiveGrid.RowDefinitions[3].Height = summary;
        }

        private static GridLength Px(double value) => new(value, GridUnitType.Pixel);
        private static GridLength Star() => new(1, GridUnitType.Star);

        private enum OverviewWorkspace
        {
            Agenda,
            Note,
            Notes,
            Deadlines,
            Forms,
            Productivity
        }
    }
}
