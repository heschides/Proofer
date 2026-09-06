using Sati.Data;
using Sati.ViewModels;
using Sati.Services;
using Sati.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        private readonly ISessionLifetime _sessionLifetime;
        private readonly SessionKeepAlive? _sessionKeepAlive;
        private readonly IIncidentReporter _incidentReporter;
        private readonly ApplicationRunState _applicationRunState;
        private readonly DatabaseActivityViewModel _databaseActivity;
        private readonly Func<DatabasePatienceWindow> _databasePatienceWindowFactory;
        private readonly TextShortcutService _textShortcutService;
        private readonly TextShortcutHook _textShortcutHook;
        private readonly DailyAgendaLauncher _dailyAgendaLauncher;
        private readonly ScratchpadView _workAgendaView;
        private ContentControl? _overviewAgendaHost;
        private ContentControl? _workAgendaParent;
        private readonly SemaphoreSlim _accountSwitchGate = new(1, 1);
        private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private const double PointerMoveTolerance = 2.0;
        private Point _lastPointerPosition;
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
            ISessionLifetime sessionLifetime,
            IIncidentReporter incidentReporter,
            ApplicationRunState applicationRunState,
            Func<SettingsWindow> settingsWindowFactory,
            Func<ScratchpadHistoryWindow> scratchpadHistoryWindowFactory,
            Func<SwitchUserWindow> switchUserWindowFactory,
            Func<LoginWindow> loginWindowFactory,
            Func<MyAccountWindow> myAccountWindowFactory,
            Func<MyAccountViewModel> myAccountViewModelFactory,
            Func<DatabasePatienceWindow> databasePatienceWindowFactory,
            TextShortcutService textShortcutService,
            TextShortcutHook textShortcutHook,
            DailyAgendaLauncher dailyAgendaLauncher,
            SessionKeepAlive? sessionKeepAlive = null)
        {
            InitializeComponent();
            _shellViewModel = shellViewModel;
            _caseManagerDashboardViewModel = caseManagerDashboardViewModel;
            _sessionService = sessionService;
            _sessionLifetime = sessionLifetime;
            _sessionKeepAlive = sessionKeepAlive;
            _incidentReporter = incidentReporter;
            _applicationRunState = applicationRunState;
            _switchUserWindowFactory = switchUserWindowFactory;
            _loginWindowFactory = loginWindowFactory;
            _myAccountWindowFactory = myAccountWindowFactory;
            _myAccountViewModelFactory = myAccountViewModelFactory;
            _databaseActivity = shellViewModel.DatabaseActivity;
            _databasePatienceWindowFactory = databasePatienceWindowFactory;
            _textShortcutService = textShortcutService;
            _textShortcutHook = textShortcutHook;
            _dailyAgendaLauncher = dailyAgendaLauncher;
            DataContext = shellViewModel;

            _workAgendaView = new ScratchpadView { DataContext = shellViewModel.Scratchpad };
            ShellWorkAgendaHost.Content = _workAgendaView;
            _workAgendaParent = ShellWorkAgendaHost;

            Loaded += async (_, _) =>
            {
                if (_sessionService.CurrentUser is { } currentUser)
                    await _textShortcutService.LoadForUserAsync(currentUser.Id);
                _textShortcutHook.Start(this);
                await _dailyAgendaLauncher.TryShowAsync(this, _shellViewModel);
            };

            _databaseActivity.PropertyChanged += OnDatabaseActivityPropertyChanged;
            _sessionLifetime.SessionEnded += OnSessionEnded;
            Activated += (_, _) => _shellViewModel.SetChatWindowVisible(true);
            Deactivated += (_, _) => _shellViewModel.SetChatWindowVisible(false);

            // Raw input is what proves someone is still at the machine, so the
            // keep-alive is told from here rather than inferring presence from
            // whichever screens happen to poll.
            if (_sessionKeepAlive is not null)
            {
                InputManager.Current.PreProcessInput += (s, e) => _sessionKeepAlive.NoteUserActivity();
                _sessionKeepAlive.Start();
            }

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
                if (e.PropertyName is nameof(ShellViewModel.IsScratchpadVisible)
                    or nameof(ShellViewModel.IsOverviewActive) or nameof(ShellViewModel.IsChatActive))
                {
                    MoveWorkAgendaToPreferredHost();
                    ApplyScratchpadVisibility();
                }
            };

            _shellViewModel.OpenSettingsWindowRequested += async (s, e) =>
            {
                var win = settingsWindowFactory();
                win.Owner = this;
                win.ShowDialog();
                await _shellViewModel.NotesViewModel.Clients.ReloadProfileSettingsAsync();
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

                var confirmation = new ConfirmationDialog(
                    "Close Sati?",
                    "Would you like to close Sati now?\n\n" +
                    "All work inside the Today's Work and Tomorrow's Work sections will be saved.",
                    "Close Sati",
                    isDestructive: true)
                {
                    Owner = this
                };
                if (confirmation.ShowDialog() != true)
                    return;

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

            // Any input at all counts as activity, including input aimed at a
            // child window, because PreProcessInput is application-wide. When the
            // screen is up, the waking input is consumed rather than delivered:
            // the keystroke that wakes Sati must not also type into a note the
            // user cannot read. This is also the seam a PIN prompt would use.
            InputManager.Current.PreProcessInput += OnPreProcessInput;
            _idleTimer.Tick += (_, _) => _shellViewModel.Idle.Evaluate();
            _idleTimer.Start();

            Closed += async (s, e) =>
            {
                await _shellViewModel.Chat.StopAsync();
                _sessionLifetime.SessionEnded -= OnSessionEnded;
                _databaseActivity.PropertyChanged -= OnDatabaseActivityPropertyChanged;
                _idleTimer.Stop();
                InputManager.Current.PreProcessInput -= OnPreProcessInput;
                _textShortcutHook.Dispose();
                CloseDatabasePatienceWindow();
                Application.Current.Shutdown();
            };
        }

        private void OnPreProcessInput(object? sender, PreProcessInputEventArgs e)
        {
            // Only real user input counts, so the app talking to itself cannot hold
            // the screen open forever.
            if (e.StagingItem.Input is not (KeyEventArgs or MouseEventArgs
                or TextCompositionEventArgs or TouchEventArgs))
                return;

            // A bare mouse move is only activity if the pointer actually moved.
            // Showing the overlay changes what is under the cursor, and WPF raises a
            // MouseMove for that alone — without this check the screen would wake
            // itself the instant it appeared.
            if (e.StagingItem.Input is MouseEventArgs
                and not (MouseButtonEventArgs or MouseWheelEventArgs))
            {
                var position = Mouse.GetPosition(this);
                if (Math.Abs(position.X - _lastPointerPosition.X) < PointerMoveTolerance &&
                    Math.Abs(position.Y - _lastPointerPosition.Y) < PointerMoveTolerance)
                    return;

                _lastPointerPosition = position;
            }

            // Consuming the waking input is deliberate: the click or keystroke that
            // brings Sati back must not also press a control or type into a note on a
            // screen the user could not read. It is also where a PIN prompt would go.
            if (_shellViewModel.Idle.RegisterActivity())
                e.Cancel();
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
        // The session ended mid-use: renewal was refused, so nothing will succeed until
        // credentials are entered again. Raised from a background thread, hence the
        // dispatcher hop before any window is touched.
        private void OnSessionEnded(object? sender, EventArgs e) =>
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                _shellViewModel.Chat.SuspendAndClear();
                await PromptForReauthenticationAsync();
            }));

        /// <summary>
        /// Asks for credentials in place rather than making the user restart Sati.
        ///
        /// Signing back in as the same person is not an account switch: the loaded
        /// screens are still theirs, so nothing is reinitialized and unsaved agenda
        /// text survives to be saved under the new token. A different account is a
        /// switch, and takes the same path the Switch User flow does.
        ///
        /// Declining leaves the window open. The session stays dead and every action
        /// says so, but forcing the app closed would take unsaved text with it.
        /// </summary>
        private async Task PromptForReauthenticationAsync()
        {
            var expected = _sessionService.CurrentUser;
            if (expected is null)
                return;

            // Shares the account-switch gate: a lapse arriving while the user is
            // already changing accounts must not stack a second modal on top.
            if (!await _accountSwitchGate.WaitAsync(0))
                return;

            var accountChanged = false;
            var sameAccountReauthenticated = false;
            try
            {
                _shellViewModel.BeginAccountTransition();
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
                var login = _loginWindowFactory();
                login.Owner = this;
                login.Title = "Session Expired — Sign in to continue";
                if (login.ShowDialog() != true || login.LoggedInUser is not { } user)
                    return;

                if (user.Id == expected.Id)
                {
                    _shellViewModel.Scratchpad.ResumeAfterReauthentication();
                    sameAccountReauthenticated = true;
                    return;
                }

                _shellViewModel.ClearOutgoingAccountContent();
                _sessionService.SetUser(user);
                accountChanged = true;
                await _textShortcutService.LoadForUserAsync(user.Id);
                await _applicationRunState.StartSessionAsync(user, _incidentReporter);
                await _incidentReporter.FlushAsync();
                await _shellViewModel.ReinitializeAsync();
                await _dailyAgendaLauncher.TryShowAsync(this, _shellViewModel);
            }
            finally
            {
                if (accountChanged)
                    _shellViewModel.CompleteAccountTransition();
                else
                    _shellViewModel.CancelAccountTransition(sameAccountReauthenticated);
                _accountSwitchGate.Release();
            }
        }

        // and on success swap the session user and reinitialize the shell.
        private async Task OpenSwitchUserFlowAsync()
        {
            if (!await _accountSwitchGate.WaitAsync(0))
                return;

            var accountChanged = false;
            try
            {
                _shellViewModel.BeginAccountTransition();
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
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
                    _shellViewModel.ClearOutgoingAccountContent();
                    _sessionService.SetUser(newUser);
                    accountChanged = true;
                    await _textShortcutService.LoadForUserAsync(newUser.Id);
                    await _applicationRunState.StartSessionAsync(newUser, _incidentReporter);
                    await _incidentReporter.FlushAsync();
                    await _shellViewModel.ReinitializeAsync();
                    await _dailyAgendaLauncher.TryShowAsync(this, _shellViewModel);
                }
            }
            finally
            {
                if (accountChanged)
                    _shellViewModel.CompleteAccountTransition();
                else
                    _shellViewModel.CancelAccountTransition();
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
            ScratchpadRail.Visibility = _shellViewModel.IsOverviewActive || _shellViewModel.IsChatActive
                ? Visibility.Collapsed
                : Visibility.Visible;

            if (!_shellViewModel.IsOverviewActive && !_shellViewModel.IsChatActive && _shellViewModel.IsScratchpadVisible)
            {
                splitterColumn.Width = new GridLength(5);
                scratchpadColumn.MinWidth = 250;
                scratchpadColumn.Width = _savedScratchpadWidth;
            }
            else
            {
                if (scratchpadColumn.Width.Value > 0) _savedScratchpadWidth = scratchpadColumn.Width;
                scratchpadColumn.MinWidth = 0;
                scratchpadColumn.Width = new GridLength(0);
                splitterColumn.Width = new GridLength(0);
            }
        }

        private void RootGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!double.IsFinite(e.NewSize.Width) || e.NewSize.Width <= 0)
                return;

            const double compactBoundary = 1440;
            const double expansionMargin = 48;
            var compact = _shellViewModel.IsCompactDisplayMode
                ? e.NewSize.Width < compactBoundary + expansionMargin
                : e.NewSize.Width < compactBoundary;
            _shellViewModel.SetCompactDisplayMode(compact);
        }

        internal void RegisterOverviewAgendaHost(ContentControl host)
        {
            _overviewAgendaHost = host;
            MoveWorkAgendaToPreferredHost();
            ApplyScratchpadVisibility();
        }

        internal void UnregisterOverviewAgendaHost(ContentControl host)
        {
            if (!ReferenceEquals(_overviewAgendaHost, host))
                return;

            _overviewAgendaHost = null;
            MoveWorkAgendaToPreferredHost();
            ApplyScratchpadVisibility();
        }

        private void MoveWorkAgendaToPreferredHost()
        {
            var target = _shellViewModel.IsOverviewActive && _overviewAgendaHost is not null
                ? _overviewAgendaHost
                : ShellWorkAgendaHost;
            if (ReferenceEquals(_workAgendaParent, target))
                return;

            if (_workAgendaParent?.Content == _workAgendaView)
                _workAgendaParent.Content = null;
            target.Content = _workAgendaView;
            _workAgendaParent = target;
        }
    }
}
