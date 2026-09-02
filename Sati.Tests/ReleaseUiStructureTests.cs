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

    // Both dashboard toolbars are horizontal StackPanels, which clip silently once their
    // children exceed the window width: entries past the edge are laid out, never drawn, and
    // nothing reports it. The supervisor toolbar gained two entries in 1.2.38 - Import from
    // Credible and Distribute caseload - and had no ScrollViewer at all.
    [Fact]
    public void BothDashboardToolbarsSurviveMoreEntriesThanFitTheWindow()
    {
        var caseManager = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagerDashboardView.xaml"));
        var supervisor = File.ReadAllText(Path.Combine(Root, "Views", "SupervisorDashboardWindow.xaml"));

        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", caseManager);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", supervisor);

        // The two entries that prompted it, so a future edit that drops one is visible here.
        Assert.Contains("NavigateToCaseloadImportCommand", supervisor);
        Assert.Contains("NavigateToCaseloadDistributionCommand", supervisor);
    }

    [Fact]
    public void ClientWorkspacesExposeOverflowInsteadOfClippingAtLowResolutions()
    {
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var assessment = File.ReadAllText(Path.Combine(
            Root, "Views", "ClientDocuments", "ComprehensiveAssessmentWorkspace.xaml"));
        var plan = File.ReadAllText(Path.Combine(
            Root, "Views", "ClientDocuments", "PersonCenteredPlanWorkspace.xaml"));
        var dhhs = File.ReadAllText(Path.Combine(
            Root, "Views", "ClientDocuments", "DhhsFormsWorkspace.xaml"));
        var release = File.ReadAllText(Path.Combine(
            Root, "Views", "ClientDocuments", "AgencyReleaseWorkspace.xaml"));

        Assert.Contains("AutomationProperties.Name=\"Consumer overview\"", clients);
        Assert.Contains("AutomationProperties.Name=\"Consumer record section navigation\"", clients);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", clients);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", assessment);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", plan);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", dhhs);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", release);

        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Disabled\"", assessment);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Disabled\"", dhhs);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Disabled\"", release);
    }

    [Fact]
    public void CompactDisplayModeWarnsAndPreservesPanelReopenControls()
    {
        var shell = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml"));
        var shellCode = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml.cs"));
        var shellViewModel = File.ReadAllText(Path.Combine(Root, "ViewModels", "ShellViewModel.cs"));
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var clientViewModel = File.ReadAllText(Path.Combine(Root, "ViewModels", "NewClientViewModel.cs"));
        var notice = File.ReadAllText(Path.Combine(Root, "Views", "DisplayAdjustmentDialog.xaml"));
        var noticeCode = File.ReadAllText(Path.Combine(Root, "Views", "DisplayAdjustmentDialog.xaml.cs"));

        Assert.Contains("DetectFor(this)", shellCode);
        Assert.Contains("ApplyCompactDisplayMode", shellCode);
        Assert.Contains("RequiresAdjustmentNotice", shellCode);
        Assert.Contains("_displayAdjustmentNoticeShown", shellCode);
        Assert.Contains("1080p", noticeCode);
        Assert.Contains("1920 × 1080", noticeCode);
        Assert.Contains("Compact display mode notice", notice);

        Assert.Contains("if (collapseScratchpad)", shellViewModel);
        Assert.Contains("IsScratchpadVisible = false", shellViewModel);
        Assert.Contains("ToggleScratchpadCommand", shell);
        Assert.Contains("IsClientListCompact = true", clientViewModel);
        Assert.Contains("ToggleClientListCommand", clients);

        Assert.Contains("ShellNavTabButton", shell);
        Assert.Contains("Primary navigation", shell);
        Assert.Contains("TextOptions.TextFormattingMode=\"Display\"", shell);
        Assert.Contains("TextOptions.TextRenderingMode=\"ClearType\"", shell);
        Assert.Contains("UseLayoutRounding=\"True\"", shell);
        Assert.Contains("IsCompactDisplayMode", clients);
        Assert.Contains("AllowCredibleProfileUpdates", clientViewModel);
        Assert.Contains(
            "Allow Credible imports to update existing consumer profiles",
            File.ReadAllText(Path.Combine(Root, "Views", "SettingsWindow.xaml")));
        Assert.Contains("VrCounselorName", clients);
        Assert.Contains("VrAssistantName", clients);
        Assert.Contains("Visibility=\"{Binding OpenWithVR", clients);
        Assert.Contains("Vocational Rehabilitation assistant title",
            File.ReadAllText(Path.Combine(Root, "Views", "SettingsWindow.xaml")));
        Assert.Contains("VrAssistantTitle", clientViewModel);
    }

    [Fact]
    public void ClientOverviewKeepsWorkingPanelsAheadOfReferencePanels()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(Root, "Views", "ClientsView.xaml"));

        var formsLabel = document.Descendants(presentation + "TextBlock")
            .Single(element => element.Attribute("Text")?.Value == "FORMS");
        var formsRegion = formsLabel.Ancestors(presentation + "Grid")
            .First(element => element.Attribute("Grid.Row") is not null);
        Assert.Equal("2", formsRegion.Attribute("Grid.Row")?.Value);
        Assert.Null(formsRegion.Attribute("MaxHeight"));
        Assert.Empty(formsRegion.Descendants(presentation + "ScrollViewer"));

        var overview = document.Descendants(presentation + "ScrollViewer")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Consumer overview");
        Assert.Equal("Visible", overview.Attribute("VerticalScrollBarVisibility")?.Value);
        Assert.Equal("Disabled", overview.Attribute("HorizontalScrollBarVisibility")?.Value);

        var sectionNavigation = document.Descendants(presentation + "ScrollViewer")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Consumer record section navigation");
        Assert.Equal(
            "Visible",
            sectionNavigation.Attribute("VerticalScrollBarVisibility")?.Value);

        var entryForm = document.Descendants(presentation + "ScrollViewer")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Consumer entry form");
        Assert.Equal("Visible", entryForm.Attribute("VerticalScrollBarVisibility")?.Value);

        var roster = document.Descendants(presentation + "ListBox")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value == "Clients");
        Assert.Equal(
            "Visible",
            roster.Attributes().Single(attribute =>
                attribute.Name.LocalName == "ScrollViewer.VerticalScrollBarVisibility").Value);

        var notes = document.Descendants(presentation + "DataGrid")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Selected person notes");
        var notesRegion = notes.Ancestors(presentation + "Grid")
            .First(element => element.Attribute("Grid.Row") is not null);
        Assert.Equal("4", notesRegion.Attribute("Grid.Row")?.Value);
        Assert.Equal("220", notesRegion.Attribute("MinHeight")?.Value);
        Assert.Equal("180", notes.Attribute("MinHeight")?.Value);

        var contacts = document.Descendants(presentation + "Border")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Consumer contacts and support team");
        var referenceRegion = contacts.Ancestors(presentation + "StackPanel")
            .First(element => element.Attribute("Grid.Row") is not null);
        Assert.Equal("6", referenceRegion.Attribute("Grid.Row")?.Value);
        Assert.Contains(referenceRegion.Descendants(), element =>
            element.Name.LocalName == "ConsumerProvidersView");
    }

    [Fact]
    public void BothClientSelectorsUseTheSamePersonPlusAction()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(Root, "Views", "ClientsView.xaml"));

        var addButtons = document.Descendants(presentation + "Button")
            .Where(element => element.Attribute("Command")?.Value.Contains(
                "OpenEntryPanelCommand", StringComparison.Ordinal) == true)
            .ToList();

        Assert.Equal(2, addButtons.Count);
        Assert.All(addButtons, button => Assert.Equal(
            "{StaticResource AddClientIconTemplate}",
            button.Attribute("ContentTemplate")?.Value));
        Assert.Single(document.Descendants(presentation + "DataTemplate"), template =>
            template.Attributes().Any(attribute =>
                attribute.Name.LocalName == "Key" && attribute.Value == "AddClientIconTemplate"));

        var source = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        Assert.DoesNotContain("&#xE72C;", source);
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
        Assert.Contains("Mark new consumer as Test", clients);
        Assert.Contains("CanMarkNewConsumerAsTest", clients);
        Assert.Contains("This designation cannot be added or removed after creation", clients);
    }

    [Fact]
    public void AdminTestConsumerDeletionIsVisibleAccessibleAndConfirmedByTheView()
    {
        var view = File.ReadAllText(Path.Combine(Root, "Views", "AdminDashboardView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(Root, "Views", "AdminDashboardView.xaml.cs"));

        Assert.Contains("DeleteTestConsumerCommand", view);
        Assert.Contains("TEST DATA ONLY", view);
        Assert.Contains("AutomationProperties.Name=\"Delete selected test consumer\"", view);
        Assert.Contains("This cannot be undone", view);
        Assert.Contains("AutomationProperties.Name=\"Test consumer\"", view);
        Assert.Contains("marked Test at creation", view);
        Assert.Contains("TestConsumerDeletionConfirmationRequested += ConfirmTestConsumerDeletion", codeBehind);
        Assert.Contains("new ConfirmationDialog", codeBehind);
        Assert.Contains("isDestructive: true", codeBehind);
        Assert.Contains("\"Delete\"", codeBehind);
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
