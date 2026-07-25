using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using System.Windows;
using System.Windows.Media;

namespace Sati.ViewModels
{
    public partial class ShellViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services & private state
        // -------------------------------------------------------------------------

        private readonly CaseManagerDashboardViewModel _notesViewModel;
        private readonly SupervisorDashboardViewModel _supervisorDashboardViewModel;
        private readonly GuidanceViewModel _guidanceViewModel;
        private readonly HelpersViewModel _helpersViewModel;
        private readonly ISessionService _sessionService;
        private readonly BillingDashboardViewModel _billingDashboardViewModel;


        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public ShellViewModel(
            CaseManagerDashboardViewModel notesViewModel,
            ScratchpadViewModel scratchpadViewModel,
            SupervisorDashboardViewModel supervisorViewModel,
            GuidanceViewModel guidanceViewModel,
            HelpersViewModel helpersViewModel,
            ISessionService sessionService,
            BillingDashboardViewModel billingDashboardViewModel)
        {
            _notesViewModel = notesViewModel;
            _supervisorDashboardViewModel = supervisorViewModel;
            _guidanceViewModel = guidanceViewModel;
            _helpersViewModel = helpersViewModel;
            _sessionService = sessionService;
            Scratchpad = scratchpadViewModel;
            _billingDashboardViewModel = billingDashboardViewModel;
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
        public CaseManagerDashboardViewModel NotesViewModel => _notesViewModel;

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------
        public bool IsBillingAvailable =>
    _sessionService.CurrentUser?.Role is UserRole.Admin;

        public bool IsBillingActive => CurrentViewModel is BillingDashboardViewModel;

        public bool IsSupervisionAvailable =>
            _sessionService.CurrentUser?.Role is UserRole.Supervisor
                or UserRole.Admin
                or UserRole.Director;

        // Active tab indicators
        public bool IsNotesActive => CurrentViewModel is CaseManagerDashboardViewModel;
        public bool IsSupervisorActive => CurrentViewModel is SupervisorDashboardViewModel;
        public bool IsGuidanceActive => CurrentViewModel is GuidanceViewModel;
        public bool IsHelpersActive => CurrentViewModel is HelpersViewModel;

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
                _sessionService.CurrentUser?.Role switch
                {
                    UserRole.CaseManager => "#5B7FA6",
                    UserRole.Supervisor => "#5A8A5A",
                    UserRole.Director => "#A6607A",
                    UserRole.Admin => "#4A3728",
                    _ => "#9C7A5C"
                }));

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentViewModelChanged(object? value)
        {
            OnPropertyChanged(nameof(IsNotesActive));
            OnPropertyChanged(nameof(IsSupervisorActive));
            OnPropertyChanged(nameof(IsGuidanceActive));
            OnPropertyChanged(nameof(IsHelpersActive));
            OnPropertyChanged(nameof(IsBillingActive));
            if (value is not SupervisorDashboardViewModel)
                _supervisorDashboardViewModel?.ClearCharts();
        }

        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------

        [RelayCommand] private void NavigateToCaseManagement() => CurrentViewModel = _notesViewModel;
        [RelayCommand] private void NavigateToSupervisorDashboard() => CurrentViewModel = _supervisorDashboardViewModel;
        [RelayCommand] private void NavigateToGuidance() => CurrentViewModel = _guidanceViewModel;
        [RelayCommand] private void NavigateToHelpers() => CurrentViewModel = _helpersViewModel;
        [RelayCommand] private void RequestSwitchUser() => SwitchUserRequested?.Invoke(this, EventArgs.Empty);
        [RelayCommand] public void OpenSettingsWindow() => OpenSettingsWindowRequested?.Invoke(this, true);
        [RelayCommand] private void NavigateToBilling() => CurrentViewModel = _billingDashboardViewModel;
        [RelayCommand] private void ToggleScratchpad() => IsScratchpadVisible = !IsScratchpadVisible;
        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            await Scratchpad.InitializeAsync();
            // Awaited, not fire-and-forget: the dashboard's own People-load must finish
            // before the NotesLog and Clients reloads run theirs. Overlapping People-loads
            // each demand a LocalDB sort grant and stall on RESOURCE_SEMAPHORE; sequenced,
            // only one grant is live at a time.
            await _notesViewModel.InitializeAsync();
            await _notesViewModel.NotesLog.ReloadAsync();
            await _notesViewModel.Clients.ReloadAsync();

            OnPropertyChanged(nameof(UserGreeting));
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(AvatarBrush));
            OnPropertyChanged(nameof(IsSupervisionAvailable));
            OnPropertyChanged(nameof(IsBillingAvailable));
            NavigateByRole();
        }

        public async Task ReinitializeAsync()
        {
            await Scratchpad.SaveScratchpadAsync(Scratchpad.ScratchpadContent);
            await Scratchpad.InitializeAsync();
            _notesViewModel.Reset();
            await _notesViewModel.InitializeAsync();
            await _notesViewModel.NotesLog.ReloadAsync();
            await _notesViewModel.Clients.ReloadAsync();
            OnPropertyChanged(nameof(UserGreeting));
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(AvatarBrush));
            OnPropertyChanged(nameof(IsSupervisionAvailable));
            OnPropertyChanged(nameof(IsBillingAvailable));
            NavigateByRole();
        }

        private void NavigateByRole()
        {
            if (_sessionService.CurrentUser?.Role is UserRole.Supervisor or UserRole.Admin or UserRole.Director)
                _ = InitializeSupervisorAsync();

            NavigateToCaseManagement();
        }

        private async Task InitializeSupervisorAsync()
        {
            await _supervisorDashboardViewModel.InitializeAsync();
        }


    }
}