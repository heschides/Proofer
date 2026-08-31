using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Sati.ViewModels.Admin;
using System.Windows;
using System.Windows.Media;

namespace Sati.ViewModels
{
    public partial class ShellViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        // Case Management is a sub-tab host: it owns the dashboard plus Guidance,
        // Reference, and AT Requests, which used to sit beside it at the top level.
        private readonly CaseManagementViewModel _caseManagementViewModel;
        private readonly SupervisorDashboardViewModel _supervisorDashboardViewModel;
        private readonly ISessionService _sessionService;
        private readonly BillingDashboardViewModel _billingDashboardViewModel;
        private readonly AdminDashboardViewModel _adminDashboardViewModel;
        private readonly PlatformHealthViewModel _platformHealthViewModel;
        private readonly DataEnvironmentInfo _dataEnvironment;
        private readonly IApiCompatibilityService _apiCompatibility;


        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ShellViewModel(
            CaseManagementViewModel caseManagementViewModel,
            ScratchpadViewModel scratchpadViewModel,
            SupervisorDashboardViewModel supervisorViewModel,
            ISessionService sessionService,
            BillingDashboardViewModel billingDashboardViewModel,
            AdminDashboardViewModel adminDashboardViewModel,
            PlatformHealthViewModel platformHealthViewModel,
            DataEnvironmentInfo dataEnvironment,
            IApiCompatibilityService apiCompatibility,
            DatabaseActivityViewModel databaseActivity)
        {
            _apiCompatibility = apiCompatibility;
            _caseManagementViewModel = caseManagementViewModel;
            _supervisorDashboardViewModel = supervisorViewModel;
            _sessionService = sessionService;
            Scratchpad = scratchpadViewModel;
            _billingDashboardViewModel = billingDashboardViewModel;
            _adminDashboardViewModel = adminDashboardViewModel;
            _platformHealthViewModel = platformHealthViewModel;
            _dataEnvironment = dataEnvironment;
            DatabaseActivity = databaseActivity;
        }

        // -------------------------------------------------------------------------
        // Events
        // -------------------------------------------------------------------------

        public event EventHandler? SwitchUserRequested;
        public event EventHandler<bool>? OpenSettingsWindowRequested;

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private object? currentViewModel;

        // Open/closed state of the scratchpad panel. The actual column collapse and
        // width-restore lives in ShellWindow.xaml.cs, which reacts to this changing —
        // remembering a user-dragged GridSplitter width is pure view layout, not a
        // view-model concern. Defaults open.
        [ObservableProperty] private bool isScratchpadVisible = true;
        // -------------------------------------------------------------------------
        // Child ViewModels
        // -------------------------------------------------------------------------

        public ScratchpadViewModel Scratchpad { get; }
        public DatabaseActivityViewModel DatabaseActivity { get; }

        // Forwarded so window-close journal flushing still reaches the dashboard now
        // that Case Management owns it.
        public CaseManagerDashboardViewModel NotesViewModel => _caseManagementViewModel.Dashboard;

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------
        public bool IsCaseManagementAvailable =>
            _sessionService.CurrentUser?.HasCaseManagerPermissions == true;
        public bool IsBillingAvailable =>
            _sessionService.CurrentUser?.HasBillingPermissions == true;
        public bool IsAdminAvailable =>
            _sessionService.CurrentUser?.HasAdminPermissions == true;
        public bool IsPlatformHealthAvailable => _sessionService.CurrentUser?.Role is UserRole.PlatformOperator;
        public bool IsDemoEnvironment => _dataEnvironment.IsDemo;
        public string DataEnvironmentLabel => _dataEnvironment.DisplayName;

        public bool IsBillingActive => CurrentViewModel is BillingDashboardViewModel;
        public bool IsAdminActive => CurrentViewModel is AdminDashboardViewModel;
        public bool IsPlatformHealthActive => CurrentViewModel is PlatformHealthViewModel;

        public bool IsSupervisionAvailable =>
            _sessionService.CurrentUser?.HasSupervisorPermissions == true;

        // Active tab indicators
        public bool IsCaseManagementActive => CurrentViewModel is CaseManagementViewModel;
        public bool IsSupervisorActive => CurrentViewModel is SupervisorDashboardViewModel;

        // User header
        public string UserGreeting => $"Hello, {_sessionService.CurrentUser?.DisplayName ?? "there"}.";

        public string UserInitials
        {
            get
            {
                var name = _sessionService.CurrentUser?.DisplayName ?? "?";
                var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2
                    ? $"{parts[0][0]}{parts[^1][0]}"
                    : name.Length > 0 ? name[0].ToString() : "?";
            }
        }

        public SolidColorBrush AvatarBrush => new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(
                _sessionService.CurrentUser switch
                {
                    { Role: UserRole.PlatformOperator } => "#7A2E8E",
                    { HasAdminPermissions: true } => "#4A3728",
                    { HasBillingPermissions: true } => "#A6607A",
                    { HasSupervisorPermissions: true } => "#5A8A5A",
                    { HasCaseManagerPermissions: true } => "#5B7FA6",
                    _ => "#9C7A5C"
                }));

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsCaseManagementActive));
            OnPropertyChanged(nameof(IsSupervisorActive));
            OnPropertyChanged(nameof(IsBillingActive));
            OnPropertyChanged(nameof(IsAdminActive));
            OnPropertyChanged(nameof(IsPlatformHealthActive));
            if (value is not SupervisorDashboardViewModel)
                _supervisorDashboardViewModel?.ClearCharts();
        }

        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------

        [RelayCommand]
        private void NavigateToCaseManagement()
        {
            if (IsCaseManagementAvailable) CurrentViewModel = _caseManagementViewModel;
        }
        [RelayCommand]
        private void NavigateToSupervisorDashboard()
        {
            if (IsSupervisionAvailable) CurrentViewModel = _supervisorDashboardViewModel;
        }
        [RelayCommand] private void RequestSwitchUser() => SwitchUserRequested?.Invoke(this, EventArgs.Empty);
        [RelayCommand] public void OpenSettingsWindow() => OpenSettingsWindowRequested?.Invoke(this, true);
        [RelayCommand]
        private void NavigateToBilling()
        {
            if (IsBillingAvailable) CurrentViewModel = _billingDashboardViewModel;
        }
        [RelayCommand]
        private async Task NavigateToAdmin()
        {
            if (!IsAdminAvailable) return;
            CurrentViewModel = _adminDashboardViewModel;
            await _adminDashboardViewModel.InitializeAsync();
        }
        [RelayCommand]
        private async Task NavigateToPlatformHealth()
        {
            CurrentViewModel = _platformHealthViewModel;
            await _platformHealthViewModel.RefreshAsync();
        }
        [RelayCommand] private void ToggleScratchpad() => IsScratchpadVisible = !IsScratchpadVisible;
        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        // -------------------------------------------------------------------------
        // API compatibility
        // -------------------------------------------------------------------------

        /// <summary>
        /// Set when the server serves a different route surface than this build
        /// expects. Drives a banner rather than blocking anything: most of the
        /// application still works, and the point is to name the cause before a
        /// missing route surfaces somewhere else as a missing record.
        /// </summary>
        [ObservableProperty]
        private bool _serverSurfaceDisagrees;

        [ObservableProperty]
        private string _serverSurfaceWarning = string.Empty;

        public async Task InitializeAsync()
        {
            NotifyRoleDependentProperties();
            await CheckApiCompatibilityAsync();
            if (_sessionService.CurrentUser?.Role == UserRole.PlatformOperator)
            {
                await NavigateToPlatformHealth();
                return;
            }

            await Scratchpad.InitializeAsync();
            // Awaited, not fire-and-forget: the dashboard's own People-load must finish
            // before the NotesLog and Clients reloads run theirs. Overlapping People-loads
            // each demand a LocalDB sort grant and stall on RESOURCE_SEMAPHORE; sequenced,
            // only one grant is live at a time.
            await NotesViewModel.InitializeAsync();
            // NotesLog hosts its own NoteEntry instance; the dashboard's init only
            // covers the dashboard's copy. This loads the module's settings so its
            // narrative templates populate.
            await NotesViewModel.NotesLog.NoteEntry.InitializeAsync();
            await NotesViewModel.NotesLog.ReloadAsync();
            await NotesViewModel.Clients.ReloadAsync();

            await NavigateByRoleAsync();
        }

        /// <summary>
        /// Never allowed to break sign-in. A compatibility check that could stop a
        /// case manager working would be a worse failure than the one it detects.
        /// </summary>
        private async Task CheckApiCompatibilityAsync()
        {
            try
            {
                var compatibility = await _apiCompatibility.CheckAsync();
                ServerSurfaceDisagrees = compatibility.Disagrees;
                ServerSurfaceWarning = compatibility.Detail ?? string.Empty;
            }
            catch (Exception)
            {
                ServerSurfaceDisagrees = false;
                ServerSurfaceWarning = string.Empty;
            }
        }

        public async Task ReinitializeAsync()
        {
            // The switch flow saves the outgoing user's scratchpad and journal before
            // authentication replaces the cloud API token. From this point onward every
            // request must belong to the newly selected user.
            NotesViewModel.Reset();
            _caseManagementViewModel.ResetToDashboard();
            NotifyRoleDependentProperties();
            if (_sessionService.CurrentUser?.Role == UserRole.PlatformOperator)
            {
                await NavigateToPlatformHealth();
                return;
            }

            await Scratchpad.InitializeAsync();
            await NotesViewModel.InitializeAsync();
            // NotesLog hosts its own NoteEntry instance; the dashboard's init only
            // covers the dashboard's copy. This loads the module's settings so its
            // narrative templates populate.
            await NotesViewModel.NotesLog.NoteEntry.InitializeAsync();
            await NotesViewModel.NotesLog.ReloadAsync();
            await NotesViewModel.Clients.ReloadAsync();

            await NavigateByRoleAsync();
        }

        private void NotifyRoleDependentProperties()
        {
            OnPropertyChanged(nameof(UserGreeting));
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(AvatarBrush));
            OnPropertyChanged(nameof(IsSupervisionAvailable));
            OnPropertyChanged(nameof(IsCaseManagementAvailable));
            OnPropertyChanged(nameof(IsBillingAvailable));
            OnPropertyChanged(nameof(IsAdminAvailable));
            OnPropertyChanged(nameof(IsPlatformHealthAvailable));
        }

        private async Task NavigateByRoleAsync()
        {
            if (_sessionService.CurrentUser?.Role == UserRole.PlatformOperator)
            {
                await NavigateToPlatformHealth();
                return;
            }
            if (_sessionService.CurrentUser?.HasSupervisorPermissions == true)
                await InitializeSupervisorAsync();
            if (IsCaseManagementAvailable) NavigateToCaseManagement();
            else if (IsSupervisionAvailable) NavigateToSupervisorDashboard();
            else if (IsBillingAvailable) NavigateToBilling();
            else if (IsAdminAvailable) await NavigateToAdmin();
        }

        private async Task InitializeSupervisorAsync()
        {
            await _supervisorDashboardViewModel.InitializeAsync();
        }


    }
}
