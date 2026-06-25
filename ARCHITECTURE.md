# Sati — Architecture Reference

*Living document. Updated during structured review sessions. Last updated: 2026-06-25.*

**Review scope (2026-06-25):** Models, services, helpers, all ViewModel layers
(CaseManager, Supervisor, Billing, Children), EDI generator, DI registration.
**Not reviewed:** XAML views and converters. Views are excluded as they carry
no domain logic; converters are stateless and low-risk.

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
- The generation constructor `Form(FormType, DateTime, bool)` is the sole birth exception —
  it sets initial compliance for in-force forms at admission where the real completion
  date is unknown.
- EF Core materializes entities via the `protected Form()` parameterless constructor,
  which does not touch `IsCompliant`.
- **Cascade rule:** Any code path that changes a form's completion state MUST go through
  `MarkComplete` or `Reset`. No direct property assignment. No exceptions.

**[OPEN QUESTION]** Does anything in the services layer write `IsCompliant` directly,
bypassing these methods? To be verified when services are reviewed.

### Form Generation

**Single source of truth: `Person.GenerateFormList(DateTime effective)`**

- Called by `Person.CreatePerson()` at admission.
- Uses `FormDueDateCalculator.Compute()` for all due date math — `Person` does not
  calculate dates itself.
- Default compliance at generation: annual non-review forms → `true` (in-force at
  admission); review forms → `false` (tasks to complete).

**Related: `Person.EnsureCurrentCycleForms(DateTime, Settings)`**
- Idempotent form generation for rollover — ensures both current and next cycle have
  form records.
- Called by `PersonService.GetAllPeopleAsync` on every load. If any person gains new
  form records, the context saves once after the full loop — not per-person.
- `Settings` parameter is currently unused on this method (noted in code comment).
  Safe to remove in a follow-up sweep; `PersonService` is the only caller.

### Form Due Dates

**Single source of truth: `FormDueDateCalculator` (in `Helpers/`)**

- Both `Person.GenerateFormList` and `Person.AddMissingFormsForCycle` call it.
- `UpcomingEventService` reads due dates from stored `Form` records via
  `GetCurrentCycleForm` — it does not recompute them. This is correct: the calculator
  runs at form creation, and the stored date is the source of truth thereafter.
- No shadow copies of date logic found in any service reviewed.

### Cycle Boundaries

**Single source of truth: `Person.GetCurrentCycleBoundaries(DateTime today)`**

- Half-open convention: `[cycleStart, cycleEnd)`. Today belongs to a cycle if
  `cycleStart <= today < cycleEnd`. The anniversary date belongs to the *next* cycle.
- All cycle-aware logic (`GetCurrentCycleForm`, `EvaluateComplianceGate`,
  `EnsureCurrentCycleForms`) calls this method. No inline cycle math elsewhere on `Person`.

### Compliance Evaluation

**Single source of truth: `Person.EvaluateComplianceGate(DateTime today, FormType? beingCompleted)`**

- Returns `(bool Passed, IReadOnlyList<string> Reasons)` — one pass through the logic
  produces both the pass/fail result and the human-readable explanation.
- Required annual forms checked: PCP, ComprehensiveAssessment, Reclassification, SafetyPlan.
- Also checks all past-due reviews in the current cycle.
- `beingCompleted` parameter exempts a form being marked complete in the same action —
  prevents the gate from blocking a form from completing itself.
- `SupervisorService` does NOT duplicate this logic. Both queue methods
  (`GetPendingNotesAsync`, `GetNonCompliantNotesAsync`) and `ApproveNoteAsync` call
  `person.EvaluateComplianceGate(today).Passed` directly. One engine, no shadow copies.
- `ApproveNoteAsync` enforces the gate as a hard service-layer guard even if the UI
  pre-filters — it throws if compliance is not met, preventing bypass by direct call.

### Billing Window Evaluation

**Single source of truth: `Person.EvaluateBillingWindow(DateTime noteDate)`**

- Distinct from `EvaluateComplianceGate`. That method reasons as of *today*.
  This one reasons as of the *note's event date* — necessary for back-entered notes
  that may fall in a different cycle.
- Gated form types: PCP, ComprehensiveAssessment, Reclassification, Q1R–Q4R.
- Safety Plan, Releases, and Privacy Practices are NOT windowed here (pending
  configurable-billability-scope work).
- Window is exclusive on both ends: a note ON the due date bills; a note ON or after
  completion date bills.

### Form Display Names

**Potential duplication — needs resolution.**

There are currently two mechanisms that map `FormType` to a display string:

1. `Person.FormDisplayName(FormType)` — static method, switch expression.
2. `[Description]` attributes on `FormType` enum values + `EnumDescriptionConverter`.

These must agree at all times. If they diverge, the same form type will display
differently in different parts of the UI. One should be designated canonical and the
other should call it or be removed.

**[DECISION NEEDED]** Which is canonical? Recommendation: the `[Description]` attributes
are the more idiomatic .NET approach and require no extra call. `FormDisplayName` could
either be deleted (callers switch to the converter) or reimplemented to read the
`[Description]` attribute, making it a thin wrapper rather than a second list.

### Upcoming Events

**Single source of truth: `UpcomingEventService` (in `Data/`)**

- `UpcomingEvent` is a pure record — no Id, never persisted.
- Generated fresh on every load via `GenerateEvents(people, settings, asOf?)`.
- Form events: reads due dates from stored `Form` records via `GetCurrentCycleForm`.
  Does not recompute dates. Skips compliant forms entirely.
- Scheduled note events: 30-day lookahead window. `NoteType` drives the `UpcomingEventKind`.
- Visibility window per form: `[dueDate − openBefore, dueDate + daysAfter]`. Both
  endpoints come from `Settings`. Forms outside this window are silently excluded.
- `UpcomingEventKind.OpenReview` vs `LateReview` is determined by whether today
  is before or after the due date — not by form type.

### Workday / Holiday Exclusions

**Single source of truth: `WorkdayHelper` (in `Helpers/`) + `ExemptDate` records**

- `ExemptDate` table (per-user) is the canonical store for manual day exclusions.
- `IncentiveService` takes exempt dates as a caller-supplied `HashSet<DateTime>` in
  `GetRemainingEligibleDaysAsync` — the caller is responsible for loading them from
  `ExemptDateService` and passing them in. This is a leaky abstraction: if a second
  caller ever handles this differently, results will diverge.
- `Incentive.ExcludedDatesJson` / `ExcludedDates` is fully orphaned — no service reads
  it. The migration rollback on the AGENDA is safe to run. Do not add new callers.

---

## Services Layer

All services follow the `IDbContextFactory<SatiContext>` pattern — per-method context
lifetime via `await using`. No long-lived `_context` fields. This is correct and
consistent across all services reviewed.

### `PersonService`
- Owns persistence for `Person` CRUD.
- `GetAllPeopleAsync` is the primary load path: eager-loads `Notes` and `Forms`,
  then calls `person.EnsureCurrentCycleForms` for every person before returning.
  This means form rollover happens silently on every caseload load. One `SaveChangesAsync`
  call covers all new forms if any were added.
- **Cascade rule:** Anything that needs a fully-populated `Person` (with forms and notes)
  must go through `GetAllPeopleAsync`, not a raw context query, or it must replicate
  the `Include` calls and `EnsureCurrentCycleForms` call.

### `FormService`
- Owns persistence for `Form` updates, open-date stamping, and deletion.
- `UpdateFormAsync` is a raw `context.Forms.Update(form)` with no invariant guards.
  **This is a compliance risk.** If any caller mutates `form.IsCompliant` directly on
  the entity before calling `UpdateFormAsync`, the `MarkComplete`/`Reset` invariant is
  bypassed at the DB layer even though `IsCompliant` has `private set` — because EF
  tracks the object by reference and will persist whatever state it's in.
- **[OPEN QUESTION — PRIORITY]** Do any ViewModels mutate form state directly before
  calling `FormService.UpdateFormAsync`? Must verify in ViewModel review. If yes,
  `UpdateFormAsync` needs a guard that asserts `IsCompliant` and `CompletedDate` agree.
- `OpenFormAsync` stamps `OpenedDate = DateTime.Today` directly — this is fine, `OpenedDate`
  has no invariant.

### `SupervisorService`
- Owns the approval/return/override workflow for `Logged` notes.
- Does not duplicate compliance logic — delegates entirely to `person.EvaluateComplianceGate`.
- `ApproveNoteAsync` enforces compliance as a hard throw, not just a UI filter.
- `ApproveWithOverrideAsync` stamps `ComplianceOverride = true` and records reason +
  approver. The resulting `ClaimLine` carries `IsComplianceException = true`.
- `GetLoggedNotesAsync` (private) is the shared base query for both queue methods.
  Loads `Person` and `Forms` so compliance can be evaluated in memory.
- **Note:** When `allSupervisees = true`, the query returns ALL CaseManager-role users,
  not just supervisees of the given supervisor. This is intentional for director-level
  views but could be surprising if called with a non-director supervisorId.

### `NoteService`
- Owns persistence for `Note` CRUD and status transitions.
- `UpdateAbandonedNotesAsync` is the abandonment sweep — moves `Pending` notes older
  than the threshold to `Abandoned`. Called on startup.
- `GetMonthlyNotesAsync` uses inline `DateTime.Now` twice. Could straddle midnight
  in theory. Low risk, worth tightening.
- No compliance logic here. Status transitions happen in ViewModels; `NoteService`
  persists whatever status the caller sets.

### `BillingService`
- Owns `BillingPeriod` and `ClaimLine` persistence.
- `ValidateNoteForBilling` is a pure validation method (no DB calls) — returns
  `BillingValidationResult` with all errors collected, not just the first.
- **Bug:** `ValidateNoteForBilling` error message says "Section 13 TCM" but the
  procedure code is T1016 (Section 17). Wrong if surfaced to a user or auditor.
- `CreateClaimLineAsync` hardcodes procedure code `"T1016"`. If this ever changes
  per client or service type, it will need to be parameterized.
- `GetApprovedUnbilledNotesAsync` uses a subquery (`!context.ClaimLines.Any(...)`)
  to exclude already-billed notes. This is correct but may be slow at scale; worth
  revisiting if the ClaimLines table grows large.

### `IncentiveService`
- Owns `Incentive` CRUD and days-scheduled calculation.
- `CalculateDaysScheduled` is a private method that loops over calendar days, calling
  `WorkdayHelper.IsAlwaysExcludedWorkday`. This duplicates the loop structure that
  `WorkdayHelper` should own. If holiday exclusion logic ever changes in `WorkdayHelper`,
  this service's output will stay correct — but if someone adds a new exclusion category
  here without updating `WorkdayHelper`, they'll diverge.
- `GetRemainingEligibleDaysAsync` takes exempt dates as a `HashSet<DateTime>` parameter.
  The caller must fetch them from `ExemptDateService`. This is a leaky abstraction —
  the service cannot be called correctly without knowing to supply exempt dates.
- `GetOrCreateAsync` self-corrects stale `DaysScheduled` and `UnitsPerDay` values on
  every load. Intentional resilience against stale rows from old scheduler code.

### `SettingsService`
- `LoadAsync` seeds defaults if no settings row exists. Default values are hardcoded
  here — the only place they live. If defaults need to change, this is where to look.
- No per-user isolation. All users share one settings row.

### `AuthService`
- **DI inconsistency:** Instantiates `new PasswordHasher()` directly instead of taking
  `IPasswordHasher` through DI. `UserService` correctly takes `IPasswordHasher` via DI.
  These are inconsistent. `AuthService` silently bypasses the DI registration, making
  the hasher implementation non-swappable for auth without modifying `AuthService` directly.

### `SessionService`
- Holds the logged-in `User` for the lifetime of the application (singleton).
- `AllowComplianceOverride` flag lives here — set by supervisor-role UI, read by
  case manager ViewModels to unlock override paths.

### `ExemptDateService`
- Clean, simple CRUD over `ExemptDate` records.
- Strips time component on `AddAsync` (`date.Date`) — correct, prevents duplicate
  records from time-of-day differences.

### `EdiService`
- Owns 837P file generation and output.
- Output directory is hardcoded: `C:\Published\Sati\Contained\EDI`. Not configurable
  via settings. Worth moving to settings or appsettings if path needs to vary per install.
- File naming follows Office Ally companion guide (OATEST marker for test files).
- Delegates actual EDI content generation to `EdiGenerator.Generate()` — service only
  handles DB retrieval and file I/O.

---



*When you change X, you must also check Y.*

| If you change... | You must also check... |
|-----------------|----------------------|
| `FormType` enum (add/reorder values) | `Person.GenerateFormList`, `Person.EvaluateComplianceGate`, `Person.EvaluateBillingWindow`, `FormDueDateCalculator`, `FormDisplayName`, `[Description]` attributes, any switch expressions over `FormType` in ViewModels |
| `Form.MarkComplete` / `Form.Reset` signatures | Every caller in services and ViewModels |
| `Settings` form deadline properties | `FormDueDateCalculator`, `Person.GetOpenDaysBefore`, `UpcomingEventService` |
| `Person.GetCurrentCycleBoundaries` logic | `GetCurrentCycleForm`, `EvaluateComplianceGate`, `EnsureCurrentCycleForms`, `AddMissingFormsForCycle` — all depend on cycle math |
| `NoteStatus` enum (add/reorder values) | Stored as `int` — append only, never reorder |
| `ExemptDate` records | `WorkdayHelper`, scheduler, incentive calculation — anything that reasons about billable days |

---

## Known Rough Edges

### Stale Signatures (safe to clean up, no behavior change)

- `Person.CreatePerson(... Settings settings)` — `Settings` parameter is unused.
  Remove it and update `NewClientViewModel` call site.
- `Person.EnsureCurrentCycleForms(DateTime, Settings)` — `Settings` parameter is unused.
  Remove it and update caller(s) in `PersonService`.

### Deferred Design Decisions

- **`Settings` is per-install, not per-user.** The AGENDA notes this as future work.
  Until it's done, all users share one settings row. No FK exists yet.
- **`HealthcareSystemName` on `Person` is denormalized by design.** Three seams are
  pre-cut for future relational migration (property name, ComboBox binding path,
  JSON shape on Settings). Do not "fix" this without reading the comments in `Person.cs`.
- **`Incentive.ExcludedDatesJson` is superseded** by `ExemptDate` but the migration
  rollback hasn't run. Do not add new callers of `Incentive.ExcludedDates`.
- **Configurable billability scope** (which form types gate billing) is deferred.
  Currently hardcoded in `Person.EvaluateBillingWindow`. Safety Plan, Releases, and
  Privacy Practices are excluded pending this work.
- **`ComplianceOverride` on `Note`** — override path exists with fields for reason and
  approver. Full UI not yet wired. Do not remove these fields.

### Architectural Tension

- `Person` carries significant logic weight: form generation, cycle math, compliance
  evaluation, billing window evaluation, settings interpretation, display names.
  This is a deliberate choice — compliance logic stays close to the data it reasons
  about — but it means `Person` is load-bearing. Be cautious about adding more
  responsibilities here without considering whether a dedicated service is more appropriate.

---

## What This Document Does Not Cover Yet

- ViewModels (ownership of UI state, command wiring, dialog patterns)
- DI registration and lifetime discipline
- EDI generator (`EdiGenerator`)

*These sections will be added as the review continues.*

---

## Helpers

All helpers are static classes with no state and no DI dependencies. They are pure
functions: same input always produces same output, no side effects.

### `FormDueDateCalculator`

**Single source of truth for form due-date math — currently INCORRECT for annual forms.**

- Called by `Person.GenerateFormList` and `Person.AddMissingFormsForCycle`.
- `UpcomingEventService` reads due dates from stored `Form` records — it does not
  call this calculator. The stored date is authoritative after creation.
- Throws `ArgumentOutOfRangeException` for unhandled `FormType` values — a new enum
  value without a matching case is a runtime throw, not a silent wrong answer.

**Confirmed correct rules — verified against production spreadsheet, consistent across all 25 clients:**

| Form | Rule | Offset |
|------|------|--------|
| Q1R | cycleStart + 90d | +90 |
| Q2R | cycleStart + 180d | +180 |
| Q3R | cycleStart + 270d | +270 |
| Q4R | cycleEnd − 5d | −5 |
| Comp Assessment | cycleEnd − 120d | −120 |
| PCP | cycleEnd − 0d (due on anniversary; 90d open window is separate via PcpOpenDaysBefore) | 0 |
| Reclassification | cycleEnd − 30d | −30 |
| SafetyPlan, PrivacyPractices, all Releases | **TBD — not in spreadsheet** | unknown |

**What the current code gets wrong:**
- All annual non-review forms currently return `cycleStart`. The correct value is
  `cycleEnd − N` using `Settings.*DaysBeforeAnniversary`.
- Q4R currently returns `cycleEnd.AddDays(-1)`. The correct value is `cycleEnd.AddDays(-5)`.
- `CompAssessmentDaysBeforeAnniversary` default is `120`. ✅ correct.
- `ReclassificationDaysBeforeAnniversary` default is `30`. ✅ correct.
- `PcpDaysBeforeAnniversary` default is `0`. ✅ correct — PCP is due on the anniversary.
  The 90-day figure is `PcpOpenDaysBefore`, a separate setting governing when the PCP
  appears in the upcoming events panel. These are different concepts; do not conflate them.
- SafetyPlan, PrivacyPractices, all Releases default to `0`. ✅ correct — due on anniversary.

**What must change before this is correct:**
1. `FormDueDateCalculator.Compute` must accept `Settings` and compute
   `cycleEnd.AddDays(-settings.*DaysBeforeAnniversary)` for all annual non-review forms.
2. Q4R offset must change from `−1` to `cycleEnd.AddDays(-settings.Q4RDaysBeforeAnniversary)`.
3. `Q4RDaysBeforeAnniversary` must be added to `Settings` with a default of `5`.
4. All other `*DaysBeforeAnniversary` defaults are already correct.
5. **Existing clients have incorrect stored due dates for all annual forms.**
   After the calculator is fixed, a backfill is needed. The production spreadsheet is
   the ground truth for what those dates should be.

### `FormCellStatusCalculator`

**Pure timing → color mapping for the Caseload Matrix.**

- Input: `(Form? form, DateTime today)`. Output: `FormCellStatus`.
- Intentionally orthogonal to the open-form indicator (the border on matrix cells).
  A cell can be `DueNextMonth` AND have an open border simultaneously — these are
  separate visual layers composed in XAML, not encoded here.
- `null` form returns `NotYetOpen` defensively — should not occur once
  `EnsureCurrentCycleForms` has run, but the calculator doesn't assume that.
- `IsCompliant` is checked first. A completed form stays `Complete` regardless of
  where today falls relative to the original due date.

### `WorkdayHelper`

**Weekday and holiday exclusion logic for productivity calculation.**

- Called by `IncentiveService.CalculateDaysScheduled` and `GetRemainingEligibleDaysAsync`.
- XML doc comment still references `SchedulerViewModel` as a caller — that is dead code.
  Comment should be updated when `SchedulerViewModel` is deleted.
- `IsAlwaysExcludedWorkday` assumes the caller has already filtered Saturday/Sunday.
  The method does not re-check for weekends. All callers must filter before calling.
- Holiday logic uses `IsNthWeekday` (nth occurrence of a weekday in a month) and
  `IsLastMonday`. Both helpers are private and correct.
- Does NOT handle `ExemptDate` records. Manual exempt dates are the caller's
  responsibility (see leaky abstraction note under `IncentiveService`).

### `HealthcareSystemOptions`

**Single source of truth for the healthcare system option list and its invariants.**

- Both the settings window (edit) and client combobox (consume) route through here.
- `Normalize` enforces: trim, de-duplicate (OrdinalIgnoreCase for identity),
  alpha sort (CurrentCultureIgnoreCase for display), "Other" pinned last.
- The two-comparer pattern is intentional and documented in code — OrdinalIgnoreCase
  for identity, CurrentCultureIgnoreCase for sort. Do not collapse them.
- `MergeDefaults` is idempotent — running it twice produces the same list.
- Maine is the only state with defaults today. `DefaultsByState` is the seam for adding
  others without touching callers.
- `HealthcareSystemOption(string Name)` record is the ComboBox binding vehicle.
  When systems become relational, it gains an `Id` and `SelectedValuePath` flips from
  `"Name"` to `"Id"` — the third seam promised on `Person.HealthcareSystemName`.

### `BindingProxy`

**WPF binding intermediary for targets that don't inherit `DataContext`.**

- Used for `RowDefinition`, `ColumnDefinition`, `ContextMenu`, `Popup`, `DataGridColumn`.
- Inherits `Freezable` so it participates in the element tree's inheritance context
  and receives `DataContext` through it — a plain `DependencyObject` would not.
- No logic. Pure infrastructure.

---

## Cascade Points

*When you change X, you must also check Y.*

| If you change... | You must also check... |
|-----------------|----------------------|
| `FormType` enum (add/reorder values) | `Person.GenerateFormList`, `Person.EvaluateComplianceGate`, `Person.EvaluateBillingWindow`, `FormDueDateCalculator`, `Person.FormDisplayName`, `[Description]` attributes on enum, `UpcomingEventService` formMeta table, any switch expressions over `FormType` in ViewModels |
| `Form.MarkComplete` / `Form.Reset` signatures | Every ViewModel caller; `FormService.UpdateFormAsync` (see invariant risk below) |
| `Settings` form deadline properties | `FormDueDateCalculator` (must accept Settings — currently does not), `Person.GetOpenDaysBefore`, `UpcomingEventService` formMeta table |
| `Person.GetCurrentCycleBoundaries` logic | `GetCurrentCycleForm`, `EvaluateComplianceGate`, `EnsureCurrentCycleForms`, `AddMissingFormsForCycle` — all depend on cycle math |
| `NoteStatus` enum (add/reorder values) | Stored as `int` — append only, never reorder. Also check `NoteService.UpdateAbandonedNotesAsync` and any ViewModel status filters |
| `ExemptDate` records | `WorkdayHelper`, `IncentiveService.GetRemainingEligibleDaysAsync` (caller must supply them), productivity calculation |
| Holiday exclusion flags on `Settings` | `WorkdayHelper.IsAlwaysExcludedWorkday`, `IncentiveService.CalculateDaysScheduled` |
| `BillingStatus` enum | `BillingService.SubmitBillingPeriodAsync`, `BillingService.GetUnbilledClaimLinesAsync`, billing UI |
| `PersonService.GetAllPeopleAsync` query | Any code that depends on fully-populated `Person` objects with forms and notes. Do not bypass without replicating `Include` calls and `EnsureCurrentCycleForms` |

---

## Additional Rough Edges (from services review)

### Bugs

- `BillingService.ValidateNoteForBilling` error message says "Section 13 TCM" but
  the procedure code is T1016 (Section 17 TCM). Wrong if surfaced to a user or auditor.

### DI Inconsistency

- `AuthService` instantiates `new PasswordHasher()` directly instead of taking
  `IPasswordHasher` through DI. `UserService` correctly uses DI. The hasher is
  non-swappable for the auth path without modifying `AuthService` directly.

### Leaky Abstraction

- `IncentiveService.GetRemainingEligibleDaysAsync` requires the caller to supply
  exempt dates as a `HashSet<DateTime>`. The service cannot be called correctly without
  knowing to also call `ExemptDateService` first. Consider encapsulating this internally.

### Invariant Risk (priority)

- `FormService.UpdateFormAsync` is a raw EF update with no compliance invariant guards.
  If any ViewModel sets `IsCompliant` on a form entity before calling this method, the
  `MarkComplete`/`Reset` invariant is bypassed at the DB layer despite `private set`.
  EF tracks the object by reference and will persist whatever state it finds.
  **[OPEN QUESTION]** Do any ViewModels do this? Must verify in ViewModel review.

### Minor

- `NoteService.GetMonthlyNotesAsync` calls `DateTime.Now` twice inline. Could
  theoretically straddle midnight. Low risk; worth tightening to a single capture.
- `SatiContext.OnModelCreating` configures the `Person → User` relationship twice —
  once without a navigation property name, once with `p => p.User`. EF may silently
  merge these; worth cleaning up to remove ambiguity.
- `EdiService` output directory (`C:\Published\Sati\Contained\EDI`) is hardcoded.
  Not configurable. Acceptable for single-install use; must change before multi-user
  or MSIX deployment.

---

## ViewModels

### Compliance state writes — confirmed safe

Every call to `FormService.UpdateFormAsync` in the ViewModel layer goes through
`Form.MarkComplete()`, `Form.Reset()`, or only touches `OpenedDate`. The `private set`
invariant on `IsCompliant` is holding throughout. The one partial exception is
`ToggleForm` (see below), which uses the correct methods but passes the wrong date.

### `CaseManagerDashboardViewModel`

The load-bearing ViewModel. Owns note submission, form status commands, compliance
dialog routing, productivity calculation, and task board construction.

**`ToggleForm` bug (on AGENDA — confirmed):**
```csharp
if (form.IsCompliant)
    form.Reset();
else
    form.MarkComplete(form.DueDate);  // stamps DueDate, not today or a user-chosen date
```
When toggling a non-compliant form to compliant, it stamps `form.DueDate` as the
completion date. This is semantically wrong — it implies the form was completed on
time regardless of when the toggle happened. Combined with the `FormDueDateCalculator`
bug (annual forms have `cycleStart` as their due date), this currently stamps the
*previous* anniversary as the completion date for annual forms.

**[DECISION NEEDED]** What should `ToggleForm` stamp as completion date?
- `DateTime.Today` — simple, assumes the toggle means "done today"
- A date prompt — same picker pattern as `ComplianceFormRow`, lets user enter actual date
- Recommendation: date prompt. The compliance review dialog already has this UX.
  A manual toggle without a real date is the same problem as auto-stamping DueDate.

**`MarkFormOpened`** calls `row.Form.Reset()` then sets `OpenedDate` directly. `OpenedDate`
has no invariant, so direct assignment is fine. `Reset()` is called first, correctly.

**`MarkFormCompleted`** and **`MarkFormCompleteAsync`** both call `form.MarkComplete(DateTime.Today)`.
Clean.

**`SubmitNote`** correctly runs both compliance gate (`EvaluateComplianceGate`) and billing
window check (`EvaluateBillingWindow`). `_dialogIsWindowBlock` flag correctly routes
the hold outcome to `ComplianceBlocked` vs `HeldForCompliance`.

**`LoadNotesForPersonAsync` is `async void`** — called from `OnSelectedPersonChanged`
which cannot be async. Exceptions are unobservable. Low risk (simple read), but if
the DB call ever fails, it fails silently. Worth adding error logging at minimum.

**`SubmitNote` uses `MessageBox` directly** for the catch-block error message, while
all validation errors use `_validationDialog`. Minor inconsistency.

**`NoteStatusOptions`** uses the non-generic `Enum.GetValues(typeof(NoteStatus))`.
Style preference per CLAUDE.md is `Enum.GetValues<NoteStatus>()`.

### `ComplianceFormRow` and `ComplianceReviewViewModel`

The checkbox/date invariant is correctly enforced. `OnIsCompliantChanged` ensures
compliant implies a date, not-compliant implies none. `Commit()` is the single
write-back point, calling `MarkComplete`/`Reset` exclusively. Clean.

### `FormTaskRow`

`State` is computed from `CompletedDate` and `OpenedDate`, deliberately ignoring
`IsCompliant`. Correct reasoning: `IsCompliant` defaults true for annual docs at
admission, so using it would paint untouched forms green. This means `State` and
`IsCompliant` can diverge — a form can be `IsCompliant = true` but `State = NotStarted`
because it was never actually worked. This is intentional and correct for the task
board's purpose (tracking work done), but is a known semantic split.

### `SchedulerViewModel`

Confirmed dead code — on AGENDA for deletion. However, it is the **only active caller
of `Incentive.ExcludedDates`** (`ToggleTile` reads and writes it directly).
**The `ExcludedDatesJson` migration rollback is not safe until `SchedulerViewModel`
is deleted first.** Order of operations: delete SchedulerViewModel and its DI
registration → confirm clean build → run migration rollback.

### `NotesWindowViewModel`

`MarkNoteLogged` calls `EvaluateComplianceGate` correctly before status transition.
`SendToSupervisor` stores justification in `CaseManagerJustification` and sets status
to `Logged` — the supervisor queue receives these alongside clean-compliance notes and
must read `CaseManagerJustification` to distinguish them. Not a bug, but a workflow gap
worth documenting in any future supervisor UI work.

### `SettingsViewModel`

Clean. `SetHealthcareSystems` correctly snapshots the source before clearing the
collection to avoid mutating a LINQ query's own source mid-iteration. `SaveSettingsAsync`
correctly uses `_settings.HealthcareSystems = ...` assignment rather than in-place
mutation, honoring the gotcha documented on `Settings.cs`.

**`SettingsViewModel` does not yet expose `*DaysBeforeAnniversary` properties.**
When `FormDueDateCalculator` is updated to read these from `Settings`, corresponding
observable properties and XAML bindings will need to be added here. `Q4RDaysBeforeAnniversary`
will also need to be added.

### `ShellViewModel`

`IsBillingAvailable` is restricted to `Admin` role only. Confirm this is intentional
and not an oversight that should include `Director` or `Supervisor`.

### Dead ViewModels

- `SchedulerViewModel` — confirmed dead. Delete with `WorkdayTile`. See deletion
  order note above re: `ExcludedDatesJson` migration rollback.

### Supervisor ViewModels

**`SupervisorDashboardViewModel`**
- `InitializeAsync` makes 3 sequential DB calls per supervisee: `GetAllPeopleAsync`,
  `GetMonthlyNotesAsync`, `GetOrCreateAsync`. N+1 pattern — 10 case managers = 30
  round-trips. Not a bug, but will become slow at team scale. Future optimization:
  batch queries per supervisee set.
- Commented-out line `// SelectedCaseManager = CaseManagers.FirstOrDefault();` is
  dead code. Remove.
- `ClearCharts()` nulls OxyPlot models before navigation to prevent stale renders.
  Intentional and correct.

**`PendingApprovalsViewModel`**
- Correctly delegates all approval/return/override actions to `SupervisorService`,
  which enforces the compliance gate as a hard throw. The ViewModel catches and logs
  but does not swallow silently — `Debug.WriteLine` is the only output, which means
  failures are invisible to the user in production. Worth adding user-facing error
  feedback before multi-user deployment.
- `PendingNoteViewModel.IsComplianceException` is hardcoded `false` with a comment
  saying "set by non-compliant queue context" — but nothing sets it. If this property
  is bound in the UI, it always shows the wrong value for overridden notes. Either
  wire it to `note.ComplianceOverride` or remove it if the UI doesn't use it yet.

**`UserManagementViewModel`**
- `ResetPassword` hardcodes `"defaultpassword"` as the reset value. **Pre-release
  security fix required.** Must be replaced with a random temporary password, a
  prompted new password, or a forced-reset-on-login flow before any multi-user
  deployment.
- `SaveChanges` mutates `SelectedUser.Role` and `SelectedUser.SupervisorId` directly
  before the save succeeds. If `UpdateAsync` throws, the in-memory entity is dirty
  and the UI shows values that weren't persisted. The catch block sets a status message
  but does not roll back. Low risk, worth hardening.
- `Roles` uses non-generic `Enum.GetValues(typeof(UserRole))`. Style preference per
  CLAUDE.md: `Enum.GetValues<UserRole>()`.

**`CaseManagerSummaryViewModel`, `TeamOverviewViewModel`, `MonthlyProductivityViewModel`,
`OverdueItemsViewModel`** — read-only display ViewModels. No domain logic, no
compliance writes, no invariant risks. Clean.

### Children ViewModels

**`CalendarViewModel`**
- `ToggleExempt` correctly fires `ExemptDateChanged` after mutating exempt state, which
  triggers `CaseManagerDashboardViewModel` to refresh its exempt date cache. The event
  chain is the correct pattern for cross-ViewModel coordination here.
- `BuildMonths` rebuilds the full calendar wholesale on every change. Correct — cell
  alignment depends on the full month structure, so in-place mutation isn't viable.
- Assumes WPF's single-threaded dispatcher prevents `LoadYearAsync` and `ToggleExempt`
  from racing. Valid assumption for a WPF app; note it if threading ever changes.

**`CalendarDay`**
- `IsExempt` and `ExemptDateId` have public setters; `Date`, `IsWeekend`, and `Notes`
  use `init`. The asymmetry is intentional — `ToggleExempt` mutates these two properties
  directly on the existing instance before rebuilding months. Not a bug, but means
  `CalendarDay` is partly mutable despite its `init`-heavy appearance. Cannot be
  converted to a record without changing the toggle logic.

**`ScratchpadViewModel`**
- Auto-save timer (10 min) starts in `InitializeAsync`, not the constructor — correct,
  prevents the timer from running before a user is logged in.
- `SaveScratchpadAsync` is called explicitly on shutdown from `ShellViewModel.ReinitializeAsync`,
  providing belt-and-suspenders alongside the timer. Correct for data you can't afford to lose.
- `Debug.WriteLine` in `SaveScratchpadAsync` logs the full scratchpad content to debug
  output. Harmless in development; remove before any shared deployment — it prints
  case-manager work product to a debug trace.

**`GuidanceViewModel` and `HelpersViewModel`**
- Pure static content ViewModels. No services, no state, no risks.
- Content is hardcoded in C#. If content ever needs to be editable or sourced externally,
  these are the files to change.

**`CalendarMonth`** — pure data container, all `init`. Clean.

### Billing ViewModels

**`BillingDashboardViewModel`**
- Navigation uses `HasLoaded` guard on queue and submissions to avoid redundant loads.
- `_ = _queueViewModel.LoadAsync()` is fire-and-forget. Exceptions are unobservable;
  failures produce an empty queue with no user-facing message. Pre-release gap.

**`BillingQueueViewModel`**
- `PromoteSelectedAsync` awaits `CreateClaimLineAsync` sequentially, not in parallel.
  Intentionally correct — parallel promotion risks race conditions on `BillingPeriod`
  creation since `GetOrCreateBillingPeriodAsync` is not atomic. Do not parallelize.
- `BillingQueueItemViewModel.IsComplianceOverride` correctly reads from
  `Result.Note.ComplianceOverride`. Contrast with `PendingNoteViewModel.IsComplianceException`
  in the supervisor queue, which is hardcoded false — inconsistency to resolve when
  supervisor queue compliance display is wired.
- Three `Debug.WriteLine` calls with millisecond timestamps. Useful for profiling;
  remove before release.

**`BillingSubmissionsViewModel`**
- Correctly gates billing period scope by role: Admin/Supervisor see all periods,
  CaseManager sees only their own.
- `IsTestMode = true` by default — safe. Must be explicitly set false for real
  submission. The UI should make this flag prominent; a defaulted-true checkbox is
  easy to overlook.
- `OpenOutputFolder` uses `Process.Start("explorer.exe", folder)` — Windows-specific.
  Acceptable for a Windows-only app; note if cross-platform ever becomes a goal.

**`BillingOverviewViewModel`, `BillingRemittancesViewModel`, `BillingAlertsViewModel`**
— stubs. Placeholders for future billing features. No risks.

---

## EDI Generator

**`EdiGenerator`** — pure static translation function. No DB access, no DI. Caller
(`EdiService`) is responsible for loading `BillingPeriod` with all navigation properties:
`Lines → Note → Person → Agency`. If any required navigation is missing, the generator
throws at runtime rather than giving a clean error.

**Segment count calculation is fragile.**
```csharp
var segmentCount = sb.ToString()
    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
    .Length + 1;
```
Counts newlines, not segment terminators. Works because each segment is on its own line,
but breaks if any data field ever contains a newline character (consumer name, address).
The correct approach: count `~` characters in the output string. The X12 spec defines
SE01 as segment count, not line count.

**`PER04` phone number is hardcoded as a placeholder.**
`"3609787000"` is described in a comment as "OA support number as placeholder." This
field is the submitter's contact phone — it must be the agency's own number before any
real submission. Should be sourced from `Agency` rather than hardcoded.

**Null-forgiving operators on all agency address fields.**
`agency.Street!`, `agency.City!`, `agency.State!`, `agency.Zip!`, `agency.TaxId!`,
`agency.Npi!` — if any are null, the generator throws `NullReferenceException` at
runtime with no useful error message. `BillingService.ValidateNoteForBilling` checks
`Agency.Npi` but not the address or tax ID fields. Add agency field validation to
the pre-submission check before live use.

**`CLM02` and `SV102` use `Units` as the charge amount.**
In 837P, these fields are monetary values (dollars), not unit counts. For T1016 Maine
TCM billing, verify whether Maine Medicaid's companion guide accepts units here or
requires a dollar amount. If a dollar rate is required, `ClaimLine` will need a
`Rate` or `ChargeAmount` field.

**`claimCounter` reset per subscriber** — correct per 837P spec. LX numbering is
per-subscriber, not global.

**`isTest ? "T" : "P"` in ISA15** — correctly drives test vs. production mode.
Flows from `BillingSubmissionsViewModel.IsTestMode`, which defaults true. Safe.

**Structural correctness (what can be verified without the companion guide):**
- ISA/GS/ST/BHT envelope structure is correct.
- HL hierarchy (20 = billing provider → 22 = subscriber) is correct for 837P.
- 2000B/2010BA/2010BB/2300/2400 loop nesting is correct.
- `LX` correctly increments per service line within each subscriber.
- Segment terminator `~`, element separator `*`, sub-element separator `:` are correct
  for X12 837P.
- One group per file (`gcn = "1"`) is correct for Office Ally submissions.

**Pre-live checklist (before first real submission):**
1. Replace hardcoded `PER04` phone with agency's actual contact number.
2. Add agency field validation (street, city, state, zip, tax ID) to pre-submission check.
3. Confirm `CLM02`/`SV102` monetary vs. unit interpretation with Maine Medicaid companion guide.
4. Fix SE01 segment count to use `~` count rather than line count.
5. Test through Office Ally sandbox with `isTest = true` and verify 999 acknowledgment.

---

## DI Registration (`App.xaml.cs`)

### Lifetime summary

| Registration | Lifetime | Notes |
|---|---|---|
| All services (`IPersonService`, `INoteService`, etc.) | Transient | Correct |
| `ISessionService` / `SessionService` | Singleton | Correct — holds logged-in user for app lifetime |
| `IDbContextFactory<SatiContext>` | Singleton | Correct — per-method context via `await using` |
| `ShellViewModel`, `ShellWindow` | Singleton | Correct |
| `CaseManagerDashboardViewModel` | Singleton | Correct |
| `NotesWindowViewModel` | Singleton | Correct |
| `StatisticsViewModel` | Singleton | Correct |
| `CalendarViewModel` | Singleton | Correct |
| `BillingDashboardViewModel` and all billing sub-VMs | Singleton | Correct |
| `SupervisorDashboardViewModel` | Singleton | Correct |
| `GuidanceViewModel`, `HelpersViewModel` | Singleton | Correct — static content |
| `ScratchpadViewModel` | Transient | **Misleading** — captured by singleton `ShellViewModel`, behaves as singleton. Should be `AddSingleton` to make intent explicit. |
| `UserManagementViewModel` | Transient | **Lifetime mismatch** — injected into singleton `SupervisorDashboardViewModel`. Becomes singleton in practice. |
| `PendingApprovalsViewModel` | Transient | **Lifetime mismatch** — injected into singleton `SupervisorDashboardViewModel`. Holds `PendingNotes`/`NonCompliantNotes` collections that will go stale rather than resetting. Deliberate decision needed. |
| `NewClientViewModel` | Transient | **Misleading** — captured by singleton `CaseManagerDashboardViewModel` as `Clients`. Becomes singleton in practice. |
| `SchedulerViewModel` | Transient | **Dead code** — remove with `WorkdayTile`. |
| `ComplianceReviewViewModel` | Transient | Correct — fresh per dialog invocation. |
| Modal windows and their VMs | Transient | Correct. |

### Startup sequence

Splash (3s) → Login dialog → session set → `ShellViewModel.InitializeAsync` →
`ShellWindow.Show`. `ShutdownMode.OnExplicitShutdown` prevents premature exit when
login window closes. `db.Database.Migrate()` on every startup — correct, idempotent.

### Other notes

- `DispatcherUnhandledException` handler catches all unhandled exceptions and shows a
  `MessageBox` with the full exception. Acceptable for development; before team
  deployment, add a log file path and a shorter user-facing message.
- Factory delegates (`Func<SettingsWindow>`, `Func<NewUserWindow>`, etc.) are
  registered correctly, resolving windows through the container to honor their own
  DI graphs.
- `Func<string, UserMessageDialog>` factory is registered correctly for validation dialogs.
