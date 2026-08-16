using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Services.Billing;
using Sati.Services;
using Sati.Services.LocalAi;
using Sati.ViewModels;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Sati.Views;
using System.Windows;

namespace Sati
{
    public partial class App : Application
    {
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;

        protected override async void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (sender, args) =>
            {
                MessageBox.Show(
                    $"Unhandled exception:\n\n{args.Exception}",
                    "Sati Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            try
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                _host = Host.CreateDefaultBuilder()
                    .UseDefaultServiceProvider((_, options) =>
                    {
#if DEBUG
                        options.ValidateOnBuild = true;
                        options.ValidateScopes = true;
#endif
                    })
                    .ConfigureServices((context, services) =>
                    {
                        services.Configure<LocalAiOptions>(
                            context.Configuration.GetSection(LocalAiOptions.SectionName));

                        // Services
                        services.AddTransient<IPersonService, PersonService>();
                        services.AddTransient<IPersonContactService, PersonContactService>();
                        services.AddTransient<INoteService, NoteService>();
                        services.AddTransient<IAuthService, AuthService>();
                        services.AddTransient<IUserService, UserService>();
                        services.AddTransient<IScratchpadService, ScratchpadService>();
                        services.AddTransient<IPasswordHasher, PasswordHasher>();
                        services.AddTransient<IIncentiveService, IncentiveService>();
                        services.AddSingleton<ISessionService, SessionService>();
                        services.AddTransient<ISettingsService, SettingsService>();
                        services.AddTransient<FormDueDateBackfill>();
                        services.AddTransient<FormBulkCompletion>();
                        services.AddTransient<IUpcomingEventService, UpcomingEventService>(); 
                        services.AddTransient<IFormService, FormService>();
                        services.AddTransient<ISupervisorService, SupervisorService>();
                        services.AddTransient<IBillingService, BillingService>();
                        services.AddTransient<IEdiService, EdiService>();
                        services.AddTransient<IExemptDateService, ExemptDateService>();
                        services.AddTransient<IReviewItemService, ReviewItemService>();
                        services.AddSingleton<ThemeService>();
                        services.AddSingleton<IClientAiContextService, ClientAiContextService>();
                        services.AddSingleton<ICaseNoteFormatter, FoundryLocalCaseNoteFormatter>();
                        services.AddTransient<IATRequestService, ATRequestService>();
                        services.AddTransient<IProviderService, ProviderService>();
                        services.AddTransient<IComprehensiveAssessmentService, ComprehensiveAssessmentService>();
                        // Shell
                        services.AddSingleton<ShellViewModel>();
                        services.AddSingleton<ShellWindow>();

                        // Child ViewModels
                        services.AddSingleton<CaseManagerDashboardViewModel>();
                        services.AddSingleton<StatisticsViewModel>();
                        services.AddTransient<ScratchpadViewModel>();
                        services.AddSingleton<GuidanceViewModel>();
                        services.AddSingleton<HelperReferenceViewModel>();
                        services.AddSingleton<ATRequestViewModel>();
                        services.AddSingleton<HelpersViewModel>();
                        services.AddSingleton<ProvidersViewModel>();
                        services.AddSingleton<ReviewsViewModel>();
                        services.AddSingleton<SupervisorDashboardViewModel>();
                        services.AddTransient<UserManagementViewModel>();
                        services.AddTransient<PendingApprovalsViewModel>();

                        // Modal windows and their ViewModels
                        services.AddTransient<LoginWindow>();
                        services.AddTransient<LoginWindowViewModel>();
                        services.AddTransient<FirstRunAdminWindow>();
                        services.AddTransient<FirstRunAdminViewModel>();
                        services.AddTransient<NewUserWindow>();
                        services.AddTransient<NewUserViewModel>();
                        services.AddTransient<SettingsViewModel>();
                        services.AddTransient<SettingsWindow>();
                        services.AddSingleton<NotesWindowViewModel>();
                        services.AddTransient<ComplianceReviewViewModel>();
                        services.AddTransient<ComplianceReviewWindow>();
                        services.AddTransient<ScratchpadHistoryViewModel>();
                        services.AddTransient<ScratchpadHistoryWindow>();
                        services.AddTransient<SwitchUserViewModel>();
                        services.AddTransient<SwitchUserWindow>();
                        services.AddTransient<MyAccountViewModel>();
                        services.AddTransient<MyAccountWindow>();
                        services.AddTransient<SchedulerViewModel>();
                        services.AddTransient<NewClientViewModel>();

                        // Transient by intent: injected into two singleton hosts
                        // (CaseManagerDashboardViewModel, NotesWindowViewModel),
                        // each capturing its own long-lived, isolated instance.
                        services.AddTransient<NoteEntryViewModel>();
                        services.AddSingleton<BillingDashboardViewModel>();
                        services.AddSingleton<BillingOverviewViewModel>();
                        services.AddSingleton<BillingQueueViewModel>();
                        services.AddSingleton<BillingSubmissionsViewModel>();
                        services.AddSingleton<BillingRemittancesViewModel>();
                        services.AddSingleton<BillingAlertsViewModel>();
                        services.AddSingleton<CalendarViewModel>();

                        // Factories
                        services.AddTransient<Func<string, UserMessageDialog>>(sp => message => new UserMessageDialog(message));
                        services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
                        services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                        services.AddTransient<Func<ScratchpadHistoryWindow>>(sp => () => sp.GetRequiredService<ScratchpadHistoryWindow>());
                        services.AddTransient<Func<SwitchUserWindow>>(sp => () => sp.GetRequiredService<SwitchUserWindow>());
                        services.AddTransient<Func<MyAccountWindow>>(sp => () => sp.GetRequiredService<MyAccountWindow>());
                        services.AddTransient<Func<MyAccountViewModel>>(sp => () => sp.GetRequiredService<MyAccountViewModel>());
                        // EF Core
                        services.AddDbContextFactory<SatiContext>(options =>
                            options.UseSqlServer(context.Configuration.GetConnectionString("SatiDb")),
                            ServiceLifetime.Singleton);
                    })
                    .Build();

                _host.Start();

                // Resolve before splash/login so the user's saved appearance is
                // applied to every window created during this session.
                _host.Services.GetRequiredService<ThemeService>();

                // Migrate database
                using var scope = _host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SatiContext>();
                db.Database.Migrate();

                // Login sequence
                var splash = new SplashScreenWindow();
                splash.Show();
                await Task.Delay(3000);
                splash.Close();

                // Sati must never run without an administrator. Role editing lives behind a
                // supervisor-gated tab and the login screen only ever creates CaseManagers,
                // so a database with no Admin can never grow one from inside the app. Gate
                // startup here — after Migrate, so the Users table is guaranteed to exist,
                // and before any window that assumes a usable account.
                var userService = _host.Services.GetRequiredService<IUserService>();
                if (await userService.AdminCountAsync() == 0)
                {
                    var firstRunWindow = _host.Services.GetRequiredService<FirstRunAdminWindow>();
                    if (firstRunWindow.ShowDialog() != true)
                    {
                        Shutdown();
                        return;
                    }
                }

                var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
                bool? result = loginWindow.ShowDialog();

                if (result == true)
                {
                    var user = loginWindow.LoggedInUser;
                    if (user == null) { Shutdown(); return; }

                    var session = _host.Services.GetRequiredService<ISessionService>();
                    session.SetUser(user);

                    var shellVm = _host.Services.GetRequiredService<ShellViewModel>();
                    await shellVm.InitializeAsync();

                    var shellWindow = _host.Services.GetRequiredService<ShellWindow>();
                    shellWindow.Show();
                }
                else
                {
                    Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Startup failed:\n\n{ex}",
                    "Sati Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
            }

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                _host.Dispose();
            }

            base.OnExit(e);
        }

    }
}
