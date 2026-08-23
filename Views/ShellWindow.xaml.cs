using Sati.Data;
using Sati.ViewModels;
using Sati.Services;
using Sati.Models;
using System.Windows;
using System.Windows.Threading;

using Sati.Contracts.V1;

namespace Sati.Views
{
    public partial class ShellWindow : Window
    {
        private readonly ShellViewModel _shellViewModel;
        private readonly CaseManagerDashboardViewModel _caseManagerDashboardViewModel;
        private readonly Func<SwitchUserWindow> _switchUserWindowFactory;
        private readonly Func<LoginWindow> _loginWindowFactory;
        private readonly Func<MyAccountWindow> _myAccountWindowFactory;
        private readonly Func<MyAccountViewModel> _myAccountViewModelFactory;
        private readonly ISessionService _sessionService;
        private readonly IIncidentReporter _incidentReporter;
        private readonly ApplicationRunState _applicationRunState;
        private readonly DatabaseActivityViewModel _databaseActivity;
        private readonly Func<DatabasePatienceWindow> _databasePatienceWindowFactory;
        private readonly SemaphoreSlim _accountSwitchGate = new(1, 1);
        private DatabasePatienceWindow? _databasePatienceWindow;
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
            Func<LoginWindow> loginWindowFactory,
            Func<MyAccountWindow> myAccountWindowFactory,
            Func<MyAccountViewModel> myAccountViewModelFactory,
            Func<DatabasePatienceWindow> databasePatienceWindowFactory)
        {
            InitializeComponent();
            _shellViewModel = shellViewModel;
            _caseManagerDashboardViewModel = caseManagerDashboardViewModel;
            _sessionService = sessionService;
            _incidentReporter = incidentReporter;
            _applicationRunState = applicationRunState;
            _switchUserWindowFactory = switchUserWindowFactory;
            _loginWindowFactory = loginWindowFactory;
            _myAccountWindowFactory = myAccountWindowFactory;
            _myAccountViewModelFactory = myAccountViewModelFactory;
            _databaseActivity = shellViewModel.DatabaseActivity;
            _databasePatienceWindowFactory = databasePatienceWindowFactory;
            DataContext = shellViewModel;

            _databaseActivity.PropertyChanged += OnDatabaseActivityPropertyChanged;

            // A workstation may remain open overnight. Returning to the window
            // advances the two dated agenda views immediately; the autosave timer
            // provides the same check while the window stays active.
            Activated += async (s, e) =>
                await _shellViewModel.Scratchpad.RollForwardIfNeededAsync();

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

            _caseManagerDashboardViewModel.MarkFormCompleteRequested = async formType =>
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
            shellViewModel.SwitchUserRequested += async (s, e) =>
            {
                var vm = _myAccountViewModelFactory();
                await vm.InitializeAsync();
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

                try
                {
                    var scratchpadsSaved = await _shellViewModel.Scratchpad.SaveAllScratchpadsAsync();
                    var journalSaved = await _shellViewModel.NotesViewModel.Clients.FlushJournalAsync();
                    if (!scratchpadsSaved || !journalSaved)
                    {
                        var closeAnyway = MessageBox.Show(
                            "Sati could not save all open work. The unsaved text is still visible.\n\n" +
                            "Choose No to keep Sati open, check the connection, and try closing again. " +
                            "Choose Yes only if you want to close without saving that work.",
                            "Open Work Not Saved",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning,
                            MessageBoxResult.No);

                        if (closeAnyway != MessageBoxResult.Yes)
                            return;
                    }

                    _closeAfterSuccessfulSave = true;
                    // Do not call Close from the continuation of the Closing event.
                    // WPF can still have its internal closing guard raised even though
                    // this async handler already set Cancel. Queue the final close so
                    // the original event and its continuation unwind completely first.
                    _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ApplicationIdle);
                }
                catch (Exception ex)
                {
                    var reference = AppErrorLog.Record(ex, "application.shutdown-save");
                    _ = _incidentReporter.ReportAsync(
                        ex,
                        "application.shutdown-save",
                        reference,
                        IncidentSeverities.Critical);
                    var closeAnyway = MessageBox.Show(
                        "Sati encountered an unexpected error while saving open work. " +
                        "The text that was on screen may not have been saved.\n\n" +
                        $"Technical code: {ex.GetType().Name} (0x{ex.HResult:X8})\n" +
                        $"Support reference: {reference}\n\n" +
                        "Choose No to keep Sati open. Choose Yes only if you want to close without saving.",
                        "Could Not Finish Closing",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error,
                        MessageBoxResult.No);

                    if (closeAnyway == MessageBoxResult.Yes)
                    {
                        _closeAfterSuccessfulSave = true;
                        _ = Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.ApplicationIdle);
                    }
                }
                finally
                {
                    _isSavingOnClose = false;
                }
            };

            Closed += (s, e) =>
            {
                _databaseActivity.PropertyChanged -= OnDatabaseActivityPropertyChanged;
                CloseDatabasePatienceWindow();
                Application.Current.Shutdown();
            };
        }

        private void OnDatabaseActivityPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DatabaseActivityViewModel.IsPatienceVisible))
                return;

            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(
                    new Action(UpdateDatabasePatienceWindow),
                    DispatcherPriority.Normal);
                return;
            }

            UpdateDatabasePatienceWindow();
        }

        private void UpdateDatabasePatienceWindow()
        {
            if (!IsVisible)
            {
                CloseDatabasePatienceWindow();
                return;
            }

            if (!_databaseActivity.IsPatienceVisible)
            {
                CloseDatabasePatienceWindow();
                return;
            }

            if (_databasePatienceWindow is { IsVisible: true })
                return;

            var window = _databasePatienceWindowFactory();
            // Keep the message above a modal child such as Settings. Otherwise the shell owns
            // both sibling windows and the preview can open behind the dialog being tested.
            var activeOwnedWindow = OwnedWindows
                .OfType<Window>()
                .LastOrDefault(candidate => candidate.IsVisible && candidate.IsActive);
            window.Owner = activeOwnedWindow ?? this;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_databasePatienceWindow, window))
                    _databasePatienceWindow = null;
            };
            _databasePatienceWindow = window;
            window.Show();
        }

        private void CloseDatabasePatienceWindow()
        {
            var window = _databasePatienceWindow;
            _databasePatienceWindow = null;
            if (window is { IsVisible: true })
                window.Close();
        }

        // The switch-to-another-user flow, formerly inline in the greeting handler,
        // now reached from the My Account window's "Switch user" button. Preserved
        // Save the outgoing user's scratchpad and journal, open the switch modal,
        // and on success swap the session user and reinitialize the shell.
        private async Task OpenSwitchUserFlowAsync()
        {
            if (!await _accountSwitchGate.WaitAsync(0))
                return;

            try
            {
                var currentUser = _sessionService.CurrentUser;
                if (currentUser is null)
                    return;

                User? newUser;
                bool? result;
                if (AccountSwitchPolicy.RequiresDirectSignIn(currentUser.Role))
                {
                    // A platform operator must never enumerate an agency's directory.
                    // Use neutral credential entry instead; authentication may identify
                    // any legitimate next account without disclosing account names.
                    var login = _loginWindowFactory();
                    login.Owner = this;
                    login.Title = "Switch Account — Sign in";
                    result = login.ShowDialog();
                    newUser = login.LoggedInUser;
                }
                else
                {
                    if (!await _shellViewModel.Scratchpad.SaveAllScratchpadsAsync())
                        return;
                    if (!await _shellViewModel.NotesViewModel.Clients.FlushJournalAsync())
                    {
                        MessageBox.Show(
                            "Sati could not save the selected client's journal to the cloud, so account switching has been paused. Your text is still visible. Check the connection and try Switch User again.",
                            "Journal Not Saved",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    var picker = _switchUserWindowFactory();
                    picker.Owner = this;
                    result = picker.ShowDialog();
                    newUser = picker.NewUser;
                }

                if (result == true && newUser is not null)
                {
                    _sessionService.SetUser(newUser);
                    await _applicationRunState.StartSessionAsync(newUser, _incidentReporter);
                    await _incidentReporter.FlushAsync();
                    await _shellViewModel.ReinitializeAsync();
                }
            }
            finally
            {
                _accountSwitchGate.Release();
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
