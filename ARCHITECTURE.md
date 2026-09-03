# Sati — Architecture Reference

*Living document. Updated during structured review sessions. Last updated: 2026-09-03.*

## Inactivity privacy screen

`IdleSessionState` owns the rule: it holds the timeout, the last-activity stamp, and whether
the overlay is up, behind an injectable clock so the behavior is tested without a timer or a
window. `ShellWindow` supplies the two inputs it cannot supply itself — an application-wide
`InputManager.PreProcessInput` hook for activity and a one-second `DispatcherTimer` for the
tick. The view model never references WPF input types.

`IdleLockPreferenceService` stores the delay per Sati user, Windows profile, and data
environment, exactly as `EasyEyesPreferenceService` stores Easy Eyes. It is personal
presentation state: no migration, no agency Settings row, and it never leaves the machine.
The two services deliberately keep separate files so a malformed value in one cannot cost the
user the other; consolidating them is tracked in `AGENDA.md`.

The overlay is a privacy screen, not a security control, and both the UI and the release notes
say so. `TryDismiss` is the single exit, and `RequiresUnlockChallenge` is the seam a PIN would
use: every path that wakes the session already routes through that one method.

## Case note template

`CaseNoteTemplateComposer` turns the ticked meeting controls into a structured note. It does
not phrase the selections itself — it renders `CaseNoteFactCompiler.VisitFacts`, so the
template and the local-AI drafting path cannot describe the same checkbox two different ways.
It never removes text: existing narrative is preserved verbatim below a Meeting Narrative
header.

## Suggested follow-up

`UpcomingEventService` now answers two questions from one form table. `GenerateEvents` reports
what is actionable inside its open/late window, which is what the dashboard needs.
`NextFormSuggestion` reports the client's next outstanding form regardless of that window,
which is what the note panel needs. Both use `GetCurrentCycleForm` and `IsSatisfiedAsOf`, so
neither can name a form the compliance gate treats as met.

## Accent and button color

Every theme dictionary now supplies `AccentButtonBrush`, `AccentButtonHoverBrush`,
`AccentButtonPressedBrush`, and `OnAccentButtonBrush` alongside the accent tokens. Only
`PrimaryButton` binds the button set; selection highlights and accent type still bind
`AccentBrush`. A theme dictionary is swapped in whole, so a theme missing a key loses the fill
rather than inheriting one — a structure test asserts all fifteen supply all four.


## Easy Eyes presentation mode

Easy Eyes is personal presentation state, stored by `EasyEyesPreferenceService` per Sati user,
Windows profile, and data environment. It defaults off and never travels through the agency
Settings API or database. The singleton service notifies the open shell after a successful save,
so the setting takes effect immediately and is loaded again at sign-in or account switch.

When enabled, the shell and Settings surfaces use a 1.3 layout scale so controls with explicit font
sizes grow consistently with the rest of the interface. `ShellViewModel` supplies the mode to the
two note-list view models: their Narrative columns become hidden presentation only, without
changing note data. The Clients view computes its selector layout as the user's ordinary compact
choice OR Easy Eyes, so Easy Eyes forces the horizontal selector and disabling it restores the
underlying responsive/manual layout choice.

## Vocational Rehabilitation profile assignments

`Person.OpenWithVR` controls whether the Consumers UI reveals `VrCounselorName` and
`VrAssistantName`. The names are consumer facts and travel through the ordinary person save,
validation, revision, audit, and immutable-version paths in both Local Production and cloud Demo.
Unchecking VR hides but does not erase the assignments, preserving context if a VR case reopens.

`Settings.VrAssistantTitle` is agency-wide reference text, defaults to `VSA`, and changes only the
assistant field's displayed label. It never rewrites an assigned person's name. Closing Settings
refreshes the Consumers view so the label changes without restarting Sati.

## Credible updates to an existing consumer

`Settings.AllowCredibleProfileUpdates` is an agency-wide, Admin-managed safety switch and defaults
off. When enabled, the single-consumer review flow may fill the edit form for the deliberately
selected consumer. It still does not write: `NewClientViewModel.Submit` remains the sole demographic
writer, so authorization, validation, optimistic concurrency, audit history, and person versioning
remain identical to a hand edit in Local Production and cloud Demo.

Only accepted, mapped fields replace form values; absent or declined fields remain unchanged.
Where both the selected profile and export carry a Credible client id, different ids fail before
any form field changes. The setting does not enable bulk replacement: folder import continues to
report and skip existing ids.

## Form evidence and attestation

Evidence and `Form` records deliberately answer different questions. A form-tagged case note or
quarterly `ReviewItem` says work was documented. A current-cycle `Form` records the separate human
attestation that the form itself was completed. Saving or deleting evidence never completes or
revokes a form.

The Reviews workspace, dashboard, and Clients workspace share one `FormAttestationControl` for all
twelve form types. The date picker starts blank. `FormAttestationRules` in `Sati.Contracts.V1`
rejects future dates and dates before the form's cycle start in the WPF capture, Local
`FormService`, and API. `PUT /api/v1/forms/{id}` can change only `OpenedDate`; only the attestation
and revocation routes can change persisted completion state.

`FormAttestation` is an append-only ledger. `Form.CompletedDate` remains the authoritative scalar
read by billing and the UI, but it is now the projection of the latest attestation/revocation.
Every accepted change appends the ledger row and a PHI-minimized audit event in the same save.
`EvidenceNoteId` is a nullable citation, deliberately not a foreign key, so deleting evidence does
not rewrite attestation history. `CompletedDate` is also an optimistic-concurrency token: two
simultaneous writes cannot both succeed from the same prior state.

Every form-compliance mutation converges on
`CaseManagerDashboardViewModel.AfterFormComplianceChangedAsync`: checkbox flags, the caseload
matrix, and `UpcomingEvents` refresh together. Changes initiated by the Clients or Reviews
workspace first reload the dashboard's person snapshot, then use that same cascade. People and
upcoming-event loads take `LatestRequestTracker` identities before publishing shared UI state.

The pending-attestation list is derived from eligible form-tagged notes and outstanding forms by
person, form type, and the cycle containing the note's event date. It is not a stored prompt and
does not depend on which dashboard person happened to be selected when the note was saved.

Document-backed forms add a separate server fact. `DocumentArtifact` stores metadata for a generated,
Draft, or externally recorded annual document; PDF bytes remain only in the response/save flow.
`AnnualDocumentCatalog` maps document kinds to form types, while `FormAttestationRules` decides
whether the live artifact, same-cycle Comprehensive Assessment, or reasoned Supervisor technical
override permits attestation. A Draft never satisfies a release prerequisite. Regeneration
supersedes the previous live row, and the database permits only one live artifact for each person,
kind, and cycle.

Agency, DHHS, and Medical release generation records artifact metadata and a PHI-minimized audit
event in the same transaction. Both local and cloud form services can record an external document
with a required note, but every API route first revalidates agency and accessible-caseload scope.
The Medical Release is a distinct Sati-owned PDF generator that shares the release-choice contract;
it is not represented as a state-issued or independently approved form.

`DocumentTemplate` stores immutable published source versions. `DocumentTemplateResolution` and
`DocumentTemplateRules` in `Sati.Contracts.V1` own agency-over-default precedence, the closed token
set, and source validation. `DocumentTemplatePdfComposer` in `Sati.Forms` handles only rendering:
headings, paragraphs, bullets, simple tables, explicit page breaks, and one-pass token substitution.
`IDocumentTemplateService` has local EF and cloud HTTP implementations; template administration is
agency-Admin-only, while privacy rendering uses the same accessible-caseload gate as other documents.
The artifact freezes template owner/key/version. The provisional Sati default is seeded by migration;
ordinary agency routes cannot change it. See `DOCUMENT_TEMPLATES.md` for the source language.

## Agency authorization model

Agency access is a persisted per-user `[Flags]` value owned by
`Sati.Contracts.V1.UserPermissions`: case management, supervision, administration, and billing are
independent capabilities. `UserPermissionRules` is the sole interpreter used by the desktop and
API. The legacy `User.Role` value remains temporarily for display, signed-record compatibility,
and the orthogonal `PlatformOperator` identity; it is not an agency authorization source.

`ValidatedActorFilter` re-resolves the current permission set from the database on every API
request after confirming the token's user, agency, and legacy identity label. Permission values are
not read from caller input or trusted from a JWT, so revocation takes effect before token expiry.
The API constructs its actor server-side. Transitional local billing receives an explicit minimal
`AgencyActor` and verifies its identity, agency, and permission set against the database before use.
Neither path accepts a persistence `User` object as a network or service authorization contract.

The `AddUserPermissions` migration preserves existing access by backfilling the old labels, while
new user management edits the permission set directly. Unknown bits and an empty set deny by
default. Billing UI visibility follows billing permission, but every billing route and the local
billing service enforce it independently.

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

## Session Changelog — 2026-08-22

### Platform-neutral persistence boundary (2026-08-30)

- `Sati.Persistence` targets plain `net10.0` and owns the entity model, `SatiContext`, its
  design-time factory, the pure helpers required by entities, and all 81 migrations. It has no
  WPF reference. `WorkdayTile` remains in the desktop because it is an `ObservableObject`, not an
  entity.
- The WPF client references that assembly while retaining its transitional local EF services. Its
  startup path is still `LocalDatabaseUpdater` -> `SqlLocalDatabaseMaintenance` ->
  `Database.MigrateAsync()`; assembly ownership changed, sequencing and safeguards did not.
- `Sati.Api` references the persistence assembly so schema tooling and a future migrator have one
  cross-platform owner. The API still uses its separately scoped `ApiDbContext` at runtime; this
  move does not make the desktop context the cloud request context or erase the documented model-
  parity obligation.
- `dotnet ef migrations list` now resolves all 81 migrations from `Sati.Persistence`. The
  hand-authored `TenantScopeSettingsAndProviders` migration was given its missing context/id
  metadata; its DDL body was not changed.
- **EF tooling now needs an explicit startup project.** The repository root *is* the desktop
  project, so `dotnet ef` run from the root takes `Sati.csproj` as the startup project, loads its
  build output, and finds `Sati.Data.SatiContext` twice — once from the stale desktop assembly and
  once from `Sati.Persistence`. It reports `More than one DbContext named 'Sati.Data.SatiContext'
  was found`, which reads like a duplicate type in source and is not:

  ```
  dotnet ef migrations list --project Sati.Persistence/Sati.Persistence.csproj \
      --startup-project Sati.Persistence/Sati.Persistence.csproj --context Sati.Data.SatiContext
  ```

  Deleting a stale `bin/Debug` output makes the error go away for one session; passing
  `--startup-project` is what makes it stay away. Use the same pair of arguments for
  `migrations add`.
- Seventeen migrations dated 2026-08-07 through 2026-08-16 are hand-authored: they carry
  `[DbContext]` and `[Migration]` on the migration file itself and have no `.Designer.cs`, so they
  have no per-migration target model. EF applies and lists them normally. What they cannot support
  is `migrations remove` walking back through that range, or `migrations script --from` anchored
  inside it. Reconstructing seventeen historical snapshots is not worth the risk of getting one
  subtly wrong; the current-model snapshot in `SatiContextModelSnapshot.cs` is present and correct,
  which is what `migrations add` actually diffs against.

### Billing exchange history and Demo contingency catalog (2026-08-29)

- `BillingSubmissionEvent` and `RemittanceClaimOutcome` are append-only, agency-owned financial
  exchange history. They contain bounded operational explanations and amounts, not note narratives
  or raw inbound X12. Read routes are Admin-only and tenant-scoped.
- `RemittanceDeposit` is the deposit anchor for a remittance: claim payment total, signed provider-
  level (PLB) adjustment, 835 payment amount, and optional EFT amount are retained together. The
  shared `DepositReconciliationRules` owner exposes awaiting-EFT, penny-matched, EFT-mismatch, and
  internally unbalanced remittance states; no batch is described as reconciled until the rule says so.
- Successful 837 generation appends a `Generated` event in the same save as the retained idempotent
  file. A retry replays the original generation without adding another event.
- Submissions shows generated, transmitted, failed, 999, and 277CA activity. Remittances shows paid,
  partial, denied, reversed, unmatched, and needs-review claim outcomes. Synthetic provenance is a
  visible, non-color column in both grids.
- `Seed-BillingPipelineData.ps1` remains hard-limited to a Demo identity and adds eight submission
  stages plus six remittance and four deposit contingencies, all explicitly synthetic. A separate history consumer
  keeps the established three-ready/seven-blocked queue examples intact.
- This is a read model and scenario catalog, not transport or import. Real X12 parsing, validation,
  matching, posting, reconciliation, corrections, retention decisions, and payer certification
  remain outstanding.

Representative-payee profile:

- `Person` owns `CaseManagerIsRepPayee`, nullable monthly income, and bounded regular check-request
  needs. `RepresentativePayeeRules` in `Sati.Contracts.V1` is the shared integrity owner for the
  WPF editor, Local Production persistence, and the API.
- The Overview Profile presents an explicit accessible Yes/No choice. Yes requires a positive
  two-decimal monthly amount and a recurring-needs description; No clears the subordinate fields.
- The existing Person revision and lifecycle transaction includes all three fields. Demo writes stay
  own-caseload/tenant checked and Local Production repeats the validation at its service boundary.
  Migration `20260822210734_AddRepresentativePayeeProfile` is additive and defaults existing people
  to No without inventing financial details.
- These fields describe current recurring needs only. They do not authorize, request, approve, or
  release a check. A later billing notification requires a separate audited workflow.
- `ApiSurface.Revision` now fingerprints named persistence-contract shapes as well as routes. This
  makes a client/server mismatch visible when an older server would otherwise ignore new Person
  fields on an existing route.

## Session Changelog — 2026-08-19

DHHS form desktop workflow:

- Added a `DHHS Forms` workspace to the selected consumer's record. The workspace owns only
  presentation state and calls `IDhhsFormService`; the existing local and cloud implementations
  continue to decide where the official PDF is filled.
- `DhhsFormsViewModel` maps the official AcroForm field names to readable, grouped controls for
  consumer-directed consent. Choices are cleared whenever the selected consumer changes and only
  affirmative/nonblank selections cross the service boundary.
- Demo can read the SSN mask and send a one-time replacement value through the API for envelope
  encryption. The WPF `PasswordBox` is deliberately unbound and cleared after the send attempt;
  observable desktop state receives only `SsnStatusDto`. Local Production exposes the same screen
  but disables SSN storage and explains that the PDF field will remain blank.
- Signatures, signing dates, and signer-authority attestations are not desktop inputs. They remain
  blank on the fillable official PDF for the consumer or representative to complete.
- PDF bytes return through a ViewModel event; the view owns the save dialog. Missing demographic
  boxes remain non-blocking and are translated into a hand-completion warning after generation.

Agency release workflow:

- Added a Sati-owned `Agency Release` workspace beside the official DHHS forms. It records the
  recipient, exact information categories, authorization window, special confidentiality choices,
  revocation state, and whether the authenticated case manager attests they obtained the release.
- `AgencyReleaseRules` in the shared contracts project is the single validation owner for desktop,
  local Production, and API-backed Demo. It enforces explicit yes/no choices, bounded authorization
  windows, and descriptions for `Other` rather than permitting a visually complete but ambiguous
  document.
- `IAgencyReleaseService` keeps the workspace independent of storage mode. Local Production reads
  the consumer and agency through EF and renders on the workstation; Demo posts the same request to
  `POST /people/{personId}/agency-release.pdf`, where tenant/caseload access and identity derivation
  happen before rendering. Both paths record `agency-release.generated` without recipient PHI in
  audit metadata.
- `AgencyReleasePdfGenerator` creates a two-page Sati-branded PDF. Consumer signature lines remain
  blank. The optional staff attestation records the signed-in user and UTC generation time only and
  explicitly states that it is not the consumer's electronic signature.

---

## Session Changelog — 2026-08-07

First functional Comprehensive Assessment slice:

- Added `Sati.Persistence/Models/Assessments/ComprehensiveAssessment.cs`. Relational columns own identity,
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

### Carika limited Avalonia client (2026-08-21)

`Carika` is a Windows-targeted Avalonia client limited to authenticated caseload profile display and
case-note drafting. It references `Sati.Contracts` and calls `Sati.Api` over HTTPS; it has no EF Core,
SQL, LocalDB, migration, or database-credential dependency. The API remains authoritative for
identity, tenant/caseload authorization, note validation, workflow, audit, concurrency, and Azure SQL.

Optional local drafts are encrypted with Windows DPAPI for the current OS user and bound to the
authenticated Sati user and person. Local Whisper transcription accepts an already-provisioned model
and WAV input; the client has no cloud fallback or automatic model download. This first slice does
not capture microphone audio, and neither local execution nor encryption is a HIPAA-compliance claim.

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

- `Sati.csproj` is the existing WPF client. It retains presentation, local EF service
  implementations, and local-development workflows, but no longer owns the entity assembly or
  migration chain.
- `Sati.Api` is the ASP.NET Core server boundary for cloud workflows.
- `Sati.Contracts` contains versioned network DTOs and has no WPF or EF dependency.
- `Sati.Persistence` is the cross-platform EF/domain assembly containing the entities,
  `SatiContext`, and the complete migration chain. It does not make `SatiContext` the API's
  request context; `ApiDbContext` remains the current server model.
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

The only deletion exception is the Admin test-consumer command. It requires both a durable,
creation-only `Person.IsTestData` marker and the deleting Admin's explicit attestation. Only an Admin
may set the marker while creating a consumer; neither local nor API updates may add, remove, or
change it. Because each `PersonVersion` contains a copy of that synthetic profile and its FK is
restrictive, the command removes those versions and explicitly counted `PersonProvider` links with
the rest of the test consumer graph inside one serializable transaction. It never deletes
`AuditEvent` rows and instead appends `test-data.consumer-deleted` with only IDs and counts.
Claim-linked consumers are blocked. This command is not an inactive-client, duplicate, retention,
or legal-hold workflow.

Representative-payee status, monthly income, and regular check-request needs are ordinary live
Person profile fields inside that same tenant-scoped revision boundary. They are intentionally not
claim data or a payment instruction. `RepresentativePayeeRules` is the shared validation owner, and
the Profile clears subordinate financial fields when the status is No. Future check-release work
must create a separately authorized, auditable record rather than infer an instruction from profile
state.

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

The same dashboard also owns the Admin test-data cleanup doorway. Admin-created test consumers are
marked at creation and shown with a non-color-only `TEST` badge; that classification is immutable.
The view supplies an explicit destructive confirmation and a versioned test-only attestation;
`IAdminService` carries the command through either the local or cloud implementation. The API/local
service, not the button, enforces the marker, Admin role, agency ownership, optimistic concurrency,
billing-record protection, all-or-nothing graph deletion, and audit preservation.

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
| `FormAttestation` | `Sati.Models` | Append-only evidence of an attestation or reasoned revocation; projects the live completion date onto `Form`. |
| `DocumentArtifact` | `Sati.Models` | Metadata and supersession history for a generated, Draft, or externally recorded annual document; stores hashes, not document bytes. |
| `Note` | `Sati.Models` | Service note — visit, contact, form completion, or other. |
| `User` | `Sati.Models` | Staff member. Has role, supervisor chain, and agency affiliation. |
| `Agency` | `Sati.Models` | Billing/provider entity. Referenced by both `Person` and `User`. |
| `Settings` | `Sati.Models` | Agency-scoped business configuration. Personal UI preferences remain outside this model. |
| `Incentive` | `Sati.Models` | Monthly productivity snapshot. Per-user, per-month. |
| `Scratchpad` | `Sati.Models` | Daily freeform notes. Per-user, per-date. |
| `ExemptDate` | `Sati.Models` | Manual workday exclusions. Per-user. Canonical store for day exclusions. |
| `UpcomingEvent` | `Sati.Models` | Ephemeral record. Never persisted. Derived at runtime. |
| `BillingPeriod` | `Sati.Models.Billing` | Monthly billing container. Has many `ClaimLine`s. |
| `ClaimLine` | `Sati.Models.Billing` | One billable service note within a billing period. |
| `EdiGeneration` | `Sati.Models.Billing` | Exact 837P response retained for tenant- and actor-scoped idempotent replay. |
| `BillingSubmissionEvent` | `Sati.Models.Billing` | Append-only generated/transmitted/acknowledgment event with explicit synthetic provenance. |
| `RemittanceClaimOutcome` | `Sati.Models.Billing` | Append-only claim-level payment/denial/reversal matching read model; raw 835 import is pending. |
| `RemittanceDeposit` | `Sati.Models.Billing` | Append-only 835/EFT reconciliation anchor with explicit PLB adjustment and derived match state. |
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

**Single source of truth: `Form.Attest(FormAttestation)` and
`Form.RevokeAttestation(FormAttestation)`**

- `Form.IsCompliant` is derived as `CompletedDate.HasValue`; there is no stored compliance flag.
- `CompletedDate` has a private setter. `MarkComplete` and `Reset` are private entity helpers called
  only while appending an attestation or revocation. `SetInitialCompletion` is guarded to new,
  unattested forms and exists only for the admission confirmation before the Person graph is saved.
- New-client generation does not use the legacy "compliant with no completion date" state. When
  the admission workflow assumes an in-force annual document exists, it records the effective date
  as its completion date; the confirmation dialog lets the case manager correct that assumption.
- EF Core materializes entities via the `protected Form()` parameterless constructor,
  which does not touch `IsCompliant`.
- **Cascade rule:** persisted completion changes go through attestation/revocation. Both database
  contexts reject updates or deletes of ledger rows, the Form relationship uses restricted delete,
  and form deletion refuses any row with attestation history.

### Form Generation

**Single source of truth: `Person.GenerateFormList(DateTime effective, Settings settings)`**

- Called by `Person.CreatePerson()` at admission; `Settings` is now threaded in and forwarded to
  `FormDueDateCalculator.Compute`.
- For an effective date through today, annual non-review forms start completed on that effective
  date and review forms start incomplete. For a future effective date, every form starts incomplete;
  Sati never writes a future completion date. The confirmation dialog can correct these assumptions
  before the Person graph is saved.

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
  `AddMissingFormsForCycle` (existence check).
- **Deliberately NOT routed through it:** `CaseManagerDashboardViewModel.BuildFormRows`, a
  forward-looking `>= cycleStart` queue with no upper bound (by design). Code comment marks why —
  do not "helpfully" convert it.

### Compliance Evaluation

**Single source of truth: `Sati.Contracts.V1.BillingComplianceGate`.**

- Returns `(bool Passed, IReadOnlyList<string> Reasons)` — one pass produces both result and
  human-readable explanation. `Person.EvaluateComplianceGate` is the desktop adapter.
- A form fails the gate only when its due date has passed and it was not completed as of the
  evaluation date.
- **`Form.CompletedDate` is the only stored compliance fact.** `Form.IsCompliant` is derived from
  it (`CompletedDate.HasValue`) and the column it used to occupy was dropped in
  `AddDerivedFormCompliance`. A stored flag beside the date is a second copy of one fact kept in
  step by convention, and 147 rows proved convention insufficient.
- **Two questions, two names.** `IsCompliant` — is a completion recorded. `IsSatisfiedAsOf(date)` —
  is it in force as of that date. They differ only when a completion date has not arrived yet.
  Anything whose answer depends on today (caseload matrix, `UpcomingEvents`, task rows,
  `GetComplianceStatus`) must ask `IsSatisfiedAsOf`, which shares its predicate with
  `BillingComplianceGate.IsIncompleteAndOverdue`; checkbox bindings ask `IsCompliant`.
- **`Person.InForceSince` owns "was this already satisfied when its cycle began."** Annual
  documents are in force from their cycle start; reviews are never assumed; a cycle that has not
  started assumes nothing. Both generation paths route through it, so a form cannot be born
  compliant without a date.
- The due date remains billable. The block begins the following day and ends on the completion
  date. An absent effective date is a separate profile/data-quality issue, not an overdue form.
- **The gate reads every row in `Person.Forms`; `Person.GetCurrentCycleForm` reads one.** That
  asymmetry is why a duplicated form could block billing while every screen showed it complete —
  the checkbox, matrix and task board resolve the due-date tie to one copy, the gate sees them
  all. `dbo.Forms` now carries a unique index on `(PersonId, Type, DueDate)`, so the two readers
  cannot disagree about how many records exist. Whether the gate *should* also be cycle-scoped is
  still open (`AGENDA.md`); today a genuinely stale prior-cycle form blocks indefinitely.
- `BillingComplianceRequirements` is an agency-scoped flags value stored on `Settings`. Admins may
  enable or disable 90-day reviews, PCP, Comprehensive Assessment, Reclassification, Safety Plan,
  Privacy Practices, and each release type. The migration default preserves the former intended
  set: reviews, PCP, Comprehensive Assessment, Reclassification, and Safety Plan.
- `beingCompleted` exempts only the newest overdue instance of that form type in the same action;
  an older overdue instance of the same type still blocks.
- Desktop supervisor queues, note entry, client presentation, billing validation, and loss reports
  pass the agency setting to this owner. API supervisor, billing, and reporting endpoints load the
  tenant's setting and call the same owner. Approval remains enforced below the UI.

### Billing Window Evaluation

**Single source of truth: `BillingComplianceGate.EvaluateBillingWindow(...)`.**

- Reasons as of the *note's event date*, not today — necessary for back-entered notes in a
  different cycle. Walks `Forms` directly by each form's own due date (not `GetCurrentCycleForm`).
- The exact same agency requirement set and type mapping used by current compliance controls the
  historical window; the two decisions cannot silently disagree about a form type.
- Window is exclusive on both ends: a note ON the due date bills; a note ON or after completion
  date bills.
- `Person.EvaluateBillingWindow(...)` and `Person.IsBillingWindowBlocked(...)` are desktop adapters.
  The API and `ConsumerBillingLossReportService` call the contracts owner, so Statistics cannot
  drift to a different definition of an overdue billing gap.

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

### Provider Directory Curation

**Single source of truth: `ProviderDirectoryRules` (`Sati.Contracts.V1`).**

- The directory is an agency-wide rolodex, not a case manager's private list. Case managers,
  supervisors, directors, and Admins may add and correct entries; deletion and merge are Admin-only.
  The API's validated actor filter and the transitional local service enforce the role split rather
  than relying on which buttons are visible.
- A normalized same-name match (trimmed, internal whitespace collapsed, case-insensitive) produces
  a warning but does not block. Two real organizations may share a name; the interface asks a human
  to check instead of pretending a name is a durable identity.
- `Provider.PrimaryContact` and `Phone` remain the organization's general directory line.
  `ProviderContact` is a separate one-to-many list of named people who work there, with at most one
  primary contact. Provider contacts are agency-shared and deliberately carry no consumer identity.
- An Admin merge retains one provider row, moves affiliated children, live consumer links, named
  contacts, and the agency passthrough default, and adopts identifiers/parent only where the
  survivor has none. It refuses tier, durable-identifier, affiliation-loop, cross-agency, and
  current-consumer-link conflicts.
- Merge is a serializable transaction in both persistence paths. It records the PHI-minimized
  `provider.merged` audit action with provider IDs and moved counts. `AssessmentNeed.ProviderId`
  and its provider-name/practice/network snapshots are deliberately not rewritten: a document
  keeps what it recorded when the provider was selected.

### Provider Affiliation

**Single source of truth: `ProviderAffiliation` (`Sati.Contracts.V1`).**

- A medical entry carries `MedicalKind` (`Individual | Practice | Network`) and one
  `ParentProviderId` self-reference. Not two typed columns: a hospitalist belongs to a network
  with no practice between, so a separate network column would have to exist on individuals too,
  and could then disagree with the practice's network. One parent cannot hold that contradiction.
- Legal parents are Individual → Practice or Network, Practice → Network, Network → Network.
  Network to Network is what lets three tier names describe a four-level reality. Individual to
  Individual is refused: supervision is not affiliation.
- `ParentProviderId` is **not** gated to healthcare in the schema. Waiver providers have the same
  shape and the link is the expensive-to-retrofit part; only the vocabulary is medical. The form
  gates it, so no unvalidated hierarchy can be entered today.
- The chain is **derived, never stored**. Callers pass their own agency's rows and walk them, so
  correcting a directory entry corrects every reader; scoping the rows to one agency is what makes
  a parent from another tenant fail as "not in this directory".
- Enforced in `ApiEndpoints.ValidateProviderAffiliationAsync` and mirrored in
  `ProviderService.GuardAffiliationAsync`, matching the duplicate-identifier arrangement above.
- Deleting an entry that still has entries beneath it is refused by both paths and by
  `OnDelete(Restrict)`. `SetNull` was rejected: it would promote a whole subtree to top level with
  nothing in the interface revealing that the hierarchy had split.
- Hierarchy raises the cost of duplicate rows — two "MaineHealth" entries split the tree
  invisibly — which promotes the deferred directory-governance item to a prerequisite.

### Consumer Provider List

**Single source of truth: `ConsumerProviderRules` (`Sati.Contracts.V1`), over `PersonProvider`.**

- A row stores the provider and the relationship's own fields — role, primary-care mark,
  dates, release-on-file, order — and **no copy of the practice or network**. Those are derived
  by walking `Provider.ParentProviderId` at read time, so a physician who changes practices is
  corrected once instead of leaving a stale copy on every profile that names her. The derived
  values render read-only for the same reason: an editable derived value is a stored copy.
- `EndDate` alone says whether a link is current. There is no active flag — two columns meaning
  the same thing drift — and ending a relationship keeps the row, because who was treating
  someone in a given year is a question a case record has to answer.
- Two filtered unique indexes back the rules the services also enforce: one current primary care
  provider per consumer, and one current link per provider. Both filter on `EndDate IS NULL`,
  because an ended relationship constrains nothing: a consumer may have had several primary care
  providers, and may return to one they previously left.
- **No product cap** on list length. `MaxProvidersPerConsumer` is a runaway guard whose message
  says so. Tidiness is state, not truncation: ended links collapse behind a disclosure.
- `ProviderId` may point at any tier. A consumer whose relationship is with a walk-in clinic
  rather than a named clinician selects the practice, and the derived chain starts higher.
- A directory entry cannot be deleted while any consumer record references it, ended links
  included. The refusal carries a **count and never consumer names** — a directory screen is not
  where who-sees-whom is disclosed.
- Live profile data, following `PersonContact`: documents snapshot the resolved chain at
  generation, this stays current.

**Superseding the pre-directory fields.** `Person.PrimaryCareProvider` and
`Person.HealthcareSystemName` are free text kept in place and never cleared. `LegacyProviderLinking`
matches them to directory entries — **exact after trimming, case-insensitive, nothing else** — and
the profile panel offers a one-click link when there is a single unambiguous match. Nothing is ever
written without a person confirming it, and an ambiguous name is reported rather than resolved. A
consumer silently attached to the wrong physician is a record defect nothing in the interface would
flag, so the matcher is deliberately narrow and the writes are deliberately manual.
`PersonContactKind.HealthcareProvider` now means a human contact *at* a provider, not the clinician.

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

Both mirror the same latch safety pattern: `DryRunAsync` computes + writes a timestamped Desktop
report and arms a latch; `CommitAsync(...)` refuses unless a dry run ran *this session* and the
caller passes back the exact reviewed values. Bulk completion pins count, cutoff, and the explicitly
entered completion date. Transient DI means a fresh instance starts un-armed.

### `FormDueDateBackfill` (`Sati.Data`)
- Corrects stored `Form.DueDate` from old (cycleStart-anchored) values to the current calculator's
  output. Touches **`DueDate` only** — never `IsCompliant`/`CompletedDate`.
- Buckets each form into the cycle that *produced* it, derived from `EffectiveDate` (never from the
  wrong stored date). The old-rule offsets appear **only** in `ImpliedOldCycleStart` for bucketing;
  new dates come from `FormDueDateCalculator.Compute` — one source of date-math truth.
- Anomalies (a stored date that fits no cycle) are reported and left untouched, not guessed.
- **Run 2026-06-29: 4,095 changed, 0 anomalies.** Reusable for future imports / provider swaps.

### `FormBulkCompletion` (`Sati.Data`)
- Marks every form due ≤ a cutoff and not already complete using one explicitly entered date,
  validated for every affected cycle. Each write is a System attestation with a fixed reason and a
  separate audit event; the tool no longer stamps each form's due date.
- **Run 2026-06-29: 308 marked (all reviews), cutoff 2026-06-10 inclusive.**

---

## Services Layer

All services follow the `IDbContextFactory<SatiContext>` pattern — per-method context lifetime via
`await using`. No long-lived `_context` fields. Correct and consistent across all services.

### `PersonService`
- Owns `Person` CRUD.
- New Person writes are validated by the transport-neutral `PersonSaveRules` in `Sati.Contracts.V1`.
  It covers required values, database length limits, supported enum values, representative-payee
  rules, and the complete/unique/date-consistent initial form graph. The API uses the same owner.
- The local seam requires the new Person's assigned user to be the signed-in actor and overwrites
  agency ownership from that actor. One `SaveChangesAsync` transaction commits the Person, forms,
  first lifecycle version, and audit event; a relational rejection rolls the entire graph back.
- The API likewise commits once and builds the response from the tracked graph. It does not perform
  a second read after commit that could report a false failure after the Person already exists.
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

### Add/edit client presentation boundary

`NewClientViewModel` contains every awaited preparation and persistence failure. Its user-facing
`ClientSaveProblem` states what was saved, what failed, and the safest correction without exposing
exception details. A cloud transport failure distinguishes a request known not to have been sent
from a response whose save status is unknown; the latter requires refreshing before retrying.

The optional Settings initialization and the four selection-triggered workspace loads are also
contained. A failed read-only refresh after a successful save explicitly says the client was saved
and must not be added again. Every async load is guarded by `LatestRequestTracker` before it may
publish an error for the currently selected Person. Incident reports carry a short support reference.

Adding a waiver no longer deletes the existing form collection before confirmation or Person save.
Replacement forms travel with the Person update, so cancellation and validation cannot erase the
old rows as a side effect.

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
- An editable note may be reassigned only to another client owned by the signed-in case manager in
  the same agency. The shared note editor asks for an explicit old-name/new-name confirmation; both
  persistence paths repeat ownership and revision checks and record `note.reassigned` with Person
  IDs only, in the same transaction as the note change.
- `GetDayScheduleAsync(userId, date)` returns every note on one case manager's calendar date across
  their caseload. It exists for the service-time overlap rule and is deliberately not person-scoped.

### `BillingService`
- Owns agency-scoped `BillingPeriod`/`ClaimLine` persistence and agency billing/EDI configuration.
  Billing permission and tenant scope are enforced in the service/API, not by tab visibility.
  Every stateful method takes an explicit minimal `AgencyActor`; the local implementation reloads
  the matching database user before work, while the cloud API ignores the client actor and uses the
  server-derived actor from `ValidatedActorFilter`.
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
  the audit event. Agency/business-setting overrides remain deliberately absent; personal text-entry
  shortcuts are a separate client-local preference and never travel through this service.

### `TextShortcutService` / `TextShortcutHook`
- Owns ten personal, client-local snippets keyed by Sati environment and signed-in user inside the
  current Windows profile. Each value is limited to 200 characters. This is typing assistance, not
  an agency configuration or clinical record, so it does not use the Settings API or weaken its
  administration-permission boundary.
- The keyboard hook handles Win+Shift+number only while the Sati shell is active, a non-empty mapping
  exists, and an explicitly marked editable note narrative or Scratchpad `TextBox` has focus. Every
  other key event is passed through to Windows unchanged. Snippet text is never diagnostic-log data.

### `ScratchpadService`
- Owns one dated Scratchpad per user plus append-only retrospective comments. Today's Work loads
  the agency-local current date; Tomorrow's Agenda loads the next weekday through the shared
  `WorkAgendaDates` rule (Friday, Saturday, and Sunday resolve to Monday). The future agenda is the
  future day's actual row, not a second record copied by a rollover job, so it becomes Today's Work
  automatically and cannot be promoted twice. Scratchpad content
  carries a `Revision`; saves load the current user's tracked row and reject stale copies instead
  of updating a detached object graph.
- The API returns `409 stale_scratchpad` for stale or legacy autosaves. Content-identical autosaves
  return the current revision without a database write or audit event; accepted changes and their
  PHI-minimized `scratchpad.updated` event share one save transaction.
- The desktop retains a confirmed-content baseline for Today and Tomorrow and does not send a
  timer, account-switch, or shutdown request for an unchanged draft. A `401` save rejection stops
  the agenda timer, preserves both visible drafts, and produces one accessible session-expiry
  warning instead of retrying each tab and opening recurring error dialogs.

### `AuthService`
- **DI inconsistency:** `new PasswordHasher()` directly instead of `IPasswordHasher` via DI
  (`UserService` does it correctly). Hasher non-swappable for auth without editing `AuthService`.
- Cloud sign-in issues a 30-minute access token carrying the original authentication time. The
  desktop renews through the protected `/auth/renew` route five minutes before expiry; the API
  revalidates the current user/role/agency on every renewal and preserves that original time so a
  session cannot slide past the configured 12-hour maximum without credential entry.

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
- `JournalDraftTracker` scopes the confirmed text baseline to the displayed Person. Selection and
  shutdown flushes skip unchanged journals, and completion of an outgoing Person's late save cannot
  replace the incoming Person's baseline.

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
| `FormAttestationRules` | Attestation date legality, form-note evidence eligibility, person/type/event-cycle resolution, and the derived pending-attestation list. |
| `AnnualDocumentCatalog` | Annual-document identity, display names, form-prerequisite mapping, and later packet eligibility. |
| `BillingRules` | Payer-neutral unit arithmetic, charge rounding, NPI and procedure-code format. |
| `NoteWorkflow` | Which note status may become which, for the case manager, the supervisor, and the overdue sweep — and therefore which notes can reach approval and billing at all. |
| `NoteSchedulingPolicy` | Future dates become non-billable Scheduled Reminders, with service, form, visit, and justification fields removed before persistence. |
| `ServiceTimeline` | The 7:00 AM – 7:00 PM service day and the no-double-claimed-minute rule. |
| `AuditCsv` | The audit export's header, column order, escaping, and spreadsheet neutralization. |
| `AtRequestPublication` | Whether an AT request is complete enough to publish, what the case manager attests to, and whether a published request may still be edited. |
| `AtRequestScreenshot` | The accepted format, downscale target, and size ceiling for a pasted item evidence clip. |
| `BillingRules.IsValidNpi` | NPI check-digit validation, shared by claim generation and provider directory entry. |
| `ProviderAffiliation` | Which medical tier may belong to which, what makes a proposed parent illegal — self, loop, wrong tier, another agency — how an ancestor chain resolves, and why an entry with entries beneath it cannot be deleted. |
| `ConsumerProviderRules` | What a consumer's provider list accepts, what "current" means, the at-most-one-primary-care and one-current-link-per-provider rules, the display order, and the runaway guard that is explicitly not a clinical limit. |
| `LegacyProviderLinking` | Matching the pre-directory free-text provider fields to directory entries — exact only, ambiguity refused rather than resolved — and what to tell the case manager for each outcome. Proposes; never writes. |
| `IncidentHealthScoring` | The versioned operational health score. |
| `JournalEntry` | The stamp format, the length ceiling, and the newest-first placement of an application-written journal entry. |

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

- **`Settings` is per-agency, not per-user.** Personal text-entry shortcuts are the concrete
  user-specific requirement, but remain client-local UI preferences rather than overrides to the
  agency business-settings model.
- **`HealthcareSystemName` on `Person` is denormalized by design.** Three seams pre-cut. Read the
  comments before "fixing."
- **`Incentive.ExcludedDatesJson` superseded** by `ExemptDate`; rollback pending `SchedulerViewModel`
  deletion. No new callers.
- ~~**Configurable billability scope** deferred.~~ Implemented through agency `Settings` and the
  shared `BillingComplianceRequirements` owner on 2026-08-27.
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
| `Form.Attest` / `Form.RevokeAttestation` or the `FormAttestation` shape | `FormAttestationRules`, both database contexts and API routes, `IFormService`, shared attestation control, audit actions, migration/backfill, billing-window regression tests |
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
**Owns the grid selection only.** Selecting a row calls `NoteEntry.EnterViewMode`; deselecting
returns a locked panel to New Note and leaves an open draft alone; a double-click routes through
`OpenSelectedNoteForEdit`. Every path that would replace panel contents first calls
`NoteEntry.TryReleaseDraft()`. It no longer owns a read-only copy of a note's fields — see
**Notes page: one panel per note** below.

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
`CalendarViewModel`: year loads take a `LatestRequestTracker` identity before publishing shared UI
state; `BuildMonths` rebuilds wholesale while preserving the selected service date. Calendar
failures remain inline and retryable. `ToggleExempt` awaits each `ExemptDateChanged` subscriber and
isolates a downstream dashboard-refresh failure rather than allowing an `async void` exception to
reach WPF's dispatcher. `CalendarNoteItem` is display-only and delegates service-time labels to the
shared `ServiceTimeline` rule. The focused-day view groups notes by `Note.EventDate` (date of
service), because the current note model has no separate logged/created timestamp.
`ScratchpadViewModel`: loads separate Today and next-
workday drafts, rolls them forward after midnight on window activation or the 10-min timer, and
explicitly saves both on shutdown/user-switch; diagnostics omit scratchpad content. A conflict is
tracked and reloadable per tab, stops
the timer, preserves both visible drafts, and blocks shutdown/user switching until resolved;
identical autosaves are server-side no-ops.
`GuidanceViewModel`/`HelpersViewModel`: static content.

### Billing ViewModels
`BillingDashboardViewModel`: `HasLoaded` guards; fire-and-forget `LoadAsync` (unobservable).
`BillingQueueViewModel`: sequential promotion (intentional — don't parallelize);
`IsComplianceOverride` reads correctly (contrast supervisor queue's hardcoded false); profiling
`Debug.WriteLine`s. `BillingSubmissionsViewModel`: billing-permission-gated agency scope;
**`IsTestMode = true` by default
— must be explicitly false for real submission**; inclusive billing-month range generation produces
one retry-safe file per locked period; `Process.Start("explorer.exe", ...)` is Windows-only. Its
history grid reads append-only exchange events and derives a `Not submitted` row for every
claim-bearing period that has none, aged from its oldest service date.
`BillingRemittancesViewModel` reads append-only claim outcomes and deposit anchors. Overview is
functional; the Alerts tab is now the denial/unpaid worklist with status, aging, and fast-search
filters.

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

`SyntheticClaimExchangeTests` adds a deterministic in-memory boundary test: a generated test-mode
837P receives representative accepted 999 and 277CA responses followed by a balanced synthetic 835,
and the simulator refuses a production-mode interchange. It is deliberately test-only. It does not
perform transport, validate full X12 conformance, import acknowledgments/remittances into Sati, post
payments, or provide clearinghouse/payer certification; every pre-live checklist item above remains.

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

### Compact display mode

`DisplayLayoutService` reads the physical pixel bounds of the monitor hosting `ShellWindow` when
the native handle becomes available. Either dimension at or below the 1920 × 1080 boundary selects
compact presentation for that application session. Physical pixels decide the display tier; WPF's
device-independent units remain responsible for ordinary Windows DPI scaling.

At the 1080p boundary the adjustment is silent: `NewClientViewModel` selects the horizontal compact
consumer picker and Shell/Clients XAML use tighter margins, padding, rail width, logo, and navigation
footprint. Below that boundary `ShellViewModel.ApplyCompactDisplayMode` also closes Today's Work and
`DisplayAdjustmentDialog` explains the adjustment once, before other shell-owned startup windows,
and recommends 1080p or higher. Both ordinary toggle commands remain available: compact mode supplies
space-saving starting state, not a new permission or a permanent lock. Overflow containers remain
the final accessibility boundary. Fonts, focus indicators, and hit targets are not globally scaled
down; the shell instead uses layout rounding and WPF's display-optimized ClearType rendering.

---

## What This Document Still Doesn't Fully Cover
- Full XAML view review (only the note-entry + task-board view and converters touched this session).
- `EdiGenerator` internals beyond the pre-live checklist.

---

## Local Case-Note Drafting (Closed-World Revision, 2026-08-22)

`ICaseNoteFormatter` remains the application boundary for assisted drafting. The singleton
`FoundryLocalCaseNoteFormatter` lazily loads one in-process Foundry Local model and serializes
inference. `LocalAi:Enabled=false` prevents initialization and hides the feature; no cloud inference
fallback exists. Runtime data is rooted at `%LOCALAPPDATA%\Sati\LocalAi`.

The model is no longer given prior notes, assessments, Bio, deadlines, contacts, billing data, or
any other historical client record. `IClientAiContextService` has become a selected-client
authorization boundary: it derives the actor from the current session, requires the selected person
to belong to that actor and agency, and returns only the person's ID and first name. Its API
counterpart is the actor-derived, own-caseload-only `GET /api/v1/people/{personId}/ai-context` route.
The rough note is not sent to that endpoint.

`CaseNoteFactCompiler` takes a snapshot of the current rough narrative and current template state.
It splits every rough-note fragment into a required fact and turns every selected Visit control,
selector value, detail, and attendee snapshot into its own stable required fact ID. Unchecked,
`Not documented`, and `Not assessed` values produce no asserted finding. Consumer presence is an
explicit three-state selector rather than a checked-by-default boolean.

The model receives that closed-world packet and returns JSON sentences with the fact IDs supporting
each sentence. Shared `Sati.Contracts.V1.CaseNoteDraftRules` reject the entire draft if a required
fact is omitted, a cited template value is not retained, a fact is used in the wrong section, or the
prose introduces an unsupported name, number, quotation, negation, or content word. The required CCM
opening and `Follow-up:` envelope are rendered by Sati only after validation. Follow-up is either
explicitly supported by a current-note fact or exactly `No follow-up was documented.`; form records
are not used to invent a fallback task.

One repair attempt is permitted when the first response fails deterministic validation; the same
current fact packet and validation errors are reissued locally. The model may instead return the
exact `USE_SAFE_BASELINE` control token, in which case Sati renders and revalidates its deterministic
current-fact plan rather than asking the model to risk a rewrite. Runtime failure or two rejected
answers also uses that verified plan and surfaces a warning. `LocalAiModelCompetenceTests` is an
explicit opt-in target-device gate. It requires every representative scenario to complete through
the local runtime without a rejection warning; safe deferral is permitted because forcing prose from
an uncertain small model would conflict with the zero-addition requirement. It remains skipped unless
`SATI_RUN_LOCAL_AI_MODEL_EVAL=1` is set, because enabling it may
acquire multi-gigabyte model weights. Ordinary CI never downloads a model and covers the compiler,
validation, tenant boundary, consumer reset, deterministic renderer, and stale-result behavior.

`NoteEntryViewModel` preserves the rough narrative and requires explicit human acceptance. It
captures a deterministic fingerprint of the selected person and every source fact. A person,
template, selector, detail, or narrative change cancels and invalidates in-flight work; both result
publication and acceptance recompute the fingerprint. Switching consumers must successfully unload
the previous model before new facts can be sent, and an unload failure stops generation.

The shared note-entry control may display the selected person's soonest settings-windowed item from
`IUpcomingEventService` as a suggested follow-up. It never enters the narrative automatically. Only
the case manager's explicit **Accept suggestion** action appends an editable `Follow-up:` line, at
which point it is a current-note fact and the existing compiler recognizes it through the same
follow-up-signal owner. Existing follow-up language disables the action so one note cannot acquire
two follow-up sections. Reminder notes never show the suggestion.

This is still a development feature, not a compliance or factual-truth guarantee. Before production
it needs an approved agency note standard and de-identified evaluation corpus, pinned model/rule
versions, measured rejection and factual-fidelity thresholds on actual target devices, a deliberate
accepted-draft audit/retention design, and review of model acquisition, cache, logs, telemetry, swap,
crash dumps, device encryption, and runtime lifecycle.

---

## Database Activity Feedback (2026-08-22)

`IDatabaseActivityTracker` is the single reference-counted owner of desktop database-wait state. In
Demo, `DatabaseActivityHandler` wraps the complete authenticated HTTP exchange. In Local Production,
`DatabaseActivityCommandInterceptor` wraps EF Core scalar and non-query execution and retains reader
leases through materialization. Neither path records routes, SQL, parameters, response bodies, or
other business data.

`DatabaseActivityViewModel` converts the shared count into presentation state. The shell shows the
animated watercolor Bodhi leaf immediately while one or more calls are active. One uninterrupted
call lasting eight seconds opens a non-modal, non-activating patience window; completing the final
overlapping call cancels the timer and closes the window. A completed short call can never leave a
delayed popup behind. This is feedback only: it does not change request cancellation, timeout,
authorization, error handling, or transaction behavior.

Settings exposes a 12-second visual preview through `DatabaseActivityPreview`. It acquires the same
payload-free tracker lease but deliberately performs no HTTP or EF work, so it cannot access client
records. The Settings card mirrors the global leaf while the modal dialog is open, and the patience
window is owned by the active Sati dialog so it cannot appear behind Settings.

`CloudApiClient` retries a failed request only when the exception proves DNS name resolution failed,
which means no connection was established and the request could not have reached the API. It makes
two bounded retries after 250 milliseconds and one second. Timeouts, connection resets, and other
ambiguous failures are never repeated automatically because a mutation may already have committed.
Connectivity failures cross the Scratchpad data boundary as `ScratchpadSaveException` with safe
recovery text; the exception and operational log contain no note narrative. Expected cancellation of
the eight-second presentation delay is observed as task state rather than raised as a first-chance
`TaskCanceledException`.

---

## Notes page: one panel per note (2026-08-23)

`NotesLogView` had two places that showed a note: the shared entry module on the left, and a
read-only `NotesDetailPanel` on the right that re-declared client, type, date, status, units,
return reason, and narrative as a second set of bindings. Two renderings of the same record can
drift, and neither was authoritative. The detail panel is removed. `NoteEntryView` is now the only
place a note is read or written, in three modes carried by one pair of flags on
`NoteEntryViewModel`:

| Mode | `IsEditing` | `IsLocked` | Heading |
|---|---|---|---|
| New Note | false | false | `New Note` |
| View Note | true | true | `View Note` |
| Edit Note | true | false | `Edit Note` |

- `EnterViewMode` / `EnterEditMode` are thin wrappers over one private `LoadNote(note, locked)`.
  `ToggleLockCommand` moves between the last two; locking re-runs `LoadNote` so the panel shows the
  saved record rather than an abandoned draft.
- `AreNoteFieldsEnabled` is the single owner of "these fields may not be changed". It folds both
  reasons together — Reminder type, and lock — so a control never has to know which is in force.
- `IsDateEnabled` is deliberately separate: an unlocked Reminder keeps its date picker available.
  Choosing a future date invokes the shared `NoteSchedulingPolicy`, changes the type/status to
  Reminder/Scheduled, retains the date, and removes service, form, visit, and justification fields.
  An undated Reminder continues to use the journal-entry route; the two modes never write the same
  text to both the journal and `Notes`.
- Read-only presentation uses `IsReadOnly` for text and `IsEnabled=False` for pickers, scoped by
  implicit styles in the form `Border`'s resources. A locked narrative stays legible, selectable,
  scrollable, and copyable; a disabled `TextBox` is none of those. The lock is a mistake-guard, not
  a permission: the API remains the authority on who may change a note.
- The lock is never signalled by the padlock glyph alone. The heading beside it reads View Note or
  Edit Note and is a polite live region, and the toggle carries an automation name and help text
  describing what clicking it will do.

**Unsaved work is never discarded silently.** `HasUnsavedChanges` is an explicit flag set by the
field callbacks and cleared by `LoadNote` / `ClearNoteFields`, not a diff against the saved note —
loading writes every field and visit attendees arrive asynchronously, so a diff would report
changes the case manager never made. `TryReleaseDraft()` asks through the injected
`DiscardChangesPrompt` (`ConfirmationDialog`; a test supplies a fixed answer). Every path that
would replace panel contents goes through it: grid selection, double-click, and re-locking. A
refused prompt snaps the grid selection back to where it was.

`OpenForEdit(Note?)` on the module owns the whole double-click decision — unlock in place if the
panel already shows that note, otherwise ask and load. Both hosts call it and neither repeats it;
`NotesWindowViewModel.OpenSelectedNoteForEdit` and `CaseManagerDashboardViewModel.EnterEditMode`
are each one line. They had already drifted apart once, with the dashboard skipping the guard.

**The way back is `StartNewNoteCommand`, and it is the only one.** A New Note button sits in the
module header beside the padlock, so both hosts have it without either page declaring it, and
Escape runs the same command — bound on the module (works from anywhere in the form) and repeated
on each host page (works from its grid). The button is always visible and merely disabled when the
panel is already blank: an affordance that appears and disappears has to be rediscovered each time,
and one that materializes mid-form also reorders keyboard focus. Hosts drop their grid highlight
off the `EditorCleared` event; the module knows nothing about grids.

`ReturnToNewNote()` keeps `SelectedPerson` — `Clear()` is the full reset and is what nulls the
client. The distinction is load-bearing: on the dashboard the module's `SelectedPerson` is mirrored
onto the page and scopes the notes grid, the compliance checkboxes, and the forms, so clearing a
note there must not blank the page around it. Saving already left the client in place for the same
reason, and now takes the same path.

The notes log's old **Deselect Note** button is gone. It existed to stop the detail panel showing
one note while the editor held another; with a single panel, "un-highlight the row but keep showing
its note" describes nothing anyone wants. Nulling `SelectedNote` directly — Ctrl-clicking the row —
still returns a *locked* panel to New Note and still leaves an open edit alone.

A status changed from the notes-log grid is pushed back into the panel by
`RefreshPanelForSelectedNote`, and only while the panel is locked and showing that same note. The
panel copies a note's fields in when it loads rather than binding through to the instance, so
without this a note just marked Logged would still read Pending on screen.

**A note changed by someone else is caught at unlock.** `VerifyLoadedNoteIsCurrentAsync` re-reads
the note and compares `Revision`. It runs when the padlock opens — not on a timer — because that
is when a stale copy starts to cost something: reading an old version is a nuisance, but editing
one means overwriting another person's change or losing a finished narrative to a conflict at the
end. It is fire-and-forget behind the unlock, guarded by a `LatestRequestTracker`, so a Demo round
trip never freezes the panel and a slow reply for a note the panel has moved off cannot publish
over it. Outcomes:

| Server state | What happens |
|---|---|
| Same revision | Nothing. |
| Changed, nothing typed yet | Panel reloads from the current version; banner names the differing fields. |
| Changed, unsaved typing present | Banner only. The case manager's work is never replaced. |
| Note gone | Banner saying so. |
| Read failed | Banner saying the check could not run; the note stays editable. |

This does not replace `NoteConcurrencyException` / `ReconcileNoteConflictAsync` on save, which
remains the authoritative check and still catches a change made in the seconds afterwards. Both
paths now share `FindLatestAsync`. The banner is an assertive live region — the case manager is
about to type into a record that is not the one they think it is, so it does not wait for a pause.

**Verification.** `NotePanelRenderTests` loads the real views on the shared `WpfUiHarness` STA
thread with the application's resource dictionary, and asserts runtime grid placement, read-only
state, disabled pickers, save-button visibility, and the attendee checkboxes inside the
`ItemsControl`. Structural XAML-as-XML assertions cannot reach any of that. The harness is the
assembly's single `Application` owner: WPF's one-per-AppDomain flag survives `Shutdown()`, so a
second creator makes whichever test runs later fail.

Filters moved from a full-height right-hand column to a `WrapPanel` band directly above the grid
they scope, so the grid and the note panel each get a full-height column.

**Bug fixed in passing.** `LoadNote` attaches `_editingNote` *after* setting `SelectedPerson`.
A genuine client switch clears the panel and nulls `_editingNote`, so the previous order left
`IsEditing` true with no note behind it and the next save wrote a new note instead of updating the
one on screen — a silent duplicate in the clinical record, reachable from the notes log because its
grid lists every client's notes. Covered by
`NotePanelModeTests.LoadingANoteForADifferentClientStillUpdatesThatNote`, confirmed failing against
the old ordering.

## Daily sign-in agenda (2026-09-01)

The daily agenda is a desktop presentation feature over existing authoritative data. It does not
introduce a task record and never changes a form, assessment, due date, compliance state, or billing
decision. `DailyAgendaBuilder` reads the case manager's already-loaded caseload and agency settings,
then combines three sources:

- every incomplete overdue `Form`, using the shared
  `BillingComplianceGate.IsIncompleteAndOverdue` predicate without an upper age limit;
- forward events from `IUpcomingEventService`, excluding `LateReview` because those rows describe
  the same overdue forms owned by the first source; and
- when both lists are empty, the soonest-due unattested Comprehensive Assessment form.

Each displayed list is capped at five rows. The overdue section retains the true total and orders
oldest first. Billing-blocking overdue forms receive a separate text cue. The assessment candidate
is selected from `Form.CompletedDate`, not the assessment workflow status; only that candidate's
assessment document is fetched. `GET /api/v1/people/{personId:int}/assessments/latest` is a narrow,
read-only DTO route guarded by the ordinary person-ownership check. Progress is calculated by the
same section definitions and answer-status rule as `ComprehensiveAssessmentViewModel`.

Startup ordering is deliberate. `App` awaits `ShellViewModel.InitializeAsync`, including
`Scratchpad.InitializeAsync` and the caseload load, before showing `ShellWindow`. The window's
`Loaded` handler then invokes `DailyAgendaLauncher`. Account switches invoke it only after
`ReinitializeAsync`. Confirm writes through `ScratchpadViewModel.ScratchpadContent`, so existing
dirty tracking, autosave, and conflict handling remain in force; Skip writes nothing. Open commands
navigate to the existing case-management/form surface and do not perform a record transition.

`DailyAgendaPreferenceService` stores `ShowAtSignIn` and `LastShownDate` under
`%LOCALAPPDATA%\Sati\daily-agenda-preferences.json`, keyed by environment and Sati user id. This is
per-machine presentation state, not agency policy and not a clinical record. Disabled and
already-shown-today paths return before settings, event, or assessment reads. Query failures are
logged and cannot block sign-in. The modal uses dynamic theme resources, a Demo indicator,
non-color status text, stable automation names, keyboard defaults, and initial checkbox focus.
# Safety-plan ownership

`SafetyPlanRules` in `Sati.Contracts.V1` owns the shared document schema and submission-completeness rule. `SafetyPlans` stores versioned clinical content behind the API; `SafetyPlanPdfGenerator` produces a status-labeled PDF. The API remains authoritative for author identity, tenant access, review transitions, approval, audit events, and artifact status.
