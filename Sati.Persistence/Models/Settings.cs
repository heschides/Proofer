using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Text.Json;

namespace Sati.Models
{
    public class Settings
    {
        public int Id { get; set; }
        public int AgencyId { get; set; }
        public int Revision { get; set; } = 1;
        public int AnnualPacketOpenDaysBefore { get; set; } = Contracts.V1.AnnualPacketWindow.DefaultOpenDays;

        // Agency policy. Off by default: importing into an existing profile can
        // replace current demographics, so an administrator must opt the agency in.
        public bool AllowCredibleProfileUpdates { get; set; }

        // Display label for the second VR assignment field. The assigned person's
        // name lives on Person; changing this title never rewrites profile data.
        public string VrAssistantTitle { get; set; } =
            Contracts.V1.VocationalRehabilitationProfile.DefaultAssistantTitle;

        // Agency policy: which overdue, incomplete forms stop billing. The
        // decision itself remains in the shared BillingComplianceGate.
        public Contracts.V1.BillingComplianceRequirements BillingComplianceRequirements { get; set; } =
            Contracts.V1.BillingComplianceGate.DefaultRequirements;

        // Abandonment
        public int AbandonedAfterDays { get; set; } = 7;

        // Productivity
        public int ProductivityThreshold { get; set; } = 100;
        public decimal BaseIncentive { get; set; } = 0;
        public decimal PerUnitIncentive { get; set; } = 0;

        // AT / payment requests
        //
        // The agency passthrough fee applied to authorized payment requests: a
        // fraction (0.15 = 15%) of the TAX-INCLUSIVE subtotal. Stored as a rate,
        // not a percent — code multiplies by it directly, no /100. The single
        // source of truth for the *value*; the arithmetic that consumes it lives
        // in ATRequestCalculator. decimal(5,4) column (set in SatiContext) gives
        // room for sub-percent precision (e.g. 0.155) without a schema change.
        //
        // Property default 0.15m covers new in-memory instances; the migration
        // also writes a SQL default + backfills the existing row, so no install
        // ever computes against a zero rate.
        public decimal PassthroughRate { get; set; } = 0.15m;

        // Maine sales tax, a rate like PassthroughRate (0.055 = 5.5%), adjustable.
        // Frozen onto the request at save (snapshot-consistent with the rest of the
        // AT document). decimal(5,4) column set in SatiContext.
        public decimal SalesTaxRate { get; set; } = 0.055m;

        // The provider pre-selected on the AT page when a consumer's own support
        // provider doesn't offer passthrough (Maine AT Solutions, by seed). Nullable
        // FK to Provider — editable in the Settings window. Null = nothing
        // pre-selected; the CM picks manually. See SatiContext for the FK + the
        // deliberate choice NOT to SQL-default it.
        public int? DefaultPassthroughProviderId { get; set; }

        // Note templates
        public string VisitTemplate { get; set; } = string.Empty;
        public string ContactTemplate { get; set; } = string.Empty;
        public string DocumentationTemplate { get; set; } = string.Empty;

        // Healthcare systems
        //
        // The selectable healthcare systems are configured per install and persisted
        // as JSON in this single column, mirroring the ExcludedDatesJson pattern.
        // Today the payload is a list of names; because it's JSON, the serialized
        // shape can later hold objects (with ids / foreign keys) without a schema
        // change to this table — the third leg of the future-proofing described on
        // Person.HealthcareSystemName.
        //
        // Defaults to ["Other"] via a raw string literal — the C# 11 """ ... """
        // form, which lets the embedded double-quotes sit unescaped — so a fresh
        // install always has one selectable option rather than an empty dropdown.
        public string HealthcareSystemsJson { get; set; } = """["Other"]""";

        // A serialization view over HealthcareSystemsJson. [NotMapped] means EF
        // ignores it for the schema — only the JSON string above is a real column.
        //
        // Gotcha worth stating plainly: the getter deserializes a fresh list on every
        // access, so mutating the returned list in place — HealthcareSystems.Add(x) —
        // writes into a throwaway and persists nothing. To save a change, reassign the
        // whole list:  settings.HealthcareSystems = updated;
        [NotMapped]
        public List<string> HealthcareSystems
        {
            get => string.IsNullOrWhiteSpace(HealthcareSystemsJson)
                ? new List<string>()
                : JsonSerializer.Deserialize<List<string>>(HealthcareSystemsJson) ?? new List<string>();
            set => HealthcareSystemsJson = JsonSerializer.Serialize(value);
        }



        // Weekday exclusions
        public bool ExcludeMonday { get; set; } = false;
        public bool ExcludeTuesday { get; set; } = false;
        public bool ExcludeWednesday { get; set; } = false;
        public bool ExcludeThursday { get; set; } = false;
        public bool ExcludeFriday { get; set; } = false;

        // Federal holidays
        public bool ExcludeNewYearsDay { get; set; } = true;
        public bool ExcludeMLKDay { get; set; } = false;
        public bool ExcludePresidentsDay { get; set; } = false;
        public bool ExcludeMemorialDay { get; set; } = true;
        public bool ExcludeJuneteenth { get; set; } = false;
        public bool ExcludeIndependenceDay { get; set; } = true;
        public bool ExcludeLaborDay { get; set; } = true;
        public bool ExcludeIndigenousPeoplesDay { get; set; } = false;
        public bool ExcludeVeteransDay { get; set; } = false;
        public bool ExcludeThanksgiving { get; set; } = true;
        public bool ExcludeDayAfterThanksgiving { get; set; } = true;
        public bool ExcludeChristmas { get; set; } = true;

        // EVENT DATE SETTINGS

        // Reviews (shared across Q1R–Q4R)
        public int ReviewOpenDaysBefore { get; set; }
        public int ReviewDaysAfterDue { get; set; }

        // PCP
        public int PcpOpenDaysBefore { get; set; } = 90;
        public int PcpDaysAfterDue { get; set; }

        // Comprehensive Assessment
        public int CompAssessmentOpenDaysBefore { get; set; }
        public int CompAssessmentDaysAfterDue { get; set; }

        // Reclassification
        public int ReclassificationOpenDaysBefore { get; set; }
        public int ReclassificationDaysAfterDue { get; set; }

        // Safety Plan
        public int SafetyPlanOpenDaysBefore { get; set; }
        public int SafetyPlanDaysAfterDue { get; set; }

        // Privacy Practices
        public int PrivacyPracticesOpenDaysBefore { get; set; }
        public int PrivacyPracticesDaysAfterDue { get; set; }

        // Releases
        public int ReleaseAgencyOpenDaysBefore { get; set; }
        public int ReleaseAgencyDaysAfterDue { get; set; }

        public int ReleaseDhhsOpenDaysBefore { get; set; }
        public int ReleaseDhhsDaysAfterDue { get; set; }

        public int ReleaseMedicalOpenDaysBefore { get; set; }
        public int ReleaseMedicalDaysAfterDue { get; set; }

        // EVENT DATE OFFSETS (anniversary − N days = due date)
        // These set when each annual form is *due*, distinct from when it
        // opens in the upcoming-events dashboard (*OpenDaysBefore above).

        // Q4R is the one review anchored to the cycle *end*: Q1R–Q3R count
        // forward as +90/+180/+270 from cycleStart, but Q4R is anniversary − N,
        // so it belongs with the annual offsets here, not the forward reviews.
        // Left bare like its siblings; the default of 5 is seeded in
        // SettingsService and written into the existing row by migration.
        public int Q4RDaysBeforeAnniversary { get; set; }

        public int PcpDaysBeforeAnniversary { get; set; }
        public int CompAssessmentDaysBeforeAnniversary { get; set; }
        public int ReclassificationDaysBeforeAnniversary { get; set; }
        public int SafetyPlanDaysBeforeAnniversary { get; set; }
        public int PrivacyPracticesDaysBeforeAnniversary { get; set; }
        public int ReleaseAgencyDaysBeforeAnniversary { get; set; }
        public int ReleaseDhhsDaysBeforeAnniversary { get; set; }
        public int ReleaseMedicalDaysBeforeAnniversary { get; set; }
    }
}
