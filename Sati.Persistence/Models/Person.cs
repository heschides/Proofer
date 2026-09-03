using Sati.Data;
using Sati.Models;

using Sati.Contracts.V1;

namespace Sati
{
    public class Person : IEventSource
    {
        // -------------------------------------------------------------------------
        // Properties
        // -------------------------------------------------------------------------

        public int Id { get; private set; }
        public int UserId { get; private set; }
        public int Revision { get; set; } = 1;
        public User? User { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime BirthDate { get; set; }
        public Gender Gender { get; set; } = Gender.Unknown;
        public string SubjectPronoun => Gender switch
        {
            Gender.Male => "he",
            Gender.Female => "she",
            Gender.NonBinary => "they",
            _ => "they"
        };
        public string ObjectPronoun => Gender switch
        {
            Gender.Male => "him",
            Gender.Female => "her",
            Gender.NonBinary => "them",
            _ => "them"
        };
        public string PossessivePronoun => Gender switch
        {
            Gender.Male => "his",
            Gender.Female => "her",
            Gender.NonBinary => "their",
            _ => "their"
        };
        public string ReflexivePronoun => Gender switch
        {
            Gender.Male => "himself",
            Gender.Female => "herself",
            Gender.NonBinary => "themselves",
            _ => "themselves"
        };
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

        // Creation-time classification for wholly synthetic consumers. Only an
        // authenticated Admin may set this at birth; ordinary profile edits cannot
        // change it. The Admin test-data deletion command requires it.
        public bool IsTestData { get; set; }

        // Set once, at creation, by CreatePerson or Rehydrate — the only two writers,
        // same as Id and UserId. Never exposed as an editable field, so there is no
        // path (accidental or otherwise) for an edit to move a record's deletion
        // window. A row that predates this column is backfilled to a fixed sentinel
        // far enough in the past that it is permanently outside any window, rather
        // than a guessed real creation date. See HANDOFF_CLIENT_DELETION_POLICY.md, A2.
        public DateTime CreatedAtUtc { get; private set; }

        // Archive state. Active people appear on caseloads and generate compliance
        // work; the others do not. See HANDOFF_CLIENT_DELETION_POLICY.md's archive
        // semantics — this is a visibility and work-generation change, not a data one.
        public PersonStatus Status { get; set; } = PersonStatus.Active;
        public string? StatusNote { get; set; }
        public DateTime? StatusChangedAtUtc { get; set; }
        public int? StatusChangedByUserId { get; set; }
        public string? MaineCareId { get; set; }
        public string? DiagnosisCode { get; set; }
        public int? PlaceOfService { get; set; }

        // The client's Evergreen ID — Maine's case management system of record.
        // Surfaces on payment/authorization forms as the "EIS #" (the forms
        // predate Evergreen and still use the legacy EIS label). Nullable: not
        // every client has one recorded yet.
        public string? EvergreenId { get; set; }

        // The client's id in the agency's Credible instance, captured when a consumer is
        // imported from a Credible export. It is the dedupe and idempotency key for import:
        // re-importing the same export must report rather than duplicate.
        //
        // Deliberately bounded rather than left to the nvarchar(max) convention that
        // EvergreenId and MaineCareId follow. Dedupe wants a filtered unique index on
        // (AgencyId, CredibleClientId) eventually, and an unbounded column cannot be indexed —
        // that is what forced Form.Type to be narrowed in a later migration. Bounding it now
        // costs nothing and leaves that index a one-step change.
        //
        // Not unique across agencies: two agencies run separate Credible instances whose ids
        // collide numerically and mean different people.
        public string? CredibleClientId { get; set; }

        // -------------------------------------------------------------------------
        // Contact & support details
        // -------------------------------------------------------------------------

        // Active Vocational Rehabilitation case (Dept. of Labor) running alongside
        // Section 17 services. Distinct from the MaineCare-funded employment
        // supports below.
        public bool OpenWithVR { get; set; }
        public string? VrCounselorName { get; set; }
        public string? VrAssistantName { get; set; }

        // HasGuardian governs field visibility only; unchecking it does not null
        // GuardianName, so a lapsed-and-resumed guardianship doesn't destroy data.
        public bool HasGuardian { get; set; }
        public string? GuardianName { get; set; }

        public string? PhoneNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        // Structured claim address. The ordinary Address remains the human-facing mailing/display
        // value; X12 must not try to parse city/state/ZIP back out of free text.
        public string? BillingStreet { get; set; }
        public string? BillingCity { get; set; }
        public string? BillingState { get; set; }
        public string? BillingZip { get; set; }
        public string? PrimaryCareProvider { get; set; }

        // Deliberately denormalized as a name string. The seam for a future
        // relational model is pre-cut: this column never renames (a future
        // HealthcareSystemId/HealthcareSystem nav gets added beside it and
        // backfilled by name match), the ComboBox binds via SelectedValuePath so
        // flipping "Name" → "Id" later touches one attribute, and the option list
        // lives as JSON on Settings so its shape can grow without breaking rows.
        public string? HealthcareSystemName { get; set; }

        // Representative-payee profile information. This is current consumer financial
        // context, not a payment instruction. A future billing notification workflow must
        // reference these fields through its own audited request/approval record rather
        // than treating a profile edit as authorization to release funds.
        public bool CaseManagerIsRepPayee { get; set; }
        public bool CaseManagerIsDhhsRepresentative { get; set; }
        public bool UsesModivcare { get; set; }
        public decimal? RepPayeeMonthlyIncome { get; set; }
        public string? RepPayeeRegularCheckRequestNeeds { get; set; }

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
        public List<PersonContact> Contacts { get; set; } = [];

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
                Waiver = waiver,
                CreatedAtUtc = DateTime.UtcNow
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

        // First-cycle generation, for the creation dialog. Annual non-reviews are in
        // force from the cycle start when that cycle is the one we are in; reviews
        // stay open. The dialog then lets the case manager correct every assumption
        // before anything is saved, which is the right place to record what actually
        // happened for a backdated admission.
        public static List<Form> GenerateFormList(DateTime effective, Settings settings)
        {
            var cycleStart = effective;
            var cycleEnd = effective.AddYears(1);
            var today = DateTime.Today;

            return Enum.GetValues<FormType>()
                .Select(type => new Form(
                    type,
                    FormDueDateCalculator.Compute(type, cycleStart, cycleEnd, settings),
                    InForceSince(type, cycleStart, cycleEnd, today)))
                .ToList();
        }

        // THE single answer to "was this document already satisfied when its cycle
        // began, and if so, since when."
        //
        // A non-review form with no prerequisite is in force from the day its cycle
        // started. Document-backed forms stay outstanding until their artifact is
        // prepared and a human attests; inventing that evidence during person creation
        // would bypass FormAttestationRules.
        //
        // Historically every annual document was treated as in force because the
        // cycle was assumed to have started BY it being signed — what an admission or renewal
        // is. Reviews are never assumed: a quarterly review is an attestation that
        // work happened, and no date can be inferred for work nobody has recorded.
        //
        // ONLY the cycle we are in now gets that assumption, and this is where that
        // limit lives. Two other kinds of cycle get nothing:
        //
        //   A cycle that has not started assumes nothing. Its documents are
        //   outstanding until someone renews them, which is how missed renewal prep
        //   gets flagged.
        //
        //   A cycle that has already ENDED assumes nothing either. Sati has no record
        //   of whether those documents were renewed on time, and a later cycle
        //   beginning proves nothing — cycles turn over on the anniversary date, not
        //   because anything was signed. Marking a closed year satisfied would assert
        //   compliance nobody attested, across every historical cycle at once. Those
        //   forms are generated outstanding instead, so an unknown reads as unknown.
        //   For a backdated admission the creation dialog is where the case manager
        //   records what actually happened.
        //
        // This returns a DATE rather than a flag on purpose. The old code paths
        // expressed the same belief two different ways — GenerateFormList stamped the
        // effective date, AddMissingFormsForCycle set a bare compliant flag with no
        // date — and the second produced the 147 rows that read complete while
        // blocking billing. One helper, one answer.
        private static DateTime? InForceSince(
            FormType type, DateTime cycleStart, DateTime cycleEnd, DateTime today) =>
            !IsReviewType(type) &&
            FormAttestationRules.PrerequisiteFor(type.ToString()) == PrerequisiteKind.None &&
            cycleStart.Date <= today.Date &&
            today.Date < cycleEnd.Date
                ? cycleStart.Date
                : null;

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

            // IsSatisfiedAsOf, not IsCompliant. A completion date that has not arrived
            // is recorded but not in force; reporting it Compliant here would put this
            // status ahead of the billing gate, which still treats the form as
            // outstanding until the date arrives.
            if (form.IsSatisfiedAsOf(referenceDate))
            {
                return form.CompletedDate!.Value > form.DueDate
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

        // Ensures forms exist for the current AND next cycle. Current-cycle forms
        // with no prerequisite are created already satisfied, dated from the cycle
        // start. Document-backed forms remain outstanding until their prerequisite
        // and attestation exist. Next-cycle annuals are satisfied during the prep window as
        // renewals are signed — if the cycle rolls over with them still open, missed
        // prep is correctly flagged. Reviews are outstanding in both cycles.
        // InForceSince owns that whole distinction.
        //
        // This is the only thing that generates forms for an ongoing caseload, so if
        // it does not run, clients silently stop having compliance records once their
        // pre-created cycles run out. It was gated off between 57af6fa and the unique
        // index because it races: the membership check below reads this person's own
        // Forms, and two callers could both pass it and both insert. The index now
        // decides that race in the database, and PersonService discards the losing
        // insert and re-reads, so the guard is no longer needed.
        //
        // Settings is unused after the form-model refactor; kept so PersonService
        // doesn't change in lockstep. Remove in a follow-up sweep.
        public bool EnsureCurrentCycleForms(DateTime today, Settings settings)
        {
            if (EffectiveDate is null)
                return false;

            var effective = EffectiveDate.Value;
            var added = false;

            // Cycle 0 starts on the effective date; cycle N starts N years later.
            // Generate every cycle from admission through the one after the current
            // one — not just the current-and-next pair this used to do. A backdated
            // admission left the years in between with no forms at all, and a form
            // that was never created cannot be enforced: BillingComplianceGate has no
            // row to fail, so an entire year silently carried no compliance
            // requirements. Absent is not the same as satisfied, and generating the
            // row is what makes the difference visible.
            //
            // Closed cycles are generated outstanding — see InForceSince — so a real
            // historical gap surfaces as an open document rather than an invented
            // completion date.
            var yearsElapsed = today.Year - effective.Year;
            if (today < effective.AddYears(yearsElapsed))
                yearsElapsed--;
            var lastIndex = Math.Max(yearsElapsed + 1, 0);

            // Skip from the OLDEST end when the range is implausible. The current and
            // next cycles — the only ones that can be worked on now — are always
            // generated, and what remains is a contiguous run ending at the next
            // cycle rather than an arbitrary subset.
            var firstIndex = Math.Max(0, lastIndex + 1 - MaxGeneratedCycles);

            for (var index = firstIndex; index <= lastIndex; index++)
            {
                var cycleStart = effective.AddYears(index);
                var cycleEnd = effective.AddYears(index + 1);
                added |= AddMissingFormsForCycle(cycleStart, cycleEnd, today, settings);
            }

            return added;
        }

        // Twenty-five annual cycles is beyond any real case-management tenure and
        // still cheap; past it, the effective date is far likelier to be a typo than
        // a record.
        private const int MaxGeneratedCycles = 25;

        // Adds only the candidates this person does not already have, keyed by
        // (type, due date) — the same key as IX_Forms_PersonId_Type_DueDate, so what
        // this refuses to add is exactly what the database would refuse to store.
        //
        // Callers hold a freshly generated form list, whose members all carry Id == 0.
        // Assigning such a list over Forms looks like replacement but is not: saves go
        // through context.People.Update on a detached graph, which marks every Id == 0
        // child Added while the stored rows — absent from the graph — survive. That is
        // how a "replace the forms" call becomes a second full set, and it is the same
        // duplicate shape FormDuplicateRepair exists to clean up.
        //
        // Existing rows always win. A generated form knows nothing that should
        // overwrite a real completion date.
        public int AddMissingForms(IEnumerable<Form> candidates)
        {
            var present = Forms
                .Select(form => (form.Type, form.DueDate.Date))
                .ToHashSet();
            var added = 0;

            foreach (var candidate in candidates)
            {
                if (!present.Add((candidate.Type, candidate.DueDate.Date)))
                    continue;
                candidate.PersonId = Id;
                Forms.Add(candidate);
                added++;
            }

            return added;
        }

        // Idempotent: only adds forms missing for the cycle. Membership routes
        // through FormBelongsToCycle — the (cycleStart, cycleEnd] convention —
        // so a form created here is visible to GetCurrentCycleForm.
        private bool AddMissingFormsForCycle(
            DateTime cycleStart, DateTime cycleEnd, DateTime today, Settings settings)
        {
            // One pass over Forms per cycle rather than one per (cycle, type). This
            // now runs across a client's whole tenure, so the old nested scan was
            // O(cycles x types x forms) on every caseload load.
            var presentForCycle = Forms
                .Where(form => FormBelongsToCycle(form.DueDate, cycleStart, cycleEnd))
                .Select(form => form.Type)
                .ToHashSet();

            var added = false;

            foreach (var type in Enum.GetValues<FormType>())
            {
                if (presentForCycle.Contains(type))
                    continue;

                // InForceSince, not a bare flag: a document created already satisfied
                // carries the date that satisfied it. This call site is where the 147
                // dateless-but-compliant rows came from.
                Forms.Add(new Form(
                                    type,
                                    FormDueDateCalculator.Compute(type, cycleStart, cycleEnd, settings),
                                    InForceSince(type, cycleStart, cycleEnd, today))
                {
                    PersonId = Id
                });
                added = true;
            }

            return added;
        }

        // Returns whether the billing compliance gate passes, and if not, every
        // reason it failed. One pass produces both, so they can't drift.
        public (bool Passed, IReadOnlyList<string> Reasons) EvaluateComplianceGate(
            DateTime today,
            FormType? beingCompleted = null,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements)
        {
            var result = Contracts.V1.BillingComplianceGate.Evaluate(
                EffectiveDate,
                Forms.Select(form => new Contracts.V1.ComplianceFormSnapshot(
                    form.Type.ToString(), form.DueDate, form.CompletedDate)),
                today,
                beingCompleted?.ToString(),
                requirements);
            return (result.Passed, result.Reasons);
        }

        // Date-keyed historical billing window. It delegates to the same shared
        // requirement mapping as the current-state gate, so enabling or disabling
        // a document affects both decisions consistently. A note ON the due date
        // bills; a note ON or after completion bills. Only the gap between blocks.
        public static bool IsBillingWindowBlocked(
            FormType formType,
            DateTime dueDate,
            DateTime? completedDate,
            DateTime serviceDate,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements) =>
            Contracts.V1.BillingComplianceGate.IsBillingWindowBlocked(
                formType.ToString(), dueDate, completedDate, serviceDate, requirements);

        // Network hydration seam for the HTTP-backed Demo client. Only identity is
        // set here; CloudContractMapper applies the safe DTO fields afterward.
        // This never accepts password, tenant, or persistence-only material.
        //
        // createdAtUtc defaults to the CLR default (0001-01-01), not DateTime.UtcNow:
        // this is a bare identity stub, and an unspecified creation date must read as
        // permanently outside the deletion window, not as "just created."
        public static Person Rehydrate(int id, int userId, DateTime createdAtUtc = default) => new()
        {
            Id = id,
            UserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        /// <summary>
        /// Moves this consumer to another case manager's caseload.
        ///
        /// <para>
        /// A named operation rather than an open setter on <see cref="UserId"/>, which stays
        /// private precisely so a consumer cannot be reassigned by an ordinary property
        /// assignment somewhere in a view model. Changing who holds a clinical record is an
        /// authorization decision, and it should be as hard to do by accident as it is to
        /// find in a diff.
        /// </para>
        ///
        /// <para>
        /// This enforces nothing about <i>who</i> may perform the move — that belongs to
        /// <c>Sati.Contracts.V1.CaseloadTransferRules</c>, which both the API and the
        /// desktop-local service consult before calling this. The entity's job is only to make
        /// the mutation deliberate.
        /// </para>
        ///
        /// <para>
        /// It deliberately does <b>not</b> touch <see cref="Revision"/>. <c>userId</c> is a
        /// tracked lifecycle field, so <c>PersonLifecycleLedger.RecordChanged</c> already sees
        /// the move, writes the version row, and bumps the revision. Incrementing here as well
        /// would advance it twice for one change and hand every other open copy of the record a
        /// stale token for a transfer that happened once.
        /// </para>
        /// </summary>
        public void TransferTo(int userId)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(userId, 0);
            UserId = userId;
        }

        public IReadOnlyList<string> EvaluateBillingWindow(
            DateTime noteDate,
            Contracts.V1.BillingComplianceRequirements requirements =
                Contracts.V1.BillingComplianceGate.DefaultRequirements) =>
            Contracts.V1.BillingComplianceGate.EvaluateBillingWindow(
                Forms.Select(form => new Contracts.V1.ComplianceFormSnapshot(
                    form.Type.ToString(), form.DueDate, form.CompletedDate)),
                noteDate,
                requirements);

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
