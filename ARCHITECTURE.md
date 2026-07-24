# Sati — Architecture Reference

*Living document. Updated during structured review sessions. Last updated: 2026-06-29.*

**Review scope (2026-06-29 session):** Form due-date correctness pass — `FormDueDateCalculator`,
`Settings`, cycle-membership convention, form generation, backfill/bulk-completion tooling,
`CaseManagerDashboardViewModel.BuildFormRows`, and the `BoardTabConverter` NoteType fix.
Prior review (2026-06-25) covered Models, services, helpers, all ViewModel layers, EDI, DI.
**Now partially in scope:** converters (previously excluded) — see the `BoardTabConverter` note.

---

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
| `Settings` | `Sati.Models` | Per-install configuration. No UserId FK — not yet per-user. |
| `Incentive` | `Sati.Models` | Monthly productivity snapshot. Per-user, per-month. |
| `Scratchpad` | `Sati.Models` | Daily freeform notes. Per-user, per-date. |
| `ExemptDate` | `Sati.Models` | Manual workday exclusions. Per-user. Canonical store for day exclusions. |
| `UpcomingEvent` | `Sati.Models` | Ephemeral record. Never persisted. Derived at runtime. |
| `BillingPeriod` | `Sati.Models.Billing` | Monthly billing container. Has many `ClaimLine`s. |
| `ClaimLine` | `Sati.Models.Billing` | One billable service note within a billing period. |
| `BillingValidationResult` | `Sati.Models.Billing` | Immutable result record from billing validation. |

### Dead Code (pending removal)
- `Event.cs` — empty class, no members, not referenced anywhere.
- `WorkdayTile.cs` — inherits `ObservableObject`, belongs in Models but is a ViewModel concept. Dead along with `SchedulerViewModel`. Both should be deleted together.

---

## Ownership Map

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

### `SupervisorService`
- Owns approval/return/override for `Logged` notes. No duplicated compliance logic — delegates to
  `person.EvaluateComplianceGate`. `ApproveNoteAsync` enforces compliance as a hard throw.
- `ApproveWithOverrideAsync` stamps `ComplianceOverride = true`; the `ClaimLine` carries
  `IsComplianceException = true`.
- When `allSupervisees = true`, returns ALL CaseManager users (intentional for director views).

### `NoteService`
- Owns `Note` CRUD and status transitions. `UpdateAbandonedNotesAsync` (startup sweep) moves stale
  `Pending` → `Abandoned`. `GetMonthlyNotesAsync` uses inline `DateTime.Now` twice (midnight-straddle,
  low risk). No compliance logic here.

### `BillingService`
- Owns `BillingPeriod`/`ClaimLine` persistence. `ValidateNoteForBilling` is pure, collects all errors.
- **Bug:** error message says "Section 13 TCM" but code is T1016 (Section 17).
- `CreateClaimLineAsync` hardcodes `"T1016"`. `GetApprovedUnbilledNotesAsync` uses a `!Any(...)`
  subquery (correct; may be slow at scale).

### `IncentiveService`
- Owns `Incentive` CRUD and days-scheduled calc. `CalculateDaysScheduled` loops via
  `WorkdayHelper.IsAlwaysExcludedWorkday`. `GetRemainingEligibleDaysAsync` takes exempt dates as a
  parameter (leaky abstraction). `GetOrCreateAsync` self-corrects stale `DaysScheduled`/`UnitsPerDay`.

### `SettingsService`
- `LoadAsync` seeds defaults if no row exists — **the canonical default location** (not the model
  initializers, which are bare). Now seeds `Q4RDaysBeforeAnniversary = 5` alongside the existing
  anniversary offsets (Comp 120, Reclass 30, PCP/SafetyPlan/Privacy/Releases 0). No per-user isolation.

### `AuthService`
- **DI inconsistency:** `new PasswordHasher()` directly instead of `IPasswordHasher` via DI
  (`UserService` does it correctly). Hasher non-swappable for auth without editing `AuthService`.

### `SessionService`
- Singleton; holds logged-in `User`. `AllowComplianceOverride` flag lives here.

### `ExemptDateService`
- Clean CRUD over `ExemptDate`. Strips time on `AddAsync` (`date.Date`).

### `EdiService`
- Owns 837P generation/output. Output dir hardcoded `C:\Published\Sati\Contained\EDI`. Delegates
  content to `EdiGenerator.Generate()`.

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

- **`Settings` is per-install, not per-user.** Future work; all users share one row.
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

---

## Additional Rough Edges (from services review)

- **Bug:** `BillingService.ValidateNoteForBilling` says "Section 13 TCM"; code is T1016 (Section 17).
- **DI inconsistency:** `AuthService` uses `new PasswordHasher()` instead of DI.
- **Leaky abstraction:** `IncentiveService.GetRemainingEligibleDaysAsync` requires caller-supplied
  exempt dates.
- **Invariant risk:** `FormService.UpdateFormAsync` — raw EF update, no compliance guard. No current
  offender, but unguarded.
- **Minor:** `NoteService.GetMonthlyNotesAsync` double `DateTime.Now`; `OnModelCreating` configures
  `Person → User` twice; `EdiService` output dir hardcoded.

---

## ViewModels

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
`false`. `UserManagementViewModel`: **`ResetPassword` hardcodes `"defaultpassword"` — pre-release
security fix**; dirty-entity-on-throw; non-generic `Enum.GetValues`. Summary/overview VMs clean.

### Children ViewModels
`CalendarViewModel`: `ToggleExempt` fires `ExemptDateChanged` (correct cross-VM coordination);
`BuildMonths` rebuilds wholesale (correct). `ScratchpadViewModel`: 10-min auto-save from
`InitializeAsync`; explicit shutdown save; **`Debug.WriteLine` prints full scratchpad content —
remove before shared deployment**. `GuidanceViewModel`/`HelpersViewModel`: static content.

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
`BillingPeriod → Lines → Note → Person → Agency`; missing navigation → runtime throw.

**Pre-live checklist (before first real submission):**
1. Replace hardcoded `PER04` phone (`"3609787000"`) with the agency's contact number (source from `Agency`).
2. Add agency field validation (street, city, state, zip, tax ID) to pre-submission check.
3. Confirm `CLM02`/`SV102` monetary-vs-unit interpretation with Maine Medicaid companion guide
   (may require a `Rate`/`ChargeAmount` on `ClaimLine`).
4. Fix SE01 segment count to count `~`, not newlines.
5. Fix the "Section 13 TCM" → Section 17 / T1016 message.
6. Test through Office Ally sandbox (`isTest = true`) and verify 999 acknowledgment.

**Verified-correct structure:** ISA/GS/ST/BHT envelope; HL hierarchy (20→22); 2000B/2010BA/2010BB/
2300/2400 nesting; per-subscriber `LX`; `~`/`*`/`:` separators; one group per file. `isTest ? "T":"P"`
in ISA15 flows from `BillingSubmissionsViewModel.IsTestMode` (defaults true — safe).

---

## DI Registration (`App.xaml.cs`)

### Lifetime summary (deltas from prior review in **bold**)

| Registration | Lifetime | Notes |
|---|---|---|
| All domain services | Transient | Correct |
| **`FormDueDateBackfill`, `FormBulkCompletion`** | **Transient** | **Concrete types, no interface (one-shot tools). Fresh instance per settings-window open keeps the latch un-armed. Temporary UI only.** |
| `ISessionService` | Singleton | Holds logged-in user |
| `IDbContextFactory<SatiContext>` | Singleton | Per-method context via `await using` |
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
