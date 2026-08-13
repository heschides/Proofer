

# Sati — Refactor Agenda

A WPF MVVM case-management desktop app built with EF Core, CommunityToolkit MVVM, and SQL LocalDB.

---

## Demo hardening - 2026-08-13

- [x] Stop calendar-day selection from repeatedly raising layout error dialogs. Display-only
      `Run.Text` bindings are explicitly one-way, repeated identical UI failures are shown once
      per process, and safe XAML location metadata is included in the local technical log.
- [x] Reproduce calendar-day rendering in a real WPF window and cover the binding rule with
      automated regression tests.
- [x] Make the Demo readiness preflight distinguish an older healthy deployment from a matching
      release and report the exact deployment-parity remedy instead of a raw web exception.
- [x] Build and isolated-launch-test the version 1.2.2 installer containing the completed billing
      client, deploy the matching 1.2.2 API to Azure Demo, and verify live/ready/version health.
- [x] Apply the incident migration to Azure Demo, provision the encrypted-credential Global Admin,
      deploy matching API/client 1.2.3, verify least-privilege platform access, and isolated-launch-
      test the versioned installer.
- [x] Repair incident coverage and the supervisory billing handoff in release 1.2.5: durable retry
      outbox, platform-scoped Global Admin reporting, unclean-shutdown detection, No telemetry
      health state, reentrant window-close protection, explicit Save as Draft / Submit for
      Supervisor Review actions, and an end-to-end draft-to-test-837 integration gate. Apply the
      guarded Azure migration, deploy API 1.2.5, and isolated-launch-test its exact installer.
- [x] Regenerate and visually inspect all ten pages of the version-matched offline Demo fallback.
- [ ] Complete authenticated agency-Admin preflight, external-machine installer attestation,
      presenter rehearsal, and final evidence binding for the exact 1.2.5 installer.

---

## Phase 1 — Fix the Foundation ✅
*Goal: App starts, login works, no crashes*

- [x] Fix double `mainWindow.Show()` in `App.xaml.cs`
- [x] Implement `CaseManagerDashboardViewModel.Initialize(user)` — store logged-in user, trigger initial load
- [x] Fix `NewUserViewModel` missing null-conditional on `CloseWindowRequested` event
- [x] Make `IUserService` public
- [x] Remove hardcoded seed user from `LoginWindowViewModel`
- [x] Fix `MainPage_Activated` firing `LoadPeopleAsync()` on every focus — load once only
- [x] Add `OnModelCreating` to `SatiContext` with explicit keys and relationships

---

## Phase 2 — Remove Service Locator, Tighten DI ✅
*Goal: No more `((App)Application.Current).Services`*

- [x] Remove service locator from `LoginWindow.xaml.cs`
- [x] Use `Func<T>` factory injection for window creation throughout
- [x] Audit all remaining `GetRequiredService` calls in view code-behind

---

## Phase 3 — Complete Person/Client Management ✅
*Goal: Add, view, edit, delete clients with validation*

- [x] Add edit support to `NewClientViewModel`
- [x] Add input validation using `[NotifyDataErrorInfo]`
- [x] Fix `RemoveSelectedPerson` — was not awaiting `DeletePersonAsync`
- [x] Eager-load `Forms` and `Notes` in `PersonService.GetAllPeopleAsync`
- [x] Add compliance review dialog on client creation
- [x] EffectiveDate replaced with MM/DD TextBox with CustomValidation and waiver-gating

---

## Phase 4 — Complete Notes Workflow ✅
*Goal: Notes are fully usable — create, edit, delete, filter*

- [x] Add delete note command to `CaseManagerDashboardViewModel`
- [x] Confirm edit flow works end to end
- [x] Add status filtering (not just text search)
- [x] Unit count / duration display
- [x] Add NoteType (Visit, Contact, Other, Form) with per-type narrative templates
- [x] Add FormType nullable property on Note with migration
- [x] Form note submission triggers MarkFormCompleteRequested popup

---

## Phase 5 — Productivity, Settings, and Scheduler ✅
*Goal: Daily work tracking, configurable settings, monthly scheduler*

- [x] Settings model, migration, ISettingsService/SettingsService
- [x] SettingsWindow fully wired — billing, templates, weekday/holiday exclusion flags
- [x] Settings confirmation dialog on close summarizing changed values
- [x] Scratchpad model/service/migration with auto-save timer
- [x] Incentive model/service/migration with productivity dashboard and progress bar
- [x] Scheduler popup with workday tile grid, month navigation, DaysScheduled persistence
- [x] New month prompt — fires PromptSchedulerRequested when wasCreated is true
- [x] NoteType radio buttons with EnumToBoolConverter

---

## Phase 6 — Forms, Deadlines, and Events Dashboard ✅
*Goal: Core business logic — deadline tracking per client*

- [x] UpcomingEvent record and UpcomingEventService with full computation logic
- [x] Upcoming Tasks panel split into two columns (forms left, visits/contacts right)
- [x] Sort radio buttons wired via SortByDate computed property
- [x] Forms checklist bound to real Form entities via ToggleFormCommand
- [x] Compliance flags computed per FormType using GetCurrentCycleForm
- [x] GetCurrentCycleForm on Person model replaces FirstOrDefault throughout
- [x] EnumDescriptionConverter, BoolToVisibilityConverter, InverseBoolConverter
- [x] Description attributes on FormType enum for human-readable display
- [x] Enums moved to top-level namespace
- [x] UserId foreign key on Person, GetAllPeopleAsync filtered by userId

---

## Phase 7 — Note Polish and Client Detail ✅
*Goal: Note workflow reliability, scheduled events in upcoming panel*

- [x] IsEditing reset on client switch
- [x] Form clears on client selection
- [x] In-memory Person.Notes sync for upcoming events
- [x] NoteType persisted to database with migration
- [x] Scheduled visits and contacts appearing in Upcoming Tasks panel
- [x] ContactEvents column added

---

## Phase 8 — Polish and Portfolio Packaging ✅
*Goal: Looks good, handles errors gracefully, ships cleanly*

- [x] Global exception handling in App.xaml.cs with flat-file error log
- [x] User-facing error dialogs
- [x] ScratchpadHistoryWindow with ICollectionView search and full-content preview
- [x] Scratchpad save bug resolved via cancel/reclose pattern on Closing event
- [x] Out-of-month warning for EventDate
- [x] Font configuration — Inter globally, Cambria for narrative/scratchpad fields
- [x] Font size A/A buttons for narrative and scratchpad
- [x] Ctrl+Enter timestamp insertion in scratchpad
- [x] WPF native spell check on narrative and scratchpad fields
- [x] README.md written and pushed
- [x] Self-contained single-file executable published
- [x] SmartScreen blocking resolved

---

## Recently Resolved Bugs
*Verified before the current platform work*

- [x] **Productivity threshold ignores scheduler** — `Incentive.Threshold` hardcodes `* 19`
  instead of using `Settings.ProductivityThreshold`; panel doesn't refresh after scheduler closes
  - Fix 1: Add `UnitsPerDay` snapshot field to `Incentive` model + migration
  - Fix 2: Set `UnitsPerDay = settings.ProductivityThreshold` in `GetOrCreateAsync`
  - Fix 3: `OnIsSchedulerOpenChanged(false)` calls `RefreshIncentiveAsync()` in CaseManagerDashboardViewModel

- [x] **Refactor all services to use IDbContextFactory<SatiContext>** — current pattern holds
  a DbContext open for the entire session, causing change tracker collisions and memory bloat.
  Replace constructor-injected SatiContext with IDbContextFactory<SatiContext> across all
  services. Swap AddDbContext for AddDbContextFactory in App.xaml.cs. Each method creates
  and disposes its own context via `await using var context = _contextFactory.CreateDbContext()`.
  Do before adding any new features.
- [x] **`GetOrCreateAsync` always returns `wasCreated = false`** — new month records never
  trigger the scheduler prompt correctly; newly-created branch should return `true`
- [x] **NoteType edit not persisting** — suspected EF Core tracking issue; NoteType changes
  on existing notes not being written to DB on save
- [x] **Edit form not populating NoteType** — reopening an existing note doesn't restore
  the current NoteType value; initialization/binding bug
- [x] **Stale data in Upcoming Tasks after failed note edit** — downstream of NoteType
  persistence failure
- [x] **Missing "Scheduled" filter in AllNotes combobox** — straightforward omission
- [x] **ExcludeDayAfterThanksgiving unhandled by IsExcludedHoliday**

---

## Deferred Bugs
*Known issues, not blocking daily use*

- [ ] Scheduler day-of-week column alignment shifts month to month — tiles render
  sequentially rather than snapping to fixed Mon–Fri grid positions
- [ ] Stale ExcludedDates entries persist on Incentive after weekday exclusion is removed
  from Settings
- [x] Note abandonment threshold hardcoded to 8 days — wired to
  `SettingsService.AbandonedAfterDays`
- [x] Settings are global rather than per-user — replaced with agency-scoped settings;
  user overrides remain deferred until a concrete requirement exists

---

## Pre-Release Fixes
*Must address before shipping to team or OADS*

- [ ] Annual form regeneration — when a client's anniversary rolls over, generate new
  Form records for the new compliance cycle
- [ ] First login / scheduler prompt — verify `wasCreated` behavior across month boundaries
  once `GetOrCreateAsync` bug is fixed
- [ ] Accessibility audit — icon-only buttons missing `AutomationProperties.Name`;
  compliance checkboxes unassociated from labels; color-only overdue indicators

---

## Phase 9 — Cloud Platform Foundation
*Goal: establish the boundary on which every external deployment and future client depends.*

### Current next slice — concurrency and audit-operation breadth

- [ ] Extend revision/concurrency tokens from assessments, notes, AT requests, settings, and scratchpads to
  other records where simultaneous edits could silently lose work.
- [ ] Extend the friendly desktop conflict handling now used by notes, settings, and scratchpads to the remaining concurrent
  records instead of presenting generic API errors.
- [x] Add idempotency keys for externally retried commands beyond the database-enforced claim-line rule.
- [x] Define audit retention, legal-hold gate, controlled export, SQL-principal permissions, and monitoring.
- [ ] Implement legal-hold enforcement, production SQL grants/denies, retention jobs, and external alert routing.
- [ ] Review and remove the pre-existing Azure SQL firewall rule
      `ClientIPAddress_2026-8-12_14-41-29` if no active operator still owns it; the August 13 billing
      deployment's temporary rules were removed, but unrelated pre-existing access was not changed.

### Next major slice after billing — tenant-safe incident and health pipeline

- [x] Capture structured client and API incidents with UTC time, release, agency, actor role,
      operation, correlation/reference ID, severity, exception fingerprint, recurrence count, and
      lifecycle status; never capture note narratives, passwords, tokens, connection strings, or
      unrestricted exception messages.
- [x] Deduplicate repeated failures into stable incident groups while retaining occurrence counts,
      first/last-seen times, affected releases, and a bounded diagnostic envelope.
- [x] Add an Admin-only agency incident table showing severity, status, release, operation,
      date, and fingerprint. Enforce agency scope at the API and data boundaries, not only in the UI.
- [x] Introduce a separately named platform-operator capability for cross-tenant incident visibility.
      Do not treat an ordinary agency Admin as a master account, and audit every cross-tenant view
      or export.
- [x] Define transparent, versioned agency and platform Incident Health v1 scores from recorded
      severity, recurrence, and unresolved age; show every penalty and state explicitly that v1
      does not claim crash-free-session, availability, or job-failure coverage.
- [x] Add desktop incident search/severity/status filters, audited status-edit controls, explicit
      alert thresholds, and concurrency-safe aggregation with exact-count integration coverage.
- [ ] Add safe session denominators, API availability, and scheduled-job outcomes.
- [ ] Add retention, legal-hold, access-review, alerting, and runbook requirements; prove PHI/PII
      minimization, tenant isolation, bounded queries, concurrency, and score calculations with
      automated tests before enabling production collection.

### Completed 2026-08-13 — billing gate and 837P hardening slice

- [x] Replace the obsolete hardcoded queue code with agency-scoped procedure/modifier/rate,
      submitter, payer, and contact configuration, enforced by Admin-only API and service boundaries.
- [x] Revalidate note approval, current compliance, historical billing windows, subscriber fields,
      provider NPI/address/tax fields, and EDI configuration immediately before claim creation.
- [x] Apply the current Section 13 minimum/partial-unit rule and keep service units separate from
      calculated monetary charges in claim lines and 837P CLM/SV1 segments.
- [x] Require submit-and-lock before EDI generation, preserve retry idempotency, generate ISA16,
      calculate SE01 from ST through SE, and validate fixed ISA length and service-line structure.
- [x] Freeze subscriber, provider, submitter, and payer values in an immutable per-claim snapshot so
      later Person or Agency edits cannot silently rewrite a financial record.
- [x] Add structured subscriber claim-address fields and an accessible editor; generate 2010BA
      N3/N4 segments rather than attempting to parse a free-form address.
- [x] Add an idempotent Demo-only seed with three ready and seven deliberately blocked examples;
      verify all ten through the real billing service and keep ready rows first in the queue.
- [x] Back up and verify local Demo and Production databases before applying the migration; seed
      synthetic rows only in Demo.
- [x] Apply the additive migration and the same ten-row seed to Azure `SatiDemo`, verify 3 ready / 7
      blocked through the real service, deploy the matching Demo API, and remove temporary firewall rules.
- [ ] Obtain the agency's authoritative fee/code configuration and payer enrollment identifiers,
      then pass clearinghouse test-file validation and payer-specific acceptance. Generated 837P
      structure is tested, but Sati is not yet certified for live claims.
- [ ] Implement 999/277CA acknowledgments, claim rejection correction, 835 remittance import,
      payment/reconciliation, void/replacement claims, and operational submission transport.

### Completed 2026-08-13 -- presenter acceptance kit

- [x] Add secret-free JSON evidence output to authenticated Demo API readiness and isolated
  installer acceptance without treating health-only or same-machine results as final proof.
- [x] Add a final verifier that binds fresh API, external-machine, rehearsal, fallback, release,
  and artifact-hash evidence to the exact installer being presented.
- [x] Produce and visually verify a ten-page offline PDF fallback using only synthetic and
  explicitly representative material, with a presenter approval area and honest limitations.
- [x] Document the external-machine, rehearsal, fallback approval, and final acceptance workflow;
  the matching 1.2.3 API deployment, Global Admin verification, and isolated installer launch are
  complete, while the authenticated agency-Admin run, external-machine run, and human attestation
  remain open.

### Completed 2026-08-13 -- release notes and 1.2.0 packaging

- [x] Add a Settings **Release notes** tab tied to the installed assembly version and summarize
  Admin/audit, safety/reliability, Demo/support, and remaining production work.
- [x] Version Debug, Release, Demo, and the installer consistently as 1.2.0 and add a regression test
  so the in-app notes cannot silently drift from the packaged assembly.
- [x] Compile Debug and Demo with zero warnings and pass 78/78 API, authorization, integration,
  migration, reporting, and domain tests before producing the installer.
### Completed 2026-08-12 -- Demo recovery and acceptance tooling

- [x] Replace user-visible exception/stack-trace dumps with calm reference-number messages and a
  PHI-minimized local JSON-lines diagnostic record that excludes exception messages.
- [x] Add a reproducible self-contained Demo publish script that refuses dirty output folders,
  requires the tracked HTTPS endpoint, hashes the executable, and rejects private appsettings.
- [x] Add read-only deployed health/Admin preflight tooling, a ten-minute company-demo runbook,
  recovery guardrails, and explicit final acceptance gates.
- [x] Compile the real `Demo` configuration in CI and locally; produce and inspect a 263 MB
  self-contained artifact; confirm deployed liveness/readiness after an 80-second Free-tier wake-up.
- [ ] Deploy the current client/API pair, run authenticated preflight, launch the package on a clean
  external Windows machine, rehearse the designated synthetic path, and prove canonical reset.
### Completed 2026-08-12 -- local authorization parity

- [x] Require the signed-in case manager at the local assessment service boundary; reject caller ID
  spoofing, unassigned People, cross-agency People, and attempts to alter another author's draft.
- [x] Restrict local supervisor queues and note decisions to the signed-in reviewer, their agency,
  and either assigned supervisees or agency-wide Director/Admin scope.
- [x] Record successful local assessment and supervisor transitions in the same database save as the
  protected state change.
- [x] Add SQLite service-level regression tests for author, reviewer, assignment, agency, and audit
  boundaries while preserving the cloud API as the production authority.
### Completed 2026-08-12 -- operations visibility and records governance

- [x] Add an Admin-only, agency-scoped operations status view with database status, retained audit/EDI
  counts, oldest-record timestamps, and explicit audit/EDI retention policy values.
- [x] Add a reason-gated, one-year/10,000-row bounded audit CSV export; mark it no-store and audit the
  export without copying its business reason into server metadata.
- [x] Publish `OPERATIONS.md` with the legal-hold prerequisite, production SQL-principal separation,
  monitoring/alert expectations, operator checks, and an honest `PolicyOnly` enforcement state.
- [x] Preserve local-development/cloud parity while keeping retention destructive actions disabled
  until legal-hold controls and production authorization are reviewed.
### Completed 2026-08-12 -- billing retry safety

- [x] Give each WPF EDI-generation attempt a stable retry key and preserve it across ambiguous
  failures until the exact file has been returned and saved.
- [x] Persist the exact EDI response behind a tenant-, actor-, and key-scoped unique index so a
  repeated API request replays one file and creates only one success audit event.
- [x] Reject reuse of an EDI retry key for a different period or test/production mode.
- [x] Make billing-period submission repeatable: an already-submitted period returns its original
  success state, while a database concurrency token prevents simultaneous submissions from
  producing duplicate state changes or audit events.
- [x] Carry the retry contract through the shared API contracts and local service boundary without
  beginning the deliberately post-pilot MAUI/Avalonia business-logic extraction.

### Completed 2026-08-12 -- scratchpad concurrency boundary

- [x] Protect each user's daily Scratchpad with a `Revision` token across local SQL, cloud
  contracts, and the API; reject stale and older-client autosaves with `409 stale_scratchpad`.
- [x] Treat unchanged ten-minute autosaves as no-ops so they create neither revision churn nor
  misleading audit activity.
- [x] Preserve the unsaved text after a conflict, stop repeated autosave warnings, block shutdown
  and user switching from discarding it, and provide an explicit Reload Latest recovery action.
- [x] Record only accepted content changes in the PHI-minimized audit envelope and prove stale,
  legacy, cross-user, no-op, migration, and recovery-boundary behavior.

### Completed 2026-08-12 -- settings concurrency boundary

- [x] Add an agency Settings `Revision` token through local SQL, cloud contracts, and the API.
- [x] Reject stale and legacy settings saves with `409 stale_settings` before shared agency policy
  can be silently overwritten; advance the revision only after a successful save.
- [x] Keep the Settings window open with a friendly warning when another administrator saved first,
  preserving the attempted values for comparison with the latest agency settings.
- [x] Prove one success audit event per accepted save, no false success event for rejected attempts,
  older-client fail-closed behavior, and isolation of another agency's settings.

### Completed 2026-08-12 — first audit and concurrency slice

- [x] Add the PHI-minimized `AuditEvent` envelope, migration, action catalog, and Admin-only,
  agency-scoped audit query documented in `AUDIT_EVENTS.md`.
- [x] Record successful authentication, account changes, supervisor note decisions, assessment
  writes/submission, settings changes, billing submission, and EDI generation.
- [x] Commit each protected state transition and its audit event in the same database save/transaction.
- [x] Add an assessment `Revision` concurrency token and reject stale save/submit requests with HTTP 409.
- [x] Make claim-line creation atomic and enforce one claim line per service note with a unique index.
- [x] Test audit minimization/immutability/tenant scope, stale writes, and repeated billing commands.

### Completed 2026-08-12 — note concurrency slice

- [x] Add a Note `Revision` concurrency token and require the revision read by the caller on every
  edit, delete, supervisor approve/override/return, and automated abandonment transition.
- [x] Reject stale Note operations with HTTP 409 before they can overwrite, delete, or supersede a
  newer copy; increment revisions for every successful state transition.
- [x] Preserve an open editor's draft after a conflict, identify fields that differ from the latest
  saved copy, and refresh Notes Log and supervisor queues before another action.
- [x] Load full Note records in the Notes Log instead of caseload summaries so IDs, narratives,
  people, and revisions remain available through the cloud API transition.
- [x] Prove stale note edits, deletes, and supervisor decisions leave the newer record intact.

### Completed 2026-08-12 — AT request concurrency boundary

- [x] Treat an AT request and all of its line items as one revisioned financial aggregate.
- [x] Require the caller's expected revision for update and delete through local and cloud
  persistence; reject stale or legacy writes with `409 stale_at_request`.
- [x] Replace line items and increment the parent revision in one EF transaction so a stale
  aggregate cannot partially alter vendor, money, status, dates, or item details.
- [x] Add a typed desktop-service conflict for the future Save/Open/Delete workflow without
  prematurely separating Save from the deliberately bundled PDF-publishing feature slice.
- [x] Prove stale aggregate replacement and deletion preserve the newer request and its items.

### Completed 2026-08-12 — Person lifecycle audit

- [x] Preserve an append-only, compressed snapshot and field-level change set for every successful
  Person create, profile edit, and journal edit.
- [x] Record actor, agency, UTC timestamp, request correlation ID, and monotonically increasing
  Person revision; reject stale profile saves with HTTP 409.
- [x] Add Admin-only, agency-scoped history and auditor-PDF endpoints with no-store response headers
  and audit events for viewing/exporting the history.
- [x] Give pre-existing People an explicit current-state tracking baseline without pretending older
  changes can be reconstructed.
- [x] Verify cross-agency denial, append-only enforcement, stale-write rejection, and rendered PDF output.

### Completed 2026-08-12 — visible Admin dashboard

- [x] Add a top-level Admin destination that is hidden for every non-Admin role.
- [x] Show agency user, Person, active-user, sign-in, Person-change, and daily audit-event metrics
  without requiring Azure Portal access.
- [x] Add a readable 30-day activity feed with actor, action, resource, and local timestamp.
- [x] Add an agency Person directory, lifecycle timeline, and protected PDF download workflow.
- [x] Support both the cloud API and the transitional local-development database through
  `IAdminService`; preserve server-side Admin and tenant enforcement.
- [x] Add API integration coverage for dashboard scope and non-Admin rejection.

### Completed 2026-08-12 — tenant-enforcement breadth

- [x] Inventory every protected API route and document its authoritative tenant owner in
  `API_AUTHORIZATION.md`.
- [x] Add cross-agency rejection tests for reports, billing exports, AT requests, assessments,
  supervisor actions, and generated files.
- [x] Revalidate the token's user, agency, and role against the database on every protected request.
- [x] Centralize actor, caseload, supervisor, and assessment-authorship checks in `TenantAccess`.
- [x] Prevent supervisors from authoring a case manager's assessment while preserving read/review access.
- [x] Revalidate every billing-export source note and person against the exporting agency.

### Completed 2026-08-12 — API boundary verification

- [x] Add a dedicated cross-platform API integration/authorization test project.
- [x] Prove unauthenticated requests are rejected at the protected API boundary.
- [x] Prove cross-agency reads and writes are rejected for users, people, and providers.
- [x] Keep the API test project independent from WPF and desktop persistence.
- [x] Run the API tests in CI alongside the existing domain/migration tests.

### Repository structure — targeted follow-up, not a reshuffle

The solution-level boundaries (`Sati`, `Sati.Api`, `Sati.Contracts`, desktop/domain tests, and
API integration tests) are coherent. Avoid a broad folder move while the API transition is active.
Address these pressure points when the affected code is next changed:

- [ ] Split the large `Sati.Api/Endpoints/ApiEndpoints.cs` by feature route group while preserving
  one `/api/v1` composition point.
- [ ] Eliminate schema drift between `SatiContext` and `ApiDbContext`; make server-side persistence
  and migrations authoritative before removing local EF from the distributed WPF client.
- [ ] Rename/move the root WPF project to an explicit `Sati.Wpf` project only after direct cloud
  database responsibilities are removed; doing it now would create churn without changing coupling.
- [ ] Archive historical session material out of this active agenda once the current platform
  priorities are stable; keep a short current-work section at the top.

### Architecture and solution structure

- [x] Add `Sati.Api` ASP.NET Core project.
- [x] Add shared, versioned request/response contracts that do not expose EF entities.
- [ ] Move EF Core, migrations, password hashing, and authoritative domain operations behind the API.
- [ ] Implement HTTP-backed desktop services behind the existing service interfaces where the
  contracts remain appropriate.
- [ ] Remove direct database connectivity and `Database.Migrate()` from distributed clients.
- [x] Add API health checks, structured logs, correlation IDs, and startup validation.
- [ ] Add operational metrics and alert wiring.

### Identity and authorization

- [x] Move Sati credential verification server-side; return a safe profile and short-lived token.
- [x] Never return `PasswordHash` or `Salt` to a client.
- [ ] Add token expiration, revocation, secure recovery, and brute-force/rate-limit controls.
- [ ] Define capabilities independently from menu visibility and coarse job titles.
- [x] Derive actor, tenant, role, and caseload server-side instead of trusting supplied IDs.
- [ ] Evaluate Microsoft Entra ID/External ID and MFA for production organizations.

### Tenant model

- [ ] Decide shared-database, database-per-tenant, or hybrid production isolation.
- [x] Define the authoritative tenant owner for every protected aggregate.
- [x] Replace global settings with tenant-scoped settings; add user overrides only where required.
- [ ] Centralize tenant enforcement with query filters/interceptors and command authorization.
- [x] Add automated cross-tenant read/write/export rejection tests.
- [ ] Add tenant provisioning, suspension, migration, export, and deletion/retention procedures.

### Records, audit, and concurrency

- [x] Create append-only `AuditEvent` records for protected actions without copying
  unrestricted narrative PHI into log messages.
- [ ] Define which sensitive read events require auditing without creating an unusable volume of noise.
- [ ] Add immutable document versions, amendments, attestations, and electronic signatures.
- [ ] Define retention and legal-hold behavior by record class.
- [ ] Extend optimistic concurrency tokens and user-facing conflict resolution beyond the completed
  assessment, Person, Note, and AT persistence records. AT desktop conflict recovery lands with its
  still-deferred Save/Open/Delete and PDF-publishing workflow.
- [ ] Make commands retry-safe and idempotent where duplicate execution would cause harm.
- [ ] Move billing, approval, and submission transitions into explicit server transactions.

### Testing and delivery

- [x] Add unit, API integration, authorization, and migration-consistency tests.
- [ ] Add end-to-end tests for the packaged Demo client and deployed API.
- [x] Establish CI build and test validation across the solution.
- [ ] Add CI dependency scanning, deployment migration validation, and artifact creation.
- [ ] Establish controlled database deployment and rollback; clients never migrate cloud schemas.
- [ ] Add backup verification, point-in-time recovery exercises, disaster-recovery objectives, and
  incident alerts.
- [ ] Produce a signed self-contained Demo installer after API access is working.
- [ ] Test installation, authentication, updates, and removal on a clean non-developer machine.

### Azure Demo milestone

- [x] Split local Production and Demo databases and add fail-closed identity markers.
- [ ] Extract a versioned canonical superhero/sitcom Demo seed independent of migrations.
- [x] Provision `SatiDemo` in Azure SQL without moving production data.
- [x] Host the Demo API with managed identity and least-privilege SQL access.
- [x] Restrict Azure SQL so tester devices do not connect directly.
- [ ] Implement a nightly reset job with its own managed identity, validation, and failure alert.
- [ ] Complete an API inventory and migrate all workflows included in the colleague Demo.
- [ ] Run security, tenant-boundary, concurrency, reset, and clean-install acceptance tests.

### Production readiness gate

Production cloud migration is not authorized by completion of the Demo milestone. It requires a
separate risk assessment, architecture review, operational runbook, BAA/vendor review, penetration
testing plan, user-access process, incident-response process, and explicit approval to move real
working data.

## Mobile and Avalonia Strategy
*Decision: no bottom-up Avalonia rewrite before the cloud platform boundary exists.*

WPF remains Sati's full-power, data-dense Windows client. Cross-platform reach will come first from
the shared API and portable contracts, not from forcing the current desktop shell onto phones.
Avalonia remains a candidate for a focused mobile client and, only if real demand emerges, a future
cross-platform desktop client.

### Prerequisites

- [ ] Complete the API foundation for authentication, people, notes, and upcoming work.
- [ ] Extract portable projects that do not depend on WPF, EF Core, desktop dialogs, or local
  filesystem paths. Initial target structure:
  - `Sati.Contracts` — versioned API request/response DTOs
  - `Sati.Domain` — portable domain concepts and authoritative pure calculations
  - `Sati.Client` — authentication, HTTP transport, and shared client services
  - `Sati.Api` — server authority, EF Core, authorization, audit, and integrations
  - `Sati.Wpf` — existing Windows professional client
- [ ] Keep platform-neutral service contracts free of `Window`, `Dispatcher`, `SecureString`,
  EF entities, and other Windows/persistence-specific types.
- [ ] Define mobile security, session, offline-storage, synchronization, and device-loss rules
  before storing any protected data on a phone.

### Define the first mobile product

Do not treat the desktop application as the mobile specification. Validate the smallest useful
field-work scope, likely:

- secure login and session expiration;
- today's work and upcoming deadlines;
- client lookup and essential contact details;
- quick visit/contact note capture;
- interruption-safe local drafts and later synchronization;
- optional camera/document capture; and
- future EVV check-in/out if required.

Billing administration, EDI, provider maintenance, system settings, dense compliance matrices,
and broad supervisory analytics are not assumed to belong in the first mobile client.

### One-week Avalonia spike — after API prerequisites

- [ ] Create separate Avalonia core and Android projects; do not replace `Sati.Wpf`.
- [ ] Implement login, client list, one client summary, and basic note capture against the API.
- [ ] Approximate the Sati theme without attempting full WPF style parity.
- [ ] Test touch targets, navigation, interruption recovery, slow/offline behavior, and session
  expiration on a physical Android device—not only an emulator.
- [ ] Record porting friction around XAML styles, converters, accessibility, dialogs, and shared
  ViewModels.
- [ ] Estimate iOS requirements separately, including macOS/Xcode, signing, provisioning, and
  distribution.
- [ ] Decide whether to proceed based on field usability and maintenance cost rather than the fact
  that the sample compiles.

### Explicit non-goals

- No week-long attempt at whole-application Avalonia feature parity.
- No mobile client that connects directly to SQL or embeds a database credential.
- No virtualized desktop-window interface presented as a finished phone experience.
- No parallel copy of authoritative business rules in WPF and Avalonia.
- No abandonment of WPF unless cross-platform desktop demand and a measured migration case justify
  it.
## Billing Pipeline (historical plan; superseded by the completed hardening slice above)

### Data model changes needed
- [ ] Add `CompletedDate DateTime?` to `Form` + migration
- [ ] Add `IsBillable bool` to `Note` + migration (default true, computed at creation)
- [ ] Add `BillingStatus` enum: `Pending | Approved | Rejected | Queued`
- [ ] Add `SupervisorApprovedById int?` and `SupervisorApprovedAt DateTime?` to `Note`

### Logic
- [ ] Add `FormComplianceStatus` enum: `NotYetDue | InWindow | CompliantOnTime | CompliantLate | Overdue | NoForm`
- [ ] Add `GetComplianceStatus(FormType, DateTime, Settings)` to `Person`
- [ ] Wire `IsBillable` computation into `SubmitNewNoteAsync` — check all required forms at `EventDate`
- [ ] `IsBillable == false` notes route to supervisor review queue, not billing queue

### Supervisor dashboard
- [ ] Add billing review queue sub-view — shows all `IsBillable == false` notes
- [ ] Supervisor approve → `IsBillable = true`, set `SupervisorApprovedById` and `SupervisorApprovedAt`
- [ ] Supervisor reject → note stays non-billable, logged in audit trail

### Rules
- Compliant on time + in window = billable
- Compliant late = compliant on paper, gap period unbillable
- Overdue = not billable until resolved and supervisor-approved
- Notes persist regardless of billability — always a valid service record
---

## Future Roadmap

### Local AI case-note drafting

Development slice shipped 2026-08-07: lazy in-process Foundry Local integration, `phi-4-mini`,
500-word input cap, editable rules file, progress state, separate preview, explicit accept/discard,
and deterministic warnings for omitted numeric details and placeholders. The original narrative is
not changed unless the user accepts the draft. `LocalAi:Enabled=false` disables the feature.

First approved style rule added 2026-08-07: required `Community Case Manager (CCM)
[full name]` opening, required final `Follow-up:` section, actual form-record fallback
when no follow-up is evident, SLP/SLC role expansion, and trailing whole-number unit
shorthand. One rough-note/desired-note pair is now embedded in `AI_CASE_NOTE_RULES.md`.

Structured visit slice shipped 2026-08-07: consumer-profile contacts/support-team editor,
separately loaded contact service, visit attendee selection, constrained setting/appearance/
participation/safety choices, independent verified-fact checkboxes, additional-attendee and
observation detail fields, and a persisted note-owned JSON snapshot. The local formatter receives
these selections in a trusted current-visit block; historical AI context remains background-only.
Concern selections require descriptive text, while `Not documented` never becomes a default
normal observation.

Before any shared or production release:

- Obtain the authoritative agency case-note policy and replace/refine `AI_CASE_NOTE_RULES.md`.
- Assemble at least 50-100 de-identified rough-note/approved-note examples spanning visits,
  contacts, forms, sparse notes, ambiguity, quotations, negative statements, and safety content.
- Define and pass acceptance thresholds for zero invented facts, retained attribution/negation,
  required formatting, latency, memory use, and accessibility.
- Add regression tests with a fake formatter plus a separately run local-model evaluation suite.
- Persist an audit record for accepted AI drafts: source, draft, final user-edited text, rule-set
  version, model alias/version/hash, user, timestamps, and explicit acceptance. Decide retention and
  access rules before adding this to the database.
- Add cancellation and model-download/retry controls; test first-run, cached/offline, low-disk,
  unavailable-model, corrupt-cache, and runtime-unload behavior.
- Complete privacy, security, clinical/documentation, labor, and records-retention review. Confirm
  runtime telemetry is disabled or contains no PHI and that no cloud fallback can occur.
- Pin the approved model variant and license terms; prevent an unreviewed catalog update from
  changing production output.
- Add an administrative release gate so production builds default to disabled until formally
  approved, even if a development `appsettings.json` is copied accidentally.
*Parked for post-OADS or v2.0+*

- [ ] **Historical productivity viewer** — query past Incentive rows paired with monthly
  note data to display a full productivity history per user; infrastructure already exists
- [ ] Per-client detail view — all notes, forms, compliance status, and upcoming events
  scoped to one client
- [ ] User management / admin panel — add, edit, deactivate users
- [ ] Soft-delete recycle bin for notes
- [ ] DataGrid column cleanup — replace AutoGenerateColumns with explicit column definitions
- [ ] Sati.Core extraction — shared class library for models, services, EF context
- [ ] Sati.Api — ASP.NET Core Web API (GET /clients, POST /notes)
- [ ] Sati.Mobile — MAUI app for field note entry and upcoming task visibility
- [ ] Azure Cognitive Services Speech-to-Text for field note dictation
- [ ] reMarkable integration — push visit PDFs, pull annotated docs

---

## Comprehensive Assessment, PCP, and OADS Workflow
*Goal: Replace Evergreen with a person-centered, waiver-agnostic assessment and plan workflow that is practical for case managers, reviewable by supervisors and OADS, and safe for billing.*

### Comprehensive Assessment — first functional slice shipped 2026-08-07

- [x] Replace the client-workspace placeholder with a desktop-oriented assessment editor.
- [x] Add eight navigable domains with an initial set of practical questions.
- [x] Add expandable guidance to every question: why it is asked, what a complete
  answer includes, and what to avoid.
- [x] Persist drafts in `ComprehensiveAssessments` and auto-save after edits.
- [x] Store the assessment body as one versioned JSON aggregate while workflow and
  ownership fields remain relational/queryable.
- [x] Record report contributors separately from the assigned case-manager author.
- [x] Use one team assessment by default with optional dissenting perspectives.
- [x] Model support as combinable characteristics, not a false linear scale.
- [x] Enforce logical exclusions: "No support currently needed" clears active support
  selections; `Varies` requires at least one concrete support and an explanation.
- [x] Require every question to be addressed before submission; follow-up-required
  answers do not count as complete.
- [x] Add structured identified needs with type, desired result, and optional provider
  association.
- [x] Enforce authoring by caseload ownership. Supervisor status alone does not grant
  permission to rewrite another case manager's answers.
- [x] Add submission to supervisor review and immutable approved/superseded states in
  the domain model.
- [x] Change the new/default Comprehensive Assessment deadline from 120 to 60 days
  before the PCP anniversary.
- [x] Add migration `20260807120000_AddComprehensiveAssessments`.

### Comprehensive Assessment — content and usability follow-up

- [ ] Flesh out the assessment with the additional specific questions identified by
  case-manager and OADS review; keep questions waiver-agnostic.
- [ ] Add a realistic good-answer example to every question, alongside the existing
  inclusion and avoidance guidance.
- [ ] Review every question for plain language, practical answerability, duplication,
  trauma-informed wording, dignity, and relevance to authorization.
- [ ] Decide which questions are conditional and implement branching without hiding
  previously entered answers.
- [ ] Add question-level comments, supervisor flags, resolution state, and return reason.
- [ ] Add a visible validation summary with links that move focus directly to every
  incomplete or contradictory answer.
- [ ] Validate contributor rows, identified needs, provider associations, and dissenting
  opinions—not only the core question set—before submission.
- [ ] Add need urgency, current supports, unmet component, health/safety implication,
  responsible next action, status, and resolution history.
- [ ] Replace the temporary provider-name entry with selection from the future
  consumer/provider association model while retaining a document snapshot.
- [ ] Add `Not assessed` follow-up ownership and due date; permit completion only through
  an explicitly documented supervisor exception where policy allows.
- [ ] Add autosave retry/recovery, unsaved-change shutdown flush, concurrency handling,
  and protection against two sessions editing the same draft.
- [ ] Remove the service-locator construction in
  `ComprehensiveAssessmentWorkspace.xaml.cs`; inject/factory-create the workspace and
  ViewModel consistently with Sati's DI rule.
- [ ] Perform keyboard-only, JAWS, high-contrast, 200% scaling, and 1280x768 layout QA.
- [ ] Add unit tests for completion rules, support-selection exclusions, ownership,
  version immutability, serialization compatibility, and submission transitions.

### Supervisor assessment workflow

- [ ] Add a supervisor review queue for Comprehensive Assessments.
- [ ] Allow supervisors to flag individual sections/questions, comment, and return a
  submission without rewriting the author's answers.
- [ ] Make supervisor approval wholesale, with all unresolved flags blocking approval.
- [ ] Permit supervisors who carry a caseload to author only their own assigned clients'
  assessments; keep the author and reviewer capabilities separate.
- [ ] Record submission, return, resubmission, approval, actor, timestamps, reason, and
  exact version in append-only audit history.
- [ ] On approval, lock the version and mark the matching legacy `Form` complete through
  the existing `Form` invariant rather than writing compliance fields directly.

### Documents, signatures, and versions

- [ ] Publish a version-identified Comprehensive Assessment PDF.
- [ ] Retain both the generated unsigned PDF and uploaded physically signed scan.
- [ ] Record signer, role, signature method, upload actor, and timestamps against the
  exact frozen version.
- [ ] Add the same publish/print/upload workflow to the PCP.
- [ ] Ensure any post-publication edit creates a new version and signature cycle; never
  replace or silently mutate a signed or approved version.
- [ ] Establish secure document storage, malware scanning, retention, download/export
  authorization, and accessible PDF generation.

### Electronic signature portal — vetted direction

The signature feature should be a secure Sati web portal reached from an email
notification—not a Sati-operated mail server and not an email reply treated as the
authoritative signature. Email is the delivery channel; Sati owns the identity check,
review, intent-to-sign action, immutable evidence, and resulting signed artifact.

#### Policy gates before implementation

- [ ] Obtain written OADS/OMS confirmation that the MaineCare electronic-signature
  notice supersedes the OADS PCP manual's physical-signature instructions for the PCP
  Face Sheet and for annual/reversioned plans.
- [ ] Confirm separately whether electronic signing is accepted for provider/team
  Agreement Sheet signatures; the current MaineCare notice expressly discusses member
  signatures, while the published OADS manual still says implementing Team Members must
  physically sign the Agreement Sheet.
- [ ] Confirm what exact evidence Resource Coordinators must receive and whether a
  generated signed PDF plus audit certificate qualifies as the retained original.
- [ ] Confirm the required signers and signature meaning for the Comprehensive Assessment
  independently from the PCP workflow.
- [ ] Document the authority and identity-proofing rules for a Person, guardian,
  authorized representative, case manager, provider implementer, supervisor, and OADS
  Resource Coordinator. A proxy must sign in the proxy's own name and capacity, never as
  though they were the Person.
- [ ] Preserve a paper, in-person, and accessible assisted-signature path. Electronic
  transactions must be consensual and must not become a condition of receiving services.

Policy basis reviewed 2026-08-07:

- [MaineCare's electronic-signature notice](https://www1.maine.gov/dhhs/oms/providers/provider-bulletins/notice-regarding-electronic-signatures-2024-09-16)
  permits member electronic signatures under enforcement discretion when the system
  authenticates the signer, prevents signing an incomplete document, complies with
  privacy/security requirements including HIPAA, and retains proof plus the signed record.
- [Maine UETA](https://legislature.maine.gov/statutes/10/title10ch1051sec0.html)
  recognizes an electronic process adopted with intent to sign, requires agreement to
  transact electronically, permits codes/security procedures as attribution evidence,
  and requires an accurate record that remains accessible for later reference.
- [42 CFR 441.301(c)(2)(ix)](https://www.ecfr.gov/current/title-42/chapter-IV/subchapter-C/part-441/subpart-G/section-441.301)
  requires the PCP to be finalized with the individual's informed written consent and
  signed by the individual and all people/providers responsible for implementation.
- The [published OADS PCP manual](https://www.maine.gov/dafs/bablo/sites/maine.gov.dhhs/files/documents/PCPManualpdf.pdf)
  still instructs case managers to obtain physical signatures and maintain originals;
  this unresolved conflict is why written OADS/OMS direction is a release gate.

#### Signature workflow and domain model

- [ ] Freeze a complete, version-identified document before creating any signature
  request. Store the final PDF, document version, cryptographic hash, and publication
  timestamp; never allow the published content to mutate in place.
- [ ] Model `SignatureEnvelope`, `SignatureRequest`, `RequiredSigner`, and append-only
  `SignatureEvent` records separately from document approval and authorization state.
- [ ] Create one uniquely addressed request per required signer. Do not treat a shared
  family email, provider group mailbox, meeting attendance, or document contribution as
  proof that a particular person signed.
- [ ] Track signer name, role/capacity, organization, required/optional status, delivery
  status, authentication method, signed/declined/requested-changes state, and timestamps.
- [ ] Give the signer three explicit outcomes after reviewing the exact frozen document:
  sign/agree, decline, or request changes. Capture the exact intent and consent language
  displayed at the moment of signature.
- [ ] Keep signature completion, supervisor approval, OADS wholesale approval,
  Classification, service authorization, and billability as distinct states. One must
  never silently imply another.
- [ ] Require a new document version and signature cycle after any substantive change.
  Retain the prior version, its signatures, and the reason for supersession.
- [ ] Generate a signed PDF or audit certificate logically associated with the frozen
  PDF, and retain both the original final artifact and complete signature evidence.
- [ ] Distribute a downloadable/printable copy to the Person/guardian and other entitled
  participants after completion, using the same access controls as the signing portal.

#### Email delivery and authentication

- [ ] Use a managed outbound transactional-email service under the required HIPAA
  agreement and configure domain authentication/deliverability. Do not operate inbound
  SMTP or require the signer to reply to an email.
- [ ] Keep notification emails generic and free of assessment/PCP content and unnecessary
  identifiers. Do not automatically attach PHI; the opaque, random, single-use link opens
  the protected portal where the document is shown after authentication.
- [ ] Confirm the email address and preferred confidential communication method during
  enrollment, and support correction/revocation without silently retargeting an existing
  signature request.
- [ ] Make link tokens high-entropy, single-use, short-lived, revocable, rate-limited,
  and stored only as hashes. Never place a person ID, document ID, MaineCare ID, or other
  meaningful identifier in a URL.
- [ ] If a pre-established signing code is retained, establish it only after verified
  enrollment, hash it using the password-storage standard, never reveal it to staff, and
  rate-limit/lock failed attempts. Do not send the code through the same email as the link.
- [ ] Prefer an existing authenticated provider account, passkey, or short-lived code
  delivered through an independent verified channel. NIST does not treat email as an
  acceptable independent out-of-band authentication channel.
- [ ] Design a verified recovery and assisted-signing process for people who forget a
  code, share an email account, lack a mobile phone, use supported decision making, or
  cannot independently operate the portal. Recovery must not be easier to exploit than
  the normal signature flow.

#### Security, evidence, operations, and accessibility

- [ ] Put the public signing surface behind a trusted web application/API boundary;
  the WPF client and direct database access cannot serve as the Internet-facing security
  boundary. Authorize every request by capability, organization, signer, document version,
  and workflow state.
- [ ] Record append-only evidence including document hash/version, signer identity and
  capacity, authentication method, consent/intent text, issuance/view/sign timestamps,
  delivery events, failed attempts, revocation/expiration, and administrative actions.
- [ ] Decide through privacy/security review whether IP address and user-agent evidence is
  necessary and proportionate; document retention and access rules for that metadata.
- [ ] Encrypt PHI in transit and at rest, segregate signing secrets from document storage,
  prevent replay, scan uploaded physical-signature fallbacks for malware, and include the
  portal/vendor in risk analysis, incident response, breach response, and business-
  associate agreements.
- [ ] Add reminders, expiration, resend, email-bounce handling, signer replacement,
  revocation, and escalation without changing the frozen document or losing history.
- [ ] Make the signing page work at common mobile and desktop sizes with keyboard-only
  navigation, screen readers, magnification, high contrast, plain language, limited-
  English support, and an accessible downloadable document.
- [ ] Add automated tests for incomplete-document blocking, wrong signer, shared email,
  expired/replayed links, brute-force limits, version mismatch, signer replacement,
  decline/request-changes, partial multi-signer completion, post-signature mutation,
  audit immutability, and separation from OADS approval/authorization.

This is a medium-to-large architecture feature despite its intentionally simple signer
experience. The email sender is a small component; the public portal, trusted API,
identity and authority proof, immutable document/version handling, multi-signer state,
audit evidence, accessibility, and policy acceptance are the substantive work.

### Person-Centered Plan

- [ ] Build the PCP at intake and annually, using the approved assessment and live
  consumer profile as sources without silently mutating approved plan versions.
- [ ] Record all PCP meeting participants, roles/relationships, invitation/attendance
  status, attendance method, and signature/acknowledgment state.
- [ ] Default the assigned case manager as meeting organizer, with an audited override.
- [ ] Add profile-to-PCP change rules: informational, potentially material, material,
  and authorization-affecting.
- [ ] Generate a reviewable PCP change set or amendment from material profile changes;
  require case-manager confirmation and supervisor review for important changes.
- [ ] Add OADS Resource Coordinator review with section flags/comments but wholesale
  approval/return of the PCP.
- [ ] Add authorized services as a deliberately unfinished section boundary now; later
  connect it to Providers, authorization periods, units, frequency, duration, funding,
  assessed needs, and goals using immutable snapshots.
- [ ] Preserve the distinction between assessment facts, supervisor attestation, PCP,
  OADS decision, classification, and authorization.

### Classification and future OADS access

- [ ] Keep Comprehensive Assessment and PCP waiver-agnostic.
- [ ] Implement waiver/level-of-care determination in Classification, including future
  Lifespan Waiver support.
- [ ] Add the OADS Resource Coordinator role and narrow capabilities rather than relying
  on menu visibility or broad job-title permissions.
- [ ] Record approval, denial, return, effective/expiration dates, cited evidence,
  decision-maker, and immutable decision history.
- [ ] Add assignment, delegation, temporary coverage, reassignment, and recusal workflows.

### Deadlines, reminders, reviews, and billing

- [ ] Reconcile already-generated Comprehensive Assessment `Form.DueDate` values from
  the legacy 120-day offset to the agreed 60-day-before-PCP rule through an inspected
  dry run; the migration changes the setting/default but deliberately does not guess at
  existing records.
- [ ] Continue using the existing Form/ReviewItem reminder calculations as the canonical
  deadline source; do not create parallel assessment date arithmetic.
- [ ] Block PCP submission when its Comprehensive Assessment is overdue, with a documented
  supervisor-or-higher override containing reason, actor, timestamp, expiration, and
  affected version.
- [ ] At midnight after PCP expiration, mark subsequently submitted case notes permanently
  unbillable; there is no grace period and later PCP completion is not retroactive.
- [ ] Apply the same permanent billing-gap rule to overdue 90-day reviews, using Sati's
  existing review due-date calculations.
- [ ] Preserve all concurrent unbillable reasons on the note and show them before note
  submission as well as in billing exports.
- [ ] Define and implement the exact instant at which billability resumes after a late
  PCP or 90-day review is completed.
- [ ] Prevent unbillable notes from entering MIHMS claim generation while retaining them
  as valid service documentation.
- [ ] Add regression tests around midnight boundaries, back-entered notes, multiple
  simultaneous compliance failures, overrides, and permanent non-retroactivity.

## Session Log

| Date | Phase | What was done |
|------|-------|---------------|
| 3/19 | Ph5 | Settings model, migration, ISettingsService/SettingsService, SettingsViewModel, wired into CaseManagerDashboardViewModel |
| 3/20 | Ph5 | Scratchpad model/service/migration, auto-save timer, NoteType enum, template insertion |
| 3/21 | Ph5 | NoteType radio buttons, EnumToBoolConverter, Incentive model/service/migration, productivity dashboard, weekday/holiday exclusion flags |
| 3/22 | Ph5 | SchedulerViewModel, WorkdayTile, Incentive ExcludedDates, ISessionService singleton, scheduler popup XAML |
| 3/23 | Ph5 | Scheduler popup fully working — tile toggling, month navigation, DaysScheduled persistence |
| 3/23 | Ph5 | SettingsWindow fully wired — billing, templates, weekday/holiday flags, auto-save on close |
| 3/23 | Ph5 | Day after Thanksgiving flag, tuple return from GetOrCreateAsync, custom new month prompt |
| 3/25 | Ph6 | SafetyPlan + PrivacyPractices added to FormType. 18 form deadline offset properties in Settings. UpcomingEventService created. |
| 3/26 | Ph6 | UpcomingEventService wired into CaseManagerDashboardViewModel, UserId FK on Person, GetAllPeopleAsync filtered by userId |
| 3/28 | Ph6 | Upcoming Tasks split into two columns, sort radio buttons, EffectiveDate refactor, form note templates, FormType on Note with migration |
| 3/29 | Ph6 | MarkFormCompleteRequested wired end to end, compliance checklist bound to real data, GetCurrentCycleForm added, ComplianceReviewWindow on client creation |
| 3/29 | Ph7 | Note workflow fixes — IsEditing reset, form clears, NoteType persistence, scheduled visits/contacts in Upcoming Tasks |
| 4/8  | Bug | Diagnosed productivity threshold bug — hardcoded * 19, stale _incentive after scheduler closes. Fix plan: UnitsPerDay snapshot field, OnIsSchedulerOpenChanged refresh, GetOrCreateAsync wasCreated fix. Historical productivity viewer scoped as future feature. |
| 8/6  | AT | AT Request item entry (slice 1c) — item cards with Name/URL/Cost/Qty on the editor left pane, live subtotal/passthrough/total readout, `ATRequestItemEditorViewModel` write-through with parent total-change callback. Added `Url` field to `ATRequestItem` (+migration). Provider slice 1: `Provider` model, `ProviderType`/`WaiverService` enums, `Settings.SalesTaxRate` + `DefaultPassthroughProviderId`, migration `AddProviderAndSalesTax`, Maine AT Solutions seeded. |
| 8/7  | AT | Provider slice 2: `IProviderService`/`ProviderService`, `ProviderEditorViewModel` (bit-per-checkbox flags + passthrough reveal), `ProvidersViewModel` master-detail, `ProvidersView`, Providers tab grafted into CM sub-nav, DI wired. Settings window gained sales-tax-rate box + default-passthrough-provider dropdown (SelectedValue→int? FK). |
| 8/7  | Assessment | First functional Comprehensive Assessment slice: versioned JSON-backed draft, autosave, eight-domain desktop editor, practical per-question guidance, contributors, dissent, combinable support characteristics, structured needs, caseload ownership, completion gate, supervisor submission state, migration, and 60-day default offset. |



## From note-entry extraction + per-consumer Journal session (2026-07-29)

### Data integrity
- [ ] **Duplicate forms surfacing in billing window.** `EvaluateBillingWindow`
      displayed the same PCP three times for one client (Christian Bobe) — it
      iterates `Forms.Where(gated)` with no dedup, so duplicate/multi-cycle PCP
      rows all match. Not cosmetic: duplicate `Form` rows on a Person could skew
      compliance elsewhere. Trace where the triplicate came from (form generation?
      rollover?) before deduping the display — the display is the symptom, the
      rows are the bug.

### Data loss risk
- [x] **App-close flush for Journal + Scratchpad.** Quitting via the window X
      within 2s of typing loses the Journal tail (debounce hasn't fired). Same
      gap likely affects Scratchpad — its only shutdown save is in
      `ShellViewModel.ReinitializeAsync` (user-switch), not true app-close. Fix:
      `ShellWindow.OnClosing` handler calling `_notesViewModel.Clients.FlushJournalAsync()`
      and the scratchpad save. User-switch is already covered.

### Consolidation left unfinished
- [ ] **NotesLog has two compliance dialogs.** The extracted entry module carries
      its own (entry-form path); the old host-level overlay in `NotesLogView.xaml`
      (`Grid.ColumnSpan="5"`, fixed `Width="460"`, no height bound) still serves the
      context-menu "Mark Note Logged" path via `NotesWindowViewModel`. Both work,
      driven by different VMs, but it's one screen with two dialog definitions.
      Fold the context-menu path onto the module's dialog, delete the host overlay.
- [ ] **User-switch doesn't reset the NotesLog entry module's draft.** Dashboard's
      `Reset()` cascades to its `NoteEntry.Reset()`; NotesLog's copy only gets
      `SetPeople` on reload (which clears selection but not a half-typed narrative).
      Add `NotesLog.NoteEntry.Reset()` to the reinit path.

### Cleanup
- [ ] **Delete dead `NotesWindowViewModel.LoadAsync`.** Superseded by `ReloadAsync`
      (which also does sentinel reset + property notifications). Nothing calls the
      private `LoadAsync` — confirmed via Shell lifecycle. Shadow copy of load logic.
- [ ] **`SendToSupervisor`/`Cancel` event asymmetry in `NotesWindowViewModel`.**
      `Cancel` fires `NoteStatusChanged`; `SendToSupervisor` doesn't. Looks
      backwards. Verify intent, align.

### Architectural debt (widened, not fixed)
- [ ] **Tiered loading — `GetAllPeopleAsync` still fat-loads.** Base row now pulls
      `Bio` AND `Journal` (both `nvarchar(max)`) plus the full Notes/Forms graph via
      dual `.Include` + `.AsSplitQuery()`. Journal's on-demand methods dodge this,
      but the base load is unchanged. Still the `RESOURCE_SEMAPHORE` culprit; now
      with one more unbounded column riding along. Projection-to-summary-DTO remains
      the fix.

## From AT Requests + Provider Directory session (2026-08-07)

### Shipped
- [x] **AT Request item entry (slice 1c).** Editor left pane now has an "Items or
      Services" section: one card per line item with Name, URL, Cost, and Quantity,
      plus an Add Item button and per-card Remove. `ATRequestItemEditorViewModel`
      is the write-through row wrapper; cost/qty edits fire a parent callback that
      re-raises the request totals. Live subtotal / 15% passthrough / total readout
      under the sales-tax box mirrors the form preview.
- [x] **`ATRequestItem.Url`.** Nullable string, stored on the item now, destined for
      the future screenshots-with-clickable-links page 2. NOT rendered on the page-1
      OADS form. URL extraction from retailer pages was scoped and **rejected** —
      scraping is fragile and out of place in a case-management app. (+migration)
- [x] **Provider directory (slices 1-2).** New `Provider` model (structured address,
      `[Flags] WaiverService OfferedServices`, `ProvidesPassthroughService` bool,
      flat passthrough billing strings). `ProviderType`/`WaiverService` enums.
      Providers tab under CM sub-nav - master-detail CRUD, passthrough checkbox
      reveals the three billing fields. Maine AT Solutions seeded as the passthrough
      default. `IProviderService`/`ProviderService`, `ProviderEditorViewModel`,
      `ProvidersViewModel`, `ProvidersView`, DI, migration `AddProviderAndSalesTax`.
- [x] **Settings: sales tax + default passthrough provider.** `Settings.SalesTaxRate`
      (0.055 default, a rate not an amount) and nullable `DefaultPassthroughProviderId`
      FK. Settings window gained a tax-rate box and a provider dropdown
      (`SelectedValue`->int? FK, `SelectedValuePath=Id`).

### Remaining on this feature (slices 3-4)
- [ ] **AT page passthrough dropdown (slice 3).** Dropdown of passthrough providers,
      pre-selected to `Settings.DefaultPassthroughProviderId`. On select, snapshot-copy
      the provider's Name/BillingLocationEis/ProgramContact/BillingContact onto the
      request's `Vendor*` fields (same freeze-at-select semantics as client/CM). Keep
      a nullable `ProviderId` FK on `ATRequest` alongside the snapshot.
- [ ] **Item numbers on the OADS form.** 1, 2, 3... in the form's Item # column via
      WPF `AlternationIndex` + a +1 converter. No data change.
- [ ] **Sales-tax freeze (slice 4).** Auto-compute tax = subtotal x `SalesTaxRate` and
      freeze the amount onto the request at save. Bundle with the deferred Save + PDF
      export batch (both are the same trip to disk).
- [ ] **AT request Save + Publish PDF.** Still unbuilt by design. `NewRequest` builds
      in memory; `CloseEditor` discards; nothing calls `AddAsync`/`UpdateAsync` yet.
      Save lands with PDF export as one batch. The persistence boundary now has aggregate
      revision enforcement and a typed conflict ready for that workflow.

### Deferred (needs a later slice)
- [ ] **Client<->provider association.** The AT dropdown can only list *all* passthrough
      providers - Sati has no link from a consumer to *their* home/community-support
      provider, so it can't pre-select "this client's agency." That association is its
      own model + slice. `OfferedServices` (the four waiver flags) is inert until it lands.
- [ ] **AT Assessments as a waiver service.** Maine AT Solutions actually offers it;
      left out of `WaiverService` until something consumes the offering data.
- [ ] **Providers tab governance.** Provider directory is agency-level shared reference
      data but currently lives under CM sub-nav. In the multi-user future it should be
      admin-curated to prevent duplicate provider rows across CMs.

### Tech debt confirmed against disk this session
- [ ] **Dead files still present:** `ViewModels/SchedulerViewModel.cs` and
      `Models/WorkdayTile.cs` - delete together. (`Models/Event.cs` already removed.)
- [ ] **Filename typo:** `Data/Billing/IdeService.cs` should be `EdiService.cs`
      (class name is fine; only the file is misnamed).

## From MVVM / SOLID architecture audit (2026-08-07)

The current architecture has a sound base: ViewModels do not access EF directly,
services generally use injected interfaces and per-method `IDbContextFactory` contexts,
the composition root is centralized in `App.xaml.cs`, and important compliance behavior
already lives in `Person` and `Form`. The items below are targeted refactors, not grounds
for a rewrite.

### P1 — security and active correctness

- [ ] **Enforce assessment authorship on every write at the service boundary.**
      `ComprehensiveAssessmentService.SaveDocumentAsync` accepts only an assessment ID
      and document, so it can update another author's editable draft if invoked outside
      the current UI path. Require a trusted authenticated actor/capability and verify
      ownership, assignment, organization, and workflow status before saving. Apply the
      same rule to submission instead of treating a caller-supplied user ID as identity.
- [ ] **Enforce supervisory authorization inside `SupervisorService`.**
      `ApproveNoteAsync`, `ApproveWithOverrideAsync`, and `ReturnNoteAsync` trust the
      supplied `supervisorId`; the queue also accepts an `allSupervisees` switch. Verify
      the authenticated actor's role/capabilities, agency scope, and actual supervisory
      relationship before reading or changing a note. Before OADS or other external users
      enter Sati, place these checks behind a trusted application/API boundary rather than
      relying on a desktop UI with direct database access.
- [x] **Fix the new-account `AssignedAgency` null path.** `NewUserViewModel.CreateUser`
      dereferences `AssignedAgency.Id`, but `AssignedAgency` is never initialized and
      `NewUserWindow` has no agency selector. Add the intended agency source/selection,
      validate it before user creation, and cover the flow with a test. This is the
      nullable warning currently emitted at `NewUserViewModel.cs:70`.

### P2 — MVVM boundaries and maintainability

- [ ] **Remove the Comprehensive Assessment service locator.**
      `ComprehensiveAssessmentWorkspace` constructs its own ViewModel through
      `Application.Current.Services`. Create the workspace/ViewModel through the
      composition root or a typed injected factory so dependencies are explicit and the
      workspace is testable.
- [ ] **Move concrete dialogs and WPF application access out of ViewModels.** Several
      ViewModels directly call `MessageBox.Show`, `Application.Current`, `ShowDialog`, or
      depend on a concrete `UserMessageDialog`. Replace these with narrow interaction,
      navigation, notification, and dispatcher/scheduler abstractions—or events handled
      by the View. Keep purely visual window ownership in code-behind.
- [ ] **Make View event subscriptions detachable.** `ClientsView.OnDataContextChanged`
      attaches an anonymous `ComplianceReviewRequested` handler without removing it from
      the previous ViewModel. Use a named handler or
      an explicit attach/detach lifecycle. On unload, also detach
      `ComprehensiveAssessmentWorkspace` from its parent ViewModel. Verify that view
      recreation or user switching cannot produce duplicate dialogs or retained views.
- [ ] **Split `CaseManagerDashboardViewModel` by feature responsibility.** It is roughly
      900 lines with about sixteen constructor dependencies and currently coordinates
      notes, forms, upcoming work, incentives, clients, statistics, reviews, providers,
      calendar, and compliance effects. Retain it as a thin dashboard/module coordinator
      while moving feature state and commands into focused child ViewModels/application
      services.
- [ ] **Split `NewClientViewModel` by feature responsibility.** It is roughly 830 lines
      and owns client CRUD/editing, notes loading, forms, reviews, appointments,
      healthcare reference data, and journal autosave. Extract focused client-profile,
      journal, appointment, and compliance/document components while preserving the
      current single Overview experience.
- [ ] **Extract a versioned Comprehensive Assessment definition catalog.**
      `ComprehensiveAssessmentViewModel.BuildSections` currently owns question text,
      guidance, support applicability, validation, navigation, mapping, persistence, and
      autosave. Move the content/schema into a dedicated, versioned definition provider
      so questions can grow or branch without modifying the editor and so existing JSON
      answers remain reproducible against the definition version that created them.
- [x] **Add an automated domain/workflow test project.** Prioritize
      `EvaluateComplianceGate`, `EvaluateBillingWindow`, midnight and back-entry rules,
      permanent unbillability, override separation, assessment support exclusions,
      completion rules, ownership, serialization compatibility, and workflow transitions.
      No test project was found during this audit.

### P3 — cleanup and consistency

- [ ] **Inject `IPasswordHasher` into `AuthService`.** It is already registered, but
      `AuthService.AuthenticateAsync` constructs `PasswordHasher` directly.
- [ ] **Align DI registrations with effective lifetimes.** `ScratchpadViewModel`,
      `NewClientViewModel`, `UserManagementViewModel`, and `PendingApprovalsViewModel` are
      registered transient but captured by singleton parents. Decide whether each is
      intentionally session-long; register it accordingly or create it through an
      explicit scope/factory.
- [ ] **Delete the hidden legacy client-entry panel and stale state.** `ClientsView.xaml`
      still contains the old entry form and chevron in two zero-width columns, while
      `NewClientViewModel` retains `IsEntryPanelOpen`, `ToggleEntryPanel`, and legacy
      naming. Remove the duplicate markup/commands after confirming the inline Overview
      editor covers add, edit, cancel, and delete.
- [ ] **Resolve the disabled cycle-form feature switch.** The disabled
      `EnableEnsureCycleFormsOnLoad` path is still awaiting the duplicate-form reconciliation;
      the compiler warning has been removed. Either remove the obsolete path or replace it with a
      deliberate supported configuration after the duplicate-form reconciliation is
      settled.
- [ ] **Make the README architectural claim accurate.** It currently says ViewModels
      have no knowledge of Views and window creation uses factories throughout. Update it
      after the boundary work above, or describe the remaining pragmatic exceptions until
      they are removed.

### Audit verification

- [x] Rebuilt the current working tree successfully to an isolated output directory while
      the running Sati process held the normal output DLL. The rebuild completed with no
      errors and two distinct warnings: the `AssignedAgency` nullable dereference and the
      constant-false unreachable code described above.
