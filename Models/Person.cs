using Sati.Data;
using Sati.Models;

namespace Sati
{
    public class Person : IEventSource
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

        // Per-consumer freeform working journal — indefinitely long, distinct from
        // the date-scoped Scratchpad. Maps to nvarchar(max). MUST be excluded from
        // GetAllPeopleAsync: like Bio and Note.Narrative, an unbounded column on the
        // caseload-load path inflates the memory-grant estimate and feeds the
        // RESOURCE_SEMAPHORE stalls. Loaded and saved on demand via
        // IPersonService.GetJournalAsync / SaveJournalAsync only.
        public string? Journal { get; set; }

        public WaiverType Waiver { get; set; } = WaiverType.None;
        public string FullName => $"{FirstName} {LastName}".Trim();
        public int? AgencyId { get; set; }
        public Agency? Agency { get; set; } = null!;
        public string? MaineCareId { get; set; }
        public string? DiagnosisCode { get; set; }
        public int? PlaceOfService { get; set; }

        // The client's Evergreen ID — Maine's case management system of record.
        // Surfaces on payment/authorization forms as the "EIS #" (the forms
        // predate Evergreen and still use the legacy EIS label). Nullable: not
        // every client has one recorded yet.
        public string? EvergreenId { get; set; }

        // -------------------------------------------------------------------------
        // Contact & support details
        // -------------------------------------------------------------------------

        // Active Vocational Rehabilitation case (Dept. of Labor) running alongside
        // Section 17 services. Distinct from the MaineCare-funded employment
        // supports below.
        public bool OpenWithVR { get; set; }

        // HasGuardian governs field visibility only; unchecking it does not null
        // GuardianName, so a lapsed-and-resumed guardianship doesn't destroy data.
        public bool HasGuardian { get; set; }
        public string? GuardianName { get; set; }

        public string? PhoneNumber { get; set; }
        public string? Address { get; set; }
        public string? PrimaryCareProvider { get; set; }

        // Deliberately denormalized as a name string. The seam for a future
        // relational model is pre-cut: this column never renames (a future
        // HealthcareSystemId/HealthcareSystem nav gets added beside it and
        // backfilled by name match), the ComboBox binds via SelectedValuePath so
        // flipping "Name" → "Id" later touches one attribute, and the option list
        // lives as JSON on Settings so its shape can grow without breaking rows.
        public string? HealthcareSystemName { get; set; }

        // -------------------------------------------------------------------------
        // Waiver services & employment
        // -------------------------------------------------------------------------

        // One flag per statutory waiver service. Columns rather than a child
        // table: the service list is statute-stable, changing only when the
        // state changes it, and flat flags keep queries and bindings simple.
        public bool HasHomeSupport { get; set; }
        public bool HasSelfDirectedHomeSupport { get; set; }
        public bool HasSharedLiving { get; set; }
        public bool HasCommunitySupport1To1 { get; set; }
        public bool HasCommunitySupportSelfDirected { get; set; }
        public bool HasCommunitySupportDayProgram { get; set; }

        // Meaningful only when HasCommunitySupportDayProgram is true.
        public int DayProgramCount { get; set; } = 1;

        public bool HasEmploymentSpecialist { get; set; }
        public bool HasWorkSupports { get; set; }

        public bool IsEmployed { get; set; }

        // Employed with no employment-related supports from any funding stream
        // (waiver or VR) — the population whose employment parameters the case
        // manager must track directly per state requirement.
        public bool RequiresEmploymentTracking =>
            IsEmployed && !HasEmploymentSpecialist && !HasWorkSupports && !OpenWithVR;

        // Quarterly note-review slots derive from service flags. Self-directed
        // services are exempt from note review and contribute no slots.
        public int HomeNoteSlots =>
            (HasHomeSupport ? 1 : 0) + (HasSharedLiving ? 1 : 0);

        public int CommunityNoteSlots =>
            (HasCommunitySupport1To1 ? 1 : 0) +
            (HasCommunitySupportDayProgram ? DayProgramCount : 0);

        // -------------------------------------------------------------------------
        // Collections
        // -------------------------------------------------------------------------

        public List<Form> Forms { get; set; } = [];
        public List<Note> Notes { get; set; } = [];

        // Explicit interface implementation: exposes the entity's Notes as the
        // read-only INoteInfo surface IEventSource requires. List<Note> doesn't
        // satisfy IEnumerable<INoteInfo> directly (C# property types must match
        // exactly), so this adapter bridges them. Note already implements INoteInfo.
        IEnumerable<INoteInfo> IEventSource.Notes => Notes;

        // -------------------------------------------------------------------------
        // Constructor
        // -------------------------------------------------------------------------

        protected Person() { }

        // -------------------------------------------------------------------------
        // Factory
        // -------------------------------------------------------------------------

        // Settings is unused in the body but kept on the signature so existing
        // callers (NewClientViewModel) don't break. Remove in cleanup.
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

            person.Forms = GenerateFormList(effective.Value, settings);
            return person;
        }

        // Sentinel for filter dropdowns needing an "All Persons" row. Never
        // enters the DB; Id = -1 is a marker, not a key.
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

        // First-cycle generation. Annual non-reviews default compliant (cycle 1
        // begins with signed documents in place at admission); reviews default
        // non-compliant (tasks to complete during the cycle). The creation-time
        // compliance dialog handles overrides for backdated admissions.
        public static List<Form> GenerateFormList(DateTime effective, Settings settings)
        {
            var cycleStart = effective;
            var cycleEnd = effective.AddYears(1);

            return Enum.GetValues<FormType>()
                .Select(type => new Form(
                    type,
                    FormDueDateCalculator.Compute(type, cycleStart, cycleEnd, settings),
                    isCompliant: !IsReviewType(type)))
                .ToList();
        }

        // Returns (cycleStart, cycleEnd) bracketing the cycle containing today:
        // cycleStart <= today < cycleEnd. The anniversary itself belongs to the
        // next cycle. Null if EffectiveDate is unset.
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

        // Which quarter of the current cycle today falls in, 1-4. Quarters are
        // 90-day blocks from cycleStart, matching how Q1R-Q4R due dates are
        // anchored (prevAnniversary + 90/180/270/365) — so "we're in Q3" means
        // Q3R is the review currently in play.
        //
        // The clamp matters: a 365-day cycle divided into 90-day blocks leaves a
        // 5-day tail, and a leap year leaves 6. Without it, the last days before
        // the anniversary would report quarter 5. Those days belong to Q4.
        //
        // Null when EffectiveDate is unset — same contract as the boundaries
        // method it delegates to.
        public int? GetCurrentQuarter(DateTime today)
        {
            var boundaries = GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return null;

            var elapsed = (today.Date - boundaries.Value.cycleStart.Date).Days;
            return Math.Clamp(elapsed / 90 + 1, 1, 4);
        }

        // THE single definition of form-to-cycle membership: a form belongs to a
        // cycle if its due date falls in (cycleStart, cycleEnd] — after the start,
        // on OR before the end. The anniversary is INCLUSIVE because annual forms
        // are dated cycleEnd − offset and the offset-0 forms land exactly on
        // cycleEnd; an exclusive end would drop them into the next cycle.
        //
        // Every membership question in this class routes through here. The
        // dashboard's forward-looking task filter deliberately does NOT — it
        // scans current-and-future with no upper bound by design.
        private static bool FormBelongsToCycle(DateTime dueDate, DateTime cycleStart, DateTime cycleEnd)
            => dueDate > cycleStart && dueDate <= cycleEnd;

        // Returns the current-cycle form of the given type, or null if none
        // exists — the caller surfaces that as NoForm rather than borrowing a
        // stale form.
        public Form? GetCurrentCycleForm(FormType type, DateTime? asOf = null)
                    => FindCurrentCycleForm(Forms, EffectiveDate, type, asOf);

        // Static single-source-of-truth for current-cycle form lookup. Extracted so
        // PersonSummary (the blob-free sidebar DTO) can answer GetCurrentCycleForm
        // without a shadow copy of cycle-membership math. Cycle boundaries are computed
        // inline here from effectiveDate rather than via the instance
        // GetCurrentCycleBoundaries, so the helper needs no Person instance — same
        // convention, (cycleStart, cycleEnd], enforced by FormBelongsToCycle.
        public static Form? FindCurrentCycleForm(
            List<Form> forms, DateTime? effectiveDate, FormType type, DateTime? asOf = null)
        {
            if (effectiveDate is null)
                return null;

            var today = asOf ?? DateTime.Today;
            var effective = effectiveDate.Value;

            var yearsElapsed = today.Year - effective.Year;
            if (today < effective.AddYears(yearsElapsed))
                yearsElapsed--;

            var cycleStart = effective.AddYears(yearsElapsed);
            var cycleEnd = effective.AddYears(yearsElapsed + 1);

            return forms
                .Where(f => f.Type == type &&
                            FormBelongsToCycle(f.DueDate, cycleStart, cycleEnd))
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

        // Ensures forms exist for the current AND next cycle. Current-cycle
        // annual non-reviews default compliant (the cycle started because those
        // documents were signed); next-cycle annuals default non-compliant and
        // get marked true during the prep window as renewals are signed — if the
        // cycle rolls over with them still false, missed prep is correctly
        // flagged. Reviews default false in both cycles.
        //
        // Settings is unused after the form-model refactor; kept so PersonService
        // doesn't change in lockstep. Remove in a follow-up sweep.
        public bool EnsureCurrentCycleForms(DateTime today, Settings settings)
        {
            var boundaries = GetCurrentCycleBoundaries(today);
            if (boundaries is null)
                return false;

            var (cycleStart, cycleEnd) = boundaries.Value;
            var added = false;

            added |= AddMissingFormsForCycle(cycleStart, cycleEnd, defaultAnnualCompliant: true, settings);

            var nextStart = cycleEnd;
            var nextEnd = cycleEnd.AddYears(1);
            added |= AddMissingFormsForCycle(nextStart, nextEnd, defaultAnnualCompliant: false, settings);

            return added;
        }

        // Idempotent: only adds forms missing for the cycle. Membership routes
        // through FormBelongsToCycle — the (cycleStart, cycleEnd] convention —
        // so a form created here is visible to GetCurrentCycleForm.
        private bool AddMissingFormsForCycle(DateTime cycleStart, DateTime cycleEnd, bool defaultAnnualCompliant, Settings settings)
        {
            var added = false;

            foreach (var type in Enum.GetValues<FormType>())
            {
                var existsForCycle = Forms.Any(f =>
                                    f.Type == type &&
                                    FormBelongsToCycle(f.DueDate, cycleStart, cycleEnd));

                if (existsForCycle)
                    continue;

                var defaultCompliant = !IsReviewType(type) && defaultAnnualCompliant;

                Forms.Add(new Form(
                                    type,
                                    FormDueDateCalculator.Compute(type, cycleStart, cycleEnd, settings),
                                    isCompliant: defaultCompliant)
                {
                    PersonId = Id
                });
                added = true;
            }

            return added;
        }

        // Returns whether the billing compliance gate passes, and if not, every
        // reason it failed. One pass produces both, so they can't drift.
        public (bool Passed, IReadOnlyList<string> Reasons) EvaluateComplianceGate(DateTime today, FormType? beingCompleted = null)
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
                FormBelongsToCycle(f.DueDate, cycleStart, cycleEnd) &&
                f.DueDate.Date <= today.Date);

            foreach (var review in pastDueReviews)
            {
                if (review.Type == beingCompleted) continue;
                if (!review.IsCompliant)
                    reasons.Add($"{FormDisplayName(review.Type)} was due {review.DueDate:MMM d, yyyy} and is not marked compliant.");
            }

            return (reasons.Count == 0, reasons);
        }

        // Forward-looking, date-keyed billing window check: returns gated forms
        // whose missed-due-date window contains noteDate. Empty list => clear
        // to bill.
        //
        // Separate from EvaluateComplianceGate because that reasons as of TODAY;
        // this reasons as of the NOTE's date, which may sit in a different cycle
        // when back-entering — so it walks Forms directly by due date rather than
        // via GetCurrentCycleForm, which is pinned to today.
        //
        // Gated: PCP, Comp, Reclass, four reviews. Safety Plan keeps its
        // current-state check in EvaluateComplianceGate and is NOT windowed;
        // Releases and Privacy Practices are excluded pending the configurable-
        // billability-scope work.
        //
        // Endpoints exclusive on both ends: a note ON the due date bills, a note
        // ON or after the completion date bills. Only strictly between blocks.
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