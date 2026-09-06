using Sati.Services;
using Sati.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sati.Views
{
    public partial class CaseManagerDashboardContentView : UserControl
    {
        private const double SummaryBandHeight = 220;
        private readonly DispatcherTimer _resizeTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        private OverviewLayoutState? _layout;

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

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is ShellWindow shell)
                shell.RegisterOverviewAgendaHost(AgendaHost);
            else
                EnsureStandaloneAgenda();

            ApplyResponsiveLayout();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _resizeTimer.Stop();
            if (Window.GetWindow(this) is ShellWindow shell)
                shell.UnregisterOverviewAgendaHost(AgendaHost);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!IsLoaded)
                return;

            EnsureStandaloneAgenda();
            ApplyResponsiveLayout();
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

            // Apply the first measurable layout immediately. This prevents a flash
            // of the XAML defaults while the view enters the visual tree and keeps
            // off-screen rendering deterministic.
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

            switch (next.Tier)
            {
                case OverviewLayoutTier.Wide:
                    ApplyThreePaneLayout(next, noteWidth: 520, deadlineWidth: 420, gap: 12);
                    break;
                case OverviewLayoutTier.Balanced:
                    ApplyThreePaneLayout(next, noteWidth: 440, deadlineWidth: 380, gap: 12);
                    break;
                case OverviewLayoutTier.Compact:
                    // The former fourth Overview workspace is now in the main Notes
                    // tab, so this tier can retain all three primary panes.
                    ApplyThreePaneLayout(next, noteWidth: 360, deadlineWidth: 320, gap: 8);
                    break;
                default:
                    ApplyStackedLayout();
                    break;
            }
        }

        private void ResetPlacement()
        {
            foreach (var element in WorkspaceElements())
            {
                element.Visibility = Visibility.Collapsed;
                Grid.SetRow(element, 0);
                Grid.SetRowSpan(element, 3);
                Grid.SetColumn(element, 0);
                Grid.SetColumnSpan(element, 1);
            }

            NoteHost.Margin = new Thickness(0);
            AgendaHost.Margin = new Thickness(0);
            DeadlinesHost.Margin = new Thickness(0);
            SummaryHost.Margin = new Thickness(0, 10, 0, 0);

            SetColumns(Star(), Px(0), Px(0), Px(0), Px(0), Px(0), Px(0));
            SetRows(Star(), Px(0), Px(0));
        }

        private void ApplyThreePaneLayout(
            OverviewLayoutState state,
            double noteWidth,
            double deadlineWidth,
            double gap)
        {
            SetColumns(Px(noteWidth), Px(gap), Star(), Px(gap), Px(deadlineWidth), Px(0), Px(0));
            Place(NoteHost, column: 0, row: 0, rowSpan: 3);
            Place(DeadlinesHost, column: 4, row: 0, rowSpan: 3);

            if (state.ShowsLowerSummaryBand)
            {
                SetRows(Star(), Px(0), Px(SummaryBandHeight));
                Place(AgendaHost, column: 2, row: 0, rowSpan: 2);
                Place(SummaryHost, column: 2, row: 2, rowSpan: 1);
            }
            else
            {
                Place(AgendaHost, column: 2, row: 0, rowSpan: 3);
            }
        }

        private void ApplyStackedLayout()
        {
            SetColumns(Star(), Px(0), Px(0), Px(0), Px(0), Px(0), Px(0));
            SetRows(Star(), Star(), Star());

            NoteHost.Margin = new Thickness(0, 0, 0, 8);
            AgendaHost.Margin = new Thickness(0, 0, 0, 8);
            Place(NoteHost, column: 0, row: 0, rowSpan: 1);
            Place(AgendaHost, column: 0, row: 1, rowSpan: 1);
            Place(DeadlinesHost, column: 0, row: 2, rowSpan: 1);
        }

        private static void Place(
            FrameworkElement element,
            int column,
            int row,
            int rowSpan)
        {
            element.Visibility = Visibility.Visible;
            Grid.SetColumn(element, column);
            Grid.SetRow(element, row);
            Grid.SetRowSpan(element, rowSpan);
        }

        private IEnumerable<FrameworkElement> WorkspaceElements()
        {
            yield return NoteHost;
            yield return AgendaHost;
            yield return DeadlinesHost;
            yield return SummaryHost;
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

        private void SetRows(GridLength first, GridLength second, GridLength third)
        {
            var heights = new[] { first, second, third };
            for (var index = 0; index < heights.Length; index++)
                AdaptiveGrid.RowDefinitions[index].Height = heights[index];
        }

        private static GridLength Px(double value) => new(value, GridUnitType.Pixel);
        private static GridLength Star() => new(1, GridUnitType.Star);
    }
}
