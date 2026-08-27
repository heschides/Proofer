using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseUiStructureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(ReleaseUiStructureTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void RequestedDocumentDestinationsLiveAtTheDashboardLevel()
    {
        var section = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagementView.xaml"));
        var dashboard = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagerDashboardView.xaml"));
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var hub = File.ReadAllText(Path.Combine(
            Root, "Views", "ClientDocuments", "ClientDocumentHubView.xaml"));

        Assert.DoesNotContain("AT Requests", section);
        Assert.Contains("NavigateToATRequestsCommand", dashboard);
        Assert.Contains("NavigateToAuthorizedRepresentativeCommand", dashboard);
        Assert.Contains("NavigateToReleasesCommand", dashboard);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", dashboard);

        Assert.Contains("Header=\"DHHS Forms\"", clients);
        Assert.Contains("Header=\"Agency Release\"", clients);
        Assert.Contains("Header=\"AT Requests\"", clients);
        Assert.Contains("Clients.DhhsForms", hub);
        Assert.Contains("Clients.AgencyRelease", hub);
    }

    [Fact]
    public void NotesFiltersUseOneExplicitInputHeightAndBaseline()
    {
        var notes = File.ReadAllText(Path.Combine(Root, "Views", "NotesLogView.xaml"));

        Assert.Contains("<Style TargetType=\"ComboBox\"", notes);
        Assert.Contains("<Style TargetType=\"TextBox\"", notes);
        Assert.Contains("<Style TargetType=\"DatePicker\"", notes);
        Assert.True(notes.Split("<Setter Property=\"Height\" Value=\"36\" />").Length - 1 >= 3);
        Assert.Contains("Padding=\"10,0\"", notes);
    }

    [Fact]
    public void NewThemesProvideEveryResourceRequiredByTheDefaultTheme()
    {
        var required = ResourceKeys(Path.Combine(Root, "Themes", "SunlitShell.xaml"));

        foreach (var name in new[] { "PineCoast", "BlueberryMist", "HarborNight" })
        {
            var supplied = ResourceKeys(Path.Combine(Root, "Themes", $"{name}.xaml"));
            Assert.Empty(required.Except(supplied));
        }

        var service = File.ReadAllText(Path.Combine(Root, "Services", "ThemeService.cs"));
        Assert.Contains("Pine Coast", service);
        Assert.Contains("Blueberry Mist", service);
        Assert.Contains("Harbor Night", service);
    }

    [Fact]
    public void BillingComplianceChoicesAreAdminOnlyAndExplainTheOverdueBoundary()
    {
        var settings = File.ReadAllText(Path.Combine(Root, "Views", "SettingsWindow.xaml"));

        Assert.Contains("BILLING COMPLIANCE REQUIREMENTS", settings);
        Assert.Contains("blocks billing only when that document is incomplete and its due date has passed", settings);
        Assert.Contains("ComplianceQuarterlyReviews", settings);
        Assert.Contains("ComplianceComprehensiveAssessment", settings);
        Assert.Contains("ComplianceAgencyRelease", settings);
        Assert.Contains("CanManageAgencySettings", settings);
    }

    [Fact]
    public void ClientCreationFailuresAreVisibleAccessibleAndHandledByTheView()
    {
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(Root, "ViewModels", "NewClientViewModel.cs"));

        Assert.Contains("ClientSaveErrorMessage", clients);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", clients);
        Assert.Contains("ClientSaveProblemOccurred += ShowClientSaveProblem", codeBehind);
        Assert.Contains("catch (Exception exception)", viewModel);
        Assert.Contains("ReportClientSaveProblemAsync(exception, stage, creating)", viewModel);
        Assert.Contains("LoadSelectedPersonWorkspaceSafelyAsync", viewModel);
        Assert.Contains("CaptureWorkspaceLoadAsync", viewModel);
        Assert.DoesNotContain("DeleteFormsAsync(existing.Forms)", viewModel);
    }

    [Fact]
    public void ClientEditorIdentifiesRequiredFieldsAndShowsLiveCompletionState()
    {
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));

        Assert.Contains("REQUIRED TO SAVE", clients);
        Assert.Contains("Client email, optional", clients);
        Assert.Contains("Leave blank when no email address is available", clients);
        Assert.Contains("First name, required", clients);
        Assert.Contains("Last name, required", clients);
        Assert.Contains("Date of birth, required", clients);
        Assert.Contains("Biography, required", clients);
        Assert.Contains("IsFirstNameReady", clients);
        Assert.Contains("IsLastNameReady", clients);
        Assert.Contains("IsBirthDateReady", clients);
        Assert.Contains("IsBioReady", clients);
        Assert.Contains("WarningBrush", clients);
        Assert.Contains("SuccessStrongBrush", clients);
        Assert.Contains("All other details are optional", clients);
        Assert.Contains("Representative-payee income and needs become required only when Yes is selected", clients);
    }

    [Fact]
    public void ComplianceMigrationRunnerValidatesIdentityBacksUpLocalDataAndVerifiesSchema()
    {
        var script = File.ReadAllText(Path.Combine(
            Root,
            "scripts",
            "Apply-BillingComplianceRequirementsMigration.ps1"));

        Assert.Contains("DB_NAME() <> @expectedDatabase", script);
        Assert.Contains("SatiDatabaseIdentity", script);
        Assert.Contains("EnvironmentName = @expectedEnvironment", script);
        Assert.Contains("BACKUP DATABASE", script);
        Assert.Contains("SET XACT_ABORT ON", script);
        Assert.Contains("BillingComplianceRequirements", script);
        Assert.Contains("__EFMigrationsHistory", script);
        Assert.Contains("InvalidSettingsRows", script);
    }

    private static HashSet<string> ResourceKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Descendants()
            .Select(element => element.Attribute(x + "Key")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
