using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Windows.Threading;

namespace Sati.ViewModels.Supervisor
{
    public partial class SupervisorDashboardViewModel : ObservableObject
    {
        // -------------------------------------------------------------------------
        // Services
        // -------------------------------------------------------------------------

        private readonly ISessionService _sessionService;
        private readonly IPersonService _personService;
        private readonly INoteService _noteService;
        private readonly IIncentiveService _incentiveService;
        private readonly ISettingsService _settingsService;
        private readonly IUpcomingEventService _upcomingEventService;
        private readonly IUserService _userService;

        // -------------------------------------------------------------------------
        // Sub-view ViewModels
        // -------------------------------------------------------------------------

        private readonly TeamOverviewViewModel _teamOverviewViewModel;
        private readonly OverdueItemsViewModel _overdueItemsViewModel;
        private readonly MonthlyProductivityViewModel _monthlyProductivityViewModel;
        private readonly UserManagementViewModel _userManagementViewModel;
        private readonly PendingApprovalsViewModel _pendingApprovalsViewModel;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        public SupervisorDashboardViewModel(
            ISessionService sessionService,
            IPersonService personService,
            INoteService noteService,
            IIncentiveService incentiveService,
            ISettingsService settingsService,
            IUpcomingEventService upcomingEventService,
            IUserService userService,
            ThemeService themeService,
            UserManagementViewModel userManagementViewModel,
            PendingApprovalsViewModel pendingApprovalsViewModel)
        {
            _sessionService = sessionService;
            _personService = personService;
            _noteService = noteService;
            _incentiveService = incentiveService;
            _settingsService = settingsService;
            _upcomingEventService = upcomingEventService;
            _userService = userService;
            _userManagementViewModel = userManagementViewModel;

            // Rebuild the sidebar when a supervisee's supervisor/role changes.
            // async lambda is the standard fire-and-forget handler shape; InitializeAsync
            // has its own try/catch, so nothing escapes unobserved.
            _userManagementViewModel.UsersChanged += async () =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                await InitializeAsync();
                sw.Stop();
                Debug.WriteLine($">>> Sidebar rebuild took {sw.ElapsedMilliseconds} ms");
            };
            _teamOverviewViewModel = new TeamOverviewViewModel();
            _overdueItemsViewModel = new OverdueItemsViewModel();
            _monthlyProductivityViewModel = new MonthlyProductivityViewModel();

            // OxyPlot colors are resolved in code rather than DynamicResource.
            // Rebuild both chart models immediately when the active palette changes.
            themeService.ThemeChanged += (_, _) =>
            {
                _teamOverviewViewModel.Refresh(CaseManagers);
                _monthlyProductivityViewModel.Refresh(CaseManagers);
            };

            // Start on team overview
            CurrentSubView = _teamOverviewViewModel;
            _pendingApprovalsViewModel = pendingApprovalsViewModel;
        }

        // -------------------------------------------------------------------------
        // Observable properties
        // -------------------------------------------------------------------------

        [ObservableProperty] private object? currentSubView;
        [ObservableProperty] private CaseManagerSummaryViewModel? selectedCaseManager;

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public ObservableCollection<CaseManagerSummaryViewModel> CaseManagers { get; } = [];

        // -------------------------------------------------------------------------
        // Computed properties
        // -------------------------------------------------------------------------

        public string CurrentMonth => DateTime.Now.ToString("MMMM yyyy");

        public string TeamSizeLabel => CaseManagers.Count == 1
            ? "1 case manager"
            : $"{CaseManagers.Count} case managers";

        public int TotalClients => CaseManagers.Sum(cm => cm.ClientCount);
        public int TotalOverdue => CaseManagers.Sum(cm => cm.OverdueCount);
        public int TotalNotesThisMonth => CaseManagers.Sum(cm => cm.NotesThisMonth);
        public bool IsPendingApprovalsActive => CurrentSubView is PendingApprovalsViewModel;

        public string AvgComplianceLabel
        {
            get
            {
                if (CaseManagers.Count == 0) return "—";
                var avg = CaseManagers.Average(cm => cm.ProgressPercent);
                return $"{avg:0}%";
            }
        }

        // Active sub-view indicators for toolbar highlighting
        public bool IsTeamOverviewActive => CurrentSubView is TeamOverviewViewModel;
        public bool IsOverdueItemsActive => CurrentSubView is OverdueItemsViewModel;
        public bool IsMonthlyProductivityActive => CurrentSubView is MonthlyProductivityViewModel;
        public bool IsUserManagementActive => CurrentSubView is UserManagementViewModel;

        // -------------------------------------------------------------------------
        // Property change callbacks
        // -------------------------------------------------------------------------

        partial void OnCurrentSubViewChanged(object? value)
        {
            OnPropertyChanged(nameof(IsTeamOverviewActive));
            OnPropertyChanged(nameof(IsOverdueItemsActive));
            OnPropertyChanged(nameof(IsMonthlyProductivityActive));
            OnPropertyChanged(nameof(IsUserManagementActive));
            OnPropertyChanged(nameof(IsPendingApprovalsActive));
        }

        // -------------------------------------------------------------------------
        // Navigation commands
        // -------------------------------------------------------------------------
        [RelayCommand]
        private async Task NavigateToTeamOverview()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _teamOverviewViewModel.Refresh(CaseManagers);
            CurrentSubView = _teamOverviewViewModel;
        }

        [RelayCommand]
        private async Task NavigateToOverdueItems()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _overdueItemsViewModel.Refresh(CaseManagers);
            CurrentSubView = _overdueItemsViewModel;
        }

        [RelayCommand]
        private async Task NavigateToMonthlyProductivity()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _monthlyProductivityViewModel.Refresh(CaseManagers);
            CurrentSubView = _monthlyProductivityViewModel;
        }

        [RelayCommand]
        private async Task NavigateToUserManagement()
        {
            if (CurrentSubView is UserManagementViewModel)
            {
                CurrentSubView = null;
                return;
            }

            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await _userManagementViewModel.InitializeAsync();
            CurrentSubView = _userManagementViewModel;
        }

        [RelayCommand]
        private async Task NavigateToPendingApprovals()
        {
            CurrentSubView = null;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);

            var filterUserId = SelectedCaseManager?.UserId;
            await _pendingApprovalsViewModel.LoadAsync(filterUserId);
    
            CurrentSubView = _pendingApprovalsViewModel;
}
   
        // -------------------------------------------------------------------------
        // Initialization
        // -------------------------------------------------------------------------

        public async Task InitializeAsync()
        {
            try
            {
                CaseManagers.Clear();
                var supervisor = _sessionService.CurrentUser!;
                var supervisees = await _userService.GetSuperviseesAsync(supervisor.Id);


                var settings = await _settingsService.LoadAsync();
                var now = DateTime.Now;

                // Each supervisee's summary is independent, and every service uses
                // IDbContextFactory (a fresh context per call), so these loads are safe to run
                // concurrently. We build all summaries in parallel, then add them to the
                // ObservableCollection sequentially below — ObservableCollection is not
                // thread-safe and must only be mutated on the UI thread.
                var summaryTasks = supervisees.Select(async user =>
                {
                    var people = await _personService.GetPeopleForSummaryAsync(user.Id);
                    var monthlyNotes = await _noteService.GetMonthlyNotesAsync(user.Id);
                    var events = _upcomingEventService.GenerateEvents(people, settings);

                    var summary = new CaseManagerSummaryViewModel(
                        user, people, monthlyNotes, events);

                    var incentiveSw = System.Diagnostics.Stopwatch.StartNew();
                    var (incentive, _) = await _incentiveService.GetOrCreateAsync(
                        user.Id, now.Month, now.Year);
                    incentiveSw.Stop();
                    System.Diagnostics.Debug.WriteLine(
                        $">>> GetOrCreate for {user.DisplayName}: {incentiveSw.ElapsedMilliseconds} ms");

                    summary.SetThreshold(incentive?.Threshold ?? 0);
                    return summary;
                });

                var whenAllSw = System.Diagnostics.Stopwatch.StartNew();
                var summaries = await Task.WhenAll(summaryTasks);
                whenAllSw.Stop();
                System.Diagnostics.Debug.WriteLine(
                    $">>> Task.WhenAll (all {supervisees.Count} CMs) took {whenAllSw.ElapsedMilliseconds} ms");
                // Preserve the supervisee ordering from GetSuperviseesAsync.
                foreach (var summary in summaries)
                    CaseManagers.Add(summary);

                // SelectedCaseManager = CaseManagers.FirstOrDefault();

                OnPropertyChanged(nameof(TeamSizeLabel));
                OnPropertyChanged(nameof(TotalClients));
                OnPropertyChanged(nameof(TotalOverdue));
                OnPropertyChanged(nameof(TotalNotesThisMonth));
                OnPropertyChanged(nameof(AvgComplianceLabel));

                _teamOverviewViewModel.Refresh(CaseManagers);
                _overdueItemsViewModel.Refresh(CaseManagers);
            }
            catch (Exception ex)
            {
                var reference = AppErrorLog.Record(ex, "supervisor.dashboard.initialize");
                System.Windows.MessageBox.Show(
                    $"The supervisor dashboard could not be loaded. Error reference: {reference}",
                    "Dashboard Not Loaded",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                Debug.WriteLine($"SupervisorDashboardViewModel.InitializeAsync failed: {ex.Message}");
            }
        }

        // -------------------------------------------------------------------------
        // Local Methods
        // -------------------------------------------------------------------------
        public void ClearCharts()
        {
            _teamOverviewViewModel.ComplianceChartModel = null;
            _monthlyProductivityViewModel.StatusChartModel = null;
        }

    }
}
