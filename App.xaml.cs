using System.Configuration;
using System.Data;
using Sati.Views;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sati.Data;
using Sati.ViewModels;
using Microsoft.Identity.Client;
using Windows.Media.ClosedCaptioning;

namespace Sati
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;


        protected override async void OnStartup(StartupEventArgs e)
        {
            var splash = new SplashScreenWindow();
            splash.Show();

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    //REGISTRATIONS
                    //services
                    services.AddTransient<IPersonService, PersonService>();
                    services.AddTransient<INoteService, NoteService>();
                    services.AddTransient<IAuthService, AuthService>();
                    services.AddTransient<IUserService, UserService>();
                    services.AddTransient<IScratchpadService, ScratchpadService>();
                    services.AddTransient<IPasswordHasher, PasswordHasher>();
                    services.AddTransient<IIncentiveService, IncentiveService>();
                    services.AddSingleton<ISessionService, SessionService>();
                    services.AddTransient<ISettingsService, SettingsService>();
                    services.AddTransient<IUpcomingEventService, UpcomingEventService>();


                    //windows and viewmodels
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();

                    services.AddTransient<NewClientViewModel>();
                    services.AddTransient<NewClientWindow>();

                    services.AddTransient<LoginWindow>();
                    services.AddTransient<LoginWindowViewModel>();

                    services.AddTransient<NewUserWindow>();
                    services.AddTransient<NewUserViewModel>();

                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<SettingsWindow>();

                    services.AddTransient<SchedulerViewModel>();

                    services.AddTransient<Func<SettingsWindow>>(sp => () => sp.GetRequiredService<SettingsWindow>());
                    services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                    services.AddTransient<Func<NewClientWindow>>(sp => () => sp.GetRequiredService<NewClientWindow>());



                    //ef core
                    services.AddDbContext<SatiContext>(options => options.UseSqlServer(context.Configuration.GetConnectionString("SatiDb")), ServiceLifetime.Transient);

                })
                .Build();

            _host.Start();



            //LOGIN SEQUENCE
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            var loginVM = _host.Services.GetRequiredService<LoginWindowViewModel>();
            await Task.Delay(3000);
            splash.Close();
            bool? result = loginWindow.ShowDialog();

            if (result == true)
            {
                var user = loginWindow.LoggedInUser;
                if (user == null)
                    return;

                var session = _host.Services.GetRequiredService<ISessionService>();
                session.SetUser(user);

                mainVm.Initialize();
                mainWindow.Show();
            }
            else
            {
                splash.Close();
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
