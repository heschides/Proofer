using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sati.Data;
using Sati.Helpers;
using Sati.Models;
using Sati.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace Sati.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly ISettingsService _settingsService;
        private readonly IProviderService _providerService;
        private readonly FormDueDateBackfill? _backfill;
        private readonly FormBulkCompletion? _bulkCompletion;
        private readonly ThemeService _themeService;
        private Settings? _settings;

        public SettingsViewModel(
            ISettingsService settingsService,
            IProviderService providerService,
            ThemeService themeService,
            FormDueDateBackfill? backfill = null,
            FormBulkCompletion? bulkCompletion = null)
        {
            _settingsService = settingsService;
            _providerService = providerService;
            _backfill = backfill;
            _bulkCompletion = bulkCompletion;
            _themeService = themeService;
            selectedTheme = _themeService.CurrentTheme;
            _ = LoadAsync();
        }

        public IReadOnlyList<ThemeOption> ThemeOptions => _themeService.Themes;

        [ObservableProperty]
        private ThemeOption? selectedTheme;

        partial void OnSelectedThemeChanged(ThemeOption? value)
        {
            if (value is not null)
                _themeService.ApplyTheme(value.ResourceName);
        }

        // Sales tax as a rate (0.055 = 5.5%), adjustable. Frozen onto AT requests
        // at save in a later slice; here it's just the editable default.
        [ObservableProperty] private decimal salesTaxRate;

        // The passthrough-provider list for the default picker, and the chosen
        // default's Id. The combo binds SelectedValue → this, SelectedValuePath=Id,
        // so it round-trips the int? FK without matching object identity. Nullable:
        // null = no default set.
        public ObservableCollection<Provider> PassthroughProviders { get; } = [];
        [ObservableProperty] private int? defaultPassthroughProviderId;

        // ====================================================================
        // TEMPORARY MAINTENANCE — DUE-DATE BACKFILL
        // Remove this whole region (and the three controls in SettingsWindow.xaml
        // marked with the same banner) once the backfill has been run and the
        // PersonService.EnableEnsureCycleFormsOnLoad guard has been lifted.
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
        // date as complete, stamping the due date. Remove this region and
        // its controls in SettingsWindow.xaml with the rest of the scaffolding.
        // ====================================================================

        // Cutoff, editable in the maintenance UI. Defaults to the agreed value;
        // bound as text so the box starts populated but you can change it.
        [ObservableProperty] private string bulkCompleteCutoff = "2026-08-01";

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

            try
            {
                var report = await _bulkCompletion.DryRunAsync(cutoff);
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

            if (!int.TryParse(BulkCompleteConfirmCount, out var typed))
            {
                BulkCompleteStatus = "Enter the exact count from the dry run to commit.";
                return;
            }

            try
            {
                var report = await _bulkCompletion.CommitAsync(cutoff, typed);
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
            _settings = await _settingsService.LoadAsync();

            SalesTaxRate = _settings.SalesTaxRate;
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
            if (_settings is null)
                return;

            _settings.AbandonedAfterDays = AbandonedAfterDays;
            _settings.SalesTaxRate = SalesTaxRate;
            _settings.DefaultPassthroughProviderId = DefaultPassthroughProviderId;
            _settings.ProductivityThreshold = ProductivityThreshold;
            _settings.BaseIncentive = BaseIncentive;
            _settings.PerUnitIncentive = PerUnitIncentive;
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

            await _settingsService.SaveAsync(_settings);
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
}
