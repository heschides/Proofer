# Sati — Architecture Reference

*Living document. Updated during structured review sessions. Last updated: 2026-08-15,
against release 1.2.17.*

## Incident and health boundary

Unexpected desktop and authenticated API failures are grouped by agency, source, sanitized
operation, and a one-way exception-shape fingerprint. The stored envelope contains no exception
message, stack trace, request body, URL, note narrative, credential, token, or connection string.
Ordinary agency Admins can query only their agency's table. A separately provisioned
`PlatformOperator` role has an audited cross-tenant dashboard and is excluded from agency user
counts, switch-user lists, role assignment, and agency user editing.

`incident-health-v1` is a 30-day score starting at 100 and subtracting visible severity,
recurrence, and unresolved-age penalties. It deliberately does not claim crash-free-session,
availability, or background-job coverage until those denominators are collected safely. Local
JSON-line diagnostics remain workstation-only for support; the aggregated dashboard receives the
curated envelope, not those raw diagnostics.

Incident aggregation uses a bounded, keyed in-process gate plus a serializable database transaction.
The gate avoids duplicate insert races inside one process; the transaction is the authority across
separate API processes. The unique incident-key index remains the final invariant. Agency Admins can
search and filter their incident list and move a selected group among Open, Investigating, and
Resolved; status changes are audited. Alert labels are deterministic and visible: Urgent for an
unresolved critical group or score below 60, Action required below 80/three unresolved groups/high
recurrence, Watch below 95 or with any unresolved group, otherwise Normal.

**Review scope (2026-06-29 session):** Form due-date correctness pass — `FormDueDateCalculator`,
`Settings`, cycle-membership convention, form generation, backfill/bulk-completion tooling,
`CaseManagerDashboardViewModel.BuildFormRows`, and the `BoardTabConverter` NoteType fix.
Prior review (2026-06-25) covered Models, services, helpers, all ViewModel layers, EDI, DI.
**Now partially in scope:** converters (previously excluded) — see the `BoardTabConverter` note.

---

## Session Changelog — 2026-08-07

First functional Comprehensive Assessment slice:

- Added `Models/Assessments/ComprehensiveAssessment.cs`. Relational columns own identity,
  person/author, workflow status, version, and timestamps. `DocumentJson` owns the draft's
  contributor, answer, support, dissent, and identified-need aggregate.
- Added `IComprehensiveAssessmentService` / `ComprehensiveAssessmentService`, following the
  existing per-method `IDbContextFactory<SatiContext>` convention.
- Added `ComprehensiveAssessmentViewModel` and replaced the client-document placeholder with
  an eight-domain, vertically navigated workspace. It provides question-specific practical
  guidance, explicit answer dispositions, combinable support characteristics, contributors,
  dissent, needs, progress, and debounced autosave.
- Editing is currently allowed only when `SelectedPerson.UserId == CurrentUser.Id`; supervisor
  role alone does not confer authorship. Submission moves a complete draft to
  `ReadyForReview`. The supervisor queue/approval implementation remains pending.
- Added migration `20260807120000_AddComprehensiveAssessments`; startup's existing
  `Database.Migrate()` applies it. The migration updates a legacy 120-day assessment setting
  to 60 only when it still equals 120. It deliberately does not rewrite existing `Form`
  due-date rows.
- `ComprehensiveAssessmentWorkspace` currently resolves its services from `App.Services`
  because it is instantiated directly inside `ClientsView.xaml`. This reintroduces a localized
  service-locator exception and is documented debt; move workspace creation to DI/factory when
  the document-workspace composition is refactored.

## Session Changelog — 2026-06-29

The form due-date correctness pass. In dependency order:

- **`FormDueDateCalculator.Compute` now takes `Settings`** and counts backward from `cycleEnd`
  for all annual forms; Q4R = `cycleEnd − Q4RDaysBeforeAnniversary`. The "returns `cycleStart`
  for annuals / `cycleEnd−1` for Q4R" bug is gone.
- **`Settings.Q4RDaysBeforeAnniversary` added (default 5):** model initializer left bare (sibling
  pattern), seeded `= 5` in `SettingsService`, migration adds the column and runs an explicit
  `UPDATE Settings SET Q4RDaysBeforeAnniversary = 5` for the existing row. Verified in DB: `5, 120, 30`.
- **Cycle-membership convention flipped** from `[cycleStart, cycleEnd)` to `(cycleStart, cycleEnd]`,
  centralized in new `Person.FormBelongsToCycle`. Offset-0 annual forms land exactly on `cycleEnd`;
  the old exclusive end dropped them into the next cycle, hid them from `GetCurrentCycleForm`, and
  made `EnsureCurrentCycleForms` regenerate them on every load.
- **`Settings` threaded through** `GenerateFormList` → `CreatePerson` and `AddMissingFormsForCycle`
  → `EnsureCurrentCycleForms` to reach `Compute`. Those parameters are no longer dead.
- **Backfill RUN:** `FormDueDateBackfill` corrected **4,095** stored `DueDate` values (dry-run +
  count-latch two-key pattern). Recomputes each form's cycle from `EffectiveDate`, re-dates in place.
  Dry-run diff matched the production spreadsheet; **zero anomalies**.
- **Bulk-complete RUN:** `FormBulkCompletion` marked **308** non-compliant reviews (due ≤ 2026-06-10)
  complete, stamping the due date. All 308 were reviews; no annual forms touched.
- **`CaseManagerDashboardViewModel.BuildFormRows` filter changed** from `!f.IsCompliant` to
  `f.CompletedDate is null` — the task tabs show "not yet done," not "overdue." This is why the
  annual tabs were empty (their forms were compliant-but-incomplete).
- **Fixed:** the Visit `NoteType` radio was bound through `BoardTabConverter` (whose `ConvertBack`
  hardcodes `typeof(BoardTab)`), throwing `ArgumentException: 'Visit' not found` on select. Repointed
  to `EnumToBoolConverter`, matching its Contact/Other/Form siblings.

**Key clarification threaded throughout:** **`IsCompliant` means NOT OVERDUE — not complete.**
`CompletedDate is null` is the correct predicate for "needs doing." Conflating the two caused the
empty-tabs diagnosis detour; keep them distinct.

**⚠ VERIFY — operational states not confirmable from code alone:**
- `PersonService.EnableEnsureCycleFormsOnLoad` was added `false` to stop the app writing new
  duplicates mid-migration. Confirm whether it's been lifted back to `true`.
- **Duplicate-form cleanup NOT done in-session:** 372 triplicate cells across 25 real clients
  (IDs 1032–1056 less 1034, plus 1357); 347 identical triplets, 25 divergent across 5 clients
  (1033, 1043, 1047, 1050, 1056). Membership fix stops *new* duplicates; the historical ones remain.
- **Maintenance scaffolding still present?** The backfill + bulk-complete UI blocks in
  `SettingsWindow.xaml` / `SettingsViewModel.cs` and their DI registrations are temporary. The
  `FormDueDateBackfill` / `FormBulkCompletion` service classes are worth keeping as reusable
  reconciliation tools; the UI hooks are throwaway.

---

## Purpose

This document answers three questions that get harder to answer as the codebase grows:

1. **Who owns what?** Which class is the single source of truth for each piece of logic?
2. **What are the cascade points?** When X changes, what else must respond?
3. **Where are the seams?** What are the known rough edges, stale signatures, and deferred decisions?

It is not aspirational. Every claim here should be verifiable in the current code.

## Platform Direction and Architectural Boundary

This reference primarily documents the application that exists today. The target architecture
below is recorded separately so that transitional code is not mistaken for the intended cloud
design.

Sati is evolving from a WPF application that directly uses EF Core into a multi-client,
API-mediated human-services platform:

```text
WPF client             future web/mobile clients
     \                         /
              HTTPS API
                  |
      application/domain services
                  |
     EF Core + Azure SQL + background jobs
```

### Authority boundary

In the target architecture, the API is the sole authority for cloud data. It owns:

- authentication, token issuance, and session revocation;
- tenant resolution and record-level authorization;
- workflow validation and state transitions;
- database transactions and optimistic concurrency;
- audit events, document versions, and electronic attestations;
- schema migration and scheduled maintenance;
- external integrations, protected exports, and generated files.

Clients own presentation, local UI state, accessibility, and explicitly approved offline/local
capabilities. A client may calculate display-only projections, but it may not be the final authority
for permission, billability, approval, tenant ownership, or record integrity.

### Migration seam

The existing `I*Service` contracts are the primary migration seam. During transition:

1. current EF implementations move behind an ASP.NET Core API;
2. safe request/response DTOs replace EF entities at the network boundary;
3. WPF receives `Http*Service` implementations of its existing contracts where practical;
4. business rules move server-side when their result controls persistence or authorization; and
5. direct `IDbContextFactory<SatiContext>` use is removed from distributed clients.

The contracts will not be preserved blindly. Methods that accept caller-supplied `userId`, return
password-bearing `User` entities, expose tracked graphs, or combine unrelated responsibilities must
be redesigned at the boundary.

### Required platform subsystems

The cloud transition is incomplete until Sati has all of the following:

- formal tenant ownership for every protected aggregate;
- centralized tenant enforcement and cross-tenant rejection tests;
- server-side RBAC/capabilities and separation of duties;
- immutable audit events and versioned clinical/financial records;
- concurrency tokens and explicit conflict handling;
- automated unit, integration, authorization, migration, and end-to-end tests;
- health checks, structured logs, metrics, alerts, backup verification, and disaster recovery;
- controlled background jobs for reminders, reconciliation, imports, and Demo reset;
- a deployment pipeline in which clients never execute production schema migrations.

WPF remains a valid staff client. Replacing it is not a prerequisite for the API transition.
Browser and mobile clients should be added when access, field work, installation, or offline needs
justify them; they will consume the same API rather than inventing separate business rules.

### Current solution boundaries

- `Sati.csproj` is the existing WPF client and still contains local models, EF persistence,
  presentation, and local-development workflows.
- `Sati.Api` is the ASP.NET Core server boundary for cloud workflows.
- `Sati.Contracts` contains versioned network DTOs and has no WPF or EF dependency.
- `Sati.Tests` covers desktop/domain behavior and migration-model consistency.
- `Sati.Api.Tests` is cross-platform and drives the real HTTP/JWT pipeline against an isolated
  relational test database. It must not reference the WPF project.

The protected route inventory and authoritative tenant owner for every endpoint are recorded in
`API_AUTHORIZATION.md`. Every protected request passes through `ValidatedActorFilter`, which
revalidates the token's user, agency, and role against current database state. Feature endpoints
use `TenantAccess` for shared actor, caseload, supervisory, and assessment-authorship decisions.

Protected mutations use the PHI-minimized `AuditEvent` envelope described in `AUDIT_EVENTS.md`.
The mutation and audit insert share one EF Core save transaction, and application contexts reject
updates or deletes to existing audit rows. Admin audit queries are bounded and agency-scoped.
Comprehensive Assessments are the first aggregate with an explicit `Revision` concurrency token;
the API rejects stale saves/submissions with HTTP 409. Notes, AT requests (including their line
items), agency Settings, and daily per-user Scratchpads use the same revision-and-409 boundary.
Settings and Scratchpad keep attempted work visible after a conflict; Scratchpad also stops repeat
autosaves and requires an explicit reload so shutdown cannot silently discard the draft.
Claim-line duplication is prevented by a unique `NoteId` index as well as a readable conflict response.

Person profile changes additionally use a purpose-built `PersonVersion` ledger. Unlike the
PHI-minimized activity envelope, each immutable version intentionally contains a compressed full
profile snapshot and a field-level before/after change set so an authorized auditor can reconstruct
the Person over time. Person writes and their version row share one database save; a Person
`Revision` token rejects stale overwrites. Admin-only history and PDF exports verify both the Person
and its assigned user's agency and record the access in the general audit envelope. Legacy rows
receive a labeled current-state baseline when tracking first touches them; the system does not claim
to reconstruct changes made before the ledger existed.

This is a workable transition structure, not a reason for a whole-repository move. The next
structural changes should reduce real coupling: split the API endpoint monolith by feature and
make server persistence/migrations authoritative so `SatiContext` and `ApiDbContext` cannot drift.

The WPF shell exposes these server capabilities through an Admin-only dashboard. `IAdminService`
is the client seam: `CloudAdminService` calls the protected API, while `AdminService` supports the
transitional local-development database. The panel shows agency-scoped counts and activity, provides a Person history timeline, and saves the
same protected lifecycle PDF. It also exposes database/retention status and a bounded, reason-gated
audit CSV export. Retention is explicitly reported as `PolicyOnly`; `OPERATIONS.md` defines the
legal-hold gate, SQL-principal split, monitoring expectations, and remaining enforcement work.
Menu visibility is only presentation; both service implementations and all API routes independently
require Admin.

Unexpected desktop failures produce a short support reference rather than displaying stack traces.
The local JSON-lines diagnostic entry records exception type, HRESULT, target, and stack but omits
exception messages because they may contain Person names or workflow context. The Demo artifact and
preflight procedures are reproducible through `scripts/Publish-Demo.ps1`,
`scripts/Test-DemoReadiness.ps1`, and `DEMO_RUNBOOK.md`.

---

## Domain Model Overview

### Core Entities

| Entity | Namespace | Purpose |
|--------|-----------|---------|
| `Person` | `Sati` | Central domain entity. Owns compliance logic, form generation, billing window evaluation. |
| `Form` | `Sati.Models` | Represents a single compliance document for one person in one cycle. |
| `Note` | `Sati.Models` | Service note — visit, contact, form completion, or other. |
| `User` | `Sati.Models` | Staff member. Has role, supervisor chain, and agency affiliation. |
| `Agency` | `Sati.Models` | Billing/provider entity. Referenced by both `Person` and `User`. |
| `Settings` | `Sati.Models` | Agency-scoped configuration. User overrides are not currently modeled. |
| `Incentive` | `Sati.Models` | Monthly productivity snapshot. Per-user, per-month. |
| `Scratchpad` | `Sati.Models` | Daily freeform notes. Per-user, per-date. |
| `ExemptDate` | `Sati.Models` | Manual workday exclusions. Per-user. Canonical store for day exclusions. |
| `UpcomingEvent` | `Sati.Models` | Ephemeral record. Never persisted. Derived at runtime. |
| `BillingPeriod` | `Sati.Models.Billing` | Monthly billing container. Has many `ClaimLine`s. |
| `ClaimLine` | `Sati.Models.Billing` | One billable service note within a billing period. |
| `EdiGeneration` | `Sati.Models.Billing` | Exact 837P response retained for tenant- and actor-scoped idempotent replay. |
| `BillingValidationResult` | `Sati.Models.Billing` | Immutable result record from billing validation. |
| `ComprehensiveAssessment` | `Sati.Models.Assessments` | Versioned assessment envelope: ownership, workflow, timestamps, and serialized document aggregate. |
| `AssessmentDocument` | `Sati.Models.Assessments` | JSON aggregate containing contributors, keyed answers, dissent, support characteristics, and identified needs. |

### Dead Code (pending removal)
- `Event.cs` — empty class, no members, not referenced anywhere.
- `WorkdayTile.cs` — inherits `ObservableObject`, belongs in Models but is a ViewModel concept. Dead along with `SchedulerViewModel`. Both should be deleted together.

---

## Ownership Map

### Comprehensive Assessment drafts and versions

**Persistence owner: `ComprehensiveAssessmentService`.**

- `GetOrCreateDraftAsync(personId, authorUserId)` returns the author's newest Draft or
  Returned version, or creates the next version number for the person.
- `SaveDocumentAsync` serializes the entire `AssessmentDocument` aggregate to
  `DocumentJson` and refuses to modify Approved or Superseded versions.
- `SubmitForReviewAsync` checks author identity and permits only Draft/Returned to move to
  `ReadyForReview`.
- Database uniqueness on `(PersonId, Version)` prevents two records from claiming the same
  document version.
- `Revision` is an optimistic concurrency token. The client sends the revision it opened, receives
  the next revision after a successful save, and cannot overwrite a newer copy with a stale one.
- Current ownership enforcement is both UI-side (`CanEdit`) and API-side. Assessment creation,
  save, and submission require the authenticated actor to be the assigned case manager and author.
  Supervisors may read appropriate assessment context for review but cannot author in the case
  manager's place.

**Editor owner: `ComprehensiveAssessmentViewModel`.**

- A 900 ms `DispatcherTimer` debounces writes. Person changes flush the outgoing draft before
  loading the incoming consumer.
- Question definitions and practical guidance currently live in `BuildSections`; persisted
  answers use stable string keys so wording can evolve without losing saved responses.
- `AssessmentAnswerStatus.FollowUpRequired` is the default. `IsComplete` requires every question
  to be addressed and rejects any remaining follow-up-required answer.
- Support choices are a `[Flags] SupportMethod`. Setup/environment, prompting/coaching,
  hands-on assistance, another person completing an activity, and situational variation may
  coexist. `NoSupportCurrentlyNeeded` is exclusive in the ViewModel. `Varies` is complete only
  with another concrete support and explanatory detail.
- Needs are independent records inside the JSON aggregate. The current provider link is a name
  snapshot placeholder; relational consumer/provider selection is deferred.
- The current slice records general activity audit events but does not yet implement supervisor
  flags/approval, PDF/signatures, attachment storage, or immutable document versions after
  return/approval.

**Deadline owner remains `Form` + `FormDueDateCalculator`.** The assessment table does not
introduce another due-date field. `Settings.CompAssessmentDaysBeforeAnniversary` now defaults to
60. Stored `Form.DueDate` values remain authoritative and require an explicit reconciliation for
records generated under the old 120-day setting.

### Compliance State

**Single source of truth: `Form.MarkComplete(DateTime)` and `Form.Reset()`**

- `Form.IsCompliant` has `private set`. The only sanctioned writers are these two methods.
- **Semantics (important, repeatedly confused):** `IsCompliant` means **not overdue**, NOT complete.
  A form born compliant simply isn't past due yet; it flips to non-compliant when its due date
  passes without completion. `CompletedDate is null` is the predicate for "not done." A form can be
  compliant (not overdue) and incomplete (`CompletedDate is null`) at the same time — that's the
  normal state of a not-yet-due form.
- The generation constructor `Form(FormType, DateTime, bool)` is the sole birth exception —
  it sets initial compliance for in-force forms at admission where the real completion
  date is unknown.
- EF Core materializes entities via the `protected Form()` parameterless constructor,
  which does not touch `IsCompliant`.
- **Cascade rule:** Any code path that changes a form's completion state MUST go through
  `MarkComplete` or `Reset`. No direct property assignment. No exceptions.

**Resolved (ViewModel review):** No services-layer path writes `IsCompliant` directly. See the
ViewModels "compliance state writes — confirmed safe" note. `FormService.UpdateFormAsync` remains
a raw update with no guard (still a latent risk if a future caller mutates state directly).

### Form Generation

**Single source of truth: `Person.GenerateFormList(DateTime effective, Settings settings)`**

- Called by `Person.CreatePerson()` at admission; `Settings` is now threaded in and forwarded to
  `FormDueDateCalculator.Compute`.
- Default compliance at generation: annual non-review forms → `true` (in-force at
  admission); review forms → `false` (tasks to complete). Recall `true` here means "not overdue."

**Related: `Person.EnsureCurrentCycleForms(DateTime, Settings)`**
- Idempotent form generation for rollover — ensures both current and next cycle have form records.
- `Settings` is now **used** (forwarded through `AddMissingFormsForCycle` to `Compute`). The prior
  "unused parameter, safe to remove" note is obsolete.
- Called by `PersonService.GetAllPeopleAsync` on every load — **currently gated behind the temporary
  `EnableEnsureCycleFormsOnLoad` flag** (see PersonService). With correct membership `(cs, ce]` and
  corrected dates, this method is genuinely idempotent: existing forms are found, nothing is added.

### Form Due Dates

**Single source of truth: `FormDueDateCalculator` (in `Helpers/`) — corrected 2026-06-29.**

- Both `Person.GenerateFormList` and `Person.AddMissingFormsForCycle` call it, passing `Settings`.
- `UpcomingEventService` and `CaseManagerDashboardViewModel` read stored `Form.DueDate` — they do
  not recompute. The stored date is authoritative after creation.
- No shadow copies of date logic found in any service reviewed.

### Cycle Boundaries

**Today→cycle: `Person.GetCurrentCycleBoundaries(DateTime today)`.
Form→cycle: `Person.FormBelongsToCycle(dueDate, cycleStart, cycleEnd)` (new 2026-06-29).**

- `GetCurrentCycleBoundaries` keys *today* to a cycle with the half-open `[cycleStart, cycleEnd)`
  rule — today on the anniversary belongs to the *next* cycle. **Unchanged.**
- **Form-to-cycle membership is `(cycleStart, cycleEnd]`** — exclusive start, inclusive end.
  Centralized in `FormBelongsToCycle`, the single definition of membership. A form due exactly on
  the anniversary (offset-0 annuals) belongs to the cycle it *closes*, not the next one. Proven
  against real data: every stored form maps to exactly one cycle under this rule (no orphans, no
  double-counts).
- Membership call sites routed through the helper: `GetCurrentCycleForm`,
  `AddMissingFormsForCycle` (existence check), `EvaluateComplianceGate` (past-due reviews).
- **Deliberately NOT routed through it:** `CaseManagerDashboardViewModel.BuildFormRows`, a
  forward-looking `>= cycleStart` queue with no upper bound (by design). Code comment marks why —
  do not "helpfully" convert it.

### Compliance Evaluation

**Single source of truth: `Person.EvaluateComplianceGate(DateTime today, FormType? beingCompleted)`**

- Returns `(bool Passed, IReadOnlyList<string> Reasons)` — one pass produces both result and
  human-readable explanation.
- Required annual forms checked: PCP, ComprehensiveAssessment, Reclassification, SafetyPlan.
- Also checks all past-due reviews in the current cycle (via `FormBelongsToCycle`).
- `beingCompleted` exempts a form being marked complete in the same action.
- `SupervisorService` does NOT duplicate this logic — both queue methods and `ApproveNoteAsync`
  call `person.EvaluateComplianceGate(today).Passed` directly. One engine, no shadow copies.
- `ApproveNoteAsync` enforces the gate as a hard service-layer throw even if the UI pre-filters.

### Billing Window Evaluation

**Single source of truth: `Person.EvaluateBillingWindow(DateTime noteDate)`**

- Reasons as of the *note's event date*, not today — necessary for back-entered notes in a
  different cycle. Walks `Forms` directly by each form's own due date (not `GetCurrentCycleForm`).
- Gated form types: PCP, ComprehensiveAssessment, Reclassification, Q1R–Q4R.
- Safety Plan, Releases, and Privacy Practices are NOT windowed here (pending configurable-
  billability-scope work).
- Window is exclusive on both ends: a note ON the due date bills; a note ON or after completion
  date bills.
- `Person.IsBillingWindowBlocked(...)` is the shared date predicate used by both note entry and
  `ConsumerBillingLossReportService`; the Statistics report therefore cannot drift to a different
  definition of an overdue billing gap.

### Provider Directory Identity

**A `Provider` row is one agency's local record of an organization — not the organization.**

- Scope is `AgencyId`. The same organization appearing in several agencies' directories is
  correct; each holds different local contacts and notes. Uniqueness is enforced per agency only.
- `Npi` and `MaineCareProviderId` are durable identifiers, both optional, unique within an agency
  via filtered indexes. They exist so the entry can be recognised as the same organization if it
  later joins the platform as a tenant — the one part of that design that cannot be added
  retroactively.
- Enforced in `ApiEndpoints.FindDuplicateProviderAsync` and mirrored in
  `ProviderService.GuardDuplicateIdentifierAsync`, so the transitional local path does not rely on
  the API being the only caller.
- The eventual Organization registry, relationship model, and published-contact resolution are
  designed in `DECISIONS.md` and tracked in `AGENDA.md`. Reconciliation will **link**, never swap:
  no directory row is repointed and no foreign key rewritten.
- AT requests continue to snapshot vendor fields with no foreign key, so submitted requests are
  unaffected by anything that happens to a directory entry afterwards.

### Service Day and Time Overlap

**Single source of truth: `ServiceTimeline` (`Sati.Contracts.V1`)**

- Owns the loggable window (7:00 AM – 7:00 PM) and the meaning of `Note.StartTime`, which is
  stored as minutes elapsed from 7:00 AM.
- Owns the overlap rule. Scope is the **case manager and the calendar date, across the whole
  caseload** — never a single client, because two clients' notes can still double-claim one
  person's hour.
- Intervals are half-open: back-to-back notes are adjacent, not overlapping. A note never
  conflicts with the stored copy of itself.
- `OccupiesTime(status)`: Cancelled, Delayed, and Abandoned release their time; all other
  statuses hold it. Notes with no start time or no duration claim nothing.
- Referenced by `Sati.Contracts`, so the desktop client and `Sati.Api` evaluate the same code.
  `NoteEntryViewModel` uses it for the live bar and a pre-save re-check; `ApiEndpoints`
  enforces it on every note create and update (`service_time_overlap`, `service_time_window`).
  The API is the authority — the client check is feedback, not enforcement.
- Day data comes from `INoteService.GetDayScheduleAsync(userId, date)` / `GET /api/v1/notes/day`.

### Form Display Names

**Potential duplication — still needs resolution.**

Two mechanisms map `FormType` → display string: `Person.FormDisplayName(FormType)` (static switch)
and `[Description]` attributes + `EnumDescriptionConverter`. They must agree. **[DECISION NEEDED]**
which is canonical. Recommendation unchanged: prefer `[Description]`; make `FormDisplayName` a thin
wrapper or delete it.

### Upcoming Events

**Single source of truth: `UpcomingEventService` (in `Data/`)**

- `UpcomingEvent` is a pure record — no Id, never persisted. Generated fresh per load.
- Form events read stored due dates via `GetCurrentCycleForm`; do not recompute. Skips compliant
  forms. (Note: "compliant" = not overdue, so this skips not-yet-overdue forms — consistent with
  its "upcoming/late" purpose.)
- Scheduled note events: 30-day lookahead; `NoteType` drives `UpcomingEventKind`.
- Visibility window per form: `[dueDate − openBefore, dueDate + daysAfter]`, both from `Settings`.
- `OpenReview` vs `LateReview` is determined by today vs. due date — not by form type.

### Workday / Holiday Exclusions

**Single source of truth: `WorkdayHelper` (in `Helpers/`) + `ExemptDate` records**

- `ExemptDate` table (per-user) is the canonical store for manual day exclusions.
- `IncentiveService` takes exempt dates as a caller-supplied `HashSet<DateTime>` — leaky
  abstraction; the caller must load them from `ExemptDateService` and pass them in.
- `Incentive.ExcludedDatesJson` / `ExcludedDates` is orphaned — no service reads it. Migration
  rollback is safe *after* `SchedulerViewModel` is deleted (it's the last caller). Do not add callers.

---

## Maintenance Tools (added 2026-06-29 — temporary UI, keepable services)

Both mirror the same **two-key latch** safety pattern: `DryRunAsync` computes + writes a timestamped
Desktop report and arms a latch; `CommitAsync(...)` refuses unless a dry run ran *this session* and
the caller passes back the exact count (and, for bulk-complete, the exact cutoff). Transient DI, so a
fresh instance starts un-armed — a stale dry run can't authorize a commit.

### `FormDueDateBackfill` (`Sati.Data`)
- Corrects stored `Form.DueDate` from old (cycleStart-anchored) values to the current calculator's
  output. Touches **`DueDate` only** — never `IsCompliant`/`CompletedDate`.
- Buckets each form into the cycle that *produced* it, derived from `EffectiveDate` (never from the
  wrong stored date). The old-rule offsets appear **only** in `ImpliedOldCycleStart` for bucketing;
  new dates come from `FormDueDateCalculator.Compute` — one source of date-math truth.
- Anomalies (a stored date that fits no cycle) are reported and left untouched, not guessed.
- **Run 2026-06-29: 4,095 changed, 0 anomalies.** Reusable for future imports / provider swaps.

### `FormBulkCompletion` (`Sati.Data`)
- Marks every form due ≤ a cutoff and not already compliant as complete via `Form.MarkComplete`
  (stamping the due date). One-time reconciliation against an external tracking sheet.
- **Run 2026-06-29: 308 marked (all reviews), cutoff 2026-06-10 inclusive.**

---

## Services Layer

All services follow the `IDbContextFactory<SatiContext>` pattern — per-method context lifetime via
`await using`. No long-lived `_context` fields. Correct and consistent across all services.

### `PersonService`
- Owns `Person` CRUD.
- `GetAllPeopleAsync` is the primary load path: eager-loads `Notes` and `Forms`, then (when enabled)
  calls `person.EnsureCurrentCycleForms` for every person before returning; one `SaveChangesAsync`
  covers all additions.
- **⚠ TEMPORARY GUARD:** `EnableEnsureCycleFormsOnLoad` (const) gates the generate-and-save pass.
  Added `false` during the due-date migration because, while the membership convention had moved to
  `(cs, ce]` but stored dates were still old, the pass would *add a fresh duplicate for every annual
  form on every load*. With the backfill complete, this can be lifted — but confirm the duplicate
  cleanup first (a lifted pass over triplicated data is fine, but you want clean rows first). Remove
  the flag and unwrap the `if` when done.
- **Cascade rule:** Anything needing a fully-populated `Person` must go through `GetAllPeopleAsync`
  or replicate its `Include` calls (and, once re-enabled, the `EnsureCurrentCycleForms` call).

### `FormService`
- Owns `Form` updates, open-date stamping, deletion.
- `UpdateFormAsync` is a raw `context.Forms.Update(form)` with no invariant guards. If a caller ever
  mutates `form.IsCompliant` directly before calling it, the `MarkComplete`/`Reset` invariant is
  bypassed at the DB layer (EF tracks by reference). ViewModel review found no current offender, but
  the guard is still absent. `OpenFormAsync` stamps `OpenedDate` directly — fine, no invariant.

### `ComprehensiveAssessmentService`

- Owns assessment draft creation, JSON document persistence, and author submission.
- Uses `IDbContextFactory<SatiContext>` with one context per call.
- Approved and Superseded records are write-protected by `SaveDocumentAsync`.
- The local and cloud implementations derive actor identity from the signed-in session/token and
  require the assigned case manager and agency on create, save, and submission.
- Successful create/update/submit transitions are audited; `Revision` rejects stale writes.
- **Pending:** supervisor return/approval, immutable document-version history, attachment/PDF
  storage, and transactionally marking the corresponding legacy `Form` complete on approval.

### `SupervisorService`
- Owns approval/return/override for `Logged` notes. No duplicated compliance logic — delegates to
  `person.EvaluateComplianceGate`. `ApproveNoteAsync` enforces compliance as a hard throw.
- `ApproveWithOverrideAsync` stamps `ComplianceOverride = true`; the `ClaimLine` carries
  `IsComplianceException = true`.
- Supervisor scope is limited to assigned case managers in the same agency; Director/Admin scope
  may include all case managers in that agency but never another agency. Caller IDs cannot override
  the signed-in reviewer, and successful decisions are audited with the note transition.

### `NoteService`
- Owns `Note` CRUD and status transitions. `UpdateAbandonedNotesAsync` (startup sweep) moves stale
  `Pending` → `Abandoned`. `GetMonthlyNotesAsync` uses inline `DateTime.Now` twice (midnight-straddle,
  low risk). No compliance logic here.
- `GetDayScheduleAsync(userId, date)` returns every note on one case manager's calendar date across
  their caseload. It exists for the service-time overlap rule and is deliberately not person-scoped.

### `BillingService`
- Owns agency-scoped `BillingPeriod`/`ClaimLine` persistence and agency billing/EDI configuration.
  Admin authorization and tenant scope are enforced in the service/API, not by tab visibility.
- `ValidateNoteForBilling` collects approval, duration, current-compliance, historical billing-window,
  subscriber, provider, and EDI-configuration failures. Claim creation repeats validation against
  freshly loaded records before writing.
- Section 13 unit arithmetic is shared in `BillingRules`: substantive contacts up to 15 minutes
  receive one unit; longer services retain two-decimal partial 15-minute units. `ChargeAmount` is
  calculated separately from units using the agency's configured unit rate.
- Claim creation freezes subscriber/provider/submitter/payer values into a versioned JSON snapshot.
  The generator does not read mutable Person or Agency values for an existing financial record.
- Database uniqueness on service-note ID and billing-period owner/month/year makes simultaneous
  promotion/period creation fail safely; local and API paths translate repeat attempts.

### `IncentiveService`
- Owns `Incentive` CRUD and days-scheduled calc. `CalculateDaysScheduled` loops via
  `WorkdayHelper.IsAlwaysExcludedWorkday`. `GetRemainingEligibleDaysAsync` takes exempt dates as a
  parameter (leaky abstraction). `GetOrCreateAsync` self-corrects stale `DaysScheduled`/`UnitsPerDay`.

### `SettingsService`
- `LoadAsync` resolves the signed-in user's agency and seeds one settings row for that agency if
  none exists. `SaveAsync` refuses to update a row outside the current agency and rejects an older
  `Revision` rather than silently replacing a newer administrator's changes. The API mirrors this
  with `409 stale_settings`, and successful revision advancement shares the same save transaction as
  the audit event. User-specific overrides are deliberately absent until a concrete requirement exists.

### `ScratchpadService`
- Owns one daily Scratchpad per user plus append-only retrospective comments. Scratchpad content
  carries a `Revision`; saves load the current user's tracked row and reject stale copies instead
  of updating a detached object graph.
- The API returns `409 stale_scratchpad` for stale or legacy autosaves. Content-identical autosaves
  return the current revision without a database write or audit event; accepted changes and their
  PHI-minimized `scratchpad.updated` event share one save transaction.

### `AuthService`
- **DI inconsistency:** `new PasswordHasher()` directly instead of `IPasswordHasher` via DI
  (`UserService` does it correctly). Hasher non-swappable for auth without editing `AuthService`.

### `SessionService`
- Singleton; holds logged-in `User`. `AllowComplianceOverride` flag lives here.

### `ExemptDateService`
- Clean CRUD over `ExemptDate`. Strips time on `AddAsync` (`date.Date`).

### `EdiService`
- Owns 837P generation/output. Local-development files use the signed-in user's LocalApplicationData
  directory instead of a machine-global administrator-only path. Cloud responses remain API files.
- A generation attempt carries a stable GUID retry key. The exact file name and content are stored
  under a unique `(AgencyId, ActorUserId, IdempotencyKey)` boundary before the response is returned;
  an ambiguous network retry therefore replays the same 837P instead of creating another file or
  success audit event. Reusing a key for different inputs is rejected.

---

## Cross-cutting coordination primitives (2026-08-14)

Small, single-purpose types introduced by the concurrency audit. They exist so that timing
correctness is a named, testable thing rather than an ad-hoc flag in each ViewModel. See
`CONCURRENCY_AUDIT.md` for the findings that produced them.

### `LatestRequestTracker` (`Services`)
- Gives overlapping reads a monotonically increasing identity so only the newest may publish into
  shared UI state. Used where a slow response for a previous selection could otherwise overwrite the
  current one — calendar month navigation, client-note selection, and the note-entry service day.
- Rule for new screens: any load triggered by selection or navigation takes an identity before it
  starts and checks `IsCurrent` before it writes.

### `JournalSaveCoordinator` (`Services`)
- Serializes journal autosaves and account-switch flushes so overlapping cloud updates cannot
  compete for the same record.

### `AccountSwitchPolicy` / `SettingsAccessPolicy` (`Services`)
- Named decision owners for whether an account switch may proceed and who may reach agency
  configuration. Keeping these out of the ViewModels is what allows them to be unit-tested without
  a window.

### `IncidentOutbox` (`Data/Cloud`)
- Durable local queue for incident reports, retried after sign-in when a connection or process
  interruption prevented delivery. Stored under `%LOCALAPPDATA%\SatiLogica\Sati\IncidentOutbox`.

### `ConsumerSessionBoundary` (`Services/LocalAi`)
- Tracks which consumer the shared in-process model last drafted for. Sati does not trust the
  native local-inference runtime to discard conversational state between chat-completion calls, so
  a change of target consumer forces a clean model reload before the next generation. This is a
  confidentiality boundary, not an optimization: it prevents one consumer's context from
  influencing another's draft.

## Shared rule owners (`Sati.Contracts.V1`)

Types referenced by both the desktop client and `Sati.Api`, so a rule cannot be enforced two
different ways. Adding a rule that decides permission, billability, or record status belongs here
rather than in either client.

| Owner | Rule |
|---|---|
| `BillingComplianceGate` | Whether a client's paperwork permits billing, with reasons. |
| `BillingRules` | Payer-neutral unit arithmetic, charge rounding, NPI and procedure-code format. |
| `NoteWorkflow` | Which note status may become which, for the case manager, the supervisor, and the overdue sweep — and therefore which notes can reach approval and billing at all. |
| `ServiceTimeline` | The 7:00 AM – 7:00 PM service day and the no-double-claimed-minute rule. |
| `AuditCsv` | The audit export's header, column order, escaping, and spreadsheet neutralization. |
| `AtRequestPublication` | Whether an AT request is complete enough to publish, what the case manager attests to, and whether a published request may still be edited. |
| `AtRequestScreenshot` | The accepted format, downscale target, and size ceiling for a pasted item evidence clip. |
| `BillingRules.IsValidNpi` | NPI check-digit validation, shared by claim generation and provider directory entry. |
| `IncidentHealthScoring` | The versioned operational health score. |

---

## Known Rough Edges

### Data Integrity (new — pending)

- **Duplicate forms:** 372 triplicated `(person, cycle, type)` cells across 25 real clients
  (1032–1056 less 1034, plus 1357), all in future cycles. Origin: pre-fix `GetAllPeopleAsync`
  regeneration across boundary crossings under the old membership rule. 347 identical triplets
  (mechanically collapsible); 25 divergent on compliance across 5 clients (1033, 1043, 1047, 1050,
  1056) — those need Josh's per-client judgment before dedup (delete on real data). Backfill dated
  all copies correctly; dedup is the remaining step. Do this before lifting `EnableEnsureCycleFormsOnLoad`.

### Stale Signatures

- ~~`Person.CreatePerson(... Settings settings)` unused~~ — **now used** (forwards to
  `GenerateFormList`). Not stale.
- ~~`Person.EnsureCurrentCycleForms(DateTime, Settings)` unused~~ — **now used** (forwards to
  `AddMissingFormsForCycle` → `Compute`). Not stale.
- Consider retrofitting `= 120` / `= 30` onto the Comp/Reclass model initializers to kill the
  "misleading bare defaults" smell (cosmetic; the seed is the real source).

### Deferred Design Decisions

- **`Settings` is per-agency, not per-user.** Add user overrides only for a concrete requirement.
- **`HealthcareSystemName` on `Person` is denormalized by design.** Three seams pre-cut. Read the
  comments before "fixing."
- **`Incentive.ExcludedDatesJson` superseded** by `ExemptDate`; rollback pending `SchedulerViewModel`
  deletion. No new callers.
- **Configurable billability scope** deferred; hardcoded in `EvaluateBillingWindow`.
- **`ComplianceOverride` on `Note`** — fields exist, full UI not wired. Do not remove.

### Architectural Tension

- `Person` carries heavy logic weight (form generation, cycle math, membership, compliance, billing
  window, display names). Deliberate — compliance logic stays near its data — but load-bearing. Be
  cautious adding responsibilities.

---

## Helpers

All helpers are static, stateless, DI-free pure functions.

### `FormDueDateCalculator`

**Single source of truth for due-date math. Corrected 2026-06-29 — now takes `Settings`.**

Signature: `Compute(FormType type, DateTime cycleStart, DateTime cycleEnd, Settings settings)`.
Throws `ArgumentOutOfRangeException` for unhandled `FormType`.

**Two families, opposite ends of the cycle:**

| Form | Rule | Source |
|------|------|--------|
| Q1R / Q2R / Q3R | `cycleStart + 90 / 180 / 270` | literal (fixed regulatory intervals) |
| Q4R | `cycleEnd − Q4RDaysBeforeAnniversary` (5) | Settings |
| Comp Assessment | `cycleEnd − CompAssessmentDaysBeforeAnniversary` (120) | Settings |
| Reclassification | `cycleEnd − ReclassificationDaysBeforeAnniversary` (30) | Settings |
| PCP | `cycleEnd − PcpDaysBeforeAnniversary` (0) — due on anniversary | Settings |
| SafetyPlan / PrivacyPractices / Releases | `cycleEnd − *DaysBeforeAnniversary` (0) | Settings |

- Every annual form reads its **own** setting; nothing hardcoded (multi-agency requirement). A form
  set to 0 is due exactly on `cycleEnd` — which is why form membership had to move to `(cs, ce]`.
- Q1R–Q3R are intentionally *not* settings-driven (fixed intervals). The Q4R-reads-a-setting /
  Q1–Q3-don't asymmetry is deliberate and on the record.
- **Verified against the production spreadsheet — all 25 clients, zero exceptions.** Offset-0 annual
  types weren't in the spreadsheet but are confirmed by Josh as due on the effective date.
- Note: `PcpOpenDaysBefore` (90) is a *separate* setting governing when the PCP surfaces in the
  upcoming/task views — not the due date. Do not conflate.

### `FormCellStatusCalculator`
Pure timing→color for the Caseload Matrix. `(Form?, today) → FormCellStatus`. Orthogonal to the
open-form border (composed in XAML). `null` → `NotYetOpen` defensively. `IsCompliant` (i.e., not
overdue) checked first; a completed form stays `Complete` regardless of today vs. due date.

### `WorkdayHelper`
Weekday/holiday exclusion for productivity. XML comment still names dead `SchedulerViewModel`.
`IsAlwaysExcludedWorkday` assumes weekends pre-filtered. Does NOT handle `ExemptDate` (caller's job).

### `HealthcareSystemOptions`
Single source for the healthcare-system option list + invariants. `Normalize` trims, de-dupes
(Ordinal), sorts (CurrentCulture), pins "Other" last (two-comparer pattern is intentional).
`MergeDefaults` idempotent. `DefaultsByState` is the seam for non-Maine states.

### `BindingProxy`
`Freezable` binding intermediary for targets that don't inherit `DataContext` (`ContextMenu`,
`Popup`, `DataGridColumn`, etc.). Pure infrastructure.

---

## Converters (partial review — 2026-06-29)

Previously excluded as "stateless, low-risk." One live bug surfaced and was fixed:

- **`BoardTabConverter`** — bool↔`BoardTab` for the task-board pills. Its `ConvertBack` hardcodes
  `Enum.Parse(typeof(BoardTab), ...)`, so it throws on any non-`BoardTab` value.
- **`EnumToBoolConverter`** — the general-purpose sibling that parses the parameter against the bound
  property's own enum type. This is what the NoteType radios use.
- **Fixed:** the Visit NoteType radio was mistakenly bound through `BoardTabConverter` (copy-paste
  fossil), so selecting "Visit" threw `ArgumentException: 'Visit' not found`. Repointed to
  `EnumToBoolConverter`. Contact/Other/Form were already correct; the eight board pills correctly use
  `BoardTabConverter`. **Lesson for reuse:** a NoteType/value control must use `EnumToBoolConverter`;
  `BoardTabConverter` is board-tabs only.

---

## Cascade Points

*When you change X, you must also check Y.*

| If you change... | You must also check... |
|-----------------|----------------------|
| `FormType` enum (add/reorder) | `Person.GenerateFormList`, `EvaluateComplianceGate`, `EvaluateBillingWindow`, `FormDueDateCalculator`, `Person.FormDisplayName`, `[Description]` attributes, `UpcomingEventService`, any `FormType` switches in ViewModels |
| `Form.MarkComplete` / `Form.Reset` signatures | Every caller in services and ViewModels; `FormService.UpdateFormAsync` (invariant risk) |
| `Settings` anniversary-offset or deadline properties | `FormDueDateCalculator` (now **does** accept `Settings`), `Person.GetOpenDaysBefore`, `UpcomingEventService`, `SettingsService` seed, `SettingsViewModel` + XAML if user-editable |
| **Cycle-membership convention** | `Person.FormBelongsToCycle` (the one definition), and confirm `BuildFormRows` is still deliberately excluded |
| `Person.GetCurrentCycleBoundaries` logic | `GetCurrentCycleForm`, `EvaluateComplianceGate`, `EnsureCurrentCycleForms`, `AddMissingFormsForCycle`, `FormBelongsToCycle` |
| `NoteStatus` enum | Stored as `int` — append only, never reorder; `NoteService.UpdateAbandonedNotesAsync`, status filters |
| `ExemptDate` records | `WorkdayHelper`, `IncentiveService.GetRemainingEligibleDaysAsync`, productivity calc |
| Holiday flags on `Settings` | `WorkdayHelper.IsAlwaysExcludedWorkday`, `IncentiveService.CalculateDaysScheduled` |
| `BillingStatus` enum | `BillingService` submit/unbilled paths, billing UI |
| `PersonService.GetAllPeopleAsync` query | Anything needing fully-populated `Person`; don't bypass without replicating `Include`s (and `EnsureCurrentCycleForms` when re-enabled) |
| Assessment question key, status, or support flag | `BuildSections`, JSON compatibility, completion validation, PDF rendering, supervisor review, and backward-compatibility tests |
| Assessment workflow state | `ComprehensiveAssessmentService`, permissions, supervisor queue, immutable-version rules, audit events, and matching `Form` completion |
| `CompAssessmentDaysBeforeAnniversary` | `SettingsService`, `FormDueDateCalculator`, stored `Form.DueDate` reconciliation, reminders, PCP-submission gate, and billing-window tests |
| Consumer/provider association | Assessment needs, PCP authorized services, Classification, provider snapshots, authorization periods, and historical rendering |

---

## Additional Rough Edges (from services review)

- **DI inconsistency:** `AuthService` uses `new PasswordHasher()` instead of DI.
- **Leaky abstraction:** `IncentiveService.GetRemainingEligibleDaysAsync` requires caller-supplied
  exempt dates.
- **Invariant risk:** `FormService.UpdateFormAsync` — raw EF update, no compliance guard. No current
  offender, but unguarded.
- **Minor:** `NoteService.GetMonthlyNotesAsync` double `DateTime.Now`; `OnModelCreating` configures
  `Person → User` twice.

---

## ViewModels

### `ComprehensiveAssessmentViewModel`

Owns the first functional assessment editor. Stable question keys bind code-defined prompts and
guidance to JSON answers. `LoadPersonAsync` flushes the outgoing record, verifies the selected
consumer belongs to the current user's caseload, creates/loads the editable version, and applies
the aggregate to observable wrappers. Changes debounce to persistence after 900 ms.

Completion is stricter than nonblank text: every question needs an addressed status; answered
support questions need either `NoSupportCurrentlyNeeded` or a concrete support; `Varies` also
needs details; follow-up-required never completes. Submission saves first, transitions through
the service, then disables editing. Needs and contributors use write-through wrapper ViewModels.

**Known first-slice limitations:** no supervisor UI, section flags, approval transition, PDF,
signature upload, attachment store, concurrency token, save retry queue, question-definition
version, rich need validation, or runtime provider selection. The code-behind service-locator
construction is a temporary composition seam, not the preferred architecture.

### Compliance state writes — confirmed safe
Every `FormService.UpdateFormAsync` call in the ViewModel layer goes through `MarkComplete`,
`Reset`, or only touches `OpenedDate`. The `private set` invariant holds. Partial exception:
`ToggleForm` (uses the right methods but the wrong date).

### `CaseManagerDashboardViewModel`
The load-bearing ViewModel. Owns note submission, form status commands, compliance dialog routing,
productivity calc, and task board construction.

**`BuildFormRows` (updated 2026-06-29).** Task-board tabs (PCPs, Releases, Comp, Reclass, Reviews,
All) flow through here. Filter changed from `!f.IsCompliant` to **`f.CompletedDate is null`** —
"show what isn't done," not "show what's overdue." Then the existing window/overdue gate decides
visibility (`inWindow = today >= dueDate − max(openDaysBefore, DefaultLookaheadDays=90)`) and
`isOverdue` drives the red triangle. This is why the annual tabs had appeared empty: their forms
were compliant-but-incomplete, and `!IsCompliant` (i.e., overdue-only) hid them. Still uses
`>= cycleStart` with no upper bound — deliberately not the `(cs, ce]` membership helper.
*Interaction:* with duplicates still present, `OrderBy(DueDate).FirstOrDefault()` picks a copy
arbitrarily among equal dates — harmless for display, another reason to dedup.

**`ToggleForm` bug (still on AGENDA).**
```csharp
if (form.IsCompliant) form.Reset();
else form.MarkComplete(form.DueDate);  // stamps DueDate, not today/user-chosen
```
Now that the calculator is fixed, annual forms have a *future* `cycleEnd`-based due date, so toggling
one compliant stamps a completion date that hasn't happened yet — a sharper wrong than before.
**[DECISION NEEDED]** stamp `DateTime.Today` vs. prompt (recommend prompt — dialog already has the
picker). Fix before anyone toggles an annual form on a corrected client.

**Other:** `SubmitNote` correctly runs both `EvaluateComplianceGate` and `EvaluateBillingWindow`;
`_dialogIsWindowBlock` routes hold outcome. `LoadNotesForPersonAsync` is `async void` (unobservable
exceptions). `SubmitNote` catch uses `MessageBox` vs. `_validationDialog` elsewhere. `NoteStatusOptions`
uses non-generic `Enum.GetValues`.

### `ComplianceFormRow` / `ComplianceReviewViewModel`
Checkbox/date invariant correctly enforced. `Commit()` is the single write-back, via
`MarkComplete`/`Reset`. Clean.

### `FormTaskRow`
`State` computed from `CompletedDate` and `OpenedDate`, deliberately ignoring `IsCompliant` (which
defaults true/"not overdue" for annuals at admission). `State` and `IsCompliant` can diverge by
design — the board tracks *work done*, not overdue-ness.

### `SchedulerViewModel`
Dead — on AGENDA. **Only active caller of `Incentive.ExcludedDates`.** Delete it + `WorkdayTile` +
DI registration → confirm clean build → then run the `ExcludedDatesJson` migration rollback.

### `NotesWindowViewModel`
`MarkNoteLogged` calls `EvaluateComplianceGate` before transition. `SendToSupervisor` stores
`CaseManagerJustification`; supervisor queue must read it to distinguish from clean notes.

### `SettingsViewModel`
Clean. `SetHealthcareSystems` snapshots before clearing; `SaveSettingsAsync` reassigns
`HealthcareSystems` (honoring the `Settings.cs` gotcha). **Now hosts temporary maintenance regions**
(backfill + bulk-complete triggers) — banner-marked for removal. **Still does not expose the
`*DaysBeforeAnniversary` properties** in the normal settings UI; if agencies should tune Q4R/Comp/
Reclass offsets, add observable properties + XAML (the calculator already reads them from `Settings`).

### `ShellViewModel`
`IsBillingAvailable` restricted to `Admin` only — confirm intentional vs. Director/Supervisor.

### Supervisor ViewModels
`SupervisorDashboardViewModel`: N+1 load (3 calls/supervisee); dead commented line; `ClearCharts()`
nulls OxyPlot models (correct). `PendingApprovalsViewModel`: delegates to `SupervisorService` (hard
throw); `Debug.WriteLine`-only failures; `PendingNoteViewModel.IsComplianceException` hardcoded
`false`. `UserManagementViewModel`: password resets require an administrator-entered replacement
and confirmation; the API owns hashing and salting. Summary/overview VMs clean.

### Children ViewModels
`CalendarViewModel`: `ToggleExempt` fires `ExemptDateChanged` (correct cross-VM coordination);
`BuildMonths` rebuilds wholesale (correct). `ScratchpadViewModel`: 10-min auto-save from
`InitializeAsync`; explicit shutdown save; diagnostics omit scratchpad content. A save conflict
stops the timer, preserves the draft, blocks shutdown/user switching, and exposes Reload Latest;
identical autosaves are server-side no-ops.
`GuidanceViewModel`/`HelpersViewModel`: static content.

### Billing ViewModels
`BillingDashboardViewModel`: `HasLoaded` guards; fire-and-forget `LoadAsync` (unobservable).
`BillingQueueViewModel`: sequential promotion (intentional — don't parallelize);
`IsComplianceOverride` reads correctly (contrast supervisor queue's hardcoded false); profiling
`Debug.WriteLine`s. `BillingSubmissionsViewModel`: role-gated scope; **`IsTestMode = true` by default
— must be explicitly false for real submission**; `Process.Start("explorer.exe", ...)` Windows-only.
Overview/Remittances/Alerts are stubs.

---

## EDI Generator

**`EdiGenerator`** — pure static translation. Caller (`EdiService`) loads
`BillingPeriod → Lines → immutable ProfessionalClaimSnapshot`. Legacy or malformed claim lines
without that snapshot fail closed instead of silently reading today's Person/Agency values.

The generation timestamp is supplied by the caller so the persisted response, control numbers,
and filename describe one atomic attempt. Billing-period submission uses `Status` as an EF
concurrency token and treats a retry of an already-successful submission as the same success.

**Pre-live checklist (before first real submission):**
1. Replace representative Demo code/rate/payer/submitter values with the agency's verified contract,
   enrollment, and clearinghouse values.
2. Test through the clearinghouse sandbox (`isTest = true`) and receive/validate a 999 and 277CA.
3. Obtain payer-specific acceptance; implement rejection correction, transport, 835 remittance,
   reconciliation, and void/replacement workflows.
4. Complete qualified billing/compliance review. Structural generation tests are not payer certification.

**Structurally regression-tested:** fixed 106-character ISA including ISA16; ISA/GS/ST/BHT envelope;
HL hierarchy (20→22); subscriber and provider N3/N4; 2000B/2010BA/2010BB/2300/2400 nesting;
per-subscriber `LX`; separate monetary charge and units; ST-through-SE segment count; `~`/`*`/`:`
separators; one group per file. `isTest ? "T":"P"` in ISA15 flows from the UI and defaults to test.

---

## DI Registration (`App.xaml.cs`)

### Lifetime summary (deltas from prior review in **bold**)

| Registration | Lifetime | Notes |
|---|---|---|
| All domain services | Transient | Correct |
| **`FormDueDateBackfill`, `FormBulkCompletion`** | **Transient** | **Concrete types, no interface (one-shot tools). Fresh instance per settings-window open keeps the latch un-armed. Temporary UI only.** |
| `ISessionService` | Singleton | Holds logged-in user |
| `IDbContextFactory<SatiContext>` | Singleton | Per-method context via `await using` |
| `IComprehensiveAssessmentService` | Transient | Correct service lifetime; workspace currently resolves it through `App.Services` and should move to injected composition. |
| `ShellViewModel`, `ShellWindow`, dashboards, billing VMs | Singleton | Correct |
| `ScratchpadViewModel` | Transient | **Misleading** — captured by singleton `ShellViewModel`; behaves singleton. Consider `AddSingleton`. |
| `UserManagementViewModel`, `PendingApprovalsViewModel` | Transient | **Lifetime mismatch** — captured by singleton `SupervisorDashboardViewModel`; stale collections. Deliberate decision needed. |
| `NewClientViewModel` | Transient | **Misleading** — captured by singleton `CaseManagerDashboardViewModel`. |
| `SchedulerViewModel` | Transient | **Dead code** — remove with `WorkdayTile`. |
| Modal windows + VMs, `ComplianceReviewViewModel` | Transient | Correct |

### Startup sequence
Splash (3s) → Login → session set → `ShellViewModel.InitializeAsync` → `ShellWindow.Show`.
`ShutdownMode.OnExplicitShutdown`. `db.Database.Migrate()` on every startup (idempotent).
`DispatcherUnhandledException` shows the full exception in a `MessageBox` (dev-grade; add a log file
+ shorter user message before team deployment — this handler is what surfaced the LocalDB timeout
and the `BoardTabConverter` throw this session).

---

## What This Document Still Doesn't Fully Cover
- Full XAML view review (only the note-entry + task-board view and converters touched this session).
- `EdiGenerator` internals beyond the pre-live checklist.

---

## Local Case-Note Drafting (Development Slice, 2026-08-07)

`ICaseNoteFormatter` is the application boundary for assisted note drafting.
`FoundryLocalCaseNoteFormatter` is a singleton because both long-lived `NoteEntryViewModel`
instances may use it and only one multi-gigabyte model should be loaded. A semaphore serializes
inference requests. The model is initialized lazily on the first formatting request; ordinary
startup and note entry do not initialize Foundry Local.

The implementation uses the in-process `Microsoft.AI.Foundry.Local.WinML` runtime and the
configured `phi-4-mini` catalog alias. The first request may contact the model catalog and download
the selected hardware variant. Note inference occurs locally. Runtime data is explicitly rooted at
`%LOCALAPPDATA%\Sati\LocalAi`; the repository, SQL database, and note record do not contain model
weights or runtime logs.

`LocalAiOptions` is bound from the `LocalAi` section in `appsettings.json`. `Enabled=false` removes
the feature from the note-entry UI without changing XAML or DI. `AI_CASE_NOTE_RULES.md` is copied
beside the executable and forms the editable agency-policy portion of the system prompt.

The UI preserves the existing narrative and holds generated text in `AiDraftNarrative`. The user
must compare and explicitly accept it before it replaces the editable narrative; submission remains
the existing separate command. Edits to the source invalidate an outstanding draft. Numeric-token
and placeholder checks produce review warnings but are deliberately not treated as proof of factual
equivalence.

`CaseNoteFormattingRequest` also carries trusted context from the current session and selected
consumer: case-manager display name, consumer first name, and a deterministic fallback follow-up.
`NoteEntryViewModel.BuildFallbackFollowUp` selects the most recently overdue incomplete core form,
or otherwise the next upcoming incomplete core form, and supplies its stored due date. Required
opening and missing-`Follow-up:` envelopes are enforced after generation as well as in the prompt.

`IClientAiContextService` is the separate data-access boundary for client-aware drafting. Its
implementation first projects a person only when `Person.UserId == requestingUserId`; a failed
ownership check returns no context. The projection always includes the general Bio plus the
current waiver, limited care-team fields, service/employment flags, and form status. It deliberately
does not select Journal, address, phone, MaineCare ID, diagnosis code, place-of-service, or billing
fields. The Journal is therefore excluded at the SQL boundary rather than merely omitted later.

The context service adds the ten most recent non-cancelled/non-abandoned notes and up to five older
notes matched locally from meaningful terms in the rough narrative. When editing, the note being
edited is excluded. An assessment author may receive their own active Draft/Returned version;
otherwise only the latest Approved assessment is eligible. Context and per-note excerpts have
configurable size ceilings under `LocalAi`.

Historical material is wrapped as untrusted data in the model request. The system prompt forbids
following instructions found inside client records and forbids treating prior notes or assessment
answers as current-contact evidence. The draft review panel exposes the profile/service/deadline,
assessment-version, and note-ID sources used. The assembled prompt is transient and is not stored.

Visit documentation now has a separate trusted-current-facts path. `PersonContact` and
`IPersonContactService` own the consumer's live support-network directory; the Overview page edits
that reference data without loading it into the caseload query. A Visit note stores
`VisitDocumentationJson`, a note-owned snapshot containing selected attendee names/roles,
setting, appearance, participation, safety status, and independent verified-fact checkboxes.
The snapshot deliberately retains names and roles even when a profile contact is later edited or
archived. `NoteEntryViewModel` converts those explicit selections into
`StructuredVisitFacts`; the system prompt treats that block as current evidence while continuing to
treat historical client context as untrusted background. `Not documented` and `Not assessed`
choices are never translated into normal findings.

Current production gaps: no persisted source/draft/model-version audit record; no validated agency
note standard or de-identified regression corpus; no formal factual-fidelity threshold; no model
hash/version pin in the note; no cancellation control; and no security/privacy assessment of the
model catalog/cache lifecycle. The feature must remain development-only until these are resolved.
