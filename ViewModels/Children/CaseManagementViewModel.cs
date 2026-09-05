namespace Sati.ViewModels.Children;

// Keeps the shell's session and scratchpad integration pointed at one workspace.
public sealed class CaseManagementViewModel(CaseManagerDashboardViewModel dashboard)
{
    public CaseManagerDashboardViewModel Dashboard => dashboard;
    public void ResetToDashboard() => Dashboard.NavigateToOverviewCommand.Execute(null);
}
