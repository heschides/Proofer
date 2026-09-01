using Sati.Data;
using Sati.Views;
using System.Windows;

namespace Sati.Services;

public sealed class DailyAgendaLauncher(
    DailyAgendaCoordinator coordinator,
    ISessionService session,
    Func<DailyAgendaWindow> windowFactory)
{
    public async Task TryShowAsync(Window owner, ViewModels.ShellViewModel shell)
    {
        if (session.CurrentUser is not { } user)
            return;

        var viewModel = await coordinator.TryCreateAsync(
            user,
            shell.NotesViewModel.People,
            shell.Scratchpad,
            DateOnly.FromDateTime(DateTime.Today));
        if (viewModel is null)
            return;

        viewModel.OpenRequested += async (_, item) =>
            await shell.OpenAgendaItemAsync(item);
        var window = windowFactory();
        window.Owner = owner;
        window.DataContext = viewModel;
        window.ShowDialog();
    }
}
