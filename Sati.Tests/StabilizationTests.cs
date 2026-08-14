using Sati.Api.Security;
using Sati.Data;
using Sati.Data.Billing;
using Sati.Edi;
using Sati.Converters;
using Sati.Helpers;
using Sati.Models;
using Sati.Models.Billing;
using Sati.ViewModels.Billing;
using Sati.ViewModels.Children;
using Sati.ViewModels.Supervisor;
using Sati.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Reflection;
using System.Globalization;
using Sati.Contracts.V1;
using Sati.Reporting;
using Sati.Services;
using PdfSharp.Fonts;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Xunit;

namespace Sati.Tests;

public sealed class StabilizationTests
{
    [Fact]
    public void EmptyIncidentWindowIsUnknownRatherThanHealthy()
    {
        var health = IncidentHealthScoring.Calculate([], DateTime.UtcNow, 30);

        Assert.Null(health.Score);
        Assert.False(health.HasTelemetry);
        Assert.Equal("No telemetry", health.Grade);
        Assert.Equal("Unknown", health.AlertLevel);
    }

    [Fact]
    public void IncidentHealthScoreExplainsSeverityRecurrenceAndAgePenalties()
    {
        var now = new DateTime(2026, 8, 13, 16, 0, 0, DateTimeKind.Utc);
        var incidents = new[]
        {
            new IncidentGroupDto(
                1, 1, IncidentScopes.Agency, "Desktop", "Error", "billing.queue.load", "1.2.2", "1.2.2",
                "ABCDEF012345", "Open", 3, now.AddDays(-8), now.AddHours(-1), "REF100001", "Admin"),
            new IncidentGroupDto(
                2, 1, IncidentScopes.Agency, "Api", "Warning", "GET.api", "1.2.2", "1.2.2",
                "BBBBBB012345", "Resolved", 1, now.AddDays(-1), now.AddHours(-2), "REF100002", "CaseManager")
        };

        var health = IncidentHealthScoring.Calculate(incidents, now, 30);

        Assert.Equal(83, health.Score);
        Assert.Equal("Watch", health.Grade);
        Assert.Equal(13, health.SeverityPenalty);
        Assert.Equal(2, health.RecurrencePenalty);
        Assert.Equal(2, health.UnresolvedAgePenalty);
        Assert.Equal("incident-health-v1", health.FormulaVersion);
        Assert.Contains("recorded incidents only", health.Explanation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void CaseManagerMayWriteOnlyOwnedWorkflowStatuses(int status)
    {
        Assert.True(NoteWorkflow.IsCaseManagerWritableStatus(status));
    }

    [Theory]
    [InlineData(6)] // Approved
    [InlineData(7)] // Returned
    [InlineData(8)] // Abandoned
    public void CaseManagerCannotAssertServerOwnedStatuses(int status)
    {
        Assert.False(NoteWorkflow.IsCaseManagerWritableStatus(status));
    }

    [Fact]
    public void LoggedAndApprovedNotesAreLocked()
    {
        Assert.False(NoteWorkflow.CanCaseManagerEdit(2));
        Assert.False(NoteWorkflow.CanCaseManagerEdit(6));
        Assert.True(NoteWorkflow.CanCaseManagerEdit(7));
    }

    [Fact]
    public void OnlyUnsubmittedNotesCanBeDeleted()
    {
        Assert.True(NoteWorkflow.CanCaseManagerDelete(1));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(2));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(6));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(7));
    }

    [Theory]
    [InlineData(NoteStatus.Scheduled, "planned work", "supervisor review", "billing")]
    [InlineData(NoteStatus.Pending, "finish it later", "supervisor cannot review", "cannot be billed")]
    [InlineData(NoteStatus.Logged, "sends the completed note", "supervisor", "billing queue")]
    [InlineData(NoteStatus.Cancelled, "did not occur", "approval", "billing")]
    [InlineData(NoteStatus.Delayed, "postponed", "approval", "billing")]
    public void SelectableNoteStatusesExplainTheirWorkflowConsequences(
        NoteStatus status,
        string expectedMeaning,
        string expectedReviewConsequence,
        string expectedBillingConsequence)
    {
        var guidance = NoteEntryViewModel.DescribeStatus(status);

        Assert.Contains(expectedMeaning, guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedReviewConsequence, guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedBillingConsequence, guidance, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoteStatusGuidanceOccupiesItsOwnLayoutRowOnBothHostPages()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var document = System.Xml.Linq.XDocument.Load(
            Path.Combine(directory!.FullName, "Views", "NoteEntryView.xaml"));
        var statusControl = document.Descendants()
            .Single(element => element.Name.LocalName == "ComboBox" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "ItemsSource" && attribute.Value.Contains("NoteStatusOptions")));
        var guidance = document.Descendants()
            .Single(element => element.Name.LocalName == "TextBlock" &&
                element.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Text" && attribute.Value.Contains("StatusGuidance")));

        var statusRow = statusControl.Attributes().Single(attribute => attribute.Name.LocalName == "Grid.Row").Value;
        var guidanceRow = guidance.Attributes().Single(attribute => attribute.Name.LocalName == "Grid.Row").Value;
        Assert.NotEqual(statusRow, guidanceRow);
        Assert.Contains(statusControl.Attributes(), attribute =>
            attribute.Name.LocalName == "AutomationProperties.HelpText" &&
            attribute.Value.Contains("StatusGuidance"));
    }

    [Fact]
    public void SharedBillingComplianceGateReturnsEveryActionableReason()
    {
        var today = new DateTime(2026, 8, 14);
        var forms = new[]
        {
            new ComplianceFormSnapshot("PCP", new DateTime(2026, 12, 31), true),
            new ComplianceFormSnapshot("ComprehensiveAssessment", new DateTime(2026, 12, 31), false),
            new ComplianceFormSnapshot("Reclassification", new DateTime(2026, 12, 31), true),
            new ComplianceFormSnapshot("SafetyPlan", new DateTime(2026, 12, 31), true),
            new ComplianceFormSnapshot("Q2R", new DateTime(2026, 8, 1), false)
        };

        var result = BillingComplianceGate.Evaluate(
            new DateTime(2026, 1, 1), forms, today);

        Assert.False(result.Passed);
        Assert.Equal(2, result.Reasons.Count);
        Assert.Contains(result.Reasons, reason => reason.Contains("Comprehensive Assessment"));
        Assert.Contains(result.Reasons, reason =>
            reason.Contains("Q2 Review") && reason.Contains("Aug 1, 2026"));
    }

    [Fact]
    public void ClientCompliancePresentationUsesTheSharedGate()
    {
        var person = Person.Rehydrate(44, 7);
        person.EffectiveDate = new DateTime(2026, 1, 1);
        person.Forms =
        [
            new Form(FormType.PCP, new DateTime(2026, 12, 31), true),
            new Form(FormType.ComprehensiveAssessment, new DateTime(2026, 12, 31), false),
            new Form(FormType.Reclassification, new DateTime(2026, 12, 31), true),
            new Form(FormType.SafetyPlan, new DateTime(2026, 12, 31), true)
        ];

        var reasons = NewClientViewModel.GetComplianceReasons(person, new DateTime(2026, 8, 14));
        var converter = new PersonBillingComplianceConverter();

        Assert.Single(reasons);
        Assert.Contains("Comprehensive Assessment", reasons[0]);
        Assert.False(Assert.IsType<bool>(converter.Convert(person, typeof(bool), null, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void PendingReviewCardKeepsTheFailuresThatClassifiedTheNote()
    {
        var person = Person.Rehydrate(45, 7);
        person.FirstName = "Test";
        person.LastName = "Consumer";
        var note = Note.Rehydrate(90);
        note.Person = person;
        note.PersonId = person.Id;
        note.Narrative = "Review note";
        note.ComplianceFailureReasons =
        [
            "PCP is not marked compliant for the current cycle."
        ];

        var card = new PendingNoteViewModel(note);

        Assert.True(card.HasComplianceFailures);
        Assert.Equal(note.ComplianceFailureReasons, card.ComplianceFailureReasons);
    }

    [Fact]
    public async Task JournalSaveCoordinatorSerializesAutosaveAndSwitchFlush()
    {
        var coordinator = new JournalSaveCoordinator();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var inFlight = 0;
        var maximumInFlight = 0;

        async Task Save(bool wait)
        {
            var current = Interlocked.Increment(ref inFlight);
            maximumInFlight = Math.Max(maximumInFlight, current);
            if (wait)
            {
                firstEntered.SetResult();
                await releaseFirst.Task;
            }
            Interlocked.Decrement(ref inFlight);
        }

        var autosave = coordinator.TrySaveAsync(() => Save(true));
        await firstEntered.Task;
        var switchFlush = coordinator.TrySaveAsync(() => Save(false));
        await Task.Delay(25);

        Assert.False(switchFlush.IsCompleted);
        releaseFirst.SetResult();
        Assert.True((await autosave).Succeeded);
        Assert.True((await switchFlush).Succeeded);
        Assert.Equal(1, maximumInFlight);
    }

    [Fact]
    public async Task JournalSaveCoordinatorReturnsFailureWithoutDispatcherEscape()
    {
        var coordinator = new JournalSaveCoordinator();

        var result = await coordinator.TrySaveAsync(
            () => Task.FromException(new InvalidOperationException("offline")));

        Assert.False(result.Succeeded);
        Assert.IsType<InvalidOperationException>(result.Error);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(15, 1)]
    [InlineData(16, 1.07)]
    [InlineData(20, 1.33)]
    [InlineData(29, 1.93)]
    [InlineData(30, 2)]
    [InlineData(60, 4)]
    public void Section13BillingUnitsPreservePartialUnitsAfterTheMinimum(
        int? minutes, decimal expected)
    {
        Assert.Equal(expected, BillingRules.CalculateSection13Units(minutes));
    }

    [Theory]
    [InlineData("1999999984", true)]
    [InlineData("1234567893", true)]
    [InlineData("1999999985", false)]
    [InlineData("1111111111", false)]
    [InlineData("", false)]
    public void BillingNpiValidationIncludesTheCheckDigit(string value, bool expected)
    {
        Assert.Equal(expected, BillingRules.IsValidNpi(value));
    }

    [Fact]
    public void BillingUsesTheMaineCalendarDateInsteadOfUtcAtMidnightBoundary()
    {
        var utc = new DateTimeOffset(2026, 8, 13, 2, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateTime(2026, 8, 12), BillingRules.MaineBusinessDate(utc));
    }

    [Fact]
    public void Professional837UsesFrozenSubscriberProviderAndFinancialValues()
    {
        var snapshot = new ProfessionalClaimSnapshot(
            ProfessionalClaimSnapshotCodec.CurrentVersion,
            1, 101, "Alex", "Example", new DateTime(1990, 2, 3), "U", "987654321",
            "10 Claim Street", "Portland", "ME", "04101",
            "Example Agency", "1999999984", "111111111", "1 Provider Way",
            "Portland", "ME", "04101", "SATITEST1", "Billing Desk", "2075550101",
            "MEDICAID MAINE", "MCDME");
        var period = new BillingPeriod
        {
            Id = 77,
            UserId = 12,
            Month = 8,
            Year = 2026,
            Status = BillingStatus.Submitted,
            Lines =
            [
                new ClaimLine
                {
                    Id = 88,
                    NoteId = 99,
                    DateOfService = new DateTime(2026, 8, 12),
                    ProcedureCode = "G9012",
                    ProcedureModifier = "HI",
                    Units = 1.33m,
                    ChargeAmount = 33.25m,
                    ClientMaineCareId = "987654321",
                    RenderingProviderNpi = "1999999984",
                    DiagnosisCode = "F89",
                    PlaceOfService = 11,
                    ClaimSnapshotJson = ProfessionalClaimSnapshotCodec.Serialize(snapshot)
                }
            ]
        };

        var edi = EdiGenerator.Generate(period, true,
            new DateTime(2026, 8, 13, 9, 10, 0), "123456789");

        Assert.Contains("NM1*IL*1*Example*Alex****MI*987654321~", edi);
        Assert.Contains("N3*10 Claim Street~", edi);
        Assert.Contains("N4*Portland*ME*04101~", edi);
        Assert.Contains("CLM*77-99*33.25***11::1", edi);
        Assert.Contains("SV1*HC:G9012:HI*33.25*UN*1.33*11", edi);
        Assert.Equal(106, edi.Split('~', StringSplitOptions.RemoveEmptyEntries)[0].Length + 1);
        var segments = edi.Split('~', StringSplitOptions.RemoveEmptyEntries);
        var se = segments.Single(segment => segment.TrimStart().StartsWith("SE*", StringComparison.Ordinal));
        var declared = int.Parse(se.Trim().Split('*')[1]);
        var stIndex = Array.FindIndex(segments, segment => segment.TrimStart().StartsWith("ST*", StringComparison.Ordinal));
        var seIndex = Array.FindIndex(segments, segment => segment.TrimStart().StartsWith("SE*", StringComparison.Ordinal));
        Assert.Equal(seIndex - stIndex + 1, declared);
    }

    [Fact]
    public void Professional837FailsClosedWithoutAnImmutableClaimSnapshot()
    {
        var period = new BillingPeriod
        {
            Status = BillingStatus.Submitted,
            Lines = [new ClaimLine { Id = 1, NoteId = 1 }]
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            EdiGenerator.Generate(period, true, DateTime.UtcNow, "123456789"));

        Assert.Contains("immutable billing snapshot", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseNotesMatchTheDemoAssemblyVersionAndDocumentProductionGaps()
    {
        var version = typeof(Sati.ViewModels.SettingsViewModel).Assembly
            .GetName().Version?.ToString(3);
        var apiVersion = typeof(Sati.Api.Infrastructure.SatiApiOptions).Assembly
            .GetName().Version?.ToString(3);

        Assert.Equal("1.2.9", version);
        Assert.Equal(version, apiVersion);
        Assert.Equal("Reliable review and incident recovery", ProductReleaseNotes.ReleaseName);
        Assert.NotEmpty(ProductReleaseNotes.Sections);
        Assert.Contains(ProductReleaseNotes.Sections, section =>
            section.Title == "Still planned before commercial production" &&
            section.Items.Any(item => item.Contains("legal-hold", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(ProductReleaseNotes.Sections, section =>
            section.Title == "Safety and reliability" &&
            section.Items.Any(item => item.Contains("calendar-day selection", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ApplicationIconContainsSmallAndLargeWindowsFrames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        using var stream = File.OpenRead(Path.Combine(directory!.FullName, "images", "sati.ico"));
        var decoder = new IconBitmapDecoder(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var sizes = decoder.Frames.Select(frame => frame.PixelWidth).ToHashSet();

        Assert.Contains(16, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(48, sizes);
        Assert.Contains(256, sizes);
    }

    [Fact]
    public void AsyncSaveOnCloseDefersTheSecondCloseUntilDispatcherIdle()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        foreach (var file in new[] { "ShellWindow.xaml.cs", "SettingsWindow.xaml.cs" })
        {
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "Views", file));
            Assert.Contains("Dispatcher.BeginInvoke", source);
            Assert.Contains("DispatcherPriority.ApplicationIdle", source);
        }
    }

    [Fact]
    public void ShellUsesNeutralSignInForPlatformAccountSwitching()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "Views", "ShellWindow.xaml.cs"));
        Assert.Contains("AccountSwitchPolicy.RequiresDirectSignIn", source);
        Assert.Contains("_loginWindowFactory", source);
    }

    [Fact]
    public void DemoResetScriptIsPinnedToTheSyntheticDatabaseAndVerifiesItsBackup()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(
            directory!.FullName,
            "scripts",
            "Reset-LocalDemoData.ps1"));

        Assert.Contains("$database = 'SatiDemo'", script);
        Assert.Contains("EnvironmentName -cne 'Demo'", script);
        Assert.Contains("RESTORE VERIFYONLY", script);
        Assert.Contains("Get-FileHash", script);
        Assert.Contains("WITH REPLACE, RECOVERY, CHECKSUM", script);
        Assert.Contains("Get-Process -Name 'Sati', 'Sati.Demo'", script);
        Assert.DoesNotContain("SatiProduction", script);
    }

    [Fact]
    public void InstallerAcceptanceChecksInstalledVersionPublicConfigAndLiveProcess()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var script = File.ReadAllText(Path.Combine(
            directory!.FullName,
            "scripts",
            "Test-DemoInstaller.ps1"));

        Assert.Contains("SATI_DEMO_INSTALLER_TEST", script);
        Assert.Contains("appsettings.Public.json", script);
        Assert.Contains("The private appsettings.json file was installed", script);
        Assert.Contains("FileVersion", script);
        Assert.Contains("Installed app exited during startup", script);
        Assert.Contains("CloseMainWindow()", script);
        Assert.Contains("WaitForExit($CloseTimeoutSeconds * 1000)", script);
        Assert.Contains("APP_WINDOW_LIFECYCLE_PASSED", script);
        Assert.Contains("GracefulClosePassed = $true", script);
        Assert.Contains("Stop-Process -Id $app.Id", script);
        Assert.Contains("ReparsePoint", script);
    }

    [Fact]
    public void DemoAcceptanceEvidenceBindsEveryFinalGateToExactArtifacts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var scripts = Path.Combine(directory!.FullName, "scripts");
        var installer = File.ReadAllText(Path.Combine(scripts, "Test-DemoInstaller.ps1"));
        var readiness = File.ReadAllText(Path.Combine(scripts, "Test-DemoReadiness.ps1"));
        var presenter = File.ReadAllText(Path.Combine(scripts, "New-DemoPresenterAttestation.ps1"));
        var final = File.ReadAllText(Path.Combine(scripts, "Test-CompanyDemoAcceptance.ps1"));
        var fallbackBuilder = File.ReadAllText(Path.Combine(scripts, "Build-DemoFallback.py"));

        Assert.Contains("Gate = 'CleanMachineLaunch'", installer);
        Assert.Contains("ExternalMachineConfirmed", installer);
        Assert.Contains("SourceTreeDetected", installer);
        Assert.Contains("Gate = 'AuthenticatedApiReadiness'", readiness);
        Assert.Contains("Authenticated = $false", readiness);
        Assert.Contains("Authenticated = $true", readiness);
        Assert.Contains("health/version", readiness);
        Assert.Contains("ReleaseVersion", readiness);
        Assert.Contains("passed live/ready health checks but does not expose /health/version", readiness);
        Assert.Contains("deploy the matching API release before collecting readiness evidence", readiness);
        Assert.DoesNotContain("SATI_DEMO_PASSWORD =", readiness);

        Assert.Contains("ClientApiParityConfirmed", presenter);
        Assert.Contains("WalkthroughRehearsed", presenter);
        Assert.Contains("SyntheticDataOnly", presenter);
        Assert.Contains("FallbackApproved", presenter);
        Assert.Contains("LimitationsPresented", presenter);

        Assert.Contains("api-readiness.json", final);
        Assert.Contains("clean-machine.json", final);
        Assert.Contains("presenter-attestation.json", final);
        Assert.Contains("InstallerSha256", final);
        Assert.Contains("FallbackSha256", final);
        Assert.Contains("MaxEvidenceAgeHours", final);
        Assert.Contains("api.ReleaseVersion -ceq $expectedVersion", final);
        Assert.Contains("COMPANY_DEMO_ACCEPTANCE_PASSED", final);
        Assert.Contains("ET.parse(ROOT / \"Sati.csproj\")", fallbackBuilder);
        Assert.Contains("invariant=1", fallbackBuilder);
        Assert.Contains("Synthetic records only", fallbackBuilder);
    }

    [Fact]
    public void DemoRunbookDoesNotClaimAcceptanceBeforeExternalEvidenceExists()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var runbook = File.ReadAllText(Path.Combine(directory!.FullName, "DEMO_RUNBOOK.md"));
        var evidenceGuide = File.ReadAllText(Path.Combine(directory.FullName, "DEMO_ACCEPTANCE.md"));

        Assert.Contains("[ ] The current Demo client and API changes are deployed together.", runbook);
        Assert.Contains("[ ] Health and authenticated readiness checks pass", runbook);
        Assert.Contains("[ ] The packaged artifact launches on a clean Windows machine", runbook);
        Assert.Contains("[ ] The presenter has reviewed and approved", runbook);
        Assert.Contains("never edit an evidence file to make it pass", evidenceGuide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DisplayOnlyRunBindingsAreExplicitlyOneWay()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var viewRoot = Path.Combine(directory!.FullName, "Views");
        var bindingPattern = new Regex(
            "<Run\\s+[^>]*Text=\"\\{Binding(?<binding>[^}]*)\\}\"[^>]*/>",
            RegexOptions.Compiled | RegexOptions.Singleline);
        var violations = Directory.GetFiles(viewRoot, "*.xaml", SearchOption.AllDirectories)
            .SelectMany(path => bindingPattern.Matches(File.ReadAllText(path)).Cast<Match>()
                .Where(match => !match.Groups["binding"].Value.Contains("Mode=OneWay", StringComparison.Ordinal))
                .Select(match => $"{Path.GetRelativePath(directory.FullName, path)}: {match.Value}"))
            .ToList();

        Assert.Empty(violations);
        Assert.False(typeof(Note).GetProperty(nameof(Note.Units))!.CanWrite);
        var calendar = File.ReadAllText(Path.Combine(viewRoot, "CalendarView.xaml"));
        Assert.Contains("{Binding Units, Mode=OneWay}", calendar);
    }

    [Fact]
    public void IconButtonsAndCheckBoxesExposeAccessibleNames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sati.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var viewRoot = Path.Combine(directory!.FullName, "Views");
        foreach (var path in Directory.GetFiles(viewRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = System.Xml.Linq.XDocument.Load(path);
            foreach (var button in document.Descendants().Where(element => element.Name.LocalName == "Button"))
            {
                var content = button.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Content")?.Value;
                var hasAccessibleName = button.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.Name" &&
                    !string.IsNullOrWhiteSpace(attribute.Value));
                var usesIconFont = button.Descendants().Any(element =>
                    element.Name.LocalName == "TextBlock" &&
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "FontFamily" &&
                        attribute.Value.Contains("Segoe MDL2 Assets", StringComparison.Ordinal)));
                var isTextSizeGlyph = button.Descendants().Any(element =>
                    element.Name.LocalName == "TextBlock" &&
                    element.Attributes().Any(attribute =>
                        attribute.Name.LocalName == "Text" && attribute.Value == "A"));
                var hasGlyphContent = !string.IsNullOrWhiteSpace(content) && content.Length <= 2;

                if (usesIconFont || isTextSizeGlyph || hasGlyphContent)
                    Assert.True(hasAccessibleName,
                        $"{Path.GetRelativePath(directory.FullName, path)} contains an unlabeled icon button: {button}");
            }

            foreach (var checkBox in document.Descendants().Where(element => element.Name.LocalName == "CheckBox"))
            {
                var hasContent = checkBox.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "Content" && !string.IsNullOrWhiteSpace(attribute.Value));
                var hasAccessibleName = checkBox.Attributes().Any(attribute =>
                    attribute.Name.LocalName == "AutomationProperties.Name" &&
                    !string.IsNullOrWhiteSpace(attribute.Value));

                Assert.True(hasContent || hasAccessibleName,
                    $"{Path.GetRelativePath(directory.FullName, path)} contains an unlabeled checkbox: {checkBox}");
            }
        }
    }

    [Fact]
    public void OverdueMatrixCellIncludesAVisibleTextStatus()
    {
        var today = new DateTime(2026, 8, 13);
        var person = Person.CreatePerson(
            1,
            "Accessible",
            "Example",
            "Synthetic test person.",
            new DateTime(1990, 1, 1),
            new DateTime(2026, 1, 1),
            WaiverType.Section21,
            new Settings());
        person.Forms.RemoveAll(form => form.Type == FormType.PCP);
        person.Forms.Add(new Form(FormType.PCP, today.AddDays(-2), false));

        var cell = new Sati.ViewModels.FormCellViewModel(person, FormType.PCP, today);

        Assert.Equal(FormCellStatus.Overdue, cell.Status);
        Assert.StartsWith("OVERDUE", cell.CellText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread()
    {
        Exception? failure = null;
        string? currentType = null;
        var exercisedTypes = new List<string>();
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            App? application = null;
            IHost? host = null;
            FieldInfo? hostField = null;
            try
            {
                application = new App
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };
                application.InitializeComponent();
                host = Host.CreateDefaultBuilder()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<ISessionService, SessionService>();
                        services.AddSingleton<IComprehensiveAssessmentService, SmokeAssessmentService>();
                        services.AddSingleton<IPersonCenteredPlanSourceService, SmokePlanSourceService>();
                    })
                    .Build();
                hostField = typeof(App).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("App host field was not found.");
                hostField.SetValue(application, host);

                var viewTypes = typeof(App).Assembly.GetTypes()
                    .Where(type => type.IsPublic && !type.IsAbstract)
                    .Where(type => type.Namespace?.StartsWith("Sati.Views", StringComparison.Ordinal) == true)
                    .Where(type => typeof(System.Windows.FrameworkElement).IsAssignableFrom(type))
                    .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
                    .OrderBy(type => type.FullName, StringComparer.Ordinal)
                    .ToList();

                foreach (var type in viewTypes)
                {
                    currentType = type.FullName;
                    try
                    {
                        var element = Assert.IsAssignableFrom<System.Windows.FrameworkElement>(
                            Activator.CreateInstance(type));
                        if (element is System.Windows.Window window)
                        {
                            window.Show();
                            window.UpdateLayout();
                            window.Close();
                        }
                        else
                        {
                            element.Measure(new System.Windows.Size(1280, 720));
                            element.Arrange(new System.Windows.Rect(0, 0, 1280, 720));
                            element.UpdateLayout();
                            element.RaiseEvent(new System.Windows.RoutedEventArgs(
                                System.Windows.FrameworkElement.LoadedEvent));
                            element.RaiseEvent(new System.Windows.RoutedEventArgs(
                                System.Windows.FrameworkElement.UnloadedEvent));
                        }

                        exercisedTypes.Add(type.FullName!);
                        currentType = null;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Feature view '{type.FullName}' could not open, render, and close.", ex);
                    }
                }

                if (viewTypes.Count < 20)
                    throw new InvalidOperationException($"Only {viewTypes.Count} feature views were discovered.");
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (application is not null && hostField is not null)
                    hostField.SetValue(application, null);
                host?.Dispose();
                application?.Shutdown();
                completed.Set();
            }
        })
        {
            IsBackground = true,
            Name = "Sati feature-view smoke test"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(30)),
            $"Feature-view smoke test did not complete within 30 seconds. Current view: {currentType ?? "none"}; completed: {exercisedTypes.Count}.");
        thread.Join();
        Assert.Null(failure);
        Assert.True(exercisedTypes.Count >= 20,
            $"Expected at least 20 feature views, exercised {exercisedTypes.Count}.");
    }
    [Fact]
    public void RepeatedUiFailuresHaveAStableTechnicalFingerprint()
    {
        var first = new InvalidOperationException("Person-specific text is deliberately irrelevant",
            new NotSupportedException("first inner text"));
        var repeated = new InvalidOperationException("different message, same failure shape",
            new NotSupportedException("second inner text"));
        var different = new InvalidOperationException("different inner type",
            new ArgumentException("third inner text"));

        Assert.Equal(App.CreateExceptionFingerprint(first), App.CreateExceptionFingerprint(repeated));
        Assert.NotEqual(App.CreateExceptionFingerprint(first), App.CreateExceptionFingerprint(different));
        Assert.DoesNotContain("Person-specific", App.CreateExceptionFingerprint(first));
    }

    [Fact]
    public void UnexpectedErrorLogOmitsExceptionMessagesAndReturnsAReference()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"sati-error-log-{Guid.NewGuid():N}");
        const string sensitiveMessage = "Person Jane Example has private narrative text";
        try
        {
            var reference = AppErrorLog.Record(
                new InvalidOperationException(sensitiveMessage),
                "test/unsafe area",
                directory);
            var log = File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.jsonl")));

            Assert.Equal(12, reference.Length);
            Assert.Contains(reference, log);
            Assert.Contains("System.InvalidOperationException", log);
            Assert.Contains("testunsafearea", log);
            Assert.Contains("\"innerTarget\"", log);
            Assert.Contains("\"xamlLineNumber\"", log);
            Assert.DoesNotContain(sensitiveMessage, log);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
    [Fact]
    public void ApiPasswordHasherProducesVerifiableSaltedCredentials()
    {
        var service = new PasswordVerifier();
        var first = service.Hash("correct horse battery staple");
        var second = service.Hash("correct horse battery staple");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
        Assert.True(service.Verify("correct horse battery staple", first.Hash, first.Salt));
        Assert.False(service.Verify("wrong password", first.Hash, first.Salt));
    }

    [Fact]
    public void DayAfterThanksgivingIsExcludedWhenConfigured()
    {
        var settings = new Settings { ExcludeDayAfterThanksgiving = true };
        Assert.True(WorkdayHelper.IsAlwaysExcludedWorkday(new DateTime(2026, 11, 27), settings));
    }

    [Fact]
    public void PersonCreationGeneratesOneFormPerType()
    {
        var settings = new Settings();
        var person = Person.CreatePerson(
            1, "Ada", "Lovelace", string.Empty,
            new DateTime(1990, 1, 1), new DateTime(2026, 1, 1),
            WaiverType.Section21, settings);

        Assert.Equal(Enum.GetValues<FormType>().Length, person.Forms.Count);
    }

    [Fact]
    public void IncentiveCalculationAppliesThresholdAndPerUnitRate()
    {
        var incentive = new Incentive
        {
            BaseIncentive = 100m,
            PerUnitIncentive = 2m,
            UnitsPerDay = 10,
            DaysScheduled = 10
        };

        Assert.Equal(0m, incentive.Calculate(99m));
        Assert.Equal(100m, incentive.Calculate(100m));
        Assert.Equal(110m, incentive.Calculate(105m));
    }

    [Fact]
    public void IncidentHealthAlertsHaveExplicitEscalationThresholds()
    {
        var observedAt = new DateTime(2026, 8, 13, 12, 0, 0, DateTimeKind.Utc);
        var unknown = IncidentHealthScoring.Calculate([], observedAt, 30);
        var watch = IncidentHealthScoring.Calculate([
            Incident("Warning", "Open", 1, observedAt)
        ], observedAt, 30);
        var actionRequired = IncidentHealthScoring.Calculate([
            Incident("Warning", "Open", 1, observedAt) with { Id = 2, Operation = "notes.open" },
            Incident("Warning", "Open", 1, observedAt) with { Id = 3, Operation = "billing.open" },
            Incident("Warning", "Open", 1, observedAt) with { Id = 4, Operation = "forms.open" }
        ], observedAt, 30);
        var urgent = IncidentHealthScoring.Calculate([
            Incident("Critical", "Open", 1, observedAt)
        ], observedAt, 30);

        Assert.Equal("Unknown", unknown.AlertLevel);
        Assert.Equal("Watch", watch.AlertLevel);
        Assert.Equal("Action required", actionRequired.AlertLevel);
        Assert.Equal("Urgent", urgent.AlertLevel);

        static IncidentGroupDto Incident(string severity, string status, int count, DateTime now) =>
            new(1, 1, IncidentScopes.Agency, "Desktop", severity, "calendar.open", "1.2.2", "1.2.2",
                "ABCDEF0123456789", status, count, now, now, "REF_TEST01", "Admin");
    }

    [Fact]
    public void EfModelMatchesLatestMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiModelValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void ClaimLineUniquenessMigrationReplacesTheExistingIndex()
    {
        var migration = new Migrations.RequireOneClaimLinePerNote();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(Migrations.RequireOneClaimLinePerNote).GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        up.Invoke(migration, [builder]);

        var drop = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.DropIndexOperation>());
        var create = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>());
        Assert.Equal("IX_ClaimLines_NoteId", drop.Name);
        Assert.Equal("ClaimLines", drop.Table);
        Assert.Equal("IX_ClaimLines_NoteId", create.Name);
        Assert.Equal(["NoteId"], create.Columns);
        Assert.True(create.IsUnique);
    }

    [Fact]
    public void IncidentMigrationIsAdditiveAndRegistered()
    {
        var migration = new Migrations.AddIncidentHealthPipeline();
        var builder = new Microsoft.EntityFrameworkCore.Migrations.MigrationBuilder(
            "Microsoft.EntityFrameworkCore.SqlServer");
        var up = typeof(Migrations.AddIncidentHealthPipeline).GetMethod(
            "Up",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        up.Invoke(migration, [builder]);

        var create = Assert.Single(builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>());
        Assert.Equal("IncidentGroups", create.Name);
        Assert.DoesNotContain(builder.Operations, operation =>
            operation is Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation);
        Assert.Equal(2, builder.Operations.OfType<
            Microsoft.EntityFrameworkCore.Migrations.Operations.CreateIndexOperation>().Count());

        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiIncidentMigrationValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var migrations = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();
        Assert.Contains("20260813162000_AddIncidentHealthPipeline", migrations.Migrations.Keys);
    }

    [Fact]
    public void TenantOwnershipRepairIsRegisteredAsAnEfMigration()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiMigrationValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.Contains("20260812213000_ReconcileTenantOwnership", migrations.Migrations.Keys);
    }

    [Fact]
    public void NoteRevisionIsAConcurrencyTokenAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiNoteRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var noteRevision = context.Model.FindEntityType(typeof(Note))!
            .FindProperty(nameof(Note.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(noteRevision);
        Assert.True(noteRevision!.IsConcurrencyToken);
        Assert.Contains("20260812223000_AddNoteRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void AtRequestRevisionProtectsTheWholeAggregateAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiAtRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var revision = context.Model.FindEntityType(typeof(ATRequest))!
            .FindProperty(nameof(ATRequest.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
        Assert.Contains("20260812230000_AddAtRequestRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void SettingsRevisionIsAConcurrencyTokenAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiSettingsRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var revision = context.Model.FindEntityType(typeof(Settings))!
            .FindProperty(nameof(Settings.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
        Assert.Contains("20260812233000_AddSettingsRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void ScratchpadRevisionIsAConcurrencyTokenAndItsMigrationIsRegistered()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiScratchpadRevisionValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var revision = context.Model.FindEntityType(typeof(Scratchpad))!
            .FindProperty(nameof(Scratchpad.Revision));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(revision);
        Assert.True(revision!.IsConcurrencyToken);
        Assert.Contains("20260812234500_AddScratchpadRevision", migrations.Migrations.Keys);
    }

    [Fact]
    public void BillingSubmissionAndEdiGenerationHaveDatabaseRetryGuards()
    {
        var options = new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SatiEdiIdempotencyValidation;Trusted_Connection=True;Encrypt=False;")
            .Options;
        using var context = new SatiContext(options);
        var billingStatus = context.Model.FindEntityType(typeof(BillingPeriod))!
            .FindProperty(nameof(BillingPeriod.Status));
        var generation = context.Model.FindEntityType(typeof(EdiGeneration))!;
        var claim = context.Model.FindEntityType(typeof(ClaimLine))!;
        var person = context.Model.FindEntityType(typeof(Person))!;
        var retryIndex = Assert.Single(generation.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(EdiGeneration.AgencyId),
                nameof(EdiGeneration.ActorUserId),
                nameof(EdiGeneration.IdempotencyKey)]));
        var migrations = context.GetService<
            Microsoft.EntityFrameworkCore.Migrations.IMigrationsAssembly>();

        Assert.NotNull(billingStatus);
        Assert.True(billingStatus!.IsConcurrencyToken);
        Assert.True(retryIndex.IsUnique);
        Assert.Contains("20260812235500_AddEdiIdempotency", migrations.Migrations.Keys);
        Assert.Contains("20260813110000_AddBillingPipelineConfiguration", migrations.Migrations.Keys);
        Assert.NotNull(claim.FindProperty(nameof(ClaimLine.ClaimSnapshotJson)));
        Assert.NotNull(claim.FindProperty(nameof(ClaimLine.ChargeAmount)));
        Assert.NotNull(person.FindProperty(nameof(Person.BillingStreet)));
    }

    [Fact]
    public async Task DesktopKeepsTheSameEdiRetryKeyUntilGenerationSucceeds()
    {
        var edi = new RetryRecordingEdiService();
        var viewModel = new BillingSubmissionsViewModel(
            new StubBillingService(),
            edi,
            new SessionService())
        {
            SelectedPeriod = new BillingPeriod
            {
                Id = 99,
                Status = BillingStatus.Submitted,
                Lines = [new ClaimLine { Id = 1 }]
            }
        };

        await viewModel.GenerateEdiCommand.ExecuteAsync(null);
        await viewModel.GenerateEdiCommand.ExecuteAsync(null);
        await viewModel.GenerateEdiCommand.ExecuteAsync(null);

        Assert.Equal(3, edi.Keys.Count);
        Assert.Equal(edi.Keys[0], edi.Keys[1]);
        Assert.NotEqual(edi.Keys[1], edi.Keys[2]);
        Assert.All(edi.Keys, key => Assert.True(Guid.TryParse(key, out _)));
    }

    [Fact]
    public async Task DesktopAdminAuditExporterCreatesAReadablePdf()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        var person = Person.CreatePerson(
            12,
            "Lifecycle",
            "Example",
            "Initial biography.",
            new DateTime(1985, 5, 6),
            new DateTime(2025, 5, 6),
            WaiverType.Section21,
            new Settings());
        person.Revision = 2;
        var requester = User.Create(
            11,
            "admin-one",
            "Admin One",
            "hash",
            "salt",
            UserRole.Admin,
            null,
            1);
        var versions = new List<PersonVersionDto>
        {
            new(
                1, 0, 1, "TrackingBaseline", 0, "Sati tracking baseline",
                new DateTime(2026, 8, 12, 14, 0, 0, DateTimeKind.Utc),
                "desktop-baseline",
                [new("firstName", "First name", null, "Lifecycle")]),
            new(
                2, 0, 2, "Updated", 12, "Case Manager One",
                new DateTime(2026, 8, 12, 15, 0, 0, DateTimeKind.Utc),
                "desktop-update",
                [new("firstName", "First name", "Lifecycle", "Updated")])
        };

        var pdf = new PersonAuditPdfExporter().Generate(
            person,
            versions,
            new Agency { Id = 1, Name = "Agency One" },
            requester,
            new DateTime(2026, 8, 12, 16, 0, 0, DateTimeKind.Utc));

        Assert.True(pdf.Length > 2_000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var qaOutput = Environment.GetEnvironmentVariable("SATI_LOCAL_ADMIN_PDF_QA_OUTPUT");
        if (!string.IsNullOrWhiteSpace(qaOutput))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(qaOutput)!);
            await File.WriteAllBytesAsync(qaOutput, pdf);
        }
    }

    private sealed class SmokeAssessmentService : IComprehensiveAssessmentService
    {
        public Task<Models.Assessments.ComprehensiveAssessment> GetOrCreateDraftAsync(
            int personId,
            int authorUserId) => Task.FromResult(new Models.Assessments.ComprehensiveAssessment
        {
            PersonId = personId,
            AuthorUserId = authorUserId
        });

        public Task SaveDocumentAsync(
            Models.Assessments.ComprehensiveAssessment assessment,
            Models.Assessments.AssessmentDocument document) => Task.CompletedTask;

        public Task SubmitForReviewAsync(
            Models.Assessments.ComprehensiveAssessment assessment) => Task.CompletedTask;
    }

    private sealed class SmokePlanSourceService : IPersonCenteredPlanSourceService
    {
        public Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId) =>
            Task.FromResult<PersonCenteredPlanSource?>(null);
    }
    private sealed class RetryRecordingEdiService : IEdiService
    {
        public List<string> Keys { get; } = [];

        public Task<string> GenerateAndSaveAsync(int billingPeriodId, bool isTest, string idempotencyKey)
        {
            Keys.Add(idempotencyKey);
            if (Keys.Count == 1)
                throw new HttpRequestException("Simulated ambiguous network failure.");
            return Task.FromResult(@"C:\Sati\retry-safe-837p.txt");
        }
    }

    private sealed class StubBillingService : IBillingService
    {
        public Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year) =>
            throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId) =>
            throw new NotSupportedException();
        public Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync() =>
            throw new NotSupportedException();
        public Task<ClaimLine> CreateClaimLineAsync(int noteId, bool isComplianceException = false,
            string? complianceExceptionReason = null) => throw new NotSupportedException();
        public Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId) =>
            throw new NotSupportedException();
        public Task SubmitBillingPeriodAsync(int billingPeriodId) => throw new NotSupportedException();
        public Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync() => throw new NotSupportedException();
        public BillingValidationResult ValidateNoteForBilling(Note note) => throw new NotSupportedException();
        public Task<BillingConfiguration> GetBillingConfigurationAsync() => throw new NotSupportedException();
        public Task SaveBillingConfigurationAsync(BillingConfiguration configuration) => throw new NotSupportedException();
    }
}
