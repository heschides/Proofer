using System.Xml.Linq;
using Xunit;

namespace Sati.Tests;

public sealed class ReleaseUiStructureTests
{
    private static string Root => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(ReleaseUiStructureTests).Assembly.Location)!,
        "..", "..", "..", "..", ".."));

    [Fact]
    public void HelpAndDocumentsHaveSidebarsBelowTheFeatureTabs()
    {
        var section = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagementView.xaml"));
        var dashboard = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagerDashboardView.xaml"));
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var hub = File.ReadAllText(Path.Combine(
            Root, "Views", "ClientDocuments", "ClientDocumentHubView.xaml"));

        Assert.DoesNotContain("AT Requests", section);
        Assert.DoesNotContain("Content=\"Dashboard\"", section);
        var view = XDocument.Parse(dashboard);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var toolbar = view.Descendants(presentation + "StackPanel")
            .Single(e => (string?)e.Attribute("Orientation") == "Horizontal");
        Assert.Equal(new[] { "Overview", "Clients", "Notes", "Caseload Matrix", "Calendar",
            "Statistics", "Reviews", "Providers", "Help", "Documents" },
            toolbar.Elements(presentation + "Button").Select(e => (string?)e.Attribute("Content")));
        foreach (var (name, destinations) in new[] {
            ("Help navigation", new[] { "Guidance", "Reference" }),
            ("Documents navigation", new[] { "AT Requests", "Authorized Rep", "Releases" }) })
        {
            var sidebar = view.Descendants(presentation + "StackPanel")
                .Single(e => (string?)e.Attribute("AutomationProperties.Name") == name);
            Assert.Equal(destinations, sidebar.Elements(presentation + "Button")
                .Select(e => (string?)e.Attribute("Content")));
        }
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
    public void ClientEditPanelExpandsInsideTheOverviewScroller()
    {
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var document = XDocument.Load(Path.Combine(Root, "Views", "ClientsView.xaml"));

        var editPanel = document.Descendants(presentation + "Border")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Client details edit mode");

        Assert.Empty(editPanel.Descendants(presentation + "ScrollViewer"));
        Assert.Null(editPanel.Attribute("MaxHeight"));

        var overviewScroller = editPanel.Ancestors(presentation + "ScrollViewer")
            .Single(element => element.Attribute("AutomationProperties.Name")?.Value ==
                "Consumer overview");
        Assert.Equal("Visible", overviewScroller.Attribute("VerticalScrollBarVisibility")?.Value);
    }

    [Fact]
    public void AdaptiveDisplayUsesTheUsableViewportAndPreservesFeatureAccess()
    {
        var shell = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml"));
        var shellCode = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml.cs"));
        var shellViewModel = File.ReadAllText(Path.Combine(Root, "ViewModels", "ShellViewModel.cs"));
        var overview = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagerDashboardContentView.xaml"));
        var overviewCode = File.ReadAllText(Path.Combine(Root, "Views", "CaseManagerDashboardContentView.xaml.cs"));
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var clientsCode = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml.cs"));
        var clientViewModel = File.ReadAllText(Path.Combine(Root, "ViewModels", "NewClientViewModel.cs"));

        Assert.Contains("SizeChanged=\"RootGrid_SizeChanged\"", shell);
        Assert.Contains("e.NewSize.Width < compactBoundary", shellCode);
        Assert.DoesNotContain("DetectFor(this)", shellCode);
        Assert.Contains("SetCompactDisplayMode", shellViewModel);
        Assert.Contains("ToggleScratchpadCommand", shell);
        Assert.DoesNotContain("Overview workspace", overview);
        Assert.DoesNotContain("Focus note", overview);
        Assert.Contains("Monthly productivity panel", overview);
        Assert.Contains("UPCOMING DUE DATES", overview);
        Assert.Contains("OverviewLayoutPolicy.Evaluate", overviewCode);
        Assert.Contains("SizeChanged += OnSizeChanged", clientsCode);
        Assert.Contains("SetCompactDisplayMode", clientViewModel);
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
    public void EasyEyesModeScalesTheWorkspaceAndSimplifiesDenseClientViews()
    {
        var shell = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml"));
        var settings = File.ReadAllText(Path.Combine(Root, "Views", "SettingsWindow.xaml"));
        var clients = File.ReadAllText(Path.Combine(Root, "Views", "ClientsView.xaml"));
        var notes = File.ReadAllText(Path.Combine(Root, "Views", "NotesLogView.xaml"));
        var clientViewModel = File.ReadAllText(Path.Combine(Root, "ViewModels", "NewClientViewModel.cs"));

        Assert.Contains("ScaleX=\"{Binding EasyEyesScale}\"", shell);
        Assert.Contains("AutomationProperties.Name=\"Use Easy Eyes mode\"", settings);
        Assert.Contains("UseHorizontalClientSelector", clients);
        Assert.Contains("IsClientListCompact || IsEasyEyesMode", clientViewModel);
        Assert.Contains("IsEnabled=\"{Binding CanToggleClientList}\"", clients);
        Assert.Contains("Data.ShowNarrativeColumn", clients);
        Assert.Contains("Data.ShowNarrativeColumn", notes);
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

        foreach (var name in new[]
                 {
                     "PineCoast", "BlueberryMist", "BlueGrayPearl", "CedarGrove", "HarborNight",
                     "IndustrialMatte", "Paisley", "ArtNouveau", "MidCenturyModern"
                 })
        {
            var supplied = ResourceKeys(Path.Combine(Root, "Themes", $"{name}.xaml"));
            Assert.Empty(required.Except(supplied));
        }

        var service = File.ReadAllText(Path.Combine(Root, "Services", "ThemeService.cs"));
        Assert.Contains("Pine Coast", service);
        Assert.Contains("Blueberry Mist", service);
        Assert.Contains("Blue-Gray Pearl", service);
        Assert.Contains("Cedar Grove", service);
        Assert.Contains("Harbor Night", service);
        Assert.Contains("Ironworks Matte", service);
        Assert.Contains("Paisley", service);
        Assert.Contains("Art Nouveau", service);
        Assert.Contains("Mid-Century Modern", service);
    }

    [Fact]
    public void DecorativeThemesKeepPatternsOnTheShellAndCalmSurfacesUnderContent()
    {
        foreach (var name in new[]
                 {
                     "IndustrialMatte", "Paisley", "ArtNouveau", "MidCenturyModern"
                 })
        {
            var document = XDocument.Load(Path.Combine(Root, "Themes", $"{name}.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var windowBrush = Assert.Single(document.Descendants(), element =>
                element.Attribute(x + "Key")?.Value == "WindowBackgroundBrush");
            var navBrush = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "DrawingBrush" &&
                element.Attribute(x + "Key")?.Value == "NavBackgroundBrush");
            var surface = Assert.Single(document.Descendants(), element =>
                element.Attribute(x + "Key")?.Value == "SurfaceBrush");

            Assert.Equal("Tile", windowBrush.Attribute("TileMode")?.Value);
            Assert.Equal("Tile", navBrush.Attribute("TileMode")?.Value);
            Assert.NotEqual("DrawingBrush", surface.Name.LocalName);
        }
    }

    [Fact]
    public void ContentPatternsAreBlurredWithoutBlurringNavigationOrControls()
    {
        foreach (var name in new[] { "Paisley", "ArtNouveau", "MidCenturyModern" })
        {
            var document = XDocument.Load(Path.Combine(Root, "Themes", $"{name}.xaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

            var windowBrush = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "VisualBrush" &&
                element.Attribute(x + "Key")?.Value == "WindowBackgroundBrush");
            Assert.Contains(windowBrush.Descendants(), element =>
                element.Name.LocalName == "BlurEffect" &&
                double.Parse(element.Attribute("Radius")!.Value,
                    System.Globalization.CultureInfo.InvariantCulture) > 0);

            var navBrush = Assert.Single(document.Descendants(), element =>
                element.Name.LocalName == "DrawingBrush" &&
                element.Attribute(x + "Key")?.Value == "NavBackgroundBrush");
            Assert.DoesNotContain(navBrush.Descendants(), element =>
                element.Name.LocalName == "BlurEffect");
        }
    }

    [Fact]
    public void MenusAndShellIdentityUseThemeOwnedContrastingText()
    {
        var app = File.ReadAllText(Path.Combine(Root, "App.xaml"));
        Assert.Contains("<Style TargetType=\"ContextMenu\">", app);
        Assert.Contains("<Style TargetType=\"MenuItem\">", app);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{DynamicResource TextPrimaryBrush}\" />", app);

        var shell = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml"));
        Assert.Contains("AutomationProperties.Name=\"Open settings\"", shell);
        Assert.Contains("Foreground=\"{DynamicResource TextPrimaryBrush}\"", shell);
        Assert.DoesNotContain("Foreground=\"{DynamicResource WindowBackgroundBrush}\"", shell);
        Assert.Contains("BorderBrush=\"{DynamicResource AccentBrush}\"", shell);

        foreach (var view in Directory.GetFiles(Path.Combine(Root, "Views"), "*.xaml",
                     SearchOption.AllDirectories))
        {
            Assert.DoesNotContain(
                "Foreground=\"{DynamicResource WindowBackgroundBrush}\"",
                File.ReadAllText(view));
        }
    }

    [Fact]
    public void StrongStatusFillsHaveExplicitContrastingForegrounds()
    {
        foreach (var name in new[] { "States", "MidnightOpal", "HarborNight", "IndustrialMatte", "IridescentJewel" })
        {
            var theme = File.ReadAllText(Path.Combine(Root, "Themes", $"{name}.xaml"));
            Assert.True(ContrastRatio(Color(theme, "SuccessStrongBrush"), Color(theme, "OnSuccessStrongBrush")) >= 4.5);
            Assert.True(ContrastRatio(Color(theme, "DangerStrongBrush"), Color(theme, "OnDangerStrongBrush")) >= 4.5);
        }
    }

    [Fact]
    public void CalendarYearNavigationUsesVisibleVectorArrows()
    {
        var calendar = File.ReadAllText(Path.Combine(Root, "Views", "CalendarView.xaml"));

        Assert.Contains("CalendarYearNavigationButtonStyle", calendar);
        Assert.Contains("AutomationProperties.Name=\"Previous year\"", calendar);
        Assert.Contains("AutomationProperties.Name=\"Next year\"", calendar);
        Assert.Contains("Data=\"M 12,4 L 6,10 L 12,16\"", calendar);
        Assert.Contains("Data=\"M 6,4 L 12,10 L 6,16\"", calendar);
        Assert.DoesNotContain("Content=\"&#8249;\"", calendar);
        Assert.DoesNotContain("Content=\"&#8250;\"", calendar);
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
    public void FullDemoResetIsDemoOnlyAccessibleAndRequiresTypedConfirmation()
    {
        var view = File.ReadAllText(Path.Combine(Root, "Views", "AdminDashboardView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(Root, "Views", "AdminDashboardView.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            Root, "ViewModels", "Admin", "AdminDashboardViewModel.cs"));

        Assert.Contains("Restore Demo baseline", view);
        Assert.Contains("AutomationProperties.Name=\"Restore the full Demo baseline\"", view);
        Assert.Contains("Visibility=\"{Binding IsDemoEnvironment", view);
        Assert.Contains("ResetDemoCommand", view);
        Assert.Contains("new TypedConfirmationDialog", codeBehind);
        Assert.Contains("\"RESET DEMO\"", codeBehind);
        Assert.Contains("RequestFullDemoResetAsync(\"RESET DEMO\")", viewModel);
        Assert.Contains("environmentInfo?.Environment == SatiDataEnvironment.Demo", viewModel);
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

    [Fact]
    public void EveryThemeSuppliesTheButtonFillTokensThePrimaryButtonBindsTo()
    {
        var required = new[]
        {
            "AccentButtonBrush", "AccentButtonHoverBrush",
            "AccentButtonPressedBrush", "OnAccentButtonBrush"
        };

        foreach (var theme in Directory.GetFiles(Path.Combine(Root, "Themes"), "*.xaml"))
        {
            if (Path.GetFileName(theme).Equals("States.xaml", StringComparison.OrdinalIgnoreCase))
                continue;

            var supplied = ResourceKeys(theme);
            foreach (var key in required)
                Assert.True(supplied.Contains(key), $"{Path.GetFileName(theme)} is missing {key}.");
        }

        // A theme dictionary is swapped in whole, so a missing key does not fall back
        // to another palette — the button simply loses its fill.
        var app = File.ReadAllText(Path.Combine(Root, "App.xaml"));
        Assert.Contains("{DynamicResource AccentButtonBrush}", app);
        Assert.Contains("{DynamicResource OnAccentButtonBrush}", app);
        Assert.Contains("{DynamicResource AccentButtonHoverBrush}", app);
        Assert.Contains("{DynamicResource AccentButtonPressedBrush}", app);
    }

    [Fact]
    public void TheOrangeThemesKeepDarkAccentTextAndUseALighterButtonFill()
    {
        foreach (var name in new[] { "BlueGrayPearl", "CedarGrove" })
        {
            var theme = File.ReadAllText(Path.Combine(Root, "Themes", $"{name}.xaml"));

            // The accent used for type is unchanged; only the button fill moved.
            Assert.Contains("x:Key=\"AccentBrush\" Color=\"#E25507\"", theme);

            var fill = Color(theme, "AccentButtonBrush");
            var accent = Color(theme, "AccentBrush");
            Assert.True(Luminance(fill) > Luminance(accent) + 0.2,
                $"{name} button fill {fill} is not clearly lighter than accent {accent}.");

            // A light fill needs dark text on it, not the white used elsewhere.
            Assert.True(Luminance(Color(theme, "OnAccentButtonBrush")) < 0.25);
        }
    }

    [Fact]
    public void DecorativeThemePrimaryButtonsMeetNormalTextContrast()
    {
        foreach (var name in new[]
                 {
                     "IndustrialMatte", "Paisley", "ArtNouveau", "MidCenturyModern"
                 })
        {
            var theme = File.ReadAllText(Path.Combine(Root, "Themes", $"{name}.xaml"));
            var contrast = ContrastRatio(
                Color(theme, "AccentButtonBrush"),
                Color(theme, "OnAccentButtonBrush"));

            Assert.True(contrast >= 4.5,
                $"{name} primary button contrast is {contrast:F2}:1; expected at least 4.5:1.");
        }
    }

    [Fact]
    public void TheNoteTemplateButtonReplacedTheLocalAiTrigger()
    {
        var note = File.ReadAllText(Path.Combine(Root, "Views", "NoteEntryView.xaml"));

        Assert.DoesNotContain("FormatNarrativeWithAiCommand", note);
        Assert.DoesNotContain("Format with Local AI", note);
        Assert.Contains("BuildCaseNoteTemplateCommand", note);
        Assert.Contains("Build Case Note Template", note);

        // It is not an AI feature, so it must not hide when local AI is unavailable.
        Assert.Contains("Visibility=\"{Binding IsVisitNote,", note);
        Assert.Contains("AutomationProperties.Name=\"Build a case note template", note);
    }

    [Fact]
    public void TheSuggestedFollowUpRowSitsDirectlyBelowTheNarrativeBox()
    {
        var note = File.ReadAllText(Path.Combine(Root, "Views", "NoteEntryView.xaml"));

        var narrative = note.IndexOf("AutomationProperties.Name=\"Note narrative\"", StringComparison.Ordinal);
        var suggestion = note.IndexOf("IsSuggestedFollowUpVisible", StringComparison.Ordinal);

        Assert.True(narrative > 0);
        Assert.True(suggestion > narrative);
        Assert.Contains("AcceptSuggestedFollowUpCommand", note);
    }

    [Fact]
    public void TheInactivityScreenCoversTheWholeWindowAndIsAdjustable()
    {
        var shell = File.ReadAllText(Path.Combine(Root, "Views", "ShellWindow.xaml"));

        // Outside RootGrid, so the Easy Eyes scale transform does not shrink it.
        Assert.Contains("x:Name=\"ShellRoot\"", shell);
        Assert.Contains("Binding Idle.IsOverlayVisible", shell);
        Assert.Contains("<BlurEffect", shell);
        Assert.Contains("AutomationProperties.LiveSetting=\"Assertive\"", shell);

        // It must not overstate itself.
        Assert.Contains("It does not lock Windows", shell);

        var settings = File.ReadAllText(Path.Combine(Root, "Views", "SettingsWindow.xaml"));
        Assert.Contains("INACTIVITY SCREEN", settings);
        Assert.Contains("Binding IdleTimeoutChoices", settings);
        Assert.Contains("SelectedIdleTimeout", settings);
    }

    private static string Color(string theme, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            theme, $"x:Key=\"{key}\" Color=\"(#[0-9A-Fa-f]{{6,8}})\"");
        Assert.True(match.Success, $"{key} was not found.");
        return match.Groups[1].Value;
    }

    // Rough perceived brightness; enough to assert "clearly lighter" without
    // pulling in a color library.
    private static double Luminance(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length == 8)
            value = value[2..];

        var r = Convert.ToInt32(value[..2], 16) / 255.0;
        var g = Convert.ToInt32(value[2..4], 16) / 255.0;
        var b = Convert.ToInt32(value[4..6], 16) / 255.0;
        return (0.299 * r) + (0.587 * g) + (0.114 * b);
    }

    private static double ContrastRatio(string first, string second)
    {
        static double RelativeLuminance(string hex)
        {
            var value = hex.TrimStart('#');
            if (value.Length == 8)
                value = value[2..];

            static double Linear(int channel)
            {
                var component = channel / 255.0;
                return component <= 0.04045
                    ? component / 12.92
                    : Math.Pow((component + 0.055) / 1.055, 2.4);
            }

            var red = Linear(Convert.ToInt32(value[..2], 16));
            var green = Linear(Convert.ToInt32(value.Substring(2, 2), 16));
            var blue = Linear(Convert.ToInt32(value.Substring(4, 2), 16));
            return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
        }

        var firstLuminance = RelativeLuminance(first);
        var secondLuminance = RelativeLuminance(second);
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }
}
