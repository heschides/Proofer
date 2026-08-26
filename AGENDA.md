

# Sati — Refactor Agenda

## Release 1.2.25 — 2026-08-26

- [x] Add consumer email, the focused calendar-day note view, calendar failure containment, and
      future-dated non-billable reminders, with shared persistence rules and regression coverage.
- [x] Advance the client, API, installer builders, release notes, and release tests together to
      1.2.25 so the earlier 1.2.24 Local artifact is never replaced by different bytes.
- [ ] Apply and verify migration `20260825163103_AddConsumerEmail` against identity-validated
      Azure `SatiDemo`, publish API 1.2.25, and confirm live, ready, version, and contract parity.
- [ ] Generate and validate `SatiDemoSetup-1.2.25.exe` and `SatiLocalSetup-1.2.25.exe`; record their
      SHA-256 hashes before distributing either installer.

## Future-dated calendar reminders — 2026-08-26

- [x] Convert any future note date to a Scheduled Reminder in one shared contracts rule, repeated
      by the desktop-local and API persistence boundaries.
- [x] Preserve the selected date and narrative while removing service time, minutes, form/visit
      facts, and justification so a reminder cannot drift into review, productivity, or billing.
- [x] Refresh the calendar after note saves and show the reminder on its dated calendar entry with
      explicit non-billable wording; keep undated Reminders on the existing journal-only path.
- [x] Cover UI conversion, rendered date access, local persistence, calendar retrieval, API
      normalization, tenant-scoped reads, and the undated-row guard with automated tests.

## Calendar stability and focused day — 2026-08-26

- [x] Contain calendar load, exemption-update, and downstream refresh failures so they produce an
      inline retryable message instead of escaping through WPF's dispatcher and crashing Sati.
- [x] Protect year navigation from stale async responses, invalid bounds, and missing sessions;
      preserve the selected date when refreshed calendar objects replace the prior year model.
- [x] Add an accessible focused-day view showing the service-date notes, client, narrative,
      service time, duration, status, and daily totals, with keyboard-operable day buttons.
- [x] Correct local monthly and yearly note boundaries to include the entire final day, and enforce
      the signed-in user's scope in the local exemption service as the API already does.
- [x] Cover the ViewModel, rendered WPF view, local data services, and API calendar boundaries with
      regression, concurrency, failure-containment, accessibility, and tenant-isolation tests.

## Release 1.2.23 — 2026-08-25

- [x] Add consumer-profile flags for case-manager DHHS representation and Modivcare,
      plus caseload filters for those responsibilities, representative payee, VR, and
      the existing waiver/employment service flags.
- [x] Add and validate the additive People migration. The guarded, exact-IP Demo
      operation verified `SatiDemo`/`Demo`, added both columns for 177 consumers,
      wrote migration `20260825144021_AddConsumerNavigationFlags`, and removed the
      temporary SQL firewall rule immediately afterward.
- [x] Deploy API 1.2.23 with OneDeploy deployment
      `14bfdc40a2ec44b9be517c1c7884cdd9`. `/health/live` and `/health/ready` returned
      HTTP 200; `/health/version` reports 1.2.23 and contract revision `9F387A68FF69`.
- [x] Package `SatiDemoSetup-1.2.23.exe` and `SatiLocalSetup-1.2.23.exe`. Record final
      installer acceptance evidence before distributing outside this workstation.

## Release 1.2.22 — 2026-08-23

- [x] Bump the client, API, and Carika to 1.2.22 and rewrite the Settings release notes around the
      single note panel, session continuity, and the journal-reminder route.
- [x] Confirm the Demo API needs no publish: its `/health/version` reports
      `contractRevision 9F387A68FF69` and `releaseVersion 1.2.21`, and the committed
      `ApiSurface.Revision` is byte-for-byte the same fingerprint over 102 routes, including
      `POST /api/v1/auth/renew`. The deployed surface already matches this source.
- [x] Package `SatiDemoSetup-1.2.22.exe`
      (sha256 `605ff0634d485cdc3610d71608b3665992c1418d3d7e04f454251c71762be484`) and pass
      installer acceptance: five launches, each responding, closing gracefully, exit code 0,
      installed version 1.2.22.0, acceptance copy removed.
- [ ] **Publish the API at 1.2.22.** Not required for function — the route surface is identical, so
      the compatibility check stays quiet — but `/health/version` will keep reporting
      `releaseVersion 1.2.21` until it is redeployed, which makes the number a poor way to tell what
      is running. Deploy from
      `Sati.Api/Properties/PublishProfiles/sati-demo-api-satilogica - Zip Deploy.pubxml`.
- [x] Package `SatiLocalSetup-1.2.22.exe`
      (sha256 `aef3463029ad16bf589f5af00e8c86a758f1c46a2a94333b916e718aac3c8242`) and pass payload
      validation: version 1.2.22.0, integrated security confirmed, acceptance copy removed. The
      embedded LocalDB prerequisite is Microsoft-signed `SqlLocalDB.msi`, sha256
      `0891BF47652D88F06D76A339A5DB37DDC9C801D1E973E14B3B551609F1CFA4CB`.
- [ ] **Keep a durable copy of `SqlLocalDB.msi`.** The 1.2.22 build sourced it from a
      `%TEMP%\SatiInstallerInspect-*` folder left by an earlier installer inspection. The signature
      check makes that safe — a modified MSI cannot carry a valid Microsoft Authenticode signature,
      and it was verified before use — but a release input that lives in a directory Windows may
      clear at any time is not a reproducible one. Archive it somewhere permanent and record the
      hash above.
- [ ] **Decide whether machine-local commentary belongs in a shipped installer.** The Local build
      packages the gitignored `appsettings.json` verbatim, including its `//production` note
      describing this laptop's database history. It carries no credential and no client data, and
      the builder already rejects SQL credentials, but it is internal commentary that reaches an
      end user's disk.

## Ended Demo session — 2026-08-23

- [x] Treat a refused `POST /api/v1/auth/renew` as terminal: latch the client shut, raise
      `CloudApiClient.SessionEnded` once, and fail every later authenticated call locally with
      `CloudSessionEndedException` instead of repeating the rejected renewal per screen.
- [x] Clear the latch when a new token is set, so signing in again reopens the same client.
- [x] Surface an ended session in the Switch User dialog as a stated expiry rather than an empty
      account list, and repair the error line, which was collapsed by a `Visibility` binding that
      outranked its own style and ran a string through `BoolToVisibilityConverter`.
- [x] Renew on the token's own schedule (`SessionKeepAlive`), waking at `expiry - RenewalMargin`
      rather than waiting for a request to land inside a five-minute window that an idle app never
      enters. A fixed poll cannot substitute: a twenty-minute interval steps over the window and
      wakes holding an expired token.
- [x] Gate renewal on real user input, so an active desktop keeps working to the server's
      twelve-hour cap while an unattended workstation lapses after one token lifetime. The idle
      allowance is the renewal gap, not the token lifetime — measuring against the lifetime can
      never close the gate on the first cycle and silently doubles the timeout.
- [x] Offer re-authentication in place: the shell prompts on `ISessionLifetime.SessionEnded`. The
      same person signing back in is not an account switch, so nothing reinitializes and unsaved
      agenda text survives to be saved; a different account takes the existing switch path.
- [ ] Translate `CloudSessionEndedException` into `SessionExpiredException` across the remaining
      cloud services. Only `CloudUserService.GetAllAsync` does so today, so other screens still
      report an ended session through their generic failure path.

## Representative-payee profile — 2026-08-22

- [x] Add `CaseManagerIsRepPayee`, monthly income, and regular check-request needs to the
      authoritative Person profile, API contracts, lifecycle history, and Local/Demo persistence.
- [x] Add accessible Yes/No Profile controls with conditional, validated amount and recurring-needs
      fields; selecting No clears financial details that no longer apply.
- [x] Add the additive People migration and test validation, tenant isolation, audit/version history,
      contract mapping, migration shape, API compatibility, and accessible XAML controls.
- [x] Apply the guarded migration to identity-validated Local `SatiProduction` and Azure `SatiDemo`,
      deploy the matching API, and package both 1.2.21 installers (2026-08-23).
- [ ] Design the later billing-department check-release notification as its own audited workflow with
      request state, amount, purpose, due date, requester, approval/release evidence, and idempotency.
      A representative-payee profile edit must never itself authorize or initiate payment.

## Database wait feedback — 2026-08-22

- [x] Add one payload-free, reference-counted activity tracker for Demo HTTP and Local Production EF
      calls, including success, failure, cancellation, and reader-disposal cleanup.
- [x] Spin the accessible watercolor Bodhi leaf immediately while data calls are active.
- [x] After eight uninterrupted seconds, show a modeless patience window that closes automatically
      when the final overlapping request completes and never appears late after a short request.
- [x] Add deterministic timing, overlap, HTTP failure, EF reader-lifecycle, and XAML accessibility
      coverage.
- [x] Add an all-role Settings preview that holds the same activity tracker for 12 seconds without
      querying a database, so the immediate leaf and eight-second patience window can be tested.
- [x] Classify Demo connectivity failures, retry only proven DNS failures where no request was
      sent, and keep ambiguous writes from being repeated after timeouts or connection loss.
- [x] Present safe, specific Scratchpad recovery guidance without logging or displaying note text,
      and remove expected spinner-timer cancellation exceptions from debugger output.

## Carika limited client — 2026-08-21

- [x] Add an Avalonia client using safe contracts and the API, without EF/SQL/LocalDB.
- [x] Add authenticated caseload profile display and API-mediated draft-note creation.
- [x] Add contract-backed note type, workflow status, and conditional form-type selections, with
      stale draft-load and transcription suppression when the selected person or narrative changes.
- [x] Refresh the limited client's visual hierarchy and accessible note-entry status messaging.
- [x] Add DPAPI-protected, actor/person-bound optional local drafts.
- [x] Add local-only Whisper transcription of an existing WAV with no cloud fallback or auto-download.
- [ ] Add ephemeral microphone capture after privacy indicators, cancellation, device selection,
      audio-lifetime behavior, and accessibility are designed and tested.
- [ ] Add session expiry/re-authentication, sign-out/zeroization, note editing/concurrency UI,
      integration tests, packaging, threat modeling, model controls, and deployment review.

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
- [x] Correct the asynchronous save-on-close handoff and permit narrowly scoped Global Admin
      self-service password changes in release 1.2.6, with regression coverage proving agency
      user-password resets remain forbidden.
- [x] Route Global Admin account switching through neutral credential entry in release 1.2.7,
      preserving the agency-directory prohibition and containing ordinary picker load failures.
- [x] Show assigned supervisor names explicitly in both administrative and personal user profiles,
      and replace the legacy artwork with a multi-resolution professional application icon in
      release 1.2.8.
- [x] Complete the local release 1.2.9 durability slice: refine the darker Bodhi-leaf icon, finish
      the accessibility audit, make theme resources host-independent, runtime-render the feature
      views, and require repeated normal window shutdown in installer acceptance.
- [x] Regenerate and visually inspect all ten pages of the version-matched offline Demo fallback.
- [ ] Complete authenticated agency-Admin preflight, external-machine installer attestation,
      presenter rehearsal, and final evidence binding for the exact 1.2.9 installer.

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
- [x] Accessibility audit — icon-only buttons expose accessible names;
  compliance checkboxes are labeled; overdue matrix cells include a visible text status
- [ ] Verify migrations apply to an *empty* database before each release. A working
  database only ever receives new migrations and has been hand-patched over time, so two
  classes of breakage stay invisible locally and appear only when someone builds from
  zero — a new machine, a new agency, or a fresh installer test. Both were found
  2026-08-16 and are now fixed:
  1. *Migrations replaying earlier ones* — recorded as applied, so they never re-run
     locally. `UnitstoDecimal` re-added `Notes.ReturnedById` (SQL 2705); `SyncModelState`
     repeated every operation in `AddSupervisorFieldsToNote`. Both bodies are emptied,
     with the files kept so the migration IDs stay in the chain.
  2. *Model/schema drift* — `Notes.Minutes` and `Notes.StartTime` were in the model and in
     every working database, but no migration created them, so a fresh database produced a
     `Notes` table the model could not query.
     `20260816120000_AddNoteMinutesAndStartTime` adds them, `COL_LENGTH`-guarded so it is a
     no-op on databases that already have the columns without a history row for it.
  `Add-Migration` cannot catch case 2: it diffs the model against
  `SatiContextModelSnapshot`, and the snapshot already listed both properties. The gap is
  between the snapshot and what the migration files actually build.
  Run `scripts/Test-MigrationChain.ps1` (no database needed) and
  `scripts/Test-SchemaDrift.ps1` (against a *from-scratch* database) before each release.
  Verified 2026-08-16: 69 migrations replay clean, and a database built from empty reaches
  the login window with all 349 model columns present.

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

### Completed 2026-08-20 -- Tomorrow's Agenda

- [x] Add Tomorrow's Agenda beside Today's Work in the existing scratchpad panel, with both drafts
  included in autosave, app-close, and account-switch flushes.
- [x] Store the agenda as the next workday's existing per-user Scratchpad row so it becomes Today's
  Work automatically without a midnight copy or duplicate-promotion state.
- [x] Put the Friday/Saturday/Sunday-to-Monday rule in `Sati.Contracts.V1.WorkAgendaDates` and use
  the API's agency-local clock to select the cloud row; holidays remain deferred until Sati has an
  authoritative agency holiday-calendar policy.
- [x] Preserve revision conflicts independently for both tabs and cover the calendar rule, route
  ownership, stable row identity, and cross-user isolation with automated tests.
- [x] Deploy the matching API surface to hosted Demo with OneDeploy
  `2bf8bd7fb3ea4ca39595a87da836f727`; readiness returned 200 and the live contract revision
  `A4FB297F7FE6` matched the release build.

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

Closed-world revision shipped 2026-08-22: prior notes, assessments, Bio, deadlines, contacts, and
other historical records were removed from AI context. The selected-client route now returns only
the own-caseload person's ID and first name and never receives rough-note text. Every rough fragment
and every selected Visit checkbox, selector value, attendee, or detail becomes a stable required
fact. The model must return fact-cited JSON; shared deterministic rules reject the whole result for
omission, selector-value loss, wrong-section use, or unsupported names, numbers, quotations,
negation, and content words. `Not documented`, `Not assessed`, and unchecked controls assert nothing.

The original narrative remains unchanged until explicit acceptance. A source fingerprint and latest-
request identity prevent a result from publishing or being accepted after the person, narrative, or
template state changes. Consumer presence is explicitly selected instead of defaulting to present.
Cross-consumer model switching fails closed if unload fails. Follow-up is supported by a current fact
or exactly `No follow-up was documented.`; historical form deadlines are no longer inferred.
The model can explicitly select Sati's validated deterministic renderer instead of risking an
unsupported rewrite. Runtime failures and rejected model output use the same renderer with a visible
warning. The opt-in device gate requires safe completion of all synthetic scenarios through the real
local runtime; explicit safe deferral is accepted rather than forcing unsupported prose.

Before any shared or production release:

- Obtain the authoritative agency case-note policy and replace/refine `AI_CASE_NOTE_RULES.md`.
- Assemble at least 50-100 de-identified rough-note/approved-note examples spanning visits,
  contacts, forms, sparse notes, ambiguity, quotations, negative statements, and safety content.
- Define and pass acceptance thresholds for zero invented facts, retained attribution/negation,
  required formatting, latency, memory use, and accessibility.
- Run the separately controlled local-model/device evaluation suite; deterministic compiler,
  grounding, tenant-scope, and stale-client regressions are now automated. The representative
  target-device smoke gate runs only with `SATI_RUN_LOCAL_AI_MODEL_EVAL=1` so ordinary CI cannot
  acquire model weights implicitly.
- Persist an audit record for accepted AI drafts: source, draft, final user-edited text, rule-set
  version, model alias/version/hash, user, timestamps, and explicit acceptance. Decide retention and
  access rules before adding this to the database.
- Add model-download/retry controls; test first-run, cached/offline, low-disk, unavailable-model,
  corrupt-cache, and runtime-unload behavior on supported devices. In-flight generation now cancels
  when its selected client or source inputs change.
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
- [x] **Sales-tax freeze (slice 4).** Landed 2026-08-15. Tax is calculated from
      `Settings.SalesTaxRate` and frozen as an amount on the request, with a per-request
      manual override.
- [x] **AT request Save + Publish PDF.** Landed 2026-08-15 as one slice, as intended.
      Open/Save/Publish/Reopen/Export, attestation, publication lock, and the generated
      PDF. See the decision entry of the same date.

### Deferred (needs a later slice)
- [x] **Retain the executed AT request PDF — decided against, 2026-08-15.** Not deferred;
      rejected. A published request cannot change, so the PDF is a pure function of a frozen
      record and storing it would duplicate that record, screenshots and all. See the decision
      entry "The PDF is regenerated, never retained." The one residual risk is exporter code
      changes, noted on the OADS-layout item below.
- [ ] **Match the OADS Authorized Payment Information Form layout.** The generated PDF is a
      Sati document carrying the same information, not a reproduction of the state form.
      Requires the blank form. Layout change only — `ATRequestPdfExporter` is the sole owner.
      NOTE when this lands: PDFs are regenerated rather than retained, so changing the layout
      changes how every historical request re-renders. The figures stay correct; the
      presentation will not match what was submitted at the time. Accepted, but decide
      deliberately rather than discovering it afterwards.
- [ ] **Decide whether an electronic-signature standard applies to AT requests.** Sati records
      an attestation, and says so on the document. Whether OADS requires a signature meeting a
      specific standard is a question for counsel and OADS, not for the repository.
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

### Deferred from the service-day work (2026-08-14)

- [ ] **Surface service start times in the remaining note lists.** The focused calendar day now
      shows the service-time range through the shared `ServiceTimeline` rule. `NotesLogView` and
      `ClientsView`'s note grid still show only date and minutes; a time column there would let a
      case manager spot a gap or a clash without opening the entry panel.
- [ ] **Extend the overlap rule to the other note-creating paths.** `NewClientViewModel` and any
      other flow that calls `INoteService.AddNoteAsync` directly writes a note with no start time.
      Those notes claim no time and conflict with nothing, which is correct but means the day bar
      does not show them. Decide whether those flows should collect a start time.
- [ ] **Decide whether Scheduled notes should reserve time softly.** A scheduled note currently
      holds its minutes like any other commitment, so converting a plan into a separate logged
      note reports a conflict. Editing the scheduled note in place avoids it. If case managers
      routinely write a fresh note instead, a "planned" band that warns rather than blocks may fit
      the real workflow better.

### Deferred from the API security audit (2026-08-14)

See `API_SECURITY_AUDIT.md` for the full findings. Items reviewed and consciously left alone:

- [ ] **Decide the sign-in lockout policy deliberately.** `LoginAttemptGuard` allows 12 attempts per
      username per minute, so anyone who knows a username can hold that person out of the system.
      Every lockout design trades this against credential stuffing; this one has not been chosen on
      the record. Options include per-IP limits alongside per-username, exponential backoff instead
      of a hard window, or an unlock path for a supervisor.
- [ ] **Give the sign-in guard shared state before running more than one API instance.**
      `LoginAttemptGuard` is per-process, so the effective attempt limit multiplies by the instance
      count. Acceptable for single-instance Demo; not for a scaled deployment.
- [ ] **Make person lookups consistently use `TenantAccess.OwnsPersonAsync`.** `POST /at-requests`,
      `GET /people/{personId}/reviews`, and `GET /people/{personId}/appointments/latest` resolve the
      person by id and authorize on the owning user rather than also asserting
      `person.AgencyId == actor.AgencyId`. Cross-agency access is blocked today because
      `CanAccessUserAsync` requires the target user to be in the actor's agency, so this is
      consistency and defense in depth rather than an open hole. Needs a test covering a person row
      whose agency disagrees with its owner's.

### Documentation and naming cleanup (2026-08-15)

- [ ] **Rename `Data/Cloud/CloudUnavailableServices.cs`.** The filename is a historical misnomer.
      It now contains twelve fully migrated HTTP service implementations (`CloudUserService`,
      `CloudSupervisorService`, `CloudBillingService`, and others), not unavailable stubs. Anyone
      reading the file list will draw the wrong conclusion about how much of Demo is migrated.
- [x] **Settle the company name's casing.** Resolved 2026-08-15: `SatiLogica` is the formal
      rendering everywhere in code, documentation, and user-visible strings. `Satilogica` remains
      acceptable in informal prose. Azure resource names stay lowercase because DNS requires it.
      Applied across the `<Company>` property, installer and uninstaller, Start Menu folder,
      registry publisher, `%LOCALAPPDATA%` paths, and scripts. No upgrade story was needed:
      Windows paths and registry keys are case-insensitive, so existing installs and stored data
      under `Satilogica\` resolve unchanged.
- [ ] **Introduce the program names only as each ships.** `SatiLogica` is the platform;
      `Sati` is the case-management program. `Karuna` (service-provider documentation) and
      `Upekkha` (OADS-facing waiver management) are roadmap names for work that does not exist
      yet and should stay internal until the quarter before each ships.
- [ ] **Reconcile the live Demo API version.** `DATABASE_ENVIRONMENTS.md` last recorded the deployed
      API at 1.2.8 while the packaged client is 1.2.17. Deploy and verify a matched pair before
      collecting final company-demo evidence, then update the release-history note.

### Organization identity and the Karuna handoff (2026-08-15)

Design recorded in `DECISIONS.md`, "Provider directory entries are local knowledge about a shared
organization". The identifier capture landed on 2026-08-15 because it is the only part that cannot
be added retroactively. Everything below waits for Karuna.

- [x] **Capture durable identifiers on provider directory entries.** `Provider.Npi` and
      `Provider.MaineCareProviderId`, NPI check-digit validated, unique per agency via filtered
      indexes, enforced in both the API and the transitional local service.
- [ ] **Introduce the Organization registry.** Platform-wide canonical identity — legal name and
      external identifiers only. Add `Provider.OrganizationId` as a nullable link in the same
      migration. Deliberately not created yet: do not add a column pointing at a table that
      does not exist.
- [ ] **Build the match-and-link flow for onboarding.** Exact match on identifier first, then a
      reviewed candidate list for name/address near-matches. Linking must never merge or delete a
      directory entry, and must never repoint an existing foreign key.
- [ ] **Model `AgencyRelationship`.** Which case-management agencies have an active passthrough
      relationship with which organization. Without it, an organization going live would appear in
      every agency's passthrough picker — both wrong billing options and a cross-tenant
      disclosure.
- [ ] **Add the published passthrough contact set,** maintained by the organization's own tenant.
      An explicit outward-facing payload, never a projection of internal contact records. Validate
      it for completeness the same way the local form is validated: one bad publish would degrade
      every linked agency's billing contacts at once.
- [ ] **Add contact resolution with local override.** Published wins by default once linked; local
      values are demoted rather than deleted so an agency can re-assert its own named contact in
      one click.
- [ ] **Notify and audit the swap.** A notice to each affected agency when it lands, a quiet
      indication at point of use, and audit events on both sides — these contacts feed a financial
      document.
- [ ] **Flag stale drafts rather than re-snapshotting.** An AT request drafted before a swap and
      submitted after must report which vendor fields changed and let the user decide, following
      the note conflict-reconcile idiom. Never silently rewrite a financial document.
- [ ] **Consider backfilling identifiers for existing directory entries.** Rows created before
      2026-08-15 have none. A one-time prompt when a provider is next edited would close the gap
      without a bulk data exercise.

## Note pipeline — outstanding after the 2026-08-17 review

- [ ] **Give an approved note an amendment path.** Approved is terminal for every actor, so a
      supervisor who approves in error has no remedy even before a claim line exists. The right
      shape is an immutable approved version plus a linked amending note, not an un-approve that
      rewrites the record. Touches the claim-line linkage and the 837P path, which is why it was
      not folded into the workflow-table work. See `DECISIONS.md`.
- [ ] **Audit the abandonment sweep.** Neither `NoteService.UpdateAbandonedNotesAsync` nor
      `POST /notes/abandon-overdue` records an audit event, so a status change the system makes on
      its own is the one transition with no trail. Bulk writes need a summary event rather than
      one per note.
- [ ] **Make the overdue sweep respect the concurrency token.** The API route uses
      `ExecuteUpdateAsync`, which bypasses `Revision`, so a note being edited at that moment can be
      abandoned underneath its author. Bounded today by the `Pending`-only filter, but it is the
      one write in the pipeline that ignores optimistic concurrency.
- [ ] **Close the create-time gap on overlapping service time.** `AddNoteAsync` checks for an
      overlapping block and then saves in a separate step, with no transaction spanning the two.
      Two concurrent saves can both pass the check. The unique index that protects claim lines has
      no equivalent here; a database-level exclusion constraint would be the durable fix.
- [ ] **Give the local tenant rules one owner.** `Data/LocalTenantAccess.cs` mirrors
      `Sati.Api.Security.TenantAccess` by hand because the two query different entity types against
      different contexts. Two hand-written copies of a scope rule is exactly what the platform rule
      about single ownership warns against; it is tolerable only while the desktop keeps a local
      EF path at all.
- [ ] **Decide whether the desktop may assume Eastern time.** The desktop review path uses
      `DateTime.Today` where the API now uses the agency clock. Correct on a Maine workstation,
      wrong anywhere else.

## Hosted Demo migration deployment — found live on 2026-08-17

- [x] **Detect a database behind the model.** `SchemaDriftHealthCheck` compares the API model's
      tables and columns against the database and fails `/health/ready` naming what is missing,
      instead of letting the gap surface as a 500 from whichever feature touches the new column
      first.
- [ ] **Decide how SatiDemo actually receives migrations.** Nothing advances it today. The desktop
      runs `Database.Migrate()` (`App.xaml.cs:238`) but only when connected straight to SQL, and in
      Demo it goes through the API over HTTP; `Sati.Api` never migrates; `scripts/Publish-Demo.ps1`
      has no database step. A release that adds a column therefore ships code the database cannot
      satisfy. On 2026-08-17 that took out `GET /providers` — and with it AT request creation —
      and `POST /incidents`, so the telemetry channel could not report the outage either.
      The detector above makes this visible; it does not fix it.
- [ ] **Reconcile migration history with reality on the long-lived databases.** SatiDemo and
      SatiProduction have acquired columns outside the chain, so `__EFMigrationsHistory` and the
      actual schema disagree in both directions. EF's idempotent script guards only on history and
      fails with SQL 2705 on a column that exists without its history row. Until the two are
      reconciled, applying migrations to those databases needs existence-guarded scripts rather
      than the generated one.

## Journal reminders — outstanding after the 2026-08-18 change

- [x] **Deploy the API so the reminder route exists.** Done by the 1.2.21 deployment and confirmed
  2026-08-23 by unauthenticated probe: `people/{id}/journal/entries`, `people/{id}/ssn`,
  `people/{id}/forms.pdf`, and `people/{id}/agency-release.pdf` now all answer 401, where the last
  three answered 404 on 2026-08-19. The original note follows for the record.
  The hosted Demo API was release 1.2.17 on
  2026-08-18, which predates `POST /people/{personId}/journal/entries`. Until it is published from
  `Sati.Api/Properties/PublishProfiles/sati-demo-api-satilogica - Zip Deploy.pubxml`, every Demo
  reminder takes the transitional whole-journal fallback and the client page says so. Verify with an
  unauthenticated POST to that route: 401 means the route is present, 404 means the server is still
  behind.
  **The same deployment now gates four more routes.** Confirmed live on 2026-08-19 by unauthenticated
  probe — `people/{id}/notes` and `people/{id}/contacts` answer 401 while `people/{id}/ssn`,
  `people/{id}/forms.pdf`, and `people/{id}/agency-release.pdf` answer 404. In the DHHS wizard that
  404 surfaces as "the record was not found or is outside your caseload", which points the case
  manager at a caseload problem that does not exist. This is the third time a behind-server has been
  diagnosed as something else; see the startup version-comparison item below, which would replace
  per-route handling with one check.
- [x] **Remove the whole-journal fallback once nothing predates the route.** Done 2026-08-23 once
  the probe above showed the route live everywhere it needed to be. The 404 `catch`,
  `JournalReminderResult.UsedLegacyJournalWrite`, the client-page warning band text it drove, and
  `Sati.Tests/JournalReminderFallbackTests.cs` are removed; `ApplyExternalJournal` now clears a
  stale warning instead of setting one. See `DECISIONS.md`.
- [x] **Detect a behind-server generally.** Built 2026-08-19 and extended 2026-08-22. **Comparing the release number would
  not have worked** — on the day this was written the hosted API and the client both reported
  1.2.17 while the server was missing five routes, because a release is numbered when it is cut and
  not when a route is added. The comparison is therefore over the route and persistence-contract
  manifest: `ApiSurface` in `Sati.Contracts.V1` holds the generated route list, named contract-shape
  revisions, and a fingerprint of both,
  `/health/version` reports the fingerprint (never the list — that is a map of the attack surface),
  and `IApiCompatibilityService` compares once at sign-in and raises a "SERVER OUT OF DATE" banner
  with the cause. `ApiSurfaceTests` fails the build if the route manifest drifts from the API's real
  endpoint table and proves that a contract-shape change alters the fingerprint. This prevents a
  newer client from silently sending profile fields an older server would ignore. The check never throws and never blocks
  sign-in: an unreachable server is a network problem other screens report better.
- [ ] **Audit other `GetAsync<T>` calls for legitimately empty bodies.** `GetJournalAsync` threw on
  any client whose journal was never written, because `GetAsync<string?>` treats a null result as an
  empty response. Fixed there with `GetStringOrNullAsync`; other nullable-scalar routes may carry
  the same latent fault.
- [x] **Distinguish journal reminders from dated calendar reminders.** An undated Reminder remains a
  stamped journal entry and is not duplicated. A future-dated Reminder is stored once as a
  non-billable Scheduled note so the calendar, note history, and upcoming-event views can find it.

## DHHS form fill — remaining verification and profile work

The official-form filler, encrypted cloud SSN envelope, audited API routes, local
and cloud service implementations, migrations, and desktop workflow are now built.
See `DECISIONS.md`, "An official DHHS form is filled, never redrawn" and "An SSN is
cloud-only".

- **Profile gap the forms exposed.** `Person` has no email. The Release form's
  optional combined telephone/email box receives the phone number when present;
  email remains blank for hand-completion until the profile has an appropriate
  email field.
- **Field rendering is unverified against DHHS.** The byte comparison proves the
  page is unchanged; it does not prove a DHHS intake worker accepts the field
  appearance. The forms set `/NeedAppearances` so the viewer rebuilds appearance
  streams. Worth one printed submission before this is used in earnest.

## Production behind the API, without losing local Production (2026-08-18)

Direction set by Josh: "move local Production behind the API" means **adding
API-backed access to the Production database while preserving both operating
modes**, not retiring the local one. The desktop must continue to support Demo and
Production as explicitly selected data sources.

Requirements for that work:

- Explicit environment selection, extending the existing bootstrap chooser and the
  validated hard-coded environment mapping in `DATABASE_ENVIRONMENTS.md`.
- Separate credentials, configuration, service identities, and Key Vault keys per
  environment.
- Authorization checks on the API-backed Production path equal to Demo's —
  `TenantAccess` plus `ValidatedActorFilter`, not a relaxed variant.
- Conspicuous UI labeling, as the Demo indicator does today, so the operating mode
  is never ambiguous on screen.
- Safeguards against cross-environment reads and writes. `DatabaseIdentityValidator`
  and `dbo.SatiDatabaseIdentity` already gate this at connection time; per-environment
  Key Vault keys extend it to the data itself, since one environment's ciphertext is
  inert against the other's vault.

**Where plaintext SSNs exist, by mode** (see `DECISIONS.md`, "An SSN is cloud-only"):

| Mode | Plaintext SSN |
|---|---|
| Demo (API-backed) | Only in API process memory during entry and form fill, and inside the generated PDF. Demo data is synthetic. |
| Production via local EF (today) | None. SSN is cloud-only; the column is never populated or read on this path. |
| Production via API (planned) | Same as Demo: API process memory, and the generated PDF. |

Never at rest outside ciphertext, never in a DTO, an EF entity exposed to a client,
a log, telemetry, an exception, a cache, or a backup.

**Document generation splits by where the protected data is, not by document.**
Superseded by Josh's 2026-08-18 direction that local Production keep generation on
the workstation: `DhhsFormFiller` lives in the shared `Sati.Forms` library and runs
in whichever process holds the data. On the cloud path it runs server-side, because
that is where the decryptable SSN is and it must not travel. On the local path it
runs on the workstation with no network and no SSN at all. One implementation of the
stamping, two callers — a second copy would be the duplication CLAUDE.md forbids.
The AT request PDF carries no protected field and stays where it is.

**The generated PDF is itself plaintext.** The form has an SSN box, so the finished
document contains the number in the clear by design. Encryption protects the
database; it cannot protect the artifact the fill produces. Once a case manager
saves, prints, emails, or uploads that PDF, the number is loose in whatever handled
it. The controls that matter there are the BitLocker requirement in
`OPERATIONS.md`, agency-approved storage locations, and the audit event on
generation — not anything in the crypto.

### DHHS form work status

Everything below the UI is built and tested as of 2026-08-18: the encrypted columns
and the `AddEncryptedSsn` migration, the audited `POST /people/{personId}/forms.pdf`,
the SSN read and write routes, log-redaction enforcement, and both
`IDhhsFormService` implementations.

- **Migration applied 2026-08-19.** `AddEncryptedSsn` and the remaining queued schema
  migrations were applied to `SatiDemo`; local `SatiProduction` was already current.
  Controlled migration deployment remains manual; see the hosted-Demo item above.
- **The Demo Key Vault key was provisioned 2026-08-20.** The Demo API now receives
  the versionless `Ssn__KeyUri` for `ssn-demo` in the purge-protected
  `sati-demo-kv-satilogica` vault. Its system-assigned identity has only `wrapKey`
  and `unwrapKey`. Production still requires a separate key before the Production
  API path stores SSNs; never reuse the Demo vault or key there.
- **Synthetic Demo SSNs were seeded 2026-08-20.** The Admin-only operational route
  encrypted deterministic synthetic values for all 177 agency People through the
  Demo Key Vault and recorded `person.ssn-updated` per Person. The route remains
  effective only when startup validates exactly `SatiDemo` / `Demo`; ordinary SSN
  routes remain own-caseload only. `scripts/Seed-DemoSsns.ps1` is the repeatable
  wrapper for a future approved Demo reset. Do not write SSN columns directly in
  Azure SQL.
- **Demo users and agencies carry no synthetic representative information,** so every
  Demo fill currently reports the representative boxes as needing hand-completion.
- **Desktop UI completed 2026-08-19.** The selected consumer now has a `DHHS Forms`
  workspace covering both official forms, grouped consumer-directed selections,
  masked SSN status/update on the cloud path, local-Production explanation, PDF save,
  missing-field warnings, automation names, keyboard reachability, live status text,
  and selection clearing when the consumer changes. Signatures and signing dates are
  intentionally left to the fillable PDF rather than treated as ordinary data entry.
- **Local Production always prints a blank SSN box,** because SSNs are cloud-only and
  that path has no key. Reported through `DhhsFormResult.BlankFields` rather than
  left for the case manager to notice on paper. Confirmed with Josh 2026-08-18.
- **`SsnMask.IsWellFormed` is a shape check, not proof of ownership.** Nothing local
  can establish that a number belongs to the consumer.

## Agency releases and transportation documents (2026-08-19)

- **Agency release completed.** The selected-consumer workspace, shared validation contract,
  local/cloud service seam, audited no-store API route, two-page Sati PDF, staff-attestation
  confirmation, and automated desktop/API tests are in place. Consumer signatures are deliberately
  left for the document rather than represented as ordinary data entry.
- **Transportation source forms analyzed; implementation is next.** The ModivCare Standing Order
  and LogistiCare Single Trip PDFs supplied on 2026-08-19 are both one-page, flat PDFs with zero
  AcroForm fields or widgets. They cannot use the DHHS field-filling path.
- **Preserve the official/vendor page.** Build a coordinate-overlay definition for each exact source
  revision and prove that the original page content stream remains unchanged, rather than redrawing
  a lookalike. Put both behind an `ITransportationFormService` local/cloud seam, derive consumer,
  agency, and logged-in requestor identity on the authoritative side, and audit generation. Before
  operational use, print one sample of each and confirm acceptance plus the required MaineCare
  billing-section interpretation with the transportation broker.

## Local schema updates — outstanding after the 2026-08-19 safety net

See `DECISIONS.md`, "The desktop backs up before it migrates a database with records
in it".

- **No audit event for a schema change.** The startup migration runs before sign-in,
  so there is no actor and `LocalAuditTrail.Record` cannot be called. The backup file
  is the only trace. Options: attribute to a system actor, or defer the event until
  the first sign-in after an applied migration and record it then.
- **Backups are never pruned.** Every migration on a database with records writes a
  new `.bak` under `%LOCALAPPDATA%\Sati\schema-backups` and nothing removes old ones.
  Fine at the current rate; wrong once several people are running it for a year.
- **The backup is not verified.** `BACKUP DATABASE` returning without error is taken
  as success. A `RESTORE VERIFYONLY` would prove it is readable before the migration
  proceeds, which is the entire point of taking it.
- **Untested against a real diverged database.** The diverged-history path is covered
  by a fake that throws the right exception. Nothing has exercised it against an
  actual database whose `__EFMigrationsHistory` disagrees with its schema — and the
  other Windows login's `SatiProduction` is the most likely place that is true.

## SSN panel — outstanding after the 2026-08-19 profile work

- **`DhhsFormsViewModel` still has its own SSN code.** `SsnPanelViewModel` is now the
  shared owner and the consumer profile uses it, but the forms workspace was not
  refactored onto it in the same pass. Two implementations of "how do we show and
  store an SSN" is the duplication this class was created to remove; finish the move
  and delete the older copy. The forms workspace's `SsnStorageExplanation` is already
  stale — it still says local Production does not store numbers.
- **A revealed number does not time out.** It clears when the consumer changes, when
  Hide is pressed, and on any failure, but a panel left open on a locked-away
  workstation keeps showing it. A short auto-hide would close that.
- **The reveal is not rate limited.** Nothing stops a bulk read of every consumer's
  number one profile at a time. Each read is audited, so it is visible after the
  fact, but nothing makes it slow or noisy while it happens.

## Brochure source pipeline — outstanding after the 2026-08-22 recovery

The brochure now builds from `marketing/brochure/brochure.html`; see `DECISIONS.md`. What the
recovery did not settle:

- The build is verified by eye. There is no check that a slide's copy still fits its panel, and
  SVG text does not wrap, so a lengthened line silently overruns. A width assertion per text run
  would catch it.
- `scripts/build-brochure.ps1` depends on a Chromium browser being installed and on Segoe UI and
  Georgia being present. Both hold on the current build machine and neither is asserted.
- The remaining ten slides still use the original screenshot crops, several of which are stale
  relative to release 1.2.20. Reshooting them is separate work.
- The recovered source reproduces the original slide 1 layout apart from the leaf. Nothing has
  been reviewed for message, ordering, or claim accuracy, and `REGULATORY_CONCERNS.md` has not
  been applied to the brochure copy.

The pre-recovery ReportLab PDF is deliberately not retained. The HTML source is the baseline;
there is no earlier version to fall back to, and none is wanted.

## Brochure restructure — outstanding after the 2026-08-22 rewrite

The deck is 14 slides. The intended five movements were interrogative intro (1-3), case-manager workflow
(4-6), billing gates (7-9), admin, security and platform (10-12), and Carika plus direction
(13-14). Every headline is a question; slide 14 answers them. Outstanding:

- Slides 11, 12 and 13 carry dashed placeholders, not artwork. Slide 11 wants the environment
  chooser or the Demo-indicated sign-in, slide 12 wants an architecture diagram, slide 13 wants
  Carika showing a profile beside a transcript awaiting review. Each label says what to supply.
- Slide 14 now carries the roadmap and the close on one page. If the closer needs room to
  breathe, split it into a fifteenth slide.
- The layout guard in `brochure.html` (`checkLayout()`, auto-run into the browser console) is
  not enforced by `scripts/build-brochure.ps1`. A silent overrun would still ship.
- Slide 13's claims are scoped to the current Carika slice: profile display and note drafting,
  imported audio rather than live capture, separately provisioned models. Re-check that slide
  against `Carika/README.md` whenever the slice moves.
- The interrogative pass rewrote every headline. Copy has not been reviewed against
  `REGULATORY_CONCERNS.md`, and slide 9's "lost units were lost visits" asserts a relationship
  between billing data and client contact that nothing in the deck substantiates.

## Brochure slide order — parked 2026-08-23

Guided forms was moved from position 5 to position 10 and everything between shifted up one. The
current order is: cover, day in view, consumer record, note capture, review tracking, supervisor
workflow, billing gates, productivity, audit and administration, guided forms, security, platform,
Carika, close.

This crosses two of the movements the deck was structured around. Guided forms is case-manager
work sitting inside the admin, security and platform run, and the "do the work without the detour"
construction introduced on slide 4 no longer has its two intended partners adjacent to it. The
move was made deliberately and marked as temporary; either the movements or the frame needs
revisiting before the deck is final.

`checkLayout()` in `brochure.html` now also verifies that each slide's position, `data-slide`
attribute and footer number agree, and that element ids are unique across slides.

## Notes page consolidation — outstanding after the 2026-08-23 change

The notes log's Note Detail panel is gone; the shared entry panel now shows a selected note in a
locked View Note mode with a padlock toggle into Edit Note, and filters moved to a band above the
grid. Left open:

- **A note is only checked for staleness when it is unlocked.** `VerifyLoadedNoteIsCurrentAsync`
  covers the case that matters — finding out before editing rather than after — but a note left
  open in View Note for an hour still shows the copy it was loaded with, and nothing announces a
  supervisor's return while it sits there. Deliberate: polling every open panel to catch an
  uncommon event was rejected (see `DECISIONS.md`). If notes start being changed underneath
  case managers often enough to matter, the next step is a push or a refresh-on-window-activation,
  not a timer.

- **The panel has never been opened in a running client since the change.** `NotePanelRenderTests`
  now loads the real views against the real resource dictionary and asserts the resulting element
  state, which covers the parts a human would otherwise have had to check — but not how any of it
  looks. The filter band's `WrapPanel` reflow points and the note panel's column width in
  particular are guesses that only a person at the screen can judge.

Closed since that entry was written:

- ~~Nothing warns when the note being viewed has since changed on the server.~~ Unlocking now
  re-reads the note and compares `Revision`, behind the unlock so the panel never freezes on it.
  An untouched panel reloads to the current version; a panel with unsaved typing is warned and left
  alone; a removed note and a failed read each say so. The save-time concurrency check is unchanged
  and still authoritative.
- ~~No entry point back to a blank New Note from the dashboard.~~ `StartNewNoteCommand` on the
  module is offered as an always-visible New Note button in the panel header and as Escape, bound
  both on the module and on each host page. It keeps the selected client, which matters most on the
  dashboard, where that property scopes the whole page. The notes log's Deselect Note button was
  removed as a leftover of the two-panel design.
- ~~The dashboard host does not ask before replacing a draft.~~ Both hosts now route their
  double-click through `NoteEntryViewModel.OpenForEdit`, which owns the unlock-or-load-or-ask
  decision. Neither host repeats it, so they cannot drift apart again.
- ~~Layout is verified structurally, not visually.~~ `NotePanelRenderTests` loads `NotesLogView`
  and `NoteEntryView` on a real STA thread with the application's resources and asserts runtime
  grid placement, that a locked narrative is `IsReadOnly` while staying enabled and focusable,
  that pickers and radio buttons are disabled, that the save button is collapsed, and that the
  attendee checkboxes inside the `ItemsControl` lock too. Each assertion was confirmed to fail
  against a deliberately broken view.

### One WPF Application per test process

`WpfUiHarness` now owns the assembly's only `Application` and the STA thread it runs on. WPF's
one-per-AppDomain flag is never cleared — not even by `Application.Shutdown()` — so a second
creator does not merely conflict, it fails permanently, and *which* test fails depends on run
order. `StabilizationTests.ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread` used to
build its own; it now borrows the harness and installs its host through `RunWithHost`. Any future
test that needs a real view must go through the harness rather than constructing an `Application`.
