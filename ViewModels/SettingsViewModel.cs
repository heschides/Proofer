using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;

namespace Sati.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IProviderService _providerService;
        private readonly FormDueDateBackfill? _backfill;
        private readonly FormBulkCompletion? _bulkCompletion;
        private readonly ThemeService _themeService;
        private readonly ISessionService _sessionService;
        private readonly DatabaseActivityPreview _databaseActivityPreview;
        private readonly TextShortcutService _textShortcutService;
        private readonly DailyAgendaPreferenceService _dailyAgendaPreferences;
        private readonly EasyEyesPreferenceService _easyEyesPreferences;
        private readonly IdleLockPreferenceService _idlePreferences;
        private Settings? _settings;
        private bool _loadingDailyAgendaPreference;
        private bool _savedShowDailyAgendaAtSignIn = true;
        private bool _loadingEasyEyesPreference;
        private bool _savedEasyEyesMode;
        private bool _loadingIdlePreference;
        private int _savedIdleMinutes = IdleLockPreferenceService.DefaultMinutes;

        public SettingsViewModel(
            ISettingsService settingsService,
            IProviderService providerService,
            ThemeService themeService,
            ISessionService sessionService,
            DatabaseActivityViewModel databaseActivity,
            DatabaseActivityPreview databaseActivityPreview,
            TextShortcutService textShortcutService,
            DailyAgendaPreferenceService dailyAgendaPreferences,
            EasyEyesPreferenceService easyEyesPreferences,
            IdleLockPreferenceService idlePreferences,
            FormDueDateBackfill? backfill = null,
            FormBulkCompletion? bulkCompletion = null)
        {
            _settingsService = settingsService;
            _providerService = providerService;
            _backfill = backfill;
            _bulkCompletion = bulkCompletion;
            _themeService = themeService;
            _sessionService = sessionService;
            DatabaseActivity = databaseActivity;
            _databaseActivityPreview = databaseActivityPreview;
            _textShortcutService = textShortcutService;
            _dailyAgendaPreferences = dailyAgendaPreferences;
            _easyEyesPreferences = easyEyesPreferences;
            _idlePreferences = idlePreferences;
            selectedTheme = _themeService.CurrentTheme;
            TextShortcuts = new ObservableCollection<TextShortcutEditorViewModel>(
                Enumerable.Range(1, 9)
                    .Append(0)
                    .Select(digit => new TextShortcutEditorViewModel(digit)));
            _ = LoadTextShortcutsAsync();
            _ = LoadDailyAgendaPreferenceAsync();
            _ = LoadEasyEyesPreferenceAsync();
            _ = LoadIdlePreferenceAsync();
            if (CanManageAgencySettings)
                _ = LoadAsync();
        }

        public DatabaseActivityViewModel DatabaseActivity { get; }
        public IReadOnlyList<ThemeOption> ThemeOptions => _themeService.Themes;
        public bool CanManageAgencySettings =>
            SettingsAccessPolicy.CanManageAgencySettings(
                _sessionService.CurrentUser?.Permissions ?? Sati.Contracts.V1.UserPermissions.None);
        public string ReleaseVersion => $"Version {typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3)}";
        public string ReleaseName => ProductReleaseNotes.ReleaseName;
        public string ReleaseDate => ProductReleaseNotes.ReleaseDate;
        public IReadOnlyList<ReleaseNoteSection> ReleaseNoteSections => ProductReleaseNotes.Sections;
        public ObservableCollection<TextShortcutEditorViewModel> TextShortcuts { get; }

        [ObservableProperty]
        private ThemeOption? selectedTheme;

        [ObservableProperty]
        private string saveStatus = string.Empty;

        [ObservableProperty]
        private string textShortcutStatus = string.Empty;

        [ObservableProperty]
        private bool showDailyAgendaAtSignIn = true;

        [ObservableProperty]
        private string dailyAgendaPreferenceStatus = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EasyEyesScale))]
        private bool easyEyesMode;

        [ObservableProperty]
        private string easyEyesPreferenceStatus = string.Empty;

        public double EasyEyesScale => EasyEyesMode ? 1.3 : 1.0;

        // Minutes of no input before Sati covers the screen. "Never" is offered
        // because a case manager presenting from this machine should be able to
        // switch it off without editing a file.
        public IReadOnlyList<IdleTimeoutOption> IdleTimeoutChoices { get; } =
        [
            new(IdleLockPreferenceService.DisabledMinutes, "Never"),
            new(1, "After 1 minute"),
            new(2, "After 2 minutes"),
            new(5, "After 5 minutes"),
            new(10, "After 10 minutes"),
            new(15, "After 15 minutes"),
            new(20, "After 20 minutes"),
            new(30, "After 30 minutes"),
            new(45, "After 45 minutes"),
            new(60, "After 1 hour")
        ];

        [ObservableProperty]
        private IdleTimeoutOption? selectedIdleTimeout;

        [ObservableProperty]
        private string idlePreferenceStatus = string.Empty;

        [ObservableProperty]
        private string loadingIndicatorPreviewStatus =
            "Ready. The preview uses no database or client information.";

        [RelayCommand]
        private async Task PreviewLoadingIndicatorAsync()
        {
            LoadingIndicatorPreviewStatus =
                "Preview running for 12 seconds. The patience window will appear after eight seconds.";

            try
            {
                var completed = await _databaseActivityPreview.TryRunAsync();
                LoadingIndicatorPreviewStatus = completed
                    ? "Preview complete. No database or client information was accessed."
                    : "A loading-indicator preview is already running.";
            }
            catch (Exception ex)
            {
                LoadingIndicatorPreviewStatus = $"The preview could not finish. {ex.Message}";
            }
        }

        partial void OnSelectedThemeChanged(ThemeOption? value)
        {
            if (value is not null)
                _themeService.ApplyTheme(value.ResourceName);
        }

        partial void OnShowDailyAgendaAtSignInChanged(bool value)
        {
            if (!_loadingDailyAgendaPreference)
                _ = SaveDailyAgendaPreferenceAsync(value);
        }

        partial void OnEasyEyesModeChanged(bool value)
        {
            if (!_loadingEasyEyesPreference)
                _ = SaveEasyEyesPreferenceAsync(value);
        }

        private async Task LoadEasyEyesPreferenceAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                EasyEyesPreferenceStatus = "Sign in to change the Easy Eyes preference.";
                return;
            }

            var enabled = await _easyEyesPreferences.LoadForUserAsync(userId.Value);
            _loadingEasyEyesPreference = true;
            try
            {
                EasyEyesMode = enabled;
                _savedEasyEyesMode = enabled;
            }
            finally
            {
                _loadingEasyEyesPreference = false;
            }

            EasyEyesPreferenceStatus = _easyEyesPreferences.LastLoadWarning ??
                "This personal setting is saved immediately for this Sati account on this computer.";
        }

        private async Task SaveEasyEyesPreferenceAsync(bool value)
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                EasyEyesPreferenceStatus = "Sign in before changing the Easy Eyes preference.";
                return;
            }

            EasyEyesPreferenceStatus = "Saving Easy Eyes preference...";
            try
            {
                await _easyEyesPreferences.SetEnabledAsync(userId.Value, value);
                _savedEasyEyesMode = value;
                EasyEyesPreferenceStatus = "Easy Eyes preference saved.";
            }
            catch (EasyEyesPreferenceSaveException exception)
            {
                _loadingEasyEyesPreference = true;
                try
                {
                    EasyEyesMode = _savedEasyEyesMode;
                }
                finally
                {
                    _loadingEasyEyesPreference = false;
                }

                EasyEyesPreferenceStatus = $"Preference was not changed. {exception.Message}";
            }
        }

        partial void OnSelectedIdleTimeoutChanged(IdleTimeoutOption? value)
        {
            if (!_loadingIdlePreference && value is not null)
                _ = SaveIdlePreferenceAsync(value.Minutes);
        }

        private async Task LoadIdlePreferenceAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                IdlePreferenceStatus = "Sign in to change the inactivity screen.";
                return;
            }

            var minutes = await _idlePreferences.LoadForUserAsync(userId.Value);
            _loadingIdlePreference = true;
            try
            {
                SelectedIdleTimeout = ChoiceFor(minutes);
                _savedIdleMinutes = minutes;
            }
            finally
            {
                _loadingIdlePreference = false;
            }

            IdlePreferenceStatus = _idlePreferences.LastLoadWarning ??
                "This personal setting is saved immediately for this Sati account on this computer.";
        }

        private async Task SaveIdlePreferenceAsync(int minutes)
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                IdlePreferenceStatus = "Sign in before changing the inactivity screen.";
                return;
            }

            IdlePreferenceStatus = "Saving inactivity screen preference...";
            try
            {
                await _idlePreferences.SetTimeoutAsync(userId.Value, minutes);
                _savedIdleMinutes = minutes;
                IdlePreferenceStatus = "Inactivity screen preference saved.";
            }
            catch (IdleLockPreferenceSaveException exception)
            {
                _loadingIdlePreference = true;
                try
                {
                    SelectedIdleTimeout = ChoiceFor(_savedIdleMinutes);
                }
                finally
                {
                    _loadingIdlePreference = false;
                }

                IdlePreferenceStatus = $"Preference was not changed. {exception.Message}";
            }
        }

        // A stored value that is not one of the offered choices still has to
        // show as something, so it falls back to the nearest supported option.
        private IdleTimeoutOption ChoiceFor(int minutes) =>
            IdleTimeoutChoices.FirstOrDefault(choice => choice.Minutes == minutes)
            ?? IdleTimeoutChoices.MinBy(choice => Math.Abs(choice.Minutes - minutes))!;

        private async Task LoadDailyAgendaPreferenceAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                DailyAgendaPreferenceStatus = "Sign in to change the daily agenda preference.";
                return;
            }

            var preference = await _dailyAgendaPreferences.LoadForUserAsync(userId.Value);
            _loadingDailyAgendaPreference = true;
            try
            {
                ShowDailyAgendaAtSignIn = preference.ShowAtSignIn;
                _savedShowDailyAgendaAtSignIn = preference.ShowAtSignIn;
            }
            finally
            {
                _loadingDailyAgendaPreference = false;
            }

            DailyAgendaPreferenceStatus = _dailyAgendaPreferences.LastLoadWarning ??
                "This personal setting is saved immediately for this Sati account on this computer.";
        }

        private async Task SaveDailyAgendaPreferenceAsync(bool value)
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                DailyAgendaPreferenceStatus = "Sign in before changing the daily agenda preference.";
                return;
            }

            DailyAgendaPreferenceStatus = "Saving daily agenda preference...";
            try
            {
                await _dailyAgendaPreferences.SetShowAtSignInAsync(userId.Value, value);
                _savedShowDailyAgendaAtSignIn = value;
                DailyAgendaPreferenceStatus = "Daily agenda preference saved.";
            }
            catch (DailyAgendaPreferenceSaveException exception)
            {
                _loadingDailyAgendaPreference = true;
                try
                {
                    ShowDailyAgendaAtSignIn = _savedShowDailyAgendaAtSignIn;
                }
                finally
                {
                    _loadingDailyAgendaPreference = false;
                }

                DailyAgendaPreferenceStatus = $"Preference was not changed. {exception.Message}";
            }
        }

        private async Task LoadTextShortcutsAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                TextShortcutStatus = "Sign in to load personal text shortcuts.";
                return;
            }

            await _textShortcutService.LoadForUserAsync(userId.Value);
            var texts = _textShortcutService.GetActiveTexts();
            for (var index = 0; index < TextShortcuts.Count; index++)
                TextShortcuts[index].Text = texts[index];

            TextShortcutStatus = _textShortcutService.LastLoadWarning ??
                "Shortcuts are ready for this Sati account.";
        }

        [RelayCommand]
        private async Task SaveTextShortcutsAsync()
        {
            var userId = _sessionService.CurrentUser?.Id;
            if (userId is null)
            {
                TextShortcutStatus = "Sign in before saving personal text shortcuts.";
                return;
            }

            TextShortcutStatus = "Saving personal shortcuts...";
            try
            {
                await _textShortcutService.SaveForUserAsync(
                    userId.Value,
                    TextShortcuts.Select(shortcut => shortcut.Text).ToList());
                TextShortcutStatus = "Personal text shortcuts saved.";
            }
            catch (Exception exception) when (exception is TextShortcutSaveException or ArgumentException)
            {
                TextShortcutStatus = $"Shortcuts were not saved. {exception.Message}";
            }
        }

        // Sales tax as a rate (0.055 = 5.5%), adjustable. Frozen onto AT requests
        // at save in a later slice; here it's just the editable default.
        [ObservableProperty] private decimal salesTaxRate;

        // Agency-wide safety switch. An administrator must explicitly enable
        // replacing accepted demographic fields on an existing consumer profile.
        [ObservableProperty] private bool allowCredibleProfileUpdates;

        // Agency-configurable display title for the staff member assisting the
        // VR counselor. Consumer rows store the assigned name, not this label.
        [ObservableProperty] private string vrAssistantTitle =
            VocationalRehabilitationProfile.DefaultAssistantTitle;

        // The passthrough-provider list for the default picker, and the chosen
        // default's Id. The combo binds SelectedValue → this, SelectedValuePath=Id,
        // so it round-trips the int? FK without matching object identity. Nullable:
        // null = no default set.
        public ObservableCollection<Provider> PassthroughProviders { get; } = [];
        [ObservableProperty] private int? defaultPassthroughProviderId;

        // ====================================================================
        // TEMPORARY MAINTENANCE — DUE-DATE BACKFILL
        // Remove this whole region (and the three controls in SettingsWindow.xaml
        // marked with the same banner) once the backfill has been run.
        //
        // The other half of that condition is already met: the
        // EnableEnsureCycleFormsOnLoad guard is gone, since the unique index on
        // dbo.Forms now decides the race it was suppressing.
        // ====================================================================

        // What the last dry run reported it would change. You type this exact
        // number into BackfillConfirmCount to authorize the commit; the service
        // refuses any other value. Starts -1 so "no dry run yet" can't coincide
        // with a real count.
        [ObservableProperty] private int backfillDryRunChangeCount = -1;

        // The number you type to confirm. Bound to the textbox next to Commit.
        [ObservableProperty] private string backfillConfirmCount = string.Empty;

        // Human-readable outcome of the last action, shown beneath the buttons.
        // The detailed report is always the .txt file on the Desktop; this is
        // just the at-a-glance summary plus that file's path.
        [ObservableProperty] private string backfillStatus = string.Empty;

        [RelayCommand]
        private async Task BackfillDryRunAsync()
        {
            if (_backfill is null)
            {
                BackfillStatus = "This LocalDB maintenance tool is not available in the Azure Demo.";
                return;
            }

            try
            {
                var report = await _backfill.DryRunAsync();
                BackfillDryRunChangeCount = report.FormsChanged;
                BackfillStatus =
                    $"Dry run complete. {report.FormsChanged} forms would change, "
                    + $"{report.FormsUnchanged} unchanged, {report.FormsAnomalous} anomalies, "
                    + $"{report.DuplicateCells} duplicate cells. "
                    + $"To commit, type {report.FormsChanged} in the box and press Commit.\n"
                    + $"Full report: {report.ReportFilePath}";
            }
            catch (Exception ex)
            {
                BackfillStatus = $"Dry run failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task BackfillCommitAsync()
        {
            if (_backfill is null)
            {
                BackfillStatus = "This LocalDB maintenance tool is not available in the Azure Demo.";
                return;
            }

            if (!int.TryParse(BackfillConfirmCount, out var typed))
            {
                BackfillStatus = "Enter the exact change count from the dry run to commit.";
                return;
            }

            try
            {
                var report = await _backfill.CommitAsync(typed);
                BackfillStatus =
                    $"COMMITTED. {report.FormsChanged} forms updated. "
                    + $"Report: {report.ReportFilePath}";
                BackfillConfirmCount = string.Empty;
                BackfillDryRunChangeCount = -1;
            }
            catch (Exception ex)
            {
                // The service throws if no dry run ran this session or the number
                // doesn't match. Surface that message verbatim — it's written to
                // be read by exactly this caller.
                BackfillStatus = $"Commit refused: {ex.Message}";
            }
        }

        // ====================================================================
        // TEMPORARY MAINTENANCE — BULK FORM COMPLETION
        // One-time: mark every form due on/before the cutoff with no completion
        // date as complete, using one explicitly entered completion date. Remove this region and
        // its controls in SettingsWindow.xaml with the rest of the scaffolding.
        // ====================================================================

        // Cutoff, editable in the maintenance UI. Defaults to the agreed value;
        // bound as text so the box starts populated but you can change it.
        [ObservableProperty] private string bulkCompleteCutoff = "2026-08-01";
        [ObservableProperty] private string bulkCompleteCompletionDate = string.Empty;

        [ObservableProperty] private string bulkCompleteConfirmCount = string.Empty;
        [ObservableProperty] private string bulkCompleteStatus = string.Empty;

        [RelayCommand]
        private async Task BulkCompleteDryRunAsync()
        {
            if (_bulkCompletion is null)
            {
                BulkCompleteStatus = "This LocalDB maintenance tool is not available in the Azure Demo.";
                return;
            }

            if (!DateTime.TryParse(BulkCompleteCutoff, out var cutoff))
            {
                BulkCompleteStatus = "Cutoff date is not a valid date (use yyyy-MM-dd).";
                return;
            }

            if (!DateTime.TryParse(BulkCompleteCompletionDate, out var completionDate))
            {
                BulkCompleteStatus = "Completion date is required (use yyyy-MM-dd).";
                return;
            }

            try
            {
                var report = await _bulkCompletion.DryRunAsync(cutoff, completionDate);
                BulkCompleteStatus =
                    $"Dry run complete. {report.FormsMarked} forms would be marked complete "
                    + $"(cutoff {report.Cutoff:yyyy-MM-dd}, inclusive); "
                    + $"{report.AlreadyCompleted} already have completion dates and are untouched; "
                    + $"{report.LegacyCompliantMissingDate} legacy compliant rows need dates. "
                    + $"To commit, type {report.FormsMarked} in the box and press Commit.\n"
                    + $"Full report: {report.ReportFilePath}";
            }
            catch (Exception ex)
            {
                BulkCompleteStatus = $"Dry run failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task BulkCompleteCommitAsync()
        {
            if (_bulkCompletion is null)
            {
                BulkCompleteStatus = "This LocalDB maintenance tool is not available in the Azure Demo.";
                return;
            }

            if (!DateTime.TryParse(BulkCompleteCutoff, out var cutoff))
            {
                BulkCompleteStatus = "Cutoff date is not a valid date (use yyyy-MM-dd).";
                return;
            }

            if (!DateTime.TryParse(BulkCompleteCompletionDate, out var completionDate))
            {
                BulkCompleteStatus = "Completion date is required (use yyyy-MM-dd).";
                return;
            }

            if (!int.TryParse(BulkCompleteConfirmCount, out var typed))
            {
                BulkCompleteStatus = "Enter the exact count from the dry run to commit.";
                return;
            }

            try
            {
                var report = await _bulkCompletion.CommitAsync(cutoff, completionDate, typed);
                BulkCompleteStatus =
                    $"COMMITTED. {report.FormsMarked} forms marked complete. "
                    + $"Report: {report.ReportFilePath}";
                BulkCompleteConfirmCount = string.Empty;
            }
            catch (Exception ex)
            {
                BulkCompleteStatus = $"Commit refused: {ex.Message}";
            }
        }

        [ObservableProperty] private int abandonedAfterDays;
        [ObservableProperty] private int productivityThreshold;
        [ObservableProperty] private decimal baseIncentive;
        [ObservableProperty] private decimal perUnitIncentive;
        [ObservableProperty] private bool complianceQuarterlyReviews;
        [ObservableProperty] private bool compliancePcp;
        [ObservableProperty] private bool complianceComprehensiveAssessment;
        [ObservableProperty] private bool complianceReclassification;
        [ObservableProperty] private bool complianceSafetyPlan;
        [ObservableProperty] private bool compliancePrivacyPractices;
        [ObservableProperty] private bool complianceAgencyRelease;
        [ObservableProperty] private bool complianceDhhsRelease;
        [ObservableProperty] private bool complianceMedicalRelease;
        [ObservableProperty] private string visitTemplate = string.Empty;
        [ObservableProperty] private string contactTemplate = string.Empty;
        [ObservableProperty] private string documentationTemplate = string.Empty;

        [ObservableProperty] private bool excludeMonday;
        [ObservableProperty] private bool excludeTuesday;
        [ObservableProperty] private bool excludeWednesday;
        [ObservableProperty] private bool excludeThursday;
        [ObservableProperty] private bool excludeFriday;

        [ObservableProperty] private bool excludeNewYearsDay;
        [ObservableProperty] private bool excludeMLKDay;
        [ObservableProperty] private bool excludePresidentsDay;
        [ObservableProperty] private bool excludeMemorialDay;
        [ObservableProperty] private bool excludeJuneteenth;
        [ObservableProperty] private bool excludeIndependenceDay;
        [ObservableProperty] private bool excludeLaborDay;
        [ObservableProperty] private bool excludeIndigenousPeoplesDay;
        [ObservableProperty] private bool excludeVeteransDay;
        [ObservableProperty] private bool excludeThanksgiving;
        [ObservableProperty] private bool excludeDayAfterThanksgiving;
        [ObservableProperty] private bool excludeChristmas;
        
        [ObservableProperty] private int reviewOpenDaysBefore;
        [ObservableProperty] private int reviewDaysAfterDue;

        [ObservableProperty] private int pcpOpenDaysBefore;
        [ObservableProperty] private int pcpDaysAfterDue;

        [ObservableProperty] private int compAssessmentOpenDaysBefore;
        [ObservableProperty] private int compAssessmentDaysAfterDue;

        [ObservableProperty] private int reclassificationOpenDaysBefore;
        [ObservableProperty] private int reclassificationDaysAfterDue;

        [ObservableProperty] private int safetyPlanOpenDaysBefore;
        [ObservableProperty] private int safetyPlanDaysAfterDue;

        [ObservableProperty] private int privacyPracticesOpenDaysBefore;
        [ObservableProperty] private int privacyPracticesDaysAfterDue;

        [ObservableProperty] private int releaseAgencyOpenDaysBefore;
        [ObservableProperty] private int releaseAgencyDaysAfterDue;

        [ObservableProperty] private int releaseDhhsOpenDaysBefore;
        [ObservableProperty] private int releaseDhhsDaysAfterDue;
        [ObservableProperty] private int releaseMedicalOpenDaysBefore;
        [ObservableProperty] private int releaseMedicalDaysAfterDue;

        // ---- Healthcare systems ----
        // Bound to a ListBox in the settings window. Held as our own collection,
        // edited in memory, and written back to Settings on save — the same lifecycle
        // as every other field here. Mutated in place (Clear/Add) so the ListBox stays
        // bound to one instance rather than re-binding on each change.
        public ObservableCollection<string> HealthcareSystems { get; } = new();

        [ObservableProperty] private string newHealthcareSystem = string.Empty;
        [ObservableProperty] private string? selectedHealthcareSystem;
        private async Task LoadAsync()
        {
            try
            {
                _settings = await _settingsService.LoadAsync();
            }
            catch (Exception ex)
            {
                // This method starts when the settings view model is constructed.
                // Because that startup task is intentionally not awaited by the
                // constructor, it must observe its own failures.
                SaveStatus = $"Settings could not be loaded. {ex.Message}";
                return;
            }

            SalesTaxRate = _settings.SalesTaxRate;
            AllowCredibleProfileUpdates = _settings.AllowCredibleProfileUpdates;
            VrAssistantTitle = VocationalRehabilitationProfile.NormalizeAssistantTitle(
                _settings.VrAssistantTitle);
            DefaultPassthroughProviderId = _settings.DefaultPassthroughProviderId;

            PassthroughProviders.Clear();
            try
            {
                foreach (var p in await _providerService.GetPassthroughProvidersAsync())
                    PassthroughProviders.Add(p);
            }
            catch (NotSupportedException)
            {
                // The provider directory is not part of the initial Azure Demo
                // surface. Settings can still load; no local/SQL fallback occurs.
            }

            AbandonedAfterDays = _settings.AbandonedAfterDays;
            ProductivityThreshold = _settings.ProductivityThreshold;
            BaseIncentive = _settings.BaseIncentive;
            PerUnitIncentive = _settings.PerUnitIncentive;
            var compliance = _settings.BillingComplianceRequirements;
            ComplianceQuarterlyReviews = compliance.HasFlag(BillingComplianceRequirements.QuarterlyReviews);
            CompliancePcp = compliance.HasFlag(BillingComplianceRequirements.Pcp);
            ComplianceComprehensiveAssessment = compliance.HasFlag(BillingComplianceRequirements.ComprehensiveAssessment);
            ComplianceReclassification = compliance.HasFlag(BillingComplianceRequirements.Reclassification);
            ComplianceSafetyPlan = compliance.HasFlag(BillingComplianceRequirements.SafetyPlan);
            CompliancePrivacyPractices = compliance.HasFlag(BillingComplianceRequirements.PrivacyPractices);
            ComplianceAgencyRelease = compliance.HasFlag(BillingComplianceRequirements.AgencyRelease);
            ComplianceDhhsRelease = compliance.HasFlag(BillingComplianceRequirements.DhhsRelease);
            ComplianceMedicalRelease = compliance.HasFlag(BillingComplianceRequirements.MedicalRelease);
            VisitTemplate = _settings.VisitTemplate;
            ContactTemplate = _settings.ContactTemplate;
            DocumentationTemplate = _settings.DocumentationTemplate;

            ExcludeMonday = _settings.ExcludeMonday;
            ExcludeTuesday = _settings.ExcludeTuesday;
            ExcludeWednesday = _settings.ExcludeWednesday;
            ExcludeThursday = _settings.ExcludeThursday;
            ExcludeFriday = _settings.ExcludeFriday;

            ExcludeNewYearsDay = _settings.ExcludeNewYearsDay;
            ExcludeMLKDay = _settings.ExcludeMLKDay;
            ExcludePresidentsDay = _settings.ExcludePresidentsDay;
            ExcludeMemorialDay = _settings.ExcludeMemorialDay;
            ExcludeJuneteenth = _settings.ExcludeJuneteenth;
            ExcludeIndependenceDay = _settings.ExcludeIndependenceDay;
            ExcludeLaborDay = _settings.ExcludeLaborDay;
            ExcludeIndigenousPeoplesDay = _settings.ExcludeIndigenousPeoplesDay;
            ExcludeVeteransDay = _settings.ExcludeVeteransDay;
            ExcludeThanksgiving = _settings.ExcludeThanksgiving;
            ExcludeDayAfterThanksgiving = _settings.ExcludeDayAfterThanksgiving;
            ExcludeChristmas = _settings.ExcludeChristmas;
           
            ReviewOpenDaysBefore = _settings.ReviewOpenDaysBefore;
            ReviewDaysAfterDue = _settings.ReviewDaysAfterDue;
            PcpOpenDaysBefore = _settings.PcpOpenDaysBefore;
            PcpDaysAfterDue = _settings.PcpDaysAfterDue;
            CompAssessmentOpenDaysBefore = _settings.CompAssessmentOpenDaysBefore;
            CompAssessmentDaysAfterDue = _settings.CompAssessmentDaysAfterDue;
            ReclassificationOpenDaysBefore = _settings.ReclassificationOpenDaysBefore;
            ReclassificationDaysAfterDue = _settings.ReclassificationDaysAfterDue;
            SafetyPlanOpenDaysBefore = _settings.SafetyPlanOpenDaysBefore;
            SafetyPlanDaysAfterDue = _settings.SafetyPlanDaysAfterDue;
            PrivacyPracticesOpenDaysBefore = _settings.PrivacyPracticesOpenDaysBefore;
            PrivacyPracticesDaysAfterDue = _settings.PrivacyPracticesDaysAfterDue;
            ReleaseAgencyOpenDaysBefore = _settings.ReleaseAgencyOpenDaysBefore;
            ReleaseAgencyDaysAfterDue = _settings.ReleaseAgencyDaysAfterDue;
            ReleaseDhhsOpenDaysBefore = _settings.ReleaseDhhsOpenDaysBefore;
            ReleaseDhhsDaysAfterDue = _settings.ReleaseDhhsDaysAfterDue;
            ReleaseMedicalOpenDaysBefore = _settings.ReleaseMedicalOpenDaysBefore;
            ReleaseMedicalDaysAfterDue = _settings.ReleaseMedicalDaysAfterDue;

            // Normalize on load so a hand-edited or legacy JSON value still arrives
            // de-duplicated, sorted, and with the "Other" floor present.
            SetHealthcareSystems(HealthcareSystemOptions.Normalize(_settings.HealthcareSystems));
        }

        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            _ = await TrySaveSettingsAsync();
        }

        public async Task<bool> TrySaveSettingsAsync()
        {
            if (!CanManageAgencySettings)
            {
                SaveStatus = "Only an agency administrator can change operational settings.";
                return false;
            }

            if (_settings is null)
                return true;

            SaveStatus = "Saving settings...";

            _settings.AbandonedAfterDays = AbandonedAfterDays;
            _settings.SalesTaxRate = SalesTaxRate;
            _settings.AllowCredibleProfileUpdates = AllowCredibleProfileUpdates;
            _settings.VrAssistantTitle = VrAssistantTitle;
            _settings.DefaultPassthroughProviderId = DefaultPassthroughProviderId;
            _settings.ProductivityThreshold = ProductivityThreshold;
            _settings.BaseIncentive = BaseIncentive;
            _settings.PerUnitIncentive = PerUnitIncentive;
            _settings.BillingComplianceRequirements =
                (ComplianceQuarterlyReviews ? BillingComplianceRequirements.QuarterlyReviews : 0) |
                (CompliancePcp ? BillingComplianceRequirements.Pcp : 0) |
                (ComplianceComprehensiveAssessment ? BillingComplianceRequirements.ComprehensiveAssessment : 0) |
                (ComplianceReclassification ? BillingComplianceRequirements.Reclassification : 0) |
                (ComplianceSafetyPlan ? BillingComplianceRequirements.SafetyPlan : 0) |
                (CompliancePrivacyPractices ? BillingComplianceRequirements.PrivacyPractices : 0) |
                (ComplianceAgencyRelease ? BillingComplianceRequirements.AgencyRelease : 0) |
                (ComplianceDhhsRelease ? BillingComplianceRequirements.DhhsRelease : 0) |
                (ComplianceMedicalRelease ? BillingComplianceRequirements.MedicalRelease : 0);
            _settings.VisitTemplate = VisitTemplate;
            _settings.ContactTemplate = ContactTemplate;
            _settings.DocumentationTemplate = DocumentationTemplate;

            _settings.ExcludeMonday = ExcludeMonday;
            _settings.ExcludeTuesday = ExcludeTuesday;
            _settings.ExcludeWednesday = ExcludeWednesday;
            _settings.ExcludeThursday = ExcludeThursday;
            _settings.ExcludeFriday = ExcludeFriday;

            _settings.ExcludeNewYearsDay = ExcludeNewYearsDay;
            _settings.ExcludeMLKDay = ExcludeMLKDay;
            _settings.ExcludePresidentsDay = ExcludePresidentsDay;
            _settings.ExcludeMemorialDay = ExcludeMemorialDay;
            _settings.ExcludeJuneteenth = ExcludeJuneteenth;
            _settings.ExcludeIndependenceDay = ExcludeIndependenceDay;
            _settings.ExcludeLaborDay = ExcludeLaborDay;
            _settings.ExcludeIndigenousPeoplesDay = ExcludeIndigenousPeoplesDay;
            _settings.ExcludeVeteransDay = ExcludeVeteransDay;
            _settings.ExcludeThanksgiving = ExcludeThanksgiving;
            _settings.ExcludeDayAfterThanksgiving = ExcludeDayAfterThanksgiving;

            _settings.ExcludeChristmas = ExcludeChristmas;

            _settings.ReviewOpenDaysBefore = ReviewOpenDaysBefore;
            _settings.ReviewDaysAfterDue = ReviewDaysAfterDue;
            _settings.PcpOpenDaysBefore = PcpOpenDaysBefore;
            _settings.PcpDaysAfterDue = PcpDaysAfterDue;
            _settings.CompAssessmentOpenDaysBefore = CompAssessmentOpenDaysBefore;
            _settings.CompAssessmentDaysAfterDue = CompAssessmentDaysAfterDue;
            _settings.ReclassificationOpenDaysBefore = ReclassificationOpenDaysBefore;
            _settings.ReclassificationDaysAfterDue = ReclassificationDaysAfterDue;
            _settings.SafetyPlanOpenDaysBefore = SafetyPlanOpenDaysBefore;
            _settings.SafetyPlanDaysAfterDue = SafetyPlanDaysAfterDue;
            _settings.PrivacyPracticesOpenDaysBefore = PrivacyPracticesOpenDaysBefore;
            _settings.PrivacyPracticesDaysAfterDue = PrivacyPracticesDaysAfterDue;
            _settings.ReleaseAgencyOpenDaysBefore = ReleaseAgencyOpenDaysBefore;
            _settings.ReleaseAgencyDaysAfterDue = ReleaseAgencyDaysAfterDue;
            _settings.ReleaseDhhsOpenDaysBefore = ReleaseDhhsOpenDaysBefore;
            _settings.ReleaseDhhsDaysAfterDue = ReleaseDhhsDaysAfterDue;
            _settings.ReleaseMedicalOpenDaysBefore = ReleaseMedicalOpenDaysBefore;
            _settings.ReleaseMedicalDaysAfterDue = ReleaseMedicalDaysAfterDue;

            // Reassign the whole list. The Settings.HealthcareSystems wrapper persists
            // only on assignment, never on in-place mutation — this is the gotcha we
            // flagged when writing Settings.cs, now honored.
            _settings.HealthcareSystems = HealthcareSystems.ToList();

            try
            {
                await _settingsService.SaveAsync(_settings);
                SaveStatus = "Settings saved.";
                return true;
            }
            catch (SettingsConcurrencyException ex)
            {
                SaveStatus = ex.Message;
                return false;
            }
            catch (SettingsSaveException ex)
            {
                SaveStatus = $"Settings were not saved. {ex.Message}";
                return false;
            }
        }

        // Rebuilds the bound collection in place from a source list. Snapshots the
        // source first: callers often pass a LINQ query defined *over* HealthcareSystems
        // itself (the Remove command filters it), and clearing the collection before
        // enumerating a deferred query would empty the query's own source mid-iteration.
        // Materializing up front makes this safe regardless of what the caller passes.
        private void SetHealthcareSystems(IEnumerable<string> names)
        {
            var snapshot = names.ToList();
            HealthcareSystems.Clear();
            foreach (var name in snapshot)
                HealthcareSystems.Add(name);
        }

        [RelayCommand]
        private void AddHealthcareSystem()
        {
            if (string.IsNullOrWhiteSpace(NewHealthcareSystem))
                return;

            SetHealthcareSystems(
                HealthcareSystemOptions.Normalize(HealthcareSystems.Append(NewHealthcareSystem)));
            NewHealthcareSystem = string.Empty;
        }

        [RelayCommand]
        private void RemoveHealthcareSystem()
        {
            if (SelectedHealthcareSystem is null)
                return;

            // The "Other" floor is permanent; silently ignore a request to remove it.
            if (string.Equals(SelectedHealthcareSystem, HealthcareSystemOptions.Other,
                              StringComparison.OrdinalIgnoreCase))
                return;

            var remaining = HealthcareSystems.Where(s =>
                !string.Equals(s, SelectedHealthcareSystem, StringComparison.OrdinalIgnoreCase));

            SetHealthcareSystems(HealthcareSystemOptions.Normalize(remaining));
        }

        [RelayCommand]
        private void ApplyMaineDefaults()
        {
            SetHealthcareSystems(
                HealthcareSystemOptions.MergeDefaults(HealthcareSystems, HealthcareSystemOptions.Maine));
        }
    }

    /// <summary>One offered inactivity delay. Minutes of zero means never.</summary>
    public sealed record IdleTimeoutOption(int Minutes, string Label);
}
