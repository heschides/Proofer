using System.Configuration;
using System.Data;
using Proofer.Views;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Proofer.Data;
using Proofer.ViewModels;
using Microsoft.Identity.Client;
using Windows.Media.ClosedCaptioning;

namespace Proofer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private IHost? _host;
        public IServiceProvider Services => _host!.Services;

        protected override void OnStartup(StartupEventArgs e)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    //registrations
                    services.AddTransient<IPersonService, PersonService>();
                    services.AddTransient<INoteService, NoteService>();
                    services.AddTransient<IAuthService, AuthService>();
                    services.AddTransient<IUserService, UserService>();
                    services.AddTransient<IPasswordHasher, PasswordHasher>();

                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<MainWindow>();

                    services.AddTransient<NewClientViewModel>();
                    services.AddTransient<NewClientWindow>();

                    services.AddTransient<LoginWindow>();
                    services.AddTransient<LoginWindowViewModel>();

                    services.AddTransient<NewUserWindow>();
                    services.AddTransient<NewUserViewModel>();

                    services.AddTransient<Func<NewUserWindow>>(sp => () => sp.GetRequiredService<NewUserWindow>());
                    services.AddTransient<Func<NewClientWindow>>(sp => () => sp.GetRequiredService<NewClientWindow>());

                    //EF Core
                    services.AddDbContext<ProoferContext>(options => options.UseSqlServer(context.Configuration.GetConnectionString("ProoferDb")), ServiceLifetime.Transient);



                })
                .Build();

            _host.Start();




            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            var mainVm = _host.Services.GetRequiredService<MainWindowViewModel>();
            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            var loginVM = _host.Services.GetRequiredService<LoginWindowViewModel>();
            bool? result = loginWindow.ShowDialog();

        

            if (result == true)
            {
                var user = loginWindow.LoggedInUser;

                if (user == null)
                    return;
                mainVm.Initialize(user);

                mainWindow.Show();
            }
            else
            {
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
