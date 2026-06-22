using Sati.Data;
using Sati.Models;

namespace Sati
{
    public class Person
    {
        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public int Id { get; private set; }
        public int UserId { get; private set; }
        public User? User { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime BirthDate { get; set; }
        public Gender Gender { get; set; } = Gender.Unknown;
        public DateTime? EffectiveDate { get; set; }
        public string? Bio { get; set; }
        public WaiverType Waiver { get; set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; } = null!;
        public string? MaineCareId { get; set; }
        public string? DiagnosisCode { get; set; }
        public int? PlaceOfService { get; set; }

        // -------------------------------------------------------------------------
        // Contact & support details
        // -------------------------------------------------------------------------

        // Active Vocational Rehabilitation case running alongside Section 17 services.
        public bool OpenWithVR { get; set; }

        // HasGuardian drives the reveal of the guardian-name field in the edit form.
        // GuardianName is kept independent of the flag — unchecking HasGuardian does
        // not null out GuardianName — so a name typed in error-and-recovery, or a
        // guardianship that lapses and resumes, doesn't silently destroy the stored
        // value. The flag governs visibility; the user governs the data.
        public bool HasGuardian { get; set; }
        public string? GuardianName { get; set; }

        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? PrimaryCareProvider { get; set; }

        // Healthcare system, stored denormalized as a plain name string. This is a
        // deliberate design choice, not a shortcut, and the seam for a future
        // relational model is pre-cut in three places so that migration is additive
        // rather than a rewrite:
        //
        //   1. The property is named *Name* on purpose. It leaves the bare name
        //      `HealthcareSystem` free for a future navigation property and
        //      `HealthcareSystemId` free for a future foreign key. When records get
        //      relational, you add those columns and backfill by matching on this
        //      string — this column is never renamed.
        //
        //   2. In the UI, the ComboBox binds through SelectedValuePath against a
        //      HealthcareSystemOption type rather than binding directly to a string.
        //      When the option type later carries an Id, you flip SelectedValuePath
        //      from "Name" to "Id" and the ItemsSource and item template are unchanged.
        //
        //   3. The configurable option list is serialized as JSON on Settings (see
        //      HealthcareSystemsJson), so its stored shape can gain fields later
        //      without breaking rows written today.
        public string? HealthcareSystemName { get; set; }

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public List<Form> Forms { get; set; } = [];
        public List<Note> Notes { get; set; } = [];

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        protected Person() { }

        // -------------------------------------------------------------------------
        // Factory
        // -------------------------------------------------------------------------

        // Settings is no longer used in the body but kept on the signature so
        // existing callers (NewClientViewModel) don't break. Remove in cleanup.
        public static Person CreatePerson(int userId, string firstName, string lastName,
                   string bio, DateTime birthdate, DateTime? effective, WaiverType waiver, Settings settings)
        {
            var person = new Person
            {
                UserId = userId,
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Bio = bio.Trim(),
                BirthDate = birthdate,
                EffectiveDate = effective,
                Waiver = waiver
            };

            if (effective is null)
                return person;

            person.Forms = GenerateFormList(effective.Value);
            return person;
        }

        // Sentinel factory for filter dropdowns that need an "All Persons" row.
        // Bypasses GenerateFormList deliberately — this object never enters the DB
        // and has no forms, notes, or real identity. Id = -1 is a marker, not a key.
        public static Person CreateSentinel(string label)
        {
            return new Person
            {
                Id = -1,
                FirstName = label,
                LastName = string.Empty
            };
        }

        // -------------------------------------------------------------------------
        // Methods
        // -------------------------------------------------------------------------

        // First-cycle generation. Annual non-review documents default to compliant
        // because cycle 1 begins with the consumer's initial signed PCP, comp
        // assessment, and so on already in place at admission. Reviews default to
        // non-compliant — they're tasks to complete during the cycle. The
        // creation-time compliance dialog lets the user override these defaults
        // for backdated admissions where some forms are already overdue.
        public static List<Form> GenerateFormList(DateTime effective)
        {
            var cycleStart = effective;
            var cycleEnd = effective.AddYears(1);

            return Enum.GetValues<FormType>()
                .Select(type => new Form(
                    type,
                    FormDueDateCalculator.Compute(type, cycleStart, cycleEnd),
                    isCompliant: !IsReviewType(type)))
                .ToList();
        }

        // Returns (cycleStart, cycleEnd) bracketing the cycle that contains today,
        // using the half-open convention: today belongs to a cycle if cycleStart
        // <= today < cycleEnd. The anniversary date itself belongs to the next
        // cycle. Returns null if EffectiveDate is unset.
        public (DateTime cycleStart, DateTime cycleEnd)? GetCurrentCycleBoundaries(DateTime today)
        {
            if (EffectiveDate is null)
                return null;

            var effective = EffectiveDate.Value;
            var yearsElapsed = today.Year - effective.Year;
            if (today < effective.AddYears(yearsElapsed))
                yearsElapsed--;

            var cycleStart = effective.AddYears(yearsElapsed);
            var cycleEnd = effective.AddYears(yearsElapsed + 1);
            return (cycleStart, cycleEnd);
        }

        // Returns the form of the given type that belongs to the consumer's
        // current cycle. Cycle membership is half-open [cycleStart, cycleEnd) —
        // forms whose DueDate equals cycleEnd belong to the next cycle, not
        // this one. Returns null if no current-cycle form exists; the caller
        // surfaces that as NoForm rather than borrowing a stale form.
        public Form? GetCurrentCycleForm(FormType type, DateTime? asOf = null)
        {
            var today = asOf ?? DateTime.Today;
            var boundaries = GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return null;

            var (cycleStart, cycleEnd) = boundaries.Value;

            return Forms
                .Where(f => f.Type == type &&
                            f.DueDate >= cycleStart &&
                            f.DueDate < cycleEnd)
                .OrderByDescending(f => f.DueDate)
                .FirstOrDefault();
        }

        public FormComplianceStatus GetComplianceStatus(FormType type, DateTime referenceDate, Settings settings)
        {
            var form = GetCurrentCycleForm(type, referenceDate);

            if (form is null)
                return FormComplianceStatus.NoForm;

            if (form.IsCompliant)
            {
                return form.CompletedDate.HasValue && form.CompletedDate.Value > form.DueDate
                    ? FormComplianceStatus.CompliantLate
                    : FormComplianceStatus.CompliantOnTime;
            }

            var openDaysBefore = GetOpenDaysBefore(type, settings);
            var openDate = form.DueDate.AddDays(-openDaysBefore);

            if (referenceDate < openDate)
                return FormComplianceStatus.NotYetDue;

            if (referenceDate <= form.DueDate)
                return FormComplianceStatus.InWindow;

            return FormComplianceStatus.Overdue;
        }

        public static int GetOpenDaysBefore(FormType type, Settings settings) => type switch
        {
            FormType.Q1R or FormType.Q2R or FormType.Q3R or FormType.Q4R
                => settings.ReviewOpenDaysBefore,
            FormType.PCP
                => settings.PcpOpenDaysBefore,
            FormType.ComprehensiveAssessment
                => settings.CompAssessmentOpenDaysBefore,
            FormType.Reclassification
                => settings.ReclassificationOpenDaysBefore,
            FormType.SafetyPlan
                => settings.SafetyPlanOpenDaysBefore,
            FormType.PrivacyPractices
                => settings.PrivacyPracticesOpenDaysBefore,
            FormType.Release_Agency
                => settings.ReleaseAgencyOpenDaysBefore,
            FormType.Release_DHHS
                => settings.ReleaseDhhsOpenDaysBefore,
            FormType.Release_Medical
                => settings.ReleaseMedicalOpenDaysBefore,
            _ => 30
        };

        // Ensures forms exist for both the consumer's current cycle and their
        // next cycle. Defaults differ by cycle:
        //
        //   - Current cycle: annual non-reviews default to IsCompliant = true,
        //     because the cycle started because those documents were signed.
        //     Reviews default to false — tasks to complete during the cycle.
        //
        //   - Next cycle: annual non-reviews default to IsCompliant = false.
        //     The user marks them true during the prep window as each
        //     renewal is signed. If the cycle rolls over with these still
        //     false, the consumer is correctly flagged as having missed
        //     prep — the renewal didn't happen in time. Reviews default
        //     to false same as current.
        //
        // The Settings parameter is unused after the form-model refactor but
        // kept on the signature so PersonService doesn't need to change in
        // lockstep. Safe to remove in a follow-up sweep.
        public bool EnsureCurrentCycleForms(DateTime today, Settings settings)
        {
            var boundaries = GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return false;

            var (cycleStart, cycleEnd) = boundaries.Value;
            var added = false;

            // Current cycle: documents in force, reviews to do
            added |= AddMissingFormsForCycle(cycleStart, cycleEnd, defaultAnnualCompliant: true);

            // Next cycle: prep not yet done; user marks true as renewals are signed
            var nextStart = cycleEnd;
            var nextEnd = cycleEnd.AddYears(1);
            added |= AddMissingFormsForCycle(nextStart, nextEnd, defaultAnnualCompliant: false);

            return added;
        }

        // Idempotent: only adds forms that don't already exist for the cycle.
        // Cycle membership uses the half-open [cycleStart, cycleEnd) convention,
        // matching GetCurrentCycleForm so a form created here is visible there.
        private bool AddMissingFormsForCycle(DateTime cycleStart, DateTime cycleEnd, bool defaultAnnualCompliant)
        {
            var added = false;

            foreach (var type in Enum.GetValues<FormType>())
            {
                var existsForCycle = Forms.Any(f =>
                    f.Type == type &&
                    f.DueDate >= cycleStart &&
                    f.DueDate < cycleEnd);

                if (existsForCycle)
                    continue;

                // Reviews never default to compliant; annual non-reviews follow
                // the caller's instruction (true for current cycle, false for next).
                var defaultCompliant = !IsReviewType(type) && defaultAnnualCompliant;

                Forms.Add(new Form(
                    type,
                    FormDueDateCalculator.Compute(type, cycleStart, cycleEnd),
                    isCompliant: defaultCompliant)
                {
                    PersonId = Id
                });
                added = true;
            }

            return added;
        }

        // Returns whether the billing compliance gate is satisfied, and if not,
        // a human-readable list of every reason it failed. Callers that only
        // need a bool use .Passed; callers that need to explain the failure to
        // the user destructure .Reasons. Both pieces come from one pass through
        // the same logic, so they can never drift apart.
        public (bool Passed, IReadOnlyList<string> Reasons) EvaluateComplianceGate(DateTime today, FormType? beingCompleted = null)
        {
            {
                var reasons = new List<string>();

                var requiredAnnual = new[]
                {
                FormType.PCP,
                FormType.ComprehensiveAssessment,
                FormType.Reclassification,
                FormType.SafetyPlan
            };

                foreach (var type in requiredAnnual)
                {
                    if (type == beingCompleted) continue;
                    var form = GetCurrentCycleForm(type, today);
                    if (form is null || !form.IsCompliant)
                        reasons.Add($"{FormDisplayName(type)} is not marked compliant for the current cycle.");
                }

                var boundaries = GetCurrentCycleBoundaries(today);
                if (boundaries is null)
                {
                    reasons.Add("No active compliance cycle found. This client may be missing an effective date.");
                    return (false, reasons);
                }

                var (cycleStart, cycleEnd) = boundaries.Value;

                var pastDueReviews = Forms.Where(f =>
                    IsReviewType(f.Type) &&
                    f.DueDate >= cycleStart &&
                    f.DueDate < cycleEnd &&
                    f.DueDate.Date <= today.Date);

                foreach (var review in pastDueReviews)
                {
                    if (review.Type == beingCompleted) continue;
                    if (!review.IsCompliant)
                        reasons.Add($"{FormDisplayName(review.Type)} was due {review.DueDate:MMM d, yyyy} and is not marked compliant.");
                }

                return (reasons.Count == 0, reasons);
            }
        }

        // Forward-looking, date-keyed billing window check. Returns the gated forms
        // whose missed-due-date window contains noteDate — the note falls strictly
        // after a form's due date while that form was not yet completed as of the
        // note's date. Empty list => clear to bill.
        //
        // Why this is separate from EvaluateComplianceGate: that method reasons as of
        // TODAY (current-cycle paperwork in order). This one reasons as of the NOTE's
        // date, which may sit in a different cycle when back-entering notes — so it
        // walks Forms directly by each form's own due date rather than via
        // GetCurrentCycleForm, which is pinned to today.
        //
        // Gated: PCP, Comp, Reclass, and the four reviews. Safety Plan keeps its
        // existing current-state check in EvaluateComplianceGate and is NOT windowed;
        // Releases and Privacy Practices are excluded entirely, pending the
        // configurable-billability-scope work.
        //
        // Window endpoints are exclusive on both ends: a note ON the due date bills
        // (the deadline itself isn't a miss), and a note ON or after the completion
        // date bills (done by then). Only dates strictly between block.
        public IReadOnlyList<string> EvaluateBillingWindow(DateTime noteDate)
        {
            var gatedTypes = new[]
            {
                FormType.PCP,
                FormType.ComprehensiveAssessment,
                FormType.Reclassification,
                FormType.Q1R, FormType.Q2R, FormType.Q3R, FormType.Q4R
            };

            var blocking = new List<string>();

            foreach (var form in Forms.Where(f => gatedTypes.Contains(f.Type)))
            {
                var pastDue = noteDate.Date > form.DueDate.Date;
                if (!pastDue)
                    continue;

                var notDoneAsOfNote = form.CompletedDate is null
                    || noteDate.Date < form.CompletedDate.Value.Date;
                if (notDoneAsOfNote)
                    blocking.Add($"{FormDisplayName(form.Type)} was due {form.DueDate:MMM d, yyyy} "
                               + "and was not completed as of this note's date.");
            }

            return blocking;
        }

        public static string FormDisplayName(FormType type) => type switch
        {
            FormType.PCP => "PCP",
            FormType.ComprehensiveAssessment => "Comprehensive Assessment",
            FormType.Reclassification => "Reclassification",
            FormType.SafetyPlan => "Safety Plan",
            FormType.PrivacyPractices => "Privacy Practices",
            FormType.Release_Agency => "Agency Release",
            FormType.Release_DHHS => "DHHS Release",
            FormType.Release_Medical => "Medical Release",
            FormType.Q1R => "Q1 Review",
            FormType.Q2R => "Q2 Review",
            FormType.Q3R => "Q3 Review",
            FormType.Q4R => "Q4 Review",
            _ => type.ToString()
        };
        private static bool IsReviewType(FormType type) => type is
                FormType.Q1R or FormType.Q2R or FormType.Q3R or FormType.Q4R;
    }
}