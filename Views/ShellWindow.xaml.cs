using Sati.Data;
using Sati.ViewModels;
using System.Windows;

namespace Sati.Views
{
    public partial class ShellWindow : Window
    {
        private readonly ShellViewModel _shellViewModel;
        private readonly CaseManagerDashboardViewModel _caseManagerDashboardViewModel;
        private readonly Func<SwitchUserWindow> _switchUserWindowFactory;
        private readonly ISessionService _sessionService;
        private bool _isSavingOnClose = false;

        // Remembers the scratchpad column's width (including any GridSplitter resize)
        // across a collapse, so reopening restores what the user had. Seeded with the
        // XAML default of 300.
        private GridLength _savedScratchpadWidth = new GridLength(300);

        public ShellWindow(ShellViewModel shellViewModel,
            CaseManagerDashboardViewModel caseManagerDashboardViewModel,
            ISessionService sessionService,
            Func<SettingsWindow> settingsWindowFactory,
            Func<ScratchpadHistoryWindow> scratchpadHistoryWindowFactory,
            Func<SwitchUserWindow> switchUserWindowFactory)
        {
            InitializeComponent();
            _shellViewModel = shellViewModel;
            _caseManagerDashboardViewModel = caseManagerDashboardViewModel;
            _sessionService = sessionService;
            _switchUserWindowFactory = switchUserWindowFactory;
            DataContext = shellViewModel;

            // The view model owns whether the panel is open; collapsing the actual grid
            // column and restoring its width is view layout, so it lives here. React to
            // the flag flipping.
            _shellViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ShellViewModel.IsScratchpadVisible))
                    ApplyScratchpadVisibility();
            };

            _shellViewModel.OpenSettingsWindowRequested += (s, e) =>
            {
                var win = settingsWindowFactory();
                win.Owner = this;
                win.ShowDialog();
            };

            //_caseManagerDashboardViewModel.OpenClientsWindowRequested += async (s, e) =>
            //{
            //    var win = newClientWindowFactory();
            //    win.Owner = this;
            //    win.ShowDialog();
            //    await _caseManagerDashboardViewModel.LoadPeopleAsync();
            //};

            //_caseManagerDashboardViewModel.OpenNotesWindowRequested += (s, e) =>
            //{
            //    var win = notesWindowFactory();
            //    win.Owner = this;
            //    win.Show();
            //};

            _caseManagerDashboardViewModel.MarkFormCompleteRequested += (s, formType) =>
            {
                var result = MessageBox.Show(
                    $"Did you complete the {formType} today?",
                    "Form Status",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    _ = _caseManagerDashboardViewModel.MarkFormCompleteAsync(formType);
                else if (result == MessageBoxResult.No)
                    _ = _caseManagerDashboardViewModel.OpenFormAsync(formType);
            };

            _caseManagerDashboardViewModel.FormStatusRequested = async formType =>
            {
                var result = MessageBox.Show(
                    $"Did you complete the {formType} today?",
                    "Form Status",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                    await _caseManagerDashboardViewModel.MarkFormCompleteAsync(formType);
                else if (result == MessageBoxResult.No)
                    await _caseManagerDashboardViewModel.OpenFormAsync(formType);
            };

            shellViewModel.Scratchpad.OpenScratchpadHistoryRequested += async (s, e) =>
            {
                var win = scratchpadHistoryWindowFactory();
                win.Owner = this;
                await win.InitializeAsync();
                win.Show();
            };

            shellViewModel.SwitchUserRequested += async (s, e) =>
            {
                // Save scratchpad before switching users
                var content = _shellViewModel.Scratchpad.ScratchpadContent;
                await _shellViewModel.Scratchpad.SaveScratchpadAsync(content);

                var win = _switchUserWindowFactory();
                win.Owner = this;
                bool? result = win.ShowDialog();

                if (result == true && win.NewUser is not null)
                {
                    _sessionService.SetUser(win.NewUser);
                    await _shellViewModel.ReinitializeAsync();
                }
            };

            Closing += async (s, e) =>
            {
                if (!IsVisible) return;
                if (_isSavingOnClose) return;
                e.Cancel = true;
                _isSavingOnClose = true;

                var content = _shellViewModel.Scratchpad.ScratchpadContent;
                await _shellViewModel.Scratchpad.SaveScratchpadAsync(content);

                Close();
            };

            Closed += (s, e) => Application.Current.Shutdown();
        }

        // Collapses the scratchpad column to zero (after saving its current width) or
        // restores it. We address the columns by index — RootGrid.ColumnDefinitions[1]
        // is the GridSplitter's column, [2] is the scratchpad — because naming a
        // ColumnDefinition with x:Name triggers WPF's nested-BeginInit exception.
        // Toggling the column's Width is what actually reclaims the space; the Border's
        // Visibility binding only hides its contents. MinWidth must drop to 0 on
        // collapse, or the XAML MinWidth of 250 would refuse to let it shrink.
        private void ApplyScratchpadVisibility()
        {
            var splitterColumn = RootGrid.ColumnDefinitions[1];
            var scratchpadColumn = RootGrid.ColumnDefinitions[2];

            if (_shellViewModel.IsScratchpadVisible)
            {
                splitterColumn.Width = new GridLength(5);
                scratchpadColumn.MinWidth = 250;
                scratchpadColumn.Width = _savedScratchpadWidth;
            }
            else
            {
                _savedScratchpadWidth = scratchpadColumn.Width;
                scratchpadColumn.MinWidth = 0;
                scratchpadColumn.Width = new GridLength(0);
                splitterColumn.Width = new GridLength(0);
            }
        }
    }
}