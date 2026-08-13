using Sati.Data;
using Sati.ViewModels;
using Sati.Services;
using System.Windows;

namespace Sati.Views
{
    public partial class ShellWindow : Window
    {
        private readonly ShellViewModel _shellViewModel;
        private readonly CaseManagerDashboardViewModel _caseManagerDashboardViewModel;
        private readonly Func<SwitchUserWindow> _switchUserWindowFactory;
        private readonly Func<MyAccountWindow> _myAccountWindowFactory;
        private readonly Func<MyAccountViewModel> _myAccountViewModelFactory;
        private readonly ISessionService _sessionService;
        private readonly IIncidentReporter _incidentReporter;
        private readonly ApplicationRunState _applicationRunState;
        private bool _isSavingOnClose;
        private bool _closeAfterSuccessfulSave;

        // Remembers the scratchpad column's width (including any GridSplitter resize)
        // across a collapse, so reopening restores what the user had. Seeded with the
        // XAML default of 300.
        private GridLength _savedScratchpadWidth = new GridLength(300);

        public ShellWindow(ShellViewModel shellViewModel,
            CaseManagerDashboardViewModel caseManagerDashboardViewModel,
            ISessionService sessionService,
            IIncidentReporter incidentReporter,
            ApplicationRunState applicationRunState,
            Func<SettingsWindow> settingsWindowFactory,
            Func<ScratchpadHistoryWindow> scratchpadHistoryWindowFactory,
            Func<SwitchUserWindow> switchUserWindowFactory,
            Func<MyAccountWindow> myAccountWindowFactory,
            Func<MyAccountViewModel> myAccountViewModelFactory)
        {
            InitializeComponent();
            _shellViewModel = shellViewModel;
            _caseManagerDashboardViewModel = caseManagerDashboardViewModel;
            _sessionService = sessionService;
            _incidentReporter = incidentReporter;
            _applicationRunState = applicationRunState;
            _switchUserWindowFactory = switchUserWindowFactory;
            _myAccountWindowFactory = myAccountWindowFactory;
            _myAccountViewModelFactory = myAccountViewModelFactory;
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

            // The greeting opens My Account. Switch-user lives on a button inside it,
            // which raises SwitchUserRequested on the account VM — handled by the
            // extracted flow below. VM comes from the injected factory (no service
            // locator); the window's parameterless ctor takes DataContext externally.
            shellViewModel.SwitchUserRequested += (s, e) =>
            {
                var vm = _myAccountViewModelFactory();
                var win = _myAccountWindowFactory();
                win.Owner = this;
                win.DataContext = vm;

                vm.SwitchUserRequested += async (s2, e2) =>
                {
                    win.Close();
                    await OpenSwitchUserFlowAsync();
                };

                win.ShowDialog();
            };

            Closing += async (s, e) =>
            {
                if (!IsVisible) return;
                if (_closeAfterSuccessfulSave) return;
                e.Cancel = true;

                // Closing can be raised again while the asynchronous save below is
                // still running (for example, a second click on the close button).
                // Every re-entrant event must remain cancelled; otherwise WPF starts
                // closing the window and the first handler later calls Close() on a
                // window that is already closing.
                if (_isSavingOnClose) return;
                _isSavingOnClose = true;

                var content = _shellViewModel.Scratchpad.ScratchpadContent;
                if (!await _shellViewModel.Scratchpad.SaveScratchpadAsync(content))
                {
                    _isSavingOnClose = false;
                    return;
                }

                _closeAfterSuccessfulSave = true;
                Close();
            };

            Closed += (s, e) => Application.Current.Shutdown();
        }

        // The switch-to-another-user flow, formerly inline in the greeting handler,
        // now reached from the My Account window's "Switch user" button. Preserved
        // Save the outgoing user's scratchpad and journal, open the switch modal,
        // and on success swap the session user and reinitialize the shell.
        private async Task OpenSwitchUserFlowAsync()
        {
            var content = _shellViewModel.Scratchpad.ScratchpadContent;
            if (!await _shellViewModel.Scratchpad.SaveScratchpadAsync(content))
                return;
            await _shellViewModel.NotesViewModel.Clients.FlushJournalAsync();

            var win = _switchUserWindowFactory();
            win.Owner = this;
            bool? result = win.ShowDialog();

            if (result == true && win.NewUser is not null)
            {
                _sessionService.SetUser(win.NewUser);
                await _applicationRunState.StartSessionAsync(win.NewUser, _incidentReporter);
                await _incidentReporter.FlushAsync();
                await _shellViewModel.ReinitializeAsync();
            }
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
