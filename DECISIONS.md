# Sati - Decisions

*Living document. The "why" behind choices that no diagram preserves. ARCHITECTURE.md
says what owns what; this says why it was built that way and what was rejected. Newest
sections at the bottom. Last updated: 2026-09-06.*

---

## Purpose

Every non-obvious design choice in Sati has a reason. Six months later the reason is
invisible in the code - you see the `[Flags]` enum, not the join table it deliberately
isn't. This file records the fork: what was chosen, what was rejected, and why. If a
decision here stops making sense, that's the signal to revisit it - not to quietly work
around it.

---

## Platform direction

### Sati is a cloud platform with a WPF client, not a cloud-attached desktop database

The existing WPF application remains a first-class staff client and the current product-development
surface. The target system, however, is an API-mediated platform that can support Windows, web, and
mobile clients. Distributed clients do not connect directly to Azure SQL.

**Rejected:** embedding a shared SQL credential in an installer. Trusted testers and synthetic data
reduce immediate harm but do not make an extractable credential a sound product boundary.

### The API is the authority

Authentication, authorization, tenant isolation, workflow transitions, transactions, audits,
migrations, and integrations are server responsibilities. UI visibility is never an authorization
control. Caller-supplied user or agency IDs are hints at most; authoritative identity comes from the
validated server session.

### Managed identity is service-to-service identity

Azure-hosted API and background-job identities receive least-privilege access to Azure SQL without
stored passwords. Managed identity does not replace a Sati user's login and is not distributed to
desktop installations.

### Tenant isolation is structural

`AgencyId` alone does not establish safe multi-tenancy. Every protected aggregate must have an
unambiguous tenant owner, and enforcement must occur centrally with automated cross-tenant tests.
Whether production ultimately uses a shared database, database-per-tenant, or a hybrid remains an
explicit design decision; no feature may assume that a forgotten query predicate is adequate
isolation.

### Network contracts use DTOs, not EF entities

EF entities are persistence models and may contain navigation graphs, internal fields, password
material, or properties callers must not set. API request and response contracts are deliberately
small, versionable DTOs. In particular, `PasswordHash` and `Salt` never leave the server.

### Submitted healthcare records are amended, not overwritten

Drafts may remain mutable. Submission, approval, signature, claim generation, or other defined
record-finalization events create immutable versions. Later corrections produce amendments or new
versions with a linked reason and actor. Audit events are append-only.

### Person lifecycle history is a separate, PHI-bearing ledger

The small `AuditEvent` envelope remains intentionally free of narratives and profile values. A
Person audit has a different evidentiary purpose, so each revision stores a compressed full snapshot
and the exact field-level before/after changes with actor, agency, timestamp, and request ID.
Application code may append a version but may not update or delete one. Admin history access is
tenant-scoped and audited, and PDF responses are non-cacheable.

**Rejected:** storing Person values in the general activity log. That would spread PHI across every
operational audit query and make retention and access control harder to reason about.

**Rejected:** inventing history for existing People. The first touch creates an explicit tracking
baseline of current state; only subsequent changes can truthfully identify who changed what and when.

### Clients do not migrate cloud databases

`Database.Migrate()` remains acceptable during local development while the transition is underway.
Production and distributed Demo schema changes run as controlled deployment operations before the
new application version is admitted.

### Demo schema and Demo seed are separate assets

Migrations define structure. A versioned canonical seed defines the synthetic superhero/sitcom
dataset and stored demonstration logins. The intended design is a scheduled Azure job restoring
that baseline nightly under a reset-specific managed identity, separate from the API identity.

**Daily caseload refresh configured 2026-09-06.** A timer-triggered Azure Function now updates the
canonical working caseload at 3:15 AM Eastern under its own managed identity and restricted SQL
role. It rolls dates forward, completes ordinary synthetic profiles, preserves six labeled teaching
exceptions and the superhero/TV humor, repairs synthetic claim prerequisites, and fails unless its
post-commit validation passes. A live run and an immediate repeat run both succeeded.

This deliberately does not claim the stronger baseline-restoration workflow described above. It
does not stop concurrent mutations, remove every user-created Demo row, or reset stored Demo
passwords. Full baseline restoration and a notification destination for failure alerts remain
tracked in `AGENDA.md`; until then, describe the deployed feature as the daily Demo caseload refresh,
not a complete nightly reset.

### Automated tests are a platform prerequisite

Further feature work may continue, but production expansion requires tests for tenant isolation,
authorization, workflow transitions, concurrency, billing rules, audit completeness, migrations,
and reset/recovery. Manual verification is not sufficient evidence for a healthcare SaaS platform.

---

## Data & persistence

### Snapshot semantics for documents of record
AT requests freeze the client, case-manager, and (on select) vendor fields onto the
request row at creation, rather than reading them live at render. A payment request is a
document of record: it must re-render months later exactly as filed, even after the
client's name or the CM's phone changes. This is a deliberate departure from Sati's
compute-don't-store default (form due dates, upcoming events, compliance - all derived).
The live FK (`PersonId`, and the future `ProviderId`) rides alongside for
navigation/filtering but is *not* the render source.

**Rejected:** live lookup at render. It would silently rewrite filed financial documents
when source data changes.

### Rate, not amount, for every percentage
`PassthroughRate` (0.15) and `SalesTaxRate` (0.055) are stored as fractions the code
multiplies directly - no `/100` anywhere. `decimal(5,4)` columns give sub-percent room
(0.1550) without a schema change. Amounts get frozen onto the request at save; the rate
is the adjustable default.

### Passthrough applied post-tax
The 15% agency passthrough is computed on the *tax-inclusive* subtotal:
`(subtotal + tax) * (1 + rate)`. This is contrary to the OADS form's visual row order
(passthrough line sits above the tax line) but matches how the fee is actually assessed.
`ATRequestCalculator` is the single owner of this arithmetic.

### Mirrored math in the AT queue projection
`ATRequestService.GetAllForUserAsync` re-expresses the total formula inline in the LINQ
projection because EF can't translate `ATRequestCalculator.Total` into SQL. This is a
known, commented shadow copy: if the passthrough formula changes, it changes in the
calculator AND in that projection. Accepted because the alternative - loading every
item row to compute totals in memory for a list view - defeats the projection.

### Per-method `IDbContextFactory` context lifetime
Every service method creates and disposes its own context via
`await using var context = _contextFactory.CreateDbContext()`. No context outlives a
method. This kills the change-tracker collisions and memory bloat of the old
session-long-context pattern, and makes concurrent service calls safe (enables
`Task.WhenAll` in loops).

### Restrict vs. cascade on delete, per record's independent value
`ATRequest -> Person` is `Restrict`: a payment request carries snapshot columns and
survives the client's deletion (it's a financial record of its own). `ATRequestItem ->
ATRequest` is `Cascade`: line items are worthless orphaned from their request. `Note`
and `Form` cascade from `Person` for the same reason items do - no independent value.
The delete behavior encodes whether the child is a record in its own right.

---

## ViewModels & UI

### Write-through observable wrappers over plain entity POCOs
Entities (`ATRequest`, `Provider`, `ATRequestItem`) stay INotifyPropertyChanged-free.
Each gets an editor VM (`ATRequestEditorViewModel`, `ProviderEditorViewModel`, etc.)
whose bindable properties read and write the entity directly. The entity is always
current, so Save just persists it - no copy-back step. The VM is a notifying lens, not
a second store.

**Rejected:** INPC on the entities. It couples the domain model to WPF and muddies which
object is the source of truth.

### Item-total notification via injected callback, not per-row subscription
`ATRequestItemEditorViewModel` takes an `Action` from its parent and invokes it only
when cost/quantity change (Name/URL don't move money). Cheaper and clearer than the
parent subscribing to each row's `PropertyChanged` and filtering by property name - the
row already knows which of its own edits affect totals.

### One bool per `[Flags]` bit in the provider editor
The four waiver-service checkboxes each map to one bit of `Provider.OfferedServices`:
set with `| flag`, clear with `& ~flag`. The editor VM does the bit math so the XAML
binds plain bools.

### Master-detail over inline grid for provider CRUD
Providers edit as list-plus-form, not an editable DataGrid. The passthrough checkbox
reveals three conditional billing fields - a "check this, three fields appear"
interaction a grid cell can't do gracefully. Consistent with the AT page and client
patterns.

### Deferred Save + PDF as one batch
The AT request editor has no Save yet, by design. `NewRequest` builds in memory,
`CloseEditor` discards. Save is bundled with the future Publish-PDF feature because
both are the same trip to disk - designing persistence once with both callers in view
beats bolting Save on now and refactoring when export lands.

---

## Provider directory

### Passthrough is orthogonal to waiver services
`ProvidesPassthroughService` is a standalone bool, NOT a member of the `WaiverService`
flags enum, even though its checkbox renders among the waiver-service checkboxes. Maine
AT Solutions proves the axes are independent: it offers AT Assessments (a waiver service)
AND provides passthrough - two unrelated facts. Keeping passthrough separate makes "who
can the AT page pick" a clean `where ProvidesPassthroughService` with no enum-member
exception.

### Maine AT Solutions is a seed row, not a hardcoded special case
The statewide passthrough default is a `Provider` row with `ProvidesPassthroughService =
true`, seeded in the migration, pointed at by `Settings.DefaultPassthroughProviderId`.
Nothing branches on its identity. Change the setting and the default moves; the dropdown
is just "every passthrough provider."

**Rejected:** a magic "Maine AT Solutions" string or dropdown option. It would spread an
identity check across the AT page and settings.

### `DefaultPassthroughProviderId`: nullable FK, no SQL default
The FK deliberately has NO `HasDefaultValue`. A SQL default of 1 would backfill the
existing Settings row at `AddColumn` time - which can fire before the Provider seed
inserts row 1, an FK violation. Nullable-null is safe; the default gets set through the
Settings window instead. `OnDelete(SetNull)` so deleting the current default provider
clears the setting rather than blocking the delete (or stranding a retired agency).

### `[Flags]` bitmask for `OfferedServices`, not a join table
Four fixed, statutory waiver services stored as an int bitmask - the idiomatic .NET
choice. This IS a denormalization, named openly because Sati otherwise hearts normalized
structures: a `ProviderOfferedService` join table is the "correct" 3NF shape. Rejected as
ceremony for no payoff while the offering data is inert (nothing consumes it until
client<->provider links exist). Revisit if the service list becomes dynamic or needs
per-offering metadata.

### Structured provider address
Street/City/State/Zip as separate columns, mirroring `Agency` - not a single address
string. Reference data earns normalization even before anything parses it.

---

## Scope calls (things deliberately NOT built)

### URL stored, not scraped
`ATRequestItem.Url` is a plain stored field. Auto-extracting name/price/cost from a
retailer URL was scoped and rejected: retailers defeat scraping, rearrange their DOM,
and wall bots - and an outbound HTTP scraper is out of place in an app steering toward
HIPAA. The URL feeds a future screenshots-with-clickable-links page, entered by hand.

### URL not on the page-1 OADS form
The item table on `ATFormDocument` is a faithful reproduction of the state form, which
has no URL column. The URL is internal metadata surfaced in the app and (later) on
page 2, never on the filed document.

### Client<->provider association is a separate slice

The AT passthrough dropdown lists *all* passthrough providers and can't pre-select
"this client's home-support agency" because Sati has no consumer->provider link yet.
That association is its own model and slice; the four `OfferedServices` flags are inert
until it lands.

## Comprehensive Assessment and Person-Centered Plan

### Assessment scope is waiver-agnostic

The Comprehensive Assessment describes a MaineCare member with IDD and/or ASD who
receives case management. It is not a Section 21 form or a Section 29 form. Waiver and
level-of-care determinations live in Classification so the same assessment can support
Section 21, Section 29, and a future Lifespan Waiver without duplicating the person's
story or creating waiver-shaped answers.

### Assessment cadence is intake plus annual, due 60 days before PCP

An assessment is required at intake and annually. The annual assessment due date is 60
days before the PCP anniversary. Sati's existing `Form` deadline/reminder system remains
the canonical scheduler. The new migration updates a legacy default setting of 120 to
60; existing generated form rows require a separate inspected reconciliation.

### One team assessment, with dissent preserved explicitly

The ordinary answer represents the team's assessment. Sati does not force separate
"person says," "guardian says," and "case manager says" fields on every question.
Where participants disagree, the record preserves a differing perspective, its author,
discussion/resolution, and whether disagreement remains. The main answer must not be
rewritten to conceal dissent.

### Question-specific response design instead of a universal support scale

Different questions use response structures appropriate to the subject. Support is not
a single ordinal level. Where support characteristics are relevant, setup/environment,
prompting/coaching, hands-on assistance, another person completing part or all of an
activity, and variation by situation may be selected together.

`No support currently needed`, `Not applicable`, and unavailable-answer dispositions are
exclusive alternatives. `Varies` requires at least one concrete support selection and an
explanation. Every question must also provide practical guidance describing why it is
asked, what a valid answer contains, realistic examples, and answers to avoid.

### No silent blanks and no false completion

Every question must be substantively answered or assigned an explicit disposition such
as not applicable, declined, unable to assess, or follow-up required. A follow-up-required
answer blocks completion. The assessment cannot be submitted as complete while required
content remains unresolved.

### Needs are reusable domain objects

Material needs and broader support, skill, access, health/safety, relationship,
autonomy/rights, and planning needs are stored separately from narrative answers. A need
may reference a provider, PCP goal, desired result, action, and resolution history.
Changing a provider must not erase the underlying need. Approved documents retain a
snapshot even when directory information later changes.

### Authorship follows caseload ownership

Only the case manager assigned to the consumer may author that consumer's assessment.
A supervisor who carries a caseload may author assessments for those assigned consumers,
but supervisory status does not permit rewriting another case manager's answers.
Supervisors review by flags, comments, returns, and wholesale approval.

### Assessment requires supervisor approval; OADS approval begins at PCP

The Comprehensive Assessment ends with supervisor approval. OADS Resource Coordinators
do not approve the assessment. The approved assessment informs the PCP; the PCP is the
document submitted for OADS review and wholesale approval. Resource Coordinators may
flag particular PCP sections and return the plan but do not silently edit the submitted
record.

### Live profile, immutable approved documents

The consumer profile is the live source of truth. Assessments and PCPs may draw from it,
but an approved or signed document version is immutable. Profile changes may update live
context and create PCP change candidates; they never silently rewrite the operative plan.
Predefined rules classify impact, the case manager confirms applicability, and important
changes require supervisor review.

### Physical signatures retain both artifacts

Both Comprehensive Assessments and PCPs will support generated PDFs, physical signatures,
and upload of signed scans. Sati retains the generated PDF and signed scan against the
same exact version. Any substantive later edit creates a new version and signature cycle.

### PCP meeting and authorized-services record

The PCP records all meeting participants and assumes the assigned case manager organized
the meeting unless explicitly changed. Authorized services belong in the PCP. A future
top-level Providers directory will supply provider information, but each approved PCP
retains its own authorization/provider snapshot.

### Compliance and billing gaps are permanent

There is no grace period after a PCP or 90-day review deadline. At midnight after the due
date, newly submitted case notes are retained as service documentation but are permanently
unbillable. Later completion does not restore billability for the gap. Supervisors and
higher roles may override an overdue assessment solely for PCP submission, with a reasoned
audit record; this does not imply a billing override.

## Local AI-Assisted Case Notes

> Historical note: the original background-context and calculated-follow-up decisions below were
> superseded by the closed-world transformation decision dated 2026-08-22. They remain here as the
> development record, not as a description of current behavior.

### The model drafts; the case manager authors

AI output is never written directly to a note, submitted, logged, billed, approved, or used to
determine compliance. Sati retains the user's rough narrative while presenting the generated text
as a separate draft. The case manager must compare and explicitly accept the draft, may edit it,
and remains the author responsible for the submitted record.

The model may reorganize and clarify supplied facts but may not add missing services,
interventions, durations, participants, outcomes, quotations, diagnoses, consent, risk,
follow-up, or assertions of billability. Sparse source material must produce a sparse draft rather
than a plausibly completed fiction.

### On-device inference with an explicit off switch

The development slice uses Foundry Local in-process, initialized only when requested. Model catalog
lookup and first-time model acquisition may use the network, but note inference is local. The
feature is controlled by `LocalAi:Enabled`; disabling it hides the UI and prevents runtime/model
initialization. No cloud fallback is permitted silently.

### Note policy is external and versionable

Agency drafting instructions live in `AI_CASE_NOTE_RULES.md`, not inside the ViewModel or XAML.
This allows the standard to be reviewed and refined without entangling presentation code. Before
production, Sati must persist the exact rule-set/model version used for an accepted draft and test
every change against a de-identified regression corpus.

### Required note envelope and calculated follow-up

Every generated draft begins `Community Case Manager (CCM) [signed-in user's full
name]` and finishes with a `Follow-up:` section. The signed-in user's display name is
trusted application context, not guessed from the rough note. When the source does not
contain an evident follow-up, Sati supplies the model with a deterministic fallback
from the consumer's form records: the most recently overdue core form first, otherwise
the next incomplete 90-day review, Comprehensive Assessment, PCP, or Reclassification.
The model does not calculate or invent the form or due date.

### Client background is assembled, not learned

The local model is not fine-tuned on or given permanent memory of client records. On each request,
Sati builds a fresh, permission-checked context snapshot. General Bio is always included. Journal,
address, phone, MaineCare ID, diagnosis code, place-of-service, and billing fields are excluded from
the query. This is a purpose limitation, not a claim that those fields can never support a future
separately authorized AI workflow.

The first retrieval policy is intentionally understandable: ten recent notes plus up to five older
keyword-matched notes, current service flags and deadlines, and either the author's active draft
assessment or the latest approved assessment. Semantic embeddings are deferred until this behavior
has been evaluated with de-identified cases.

Prior records may clarify established names, roles, services, and deadlines, but cannot establish
what happened during the contact being documented. Client-record text is untrusted prompt data,
not executable instruction. The case manager can expand **Context used** before accepting a draft
to see the source note IDs and document version supplied to the model.

## 2026-08-14 — The local model is force-reset across a consumer switch, not trusted to reset itself

`FoundryLocalCaseNoteFormatter` keeps one Foundry Local model loaded and shared for the life of the
application, across every consumer the signed-in case manager formats a note for. The query layer
(`ClientAiContextService`, and its cloud counterpart, the `/people/{personId}/ai-context` endpoint)
was already scoped per person and per requesting user, but nothing forced the shared model instance
itself to discard whatever it held from the previous formatting call before the next one began.
Sati does not have visibility into whether the underlying native runtime retains any residual
per-call state, and does not assume it doesn't.

`CaseNoteFormattingRequest` now carries the target `PersonId`. `ConsumerSessionBoundary`
(`Services/LocalAi/ConsumerSessionBoundary.cs`) records the consumer whose facts most recently
reached the model and reports whether the next request targets someone else. When it does, the
formatter must successfully unload and reload the model before generating; an unload failure stops
the next request. Consecutive requests for the same consumer skip the reload. Isolation regressions
cover reset decisions, own-caseload identity context, absence of historical record text, and
suppression of an in-flight result after a client switch.

## 2026-08-12 — Protected API requests revalidate the actor and distinguish review from authorship

Every protected `/api/v1` request revalidates the JWT user, agency, and role against the current
database record before feature code runs. Shared `TenantAccess` checks define self, caseload, and
supervisory access, while `API_AUTHORIZATION.md` names the authoritative tenant owner for every
protected route. A supervisor's permission to read or review a case manager's work does not grant
permission to author or edit that case manager's assessment. Generated billing and AT artifacts
inherit the same tenant boundary as their source records.

## 2026-08-12 — Audit content is minimal, state changes are atomic, and stale assessments fail closed

Audit events are an activity index, not a copy of a client record. They store stable action and
resource identifiers, actor, agency, timestamp, and correlation ID; they do not store narratives,
documents, passwords, reasons, tokens, or billing identifiers. A protected mutation and its audit
event use one EF Core save transaction so neither can commit alone. Application contexts prohibit
tracked audit updates/deletes; production SQL permissions and retention/legal-hold procedures remain
required. Assessment revisions use optimistic concurrency and return HTTP 409 for stale writes.
Claim lines are unique by service-note ID, making repeat billing commands fail safely.

## 2026-08-13 — Billing records freeze their claim inputs

A claim line stores the service date, procedure/modifier, units, charge, diagnosis, place of service,
and a versioned immutable snapshot of the subscriber, billing provider, submitter, and payer values
used to create it. The 837P generator reads that snapshot rather than live Person or Agency rows.
Later corrections require a deliberate financial correction/amendment workflow; an address or agency
configuration edit must never silently rewrite a claim that was already assembled.

**Rejected:** resolving subscriber/provider values live during EDI generation. That would make the
same submitted period render differently after an ordinary profile edit and would destroy the
evidentiary meaning of the stored file.

## 2026-08-13 — Billing rules are configured per tenant and revalidated at promotion

Procedure, modifier, unit rate, submitter, payer, and contact values belong to the agency tenant and
are administered only through an Admin-authorized boundary. Queue display is advisory: claim creation
reloads and rechecks approval, current compliance, the historical billing window, identifiers,
structured addresses, provider fields, and configuration immediately before persistence.

The shared Section 13 arithmetic grants a one-unit minimum for a substantive contact up to 15 minutes
and preserves partial 15-minute units thereafter. Units and monetary charges are separate fields.
Representative Demo values prove behavior only; live use still requires the agency's authoritative
contract/rate and clearinghouse/payer acceptance.

## 2026-08-13 — Agency incident visibility is curated; cross-tenant visibility is separate

Agency Admins need enough operational information to recognize recurring failures, coordinate with
their staff, and confirm whether an incident is being investigated or resolved. Their dashboard is
therefore limited to their own agency and to a curated envelope: severity, lifecycle status,
release, sanitized operation, reference, timestamps, occurrence count, and a one-way exception-shape
fingerprint. It does not expose exception messages, stack traces, request bodies, URLs, narratives,
credentials, tokens, connection strings, or another agency's activity.

Cross-tenant support is assigned to a separately provisioned `PlatformOperator`, not to a more
powerful agency Admin. Every cross-tenant dashboard view is audited, and this identity is denied
ordinary agency business endpoints and excluded from agency user-management workflows. Raw local
diagnostics remain workstation-only support material and are not copied into the aggregated table.

The versioned health score is an operational signal, not a service-level claim. Version 1 scores
recorded incident groups active in the selected window and states that group occurrence totals are
cumulative; it does not imply crash-free sessions or measured availability until safe denominators
and job telemetry exist.

**Rejected:** exposing raw stack traces to agency Admins, giving ordinary Admins cross-tenant access,
or presenting an incident-only score as proof of uptime. Each would reveal unnecessary technical or
tenant information or overstate what the collected data can establish.

Incident updates must also survive simultaneous copies of the same failure. Sati combines a bounded
striped process gate with a serializable transaction and a unique database key. The gate is only a
contention reducer; correctness does not depend on one API instance. This preserves a single group,
an exact occurrence count, the earliest and latest observation, and the highest reported severity.
The integration suite sends 24 simultaneous reports through separate database connections to keep
that guarantee testable.

**Rejected:** an unbounded dictionary containing one lock per fingerprint, which could grow without
limit, and a plain read-then-insert sequence, which loses reports or returns unique-key failures under
contention.

## 2026-08-14 — Service time is a claim on the case manager's day, enforced server-side

A case manager cannot bill 9:00–9:15 for one client and 9:10–9:20 for another: those notes
double-claim ten minutes of one person's time. The rule is therefore scoped to the **case manager
and the calendar date, never to the client**, and it is owned by `ServiceTimeline` in
`Sati.Contracts` so the desktop client and the API evaluate identical logic.

Note.StartTime already existed as minutes elapsed from 7:00 AM but was never written by any client.
That storage convention is now the definition of the loggable day: 7:00 AM to 7:00 PM, offered at
five-minute granularity. The note-entry panel draws the day as a bar — recorded time, this draft,
and any collision — and states the verdict in a sentence, because color alone cannot carry it.

Start times remain **optional**. Every note recorded before this feature has none, and a note with
no start time claims no minutes and conflicts with nothing. Requiring one would have invalidated
historical records and blocked note types where clock time is not meaningful.

Intervals are half-open: a note ending at 9:15 and a note starting at 9:15 are adjacent, not
overlapping. Back-to-back contacts are ordinary work and must not be rejected.

Cancelled, Delayed, and Abandoned release their time — the service did not happen. Every other
status, including Scheduled, holds it, because planned work is still a commitment of the same hour.
A note never conflicts with the stored copy of itself, so editing a note's narrative does not
require moving its time.

The client checks the rule twice: live, for the bar, and again against freshly loaded data
immediately before persisting. The API repeats it on every create and update and answers
`service_time_overlap` or `service_time_window` with 409. Overlap decides what may be billed, so
a distributed client cannot be the enforcement point.

**Rejected:** snapping legacy off-grid start times to the nearest five-minute slot, which would
silently move a recorded service time; scoping the check to one client, which would miss the actual
double-billing case; and treating the client-side check as sufficient.

## 2026-08-14 — Input and grid text colors are owned by App.xaml, not by the framework theme

Two controls rendered illegibly on the dark themes because the framework's default templates paint
surfaces the app's `Background` never reaches. The ComboBox builds its closed-state chrome from a
ToggleButton whose style hard-codes a near-white fill and drops its list into a popup painted with
`SystemColors.WindowBrush`; with a themed light foreground that produced white text on near-white.
`DataGridCell` pins its foreground to a near-black system color, so grids that themed only the row
background produced dark text on a dark row.

Neither is reachable by a style setter, so `ComboBox` is fully retemplated and the `DataGrid` family
gains themed implicit styles in App.xaml. Text and selection colors for the input family now have a
single owner. Local `TextBox` styles use `BasedOn="{StaticResource {x:Type TextBox}}"` so a narrower
scope refines the theme instead of silently dropping it.

**Rejected:** per-view color patches, which is how the defect spread in the first place, and
per-theme overrides of framework brush keys, which would need repeating in all ten theme files.

## 2026-08-14 — The audit export is an execution surface, and its format has one owner

RFC 4180 quoting makes a CSV field parse correctly; it does not stop Excel, LibreOffice, or Sheets
from evaluating it. Those readers strip the quotes on import and then treat a leading `=`, `+`,
`-`, `@`, tab, or carriage return as a formula. The audit export is opened by an Admin, an auditor,
or a state reviewer, so a field that survives quoting still reaches an execution context.

`ActorDisplayName` made that a privilege-boundary crossing rather than a theoretical one: a
Supervisor may set the display name of any case manager they supervise, and only an Admin may run
the export. Sati now neutralizes any value beginning with a formula trigger by prefixing an
apostrophe, preserving the original characters after it so the record stays faithful to what was
stored. Neutralization applies to **every** column rather than only the untrusted ones, so adding a
column cannot silently reopen the hole.

The format lived in two hand-built copies — the API export and the desktop's local export — which
is how one could have been fixed and the other left behind. `Sati.Contracts.V1.AuditCsv` is now the
single owner of the header, the column order, and the escaping, and both call it.

**Rejected:** stripping or rejecting the offending characters, which would make the audit record an
inaccurate copy of what the system actually stored; and fixing only the API path, which would leave
the desktop export exposed and reintroduce the drift that caused the problem.

## 2026-08-14 — Sign-in spends the same work whether or not the account exists

The login handler returned early when no user row matched, skipping 100,000 PBKDF2 iterations. A
missing account answered in 2.9 ms against 9.9 ms for a wrong password on the development machine —
a reliable oracle for enumerating which clinician accounts exist, and a way to pick targets for the
per-username lockout.

`PasswordVerifier.VerifyMissingUser` performs the same derivation against a fixed decoy credential
and always returns false. It looks like dead code and is not: the API authorization inventory says
so explicitly, and a regression test asserts the timing property. That test was confirmed to fail
against the unfixed handler before it was kept — a security test that passes either way is worse
than no test, because it reports safety it never checked.

**Rejected:** a fixed artificial delay, which is both slower and still distinguishable under
statistical sampling, and relying on the rate limiter alone, which bounds the rate of enumeration
without preventing it.

## 2026-08-15 — Provider directory entries are local knowledge about a shared organization

A `Provider` row is not "an organization." It is **one agency's local record of** an organization:
its contacts there, its notes, whether that organization acts as a passthrough agency *for it*.
Several agencies each holding a Spurwink row is therefore correct, not redundant. The rows differ
in exactly the way they should.

That distinction becomes load-bearing when Karuna arrives, because a real organization may then
exist twice: as directory entries typed by case-management agencies, and as a tenant with its own
users and data. Three concepts have to stay separate — the **organization** (platform-wide legal
identity), the **directory entry** (per-agency local knowledge), and the **tenant** (an
organization that has logged in). One organization has many directory entries and at most one
tenant.

**Reconciliation is a link, never a swap.** When an organization onboards, it is matched to a
canonical organization identity and the existing directory entries are linked to it. No row is
repointed, no foreign key rewritten, no history disturbed — which is precisely what makes the
transition seamless. Swapping would mean chasing `Settings.DefaultPassthroughProviderId`,
`ComprehensiveAssessment.ProviderId`, and every reference added afterwards, forever.

**Rejected:** an interface with local-provider and Karuna-agency implementations. That solves
polymorphism, and the actual problem is identity — an interface would still leave the same
organization stored twice with nothing establishing they are the same. An abstraction does belong
on the *read* side, resolving available passthrough providers from both local entries and live
agencies, but it sits on top of the identity model rather than replacing it.

### Why the identifiers exist now, years before the registry they serve

`Provider.Npi` and `Provider.MaineCareProviderId` were added on 2026-08-15, long before any
Organization table. Everything else in the design can be introduced by migration later; the
identifiers cannot. A name typed today with no identifier can only ever be matched by fuzzy
comparison against hundreds of rows, by hand. Data not captured is simply gone.

Both are optional, because a directory entry is often created from a phone call before any
paperwork exists, and both are recorded because either may be the one an organization supplies.
NPI is validated with `BillingRules.IsValidNpi` — the same Luhn check already used for claim
generation — so a typo is caught at entry rather than surfacing years later as a failed match.

Uniqueness is enforced per agency by filtered unique indexes, and checked in the API and the
transitional local service so the answer names the existing entry instead of surfacing a
constraint violation. Uniqueness deliberately does **not** span agencies.

### Published contacts supersede local ones, but only as an explicit disclosure

Once an organization is a tenant, it maintains its own passthrough contact details and those
become the default for every linked agency, replacing what each agency typed. Two rules keep that
safe:

Local values are **demoted, not deleted**. An agency that genuinely has its own named contact at
that organization re-asserts it in one click, and nothing is lost. Before onboarding every local
value was only ever "I typed this because nobody authoritative existed," so adopting the published
set by default is right — but the escape hatch has to exist, because a general billing contact
replacing a specific account contact is a regression wearing the costume of an improvement.

Publishing is an **explicit act with an explicit payload**, never a view onto the organization's
internal contact records. Resolving outward-facing contacts from internal data would disclose
internal staff details to every linked agency the moment they onboard. The published set is
visible only to agencies with an active relationship, and that relationship is itself a record —
an organization going live must not appear in every agency's passthrough picker, because
passthrough is a contract between two specific parties, not a global fact.

The swap is announced twice — once to each affected agency when it happens, once at point of use
so a changed contact on an AT request is explicable — and audited on both sides, because these
contacts feed a financial document. Submitted AT requests are unaffected: they already snapshot
vendor details with no foreign key. A draft created before a swap and submitted after is **not**
silently re-snapshotted; it reports which fields changed and lets the user decide, matching the
conflict-reconcile idiom already used for notes.

---

## 2026-08-15 — Publishing an AT request records an attestation, not a signature

An AT request is a document of record that leaves the agency. Until now it could be edited
indefinitely and had no notion of being finished. Publishing closes that: it records who published
it and when, freezes the statement they affirmed, moves the request from `Development` to `Review`,
locks it, and generates the exportable PDF.

**What is captured is an attestation, and the product says so.** The signer is taken from the
authenticated session rather than typed, the recorded name is therefore the account that performed
the act, and the generated PDF carries a notice stating plainly that this is not an electronic
signature under any state or federal standard. `REGULATORY_CONCERNS.md` holds the open question of
whether OADS requires one. Claiming more than was captured would be the expensive kind of wrong:
a document that reads as executed when nothing executed it.

**The statement is frozen onto the request, not referenced.** `AtRequestPublication` owns the
current wording, but a request published this year must keep rendering the wording its signer
actually read. A signature that floats to whatever the constant says today attests to nothing in
particular.

**Publication is its own operation, because of where the trust boundary sits.** Over HTTP, an
attestation arriving in a request body is a claim by the caller about who signed. `POST
/at-requests/{id}/publish` derives the signer from the validated actor, and `SaveAtRequestRequest`
deliberately has no signer field for a client to populate — the attestation is outbound-only on the
DTO. Making it a named operation rather than "an update that happens to carry a signature" means
neither implementation can quietly start trusting the client's version. The discriminating test is
a supervisor publishing a case manager's request: an implementation that stamped the form's
case-manager name passes every same-person test and fails that one.

**The lock is server-side and tested against the stored row.** `ATRequestService.UpdateAsync` and
the API's `PUT` and `DELETE` both refuse a published request, and both ask the *stored* record
whether it is published rather than the incoming copy — asking the incoming copy asks the party
being restricted. The desktop's disabled fields are a courtesy, not a control.

**Correction is reopening, which discards the attestation.** The alternative is a signature that
stays attached while the document beneath it changes. Reopening is audited under its own action and
carries the discarded signer in its metadata, so the trail does not simply go quiet where a
signature used to be.

### The executed PDF is not retained, and that is the decision

See the entry below on regeneration. The short version: the PDF is a pure function of a frozen
record, so storing it would duplicate data the request already holds — including the screenshots,
which are the bulk of the file.

The generated PDF is a Sati document carrying the required information, not a reproduction of the
OADS Authorized Payment Information Form. If OADS requires their exact form, only
`ATRequestPdfExporter` changes; publication, attestation, and locking do not know what the page
looks like.

---

## 2026-08-15 — Item evidence is a pasted clip on its own page, beside its URL

An AT request asserts that a specific product costs a specific amount. The
evidence for that claim is what the vendor's page showed when the case manager
priced it, so items now carry a pasted screenshot, and publishing generates a
second page pairing each clip with the URL it came from.

**The URL left the line-item listing.** `ATRequestItem.Url` already carried a note
saying it was for "the future screenshots-with-clickable-links page" and was "NOT
rendered on the page-1 OADS form (the state form has no URL column)." The first
version of the exporter rendered it there anyway. Page one is the payment request
a reviewer reads; a bare link in the cost table is noise. The URL now appears once,
on the evidence page, directly above the picture it explains.

**Paste sits beside the URL field.** The two describe the same thing — where the
item was found and what it looked like there — and they travel to the evidence
page as one block. Separating them in the editor would have made the pairing an
implementation detail the user has to remember rather than something the layout
states.

**The clip count is confirmed twice.** Once live in the editor as clips are
pasted, once in the publish confirmation. Publishing locks the request, so a clip
that silently failed to attach is trivially fixable beforehand and expensive
afterwards, when correcting it means discarding the attestation and publishing
again. Both readouts are announced politely to screen readers.

**Screenshots are PNG, downscaled, and capped, with one owner.**
`AtRequestScreenshot` decides the longest edge (1400px), the byte ceiling (4 MB),
and the format. PNG rather than JPEG because the subject is a page of text and UI,
where JPEG's ringing artefacts land exactly on the characters someone needs to
read — a price, in a document that authorises a payment. The desktop enforces the
rule at the paste boundary so the user learns immediately; the API enforces the
same rule again, because a client-side limit is a courtesy and constrains nothing
about what arrives in a request body. Malformed base64 comes back as a 400 rather
than throwing.

**URLs are rendered as text, never as link annotations.** They are user-entered
values in a document that leaves the agency. A clickable target inside it is a
hazard nobody reviewed.

**A clip that will not decode does not cost the document.** The exporter reports
the gap on the page rather than failing the render, so one bad row cannot make a
request unpublishable.

### Storage note

`ATRequestItem.ScreenshotPng` is a heavy column, but unlike `ATRequest.SnapshotPng`
it has a public setter and no first-write-wins rule. That blob is evidence of a
published document and must not be replaced; this is draft content the case
manager is still assembling, no different from the item's name or price. What
stops it changing after publication is the publication lock. The queue projection
never materialises item rows at all, so clips stay out of list reads by
construction; opening a single request does load them, which is the point.

---

## 2026-08-15 — A filed AT request regenerates faithfully, from the record

AT requests are now listed on the client's profile, and any of them can be
regenerated as a PDF. The list follows the CLIENT rather than whoever currently
carries them, so transferring a caseload does not orphan a client's filed
documents.

**Regeneration is faithful, not refreshed.** Reopening a filed request next year
must produce the document that was submitted, not recompute it against whatever
the agency's terms have become. Every figure already came from the stored record
— the frozen client and case-manager snapshots, the stored tax AMOUNT, the items,
the attestation — with one exception, which this change closed.

**The passthrough rate is now frozen at publication.** It was being read from
current settings at render, so an agency renegotiating from 15% to 18% would have
silently restated the totals on every previously filed request. Sales tax freezes
as an amount because it is entered; the passthrough rate freezes as a RATE because
the document PRINTS it — "Passthrough fee (15.00%)" — and a stored amount alone
could not reproduce that label. Stored `decimal(5,4)` like `Settings.PassthroughRate`,
not the `decimal(18,2)` EF picks for money columns, because that rounds 0.055 to
0.06. Nullable, because a draft has no frozen rate and should follow the agency's
live terms; reopening releases it for the same reason.

The rate is read server-side at publication — from agency settings on the API
path, from the settings service on the desktop path — never from the payload,
matching how the signer is derived.

**The generated document is deterministic for a published request.** The PDF's own
creation and modification timestamps are pinned to the attestation time, and the
generation timestamp reaches the page only in a draft's "not published" banner. Two
regenerations months apart are identical apart from two pieces of PDF plumbing that
PDFsharp randomises per save and that carry no content: the six-letter font subset
tag, and the XMP document UUID. The regression test masks exactly those two and
compares everything else exactly.

### The PDF is regenerated, never retained

Decided 2026-08-15, after the fidelity work above made it defensible.

A published AT request cannot change. The only writers of its status are the publish route, the
reopen route, and the ordinary save path — and the save path refuses a published row, as do delete
and edit. Every input the exporter reads is therefore frozen: the client and case-manager snapshots,
the item rows, the sales tax AMOUNT, the passthrough RATE, the attestation and its wording, and the
pasted screenshots. The generation timestamp does not reach the page of a published request.

So the document is a pure function of the record, and storing it would duplicate that record —
screenshots included, which are most of the file. A hundred kilobytes per request, per client,
indefinitely, to hold something reproducible on demand. `ATRequest.SnapshotPng` already retains a
glance-able proof-of-document image, first-write-wins, for the cases where an image is what is
wanted.

**On the regulatory framing.** `REGULATORY_CONCERNS.md` says an executed artifact must not be
replaceable. That is this project's own design principle, not a citation of a Maine or federal rule,
and its actual intent — that nobody alters a document after it is signed — is what the publication
lock enforces. Treating our own aspirational language as an external requirement was overcautious.
Whether OADS or MaineCare imposes a records-retention obligation on the AGENCY is a separate
question, and one the agency answers with the underlying record, which Sati holds.

**The residual risk is code, not storage.** Regeneration is faithful as long as
`ATRequestPdfExporter` is unchanged. Matching the OADS form layout is on the agenda; the day that
lands, historical requests will regenerate in a layout that was never submitted. The content stays
correct — only the presentation moves. That is an accepted trade. If it ever needs closing, the
cheap instrument is a SHA-256 of the published PDF (32 bytes, not 100 KB), which cannot reproduce
the original but turns a silent divergence into a detectable one. Not worth adding until something
actually checks it.

### Note on the list projection

Both AT request list routes now share one row shape and one total calculation.
That refactor initially broke BOTH routes — EF could not translate a projection
into a positional record constructor — and it went unnoticed because the existing
`GET /at-requests` route had no test. It does now, by way of the per-client list
test that caught it. The projection uses object-initializer syntax, which EF
translates reliably.

---

## 2026-08-15 — An installation bootstraps its first administrator; it never ships with one

A Sati database can reach a state where nobody can administer it — a fresh install,
or a real install whose only account is a case manager. Without a way out, the owner
of the data cannot create a supervisor, cannot promote anyone, and cannot recover
except by editing the database by hand.

**No administrator is ever created automatically, and no password is ever defaulted.**
An account that exists on every install with a password the software knows is a
backdoor on every install. Sati instead refuses to have an administrator until a
human sits down and chooses a password for one. The login window offers first-run
setup when none exists; the human types the credentials; the account is created.

**The window is narrow and self-closing.** `CreateFirstAdministratorAsync` re-checks
inside the write that no administrator exists, so the check cannot be stale by the
time it acts, and creating one shuts the path permanently. The check reads the
database rather than a flag, so an administrator created by any other route — the
ordinary user-management screen, or the provisioning script — closes it just the
same. The role is forced rather than trusted from the caller: honouring an incoming
CaseManager would leave the installation with no administrator and the window still
open.

**Setup is offered, not forced.** An installation in this state usually has working
accounts already. A case manager doing real work should not be held hostage by a
prompt about administration they may not be the right person to perform.

**Desktop-local only, deliberately.** There is no API route for this. Bootstrapping
without credentials is defensible against a database the caller already has direct
access to; the same capability exposed over the network on a multi-tenant service is
not. `CloudUserService` refuses outright and points at
`scripts/Provision-DemoGlobalAdmin.ps1`, run by an operator already trusted with the
database. That script's existing restriction to `SatiDemo` is untouched.

**The password bar is higher than for ordinary accounts** — 16 characters against the
8 that self-service changes accept, matching what the hosted provisioner already
demands. This is the one credential that can create every other one, and it is typed
once at setup rather than daily.

### The agency question

Every Sati database ships with two seeded agencies ("Internal", "Sandbox Mode"), so
"use the only one" was not available. The administrator joins **the agency the
existing users are in**, because an administrator exists to administer the agency
that holds the work. PlatformOperator accounts are excluded from that calculation —
they are Sati's cross-tenant telemetry identity and say nothing about which tenant
needs administering. Users genuinely spread across several agencies is reported as
ambiguous rather than guessed at: attaching the only administrative account to the
wrong tenant is not a mistake that announces itself afterwards.

## The case-note workflow has one transition table (2026-08-17)

`Sati.Contracts.V1.NoteWorkflow` owns which note status may become which, for the
case manager, for the supervisor, and for the overdue sweep. Both the desktop
service and `Sati.Api` call it.

**Why a table and not a set.** Both paths previously asked two separate questions:
is the target status one a case manager may write, and is the current status
editable. Neither asked whether the move between them made sense. Every writable
status was therefore reachable from every editable one, so a cancelled or aged-out
note could re-enter the supervisor's queue in a single call with no intervening
documentation, and nothing described the pipeline as a pipeline.

**The invariants the table protects.** No case-manager move reaches Approved,
Returned, or Abandoned. Nothing at all leaves Approved. The only way into Approved
is a supervisor acting on a Logged note. These were already true through the
writable-status set; the table keeps them true in one place instead of two, and
adds the workflow coherence the set could not express.

**Three groups, not ten special cases.** Work in progress — Scheduled, Pending,
Delayed, HeldForCompliance, ComplianceBlocked, Returned — moves freely among the
statuses its author may assign, because those are all the same kind of unfinished
state and the note-entry screen offers them together. Closed work — Cancelled and
Abandoned — reopens as a draft first, so a narrative that was written off cannot
land back in front of a supervisor without an edit. Submitted and approved work is
not the author's to move.

A returned note is re-dispositioned freely but cannot be *saved as* Returned:
Returned is the supervisor's word about the note, not the author's to write.

**Strictness that traps a note is a defect, not a control.** A test asserts every
status can reach review again within two moves. An earlier draft of the table
blocked Returned from Scheduled and Delayed, which contradicted the note-entry
screen — it offers exactly those — on a rationale that did not survive contact
with the code, since Pending and Cancelled remove a note from the returned queue
just as thoroughly.

**Approved is terminal, and there is no amendment path.** A supervisor who approves
in error has no remedy, even before a claim line exists. This is recorded as
outstanding scope in `AGENDA.md` rather than solved here; the right answer is an
immutable approved version plus a linked amending note, per the platform rule that
submitted clinical and financial records are amended rather than overwritten.

## Review and billing read the same clock (2026-08-17)

The supervisor review routes evaluated compliance against `DateTime.Today` — the
host's date — while the billing gate used `BillingRules.MaineBusinessDate`. On a
UTC-hosted API the two disagree for the first four hours of every UTC day, which
are still the previous day in Maine. The same client could clear supervisory review
and fail the billing gate, or the reverse, purely on the hour the work was done.
Both now read the agency's date through the injected `ApiClock`.

The desktop path keeps `DateTime.Today`, which is already the Maine date on a Maine
workstation. That equivalence is an assumption about where the client runs, and it
stops being true if the desktop is ever run outside Eastern time.

## A Reminder is a journal entry, not a note (2026-08-18)

`NoteType.Reminder` appears in the note-entry picker but never produces a `Notes`
row. It writes one stamped entry to the top of the client's journal and stops
there.

The alternative — persisting the reminder as a note *and* mirroring it into the
journal — was rejected because it stores the same sentence twice. The journal is
free text a case manager edits directly, so the two copies diverge the first time
one is reworded, and nothing says which is the record. Keeping it in one place also
keeps it out of the paths that decide money and clinical status: a reminder has no
status, so it cannot enter supervisory review, the billing queue, productivity
counts, or the notes log, and no exclusion logic had to be added to any of them.

The consequence to accept: reminders do not appear in the notes log or in note
history, and are not versioned individually. What exists instead is the journal's
own trail — `person.journal-reminder-added`, distinct from `person.journal-updated`
so the trail separates an entry the application stamped from a free-text edit — and
the append-only `PersonVersion` snapshot the write already produces.

**The server prepends; the client does not.** `PUT /people/{id}/journal` replaces
the whole journal, so a client that read the journal, composed the entry locally,
and wrote it back would erase anything a concurrent session typed between its read
and its write. `POST /people/{id}/journal/entries` sends only the text and prepends
under the person's revision token. The desktop's transitional local `PersonService`
does its read-prepend-write inside one short-lived context for the same reason.

**The writer stamps the time, not the caller.** No timestamp crosses the wire; a
client-supplied one would let the record claim a moment that did not happen. The
API stamps from `ApiClock.Now` — agency-local wall clock, because an Azure host's
own local time is UTC and would present hours off the clock the case manager just
read. `Sati.Contracts.V1.JournalEntry` owns the stamp format and the placement so
the desktop-local path and the API cannot order or format entries two ways.

**Ordering at the seam matters.** The client page's journal box auto-saves on a 2s
debounce and writes the same column. Its pending edit is flushed *before* the entry
is written (`JournalWriteStartingAsync`), and the journal the writer returns is then
adopted by that page (`ReminderAdded`). Reversed, one of the two texts is lost.

## A future-dated Reminder is a scheduled note row (2026-08-26)

The 2026-08-18 journal-only decision now applies specifically to an **undated**
Reminder. Choosing a future note date has a different purpose: it creates a dated
item the calendar must be able to retrieve. That entry is stored once as a
`Notes` row with `NoteType.Reminder` and `NoteStatus.Scheduled`; it is not copied
into the journal.

`Sati.Contracts.V1.NoteSchedulingPolicy` owns the normalization. For an explicitly selected
Reminder, a future date fixes Scheduled status, preserves the date and narrative, and removes
minutes, start time, form type, visit documentation, and case-manager justification. The
2026-09-05 Today's Work decision refines the same owner for non-Reminder future work: it preserves
the selected work/form type and estimated minutes while still fixing Scheduled status and clearing
actual start time and completed facts. The desktop applies the rule immediately for understandable
UI; the local `NoteService` and API apply it again before persistence. The API uses the agency date
from `ApiClock`, so a forged or older distributed client cannot turn future work into submitted or
billable documentation.

The reminder remains Scheduled after its date arrives; no background job silently
turns planned text into a clinical note. A case manager may later delete it or
deliberately edit it into ordinary documentation through the normal note workflow.
While it remains a Reminder it has no service time, productivity units,
supervisory-review status, or path into billing. Because it is a real dated row,
it participates in ordinary note tenancy, optimistic concurrency, note-list,
calendar, and upcoming-event reads.

## A client newer than its server writes the reminder anyway, and says so (2026-08-18)

The desktop ships ahead of the API. On 2026-08-18 the hosted Demo API was release
1.2.17, which predates `POST /people/{id}/journal/entries`, so every reminder from
a current client failed — and failed *misleadingly*, because `CloudApiClient` maps
any 404 to "not found or is outside your caseload" and the client in question was
plainly on the caseload. `DEMO_RUNBOOK.md` already requires client and API to be
deployed together; this is what it looks like when they are not.

An unrouted path and an out-of-scope record answer with the same status, so the
client asks a question only one of them can answer: on 404 from the entries route,
read the journal through the older `GET /people/{id}/journal`. If the person reads
back they are in scope and the 404 was the route; if that read fails too, the
original not-found stands and nothing is written. A 403 is never treated as a
missing route.

Having established that the server is behind, the client completes the write the
old way — read, prepend through the same `JournalEntry` owner, `PUT` the whole
journal — and reports that it did, through
`JournalReminderResult.UsedLegacyJournalWrite` into the client page's existing
journal warning band. The downgrade is never silent.

**Why accept a client-side read-prepend-write at all,** having rejected it as the
primary design: the journal text box already writes this column by whole-string
`PUT` on a 2s debounce with no revision check, so the fallback is no weaker than
the column's ordinary path. It is not atomic the way the route is, which is exactly
why it is the fallback and not the design.

**Removal condition — met, and removed 2026-08-23.** The condition was that no
reachable deployment predates the journal-entries route. Confirmed by unauthenticated
probe on 2026-08-23: `journal/entries`, `ssn`, `forms.pdf`, and `agency-release.pdf`
all answer 401 where three of them answered 404 on 2026-08-19, so the 1.2.21
deployment closed the gap and the `catch` could no longer fire. The `catch`, the
`UsedLegacyJournalWrite` flag, the warning it drove, and
`Sati.Tests/JournalReminderFallbackTests.cs` are gone — they were written to go
together and they went together.

Keeping it would have left a second, non-atomic way to write a clinical record with
nothing exercising it and no deployment able to reach it. An unreachable write path
is not a safety net; it is an untested one.

**Found on the way:** `CloudApiClient.GetAsync<string?>` rejects a null result as
"an empty response", and a journal that has never been written returns an empty
body — so loading the journal of any such client raised "the journal could not be
loaded from the cloud" rather than showing an empty journal. `GetStringOrNullAsync`
expresses that shape, and `GetJournalAsync` now uses it.

## An official DHHS form is filled, never redrawn (2026-08-18)

Sati now stamps two Maine DHHS forms — Appointment of Authorized Representative
(rev. 10.10.24) and Authorization to Release/Obtain Information (rev. 11.24.25) —
from a consumer profile. `DhhsFormDefinition` in `Sati.Contracts.V1` owns which
value goes in which box; `DhhsFormFiller` in `Sati.Api` does the stamping.

**Why not the AT exporter's technique.** `ATRequestPdfExporter` composes a Sati
document with MigraDoc, and says in its own header that it is not a reproduction of
the OADS form. That is right for a document Sati owns and wrong for one it does
not: a redrawn state form is a lookalike, and a lookalike is what gets rejected at
intake. Both PDFs turned out to carry real AcroForm fields — 21 and 64 — so the
filler sets field values and never touches a page content stream.
`DhhsFormFillerTests` compares the SHA-256 of every decompressed page content
stream before and after filling and requires them equal, so the layout, seal, and
legal text are provably the ones DHHS published. Deliberately mutating the filler
to draw on the page instead fails that test on both forms.

**A profile answers who someone is, never what they agreed to.** Every checkbox on
both forms, and every signature, printed name, and signing date, records a decision
made at the moment of signing: which offices may disclose, what authority a
representative holds, whether 42 CFR Part 2 substance-use records and mental-health
records travel with the rest. Deriving any of it from stored data would manufacture
a consent nobody gave, on a page the consumer then signs. `ConsentFields` names
them per form and `AssertFillable` makes filling one an exception rather than a
code-review question. Consent choices a case manager recorded on the consumer's
instruction arrive through a separate, explicit `Selections` input, guarded from the
other direction by `AssertSelectable` so a typo cannot silently drop a choice.

The consent lists enumerate names rather than testing "is it a checkbox", because a
category test would stop protecting the free-text boxes that qualify one — "Other
(explain)", the earlier-expiry date, the initials authorizing emailed delivery.
Those are consent too, and they are text.

**The filler runs wherever the data already is.** The Appointment form asks for an
SSN, so on the cloud path the fill happens server-side and the client receives
finished PDF bytes — shipping decrypted SSNs to every workstation is exactly what
that avoids. Local Production fills on the workstation instead, with no network and
no SSN to protect. Both call the same `DhhsFormFiller` in the shared `Sati.Forms`
library; a second copy of the stamping would be the duplication this file's own
rules forbid.

**The result stays a fillable form.** Flattening would take the consumer's own pen
out of a document they are the one who has to sign.

**The desktop records choices, not signatures.** The WPF workspace offers readable
controls for disclosure scope and other consumer-directed selections, but it does
not offer fields for a signature, signing date, printed signer name, or signer-
authority attestation. Those remain blank on the fillable official PDF. This avoids
turning ordinary data entry into an unevaluated electronic-signature workflow while
still letting the case manager prepare the rest of the document with the consumer.

**Blank forms are embedded resources,** named with their revision, so the binary
that filled a form carries the exact blank it filled; a form on disk could be
swapped for another revision without anything noticing.

## An SSN is cloud-only, envelope-encrypted, and readable only during a form fill (2026-08-18)

Storing SSNs was chosen over leaving the Appointment form's SSN box blank. Sati had
no encryption at rest before this — `PasswordHasher` is a one-way PBKDF2 hash, not
reversible encryption — so the mechanism is new.

**Envelope encryption, per record.** A fresh AES-256-GCM data key and 96-bit nonce
per encryption, the data key wrapped by an Azure Key Vault key reached with the
API's managed identity holding only `wrapKey`/`unwrapKey`. Stored per row:
ciphertext, nonce, tag, wrapped data key, and the full Key Vault key identifier
including version. Recording the version per row is what makes rotation a
non-event — new rows wrap under the new version, old rows keep decrypting under
theirs, and no backfill is required.

**The binding is the part that is easy to leave out.** Tenant, record id, and field
name are bound into the encryption as additional authenticated data. Envelope
encryption alone protects the value from someone who steals the database; the
binding is what stops someone who can *write* to the table from moving one
consumer's ciphertext onto another's row and having it decrypt cleanly. Tested in
all three directions — other agency, other consumer, other field.

**GCM rather than a mode without a tag** so a modified row throws instead of
returning a plausible wrong number onto a state form.

**The last four digits are stored in the clear, deliberately.** They are what the
mask displays, they cannot reconstruct the number, and keeping them outside the
ciphertext is what lets every read path stay both fast and plaintext-free — a list
of fifty consumers costs zero Key Vault unwraps. `SsnMask` in `Sati.Contracts.V1`
owns the display form so the desktop and the API cannot mask differently.

**Decryption has exactly one caller:** the audited `POST /people/{personId}/forms.pdf`.
No ordinary read path decrypts and no DTO carries plaintext — the SSN routes answer
with `SsnMask`, including the route that just stored the number, since echoing it
back would put it in a response body, a proxy log, and a client cache. The desktop
never decrypts at all: on the cloud path it receives finished PDF bytes, and on the
local path there is no SSN and no key.

**Per-environment keys are the cross-environment safeguard.** Demo and Production
wrap under different Key Vault keys, so Demo ciphertext is inert against the
Production vault and vice versa — a mis-pointed connection string fails closed
rather than decrypting the wrong environment's data. `EnvelopeProtectorTests`
demonstrates that property directly.

## An agency release is a Sati document, with a staff attestation rather than an inferred signature (2026-08-19)

The agency release shown in the legacy reference is an agency-owned workflow rather than a
government-issued form. Sati therefore composes a clear, branded two-page release instead of
imitating the old application's screen or treating screenshots as a fillable template. The shared
contract owns recipient details, record categories, authorization dates, special confidentiality
choices, and revocation state; desktop, local Production, and API-backed Demo all validate the same
request.

**No consent is derived.** Profile data answers who the consumer, guardian, agency, and signed-in
case manager are. It never answers whether authorization was granted, what records may be disclosed,
whether specially protected information is included, or whether repeated disclosure is permitted.
Those remain explicit, required choices that are cleared whenever the selected consumer changes.

**The case manager may attest only to their own act.** Selecting “I obtained this release” requires
an immediate confirmation and stamps the authenticated staff identity and UTC generation time. The
document says this is not the consumer's electronic signature. Consumer and guardian signature
lines remain blank until Sati has a separately designed, legally reviewed electronic-signature
workflow.

**Generation follows the data boundary.** `IAgencyReleaseService` is local/cloud abstracted. Local
Production reads and renders through EF on the encrypted workstation; Demo sends the choices through
the API and renders after the server re-derives consumer, agency, and actor identities. Both paths
audit generation with PHI-minimized metadata. The resulting PDF is plaintext disclosure material
and must be saved only to an agency-approved location.

## The desktop backs up before it migrates a database with records in it (2026-08-19)

`App.xaml.cs` has always run `Database.Migrate()` for Local Production at startup,
before the splash screen. That is right for a tool whose users are case managers
rather than developers — nobody should have to run a script to open their caseload —
but it had no safety net, and the stakes are not the same on every machine. The
development database on the primary login has nothing to lose. The other Windows
login holds real consumer records, and a partner's laptop would hold theirs, with
nobody present who could read a stack trace.

`LocalDatabaseUpdater` keeps the automation and adds the net:

- Do nothing unless migrations are actually pending, which is the usual case.
- Back up first, but only when the database holds consumer records — an empty
  database would cost time and disk on every launch to protect nothing.
- A backup that cannot be written is a stop, not a warning. Migrating anyway would
  choose the least recoverable order of events available.
- A failure ends in a message naming the backup file and saying the records are
  unchanged, not an application that refuses to start and explains nothing.

**It does not try to repair a diverged history.** `SatiProduction` has acquired
columns outside the migration chain before, and deciding which side is right needs
judgement about a specific database. A startup path doing that unattended, on real
consumer records, is not a trade worth making — so it stops and says so, and the
guarded script under `scripts/` remains how that is resolved deliberately.

**Order is the load-bearing property** and is tested rather than trusted:
`LocalDatabaseUpdaterTests` asserts backup-then-migrate, and reversing the two in the
source fails three of its eight tests.

**Known gap.** The migration runs before sign-in, so there is no actor to attribute
an audit event to and none is written. The backup file and its timestamp are the only
record that a schema changed. Tracked in `AGENDA.md`.

## Local Production stores SSNs under the Windows account key (2026-08-19)

Reverses the cloud-only decision taken earlier the same day. The reason is workflow,
not architecture: filling the Appointment form is occasional, but reading a
consumer's number to the Social Security Administration on their behalf is routine
case-management work, and it cannot be done from a mask. Credible exposes a plain SSN
box for exactly this, and a local Sati that could not was not usable as a daily tool.

The envelope is unchanged — `EnvelopeProtector`, a fresh AES-256-GCM data key per
record, tenant and record and field bound in as authenticated data. Only the wrapper
differs, which is what `IKeyWrapper` existed for: Key Vault in the API,
`DpapiKeyWrapper` on the workstation. A local ciphertext is structurally identical to
an Azure one. Both now live in `Sati.Contracts` so there is one implementation.

**What DPAPI protects:** a copied database. The `.mdf` lifted off the machine, or
opened under another Windows account, will not unwrap. With the BitLocker requirement
in `OPERATIONS.md`, that covers a stolen or salvaged laptop.

**What it does not:** anything running as that user while they are signed in. DPAPI is
a boundary between Windows accounts and machines, not between programs. On a
single-operator workstation that is the boundary that matters, and it is weaker than
the cloud path — which is why the cloud path was not changed to match.

**Recovery is re-entry.** Lose the Windows profile and the wrapped keys go with it.
Acceptable only because Sati is not the system of record for an SSN; Credible is.
Nothing else in Sati is stored this way, and nothing that exists nowhere else should be.

**Databases stop being portable.** Copying a local database between logins or machines
has been done before, and the SSN column will not survive it. The stored last-four is
plaintext and keeps displaying, so the symptom would be a healthy-looking mask beside
a reveal that fails — `DpapiKeyWrapper` therefore catches the platform error and
returns a sentence naming the cause and the fix.

**A read is audited separately from what occasioned it.** `person.ssn-revealed` is
recorded whether the number was revealed for a phone call or consumed by a form fill,
mirroring `person.ssn-decrypted` on the API side. The action is recorded; the value
never is.

## Tomorrow's Agenda is tomorrow's dated scratchpad, not a rollover copy (2026-08-20)

Tomorrow's Agenda and Today's Work are two views over the existing per-user, per-date
Scratchpad aggregate. The future entry is created against the next workday and edited in place.
When that date arrives, the ordinary Today query returns the same row. There is no midnight copy,
promotion flag, or background job that could duplicate, reorder, or lose text after a retry.

`WorkAgendaDates` in `Sati.Contracts.V1` owns the date rule for both local Production and the API:
Monday through Thursday advance one day, while Friday, Saturday, and Sunday resolve to Monday.
This is a weekday rule only. Holidays are intentionally not skipped until Sati has an authoritative
agency holiday-calendar policy; silently borrowing incentive-exclusion settings would make an
individual scheduling preference control a durable work record.

The cloud client cannot submit a future date. It asks for `/scratchpad/tomorrow`, and the API derives
the date from its agency-local clock and scopes the row to the authenticated user. Both tabs retain
the existing revision/409 behavior and are flushed on app close and account switching. A desktop
left running overnight checks for rollover on window activation and on its ten-minute autosave tick;
it saves the old visible drafts before swapping either tab to the new dated rows.

## Carika is an API client, not a database client (2026-08-21)

Carika references `Sati.Contracts`, not the `Sati.Api` executable project. Its system of record is
reached through authenticated HTTPS routes, so it cannot acquire an Azure SQL credential, run
migrations, or bypass server-side caseload and tenant checks. Its initial scope is profile display
and note drafting only.

Local speech transcription has no cloud fallback. Models are provisioned separately and the first
slice transcribes user-selected WAV input without copying or retaining it. Optional narrative drafts
are DPAPI encrypted for the current Windows user and bound to the Sati actor and person IDs. This
reduces exposure from copied files but does not defend against code running as that Windows user and
is not a compliance conclusion.

## The Local Production package bootstraps software, never PHI (2026-08-21)

The Local Production deliverable is one executable containing Sati and Microsoft's signed LocalDB
MSI. The builder rejects a missing, invalidly signed, or non-Microsoft prerequisite. Installation
requests elevation for LocalDB only when absent; the Sati application remains per-user.

On first launch, Sati may provision only when the configured `SatiProduction` database is completely
absent. It applies the controlled migration chain and writes a new Production identity marker before
ordinary identity validation. If any database of that name already exists, the bootstrap path makes
no change and the fail-closed identity gate remains authoritative. No database backup, seeded
credential, user data, or PHI is packaged. A human creates the first administrator through the
existing guarded flow.

## Local case-note drafting is a closed-world transformation (2026-08-22)

This decision supersedes the earlier local-AI design that assembled Bio, assessment, deadline, and
historical-note background and calculated a form-based follow-up. The case-note model may now receive
only the selected client's minimal identity and a captured packet of current note-entry facts. The
selected-client authorization service derives the actor from the signed-in session; its API route is
GET, own-caseload-only, and receives no rough narrative.

Every rough-note fragment and selected template value is assigned a stable fact ID and is required
in the result. The model proposes a JSON plan whose sentences cite their supporting fact IDs.
`CaseNoteDraftRules` is the shared deterministic authority: it rejects omitted facts, citations that
do not retain required selector values, wrong-section use, and unsupported names, numbers,
quotations, negation, or content vocabulary. Sati, not the model, adds the fixed note envelope. In
the absence of an explicit current-note follow-up, the only permitted text is `No follow-up was
documented.` Unchecked, `Not documented`, and `Not assessed` controls supply no affirmative fact,
and consumer presence has no affirmative default.

The model may decline a rewrite by returning the exact `USE_SAFE_BASELINE` token. Sati then
renders its deterministic current-fact plan and validates it through the same shared rules. This is
a successful safe deferral, not permission to omit a fact. Runtime failures and two invalid model
answers also fall back to that plan but are surfaced to the user as warnings. The target-device
competence gate requires all representative scenarios to finish through the actual local runtime
without a rejection warning. Safe deferral counts because the product guarantee is a grounded draft,
not a requirement to force the model to change already-professional source wording.

A fingerprint covers the person, narrative, template state, user identity, and complete fact packet.
Selection or input changes cancel and invalidate generation, and Sati recomputes the fingerprint
before publishing and again before accepting a draft. A different consumer's packet cannot be sent
until the previous model unload succeeds. These controls reduce the model to a fallible prose
organizer with fail-closed output gates; they do not make generated language intrinsically truthful
or remove the need for human review, device evaluation, privacy review, and accepted-draft audit and
retention decisions before production.

The note-entry panel may present the soonest current `IUpcomingEventService` item as a suggestion,
but the due item is not itself a documented commitment and is never inserted automatically. The
case manager must explicitly accept it before Sati appends an editable `Follow-up:` line to the raw
narrative. That human action makes it a current-note fact under the existing closed-world rule. If
the narrative already contains follow-up language recognized by `CaseNoteFactCompiler`, acceptance
is disabled rather than creating a second follow-up section. This presentation feature does not add
historical records to model context and does not change the deterministic drafting boundary.

## Database waits have one payload-free activity owner (2026-08-22)

Database wait feedback is driven by a singleton reference counter at the data boundary, not by
independent `IsBusy` flags added to every screen. Demo requests enter the counter in an HTTP message
handler; Local Production commands enter it in an EF Core command interceptor, with query readers
remaining active until disposal. This covers existing and future data services using those paths,
keeps overlapping calls correct, and retains no SQL, route bodies, narratives, or other PHI.

The colorful Bodhi leaf spins immediately. The patience window is requested only after eight
continuous seconds and is modeless and non-activating, so it communicates progress without stealing
focus or blocking work. The final active lease cancels the delay and closes the window. The tracker
does not alter permissions, transactions, timeouts, retries, or error propagation.

The Settings preview is a synthetic tracker lease, not a deliberately slow database request. It
runs for 12 seconds to exercise both visual stages, is available to every signed-in role, prevents
re-entry, and has no data-service dependency. This preserves the value of a realistic UI test
without manufacturing database load or creating a route that could expose client information.

## DNS failure retries are safe only before a connection exists (2026-08-22)

A failed Scratchpad save identified by support reference `9114DA9D0544` was a DNS lookup failure for
the Demo API host. The request never reached the server, but the prior client surfaced a generic
`TaskCanceledException` after the debugger pause allowed the HTTP timeout to elapse.

`CloudApiClient` now classifies connectivity failures centrally. A request is retried at most twice,
after 250 milliseconds and one second, only when the recursive exception chain proves a name-
resolution failure. No TCP connection exists in that case, so repeating either a read or mutation
cannot duplicate a server-side result. Timeouts, connection resets, and other failures after a
connection may have delivered a mutation and therefore are not retried automatically.

Exhausted connectivity failures retain their infrastructure exception for payload-free diagnostics
but expose a safe explanation through the data-service boundary. Scratchpad save handling keeps the
draft visible, states whether the request was definitely not sent, and never includes the narrative
in the exception or operational log. Expected cancellation of the spinner's patience delay is
handled by observing task completion state, keeping normal short requests out of first-chance
exception output while preserving real delay faults.

## Representative-payee profile state is not payment authorization (2026-08-22)

`Person` stores whether the case manager is the consumer's representative payee, the monthly income
managed in that role, and a bounded description of regular check-request needs. The explicit No state
has no subordinate financial values; switching to No clears them. Yes requires a positive amount with
no fractional cents beyond two decimal places and an explicit needs description, where `None` is the
honest value when there is no recurring request. `RepresentativePayeeRules` in the shared contracts
assembly owns those rules for WPF, transitional Local Production, and the API.

All three values participate in Person optimistic concurrency and immutable lifecycle history. Demo
reads and writes remain own-caseload and tenant checked, and the additive migration defaults existing
rows to No without inferring money or needs. Because these are sensitive financial profile data, they
must not be logged in operational telemetry or added to local-AI context.

A future notification to billing is a different aggregate. It needs its own request identity, amount,
purpose, due date, requester, status, approval/release evidence, audit events, concurrency, and
idempotency. A profile save is never evidence that a check was requested or authorized.

The representative-payee fields change an existing request/response shape without adding a route.
`ApiSurface.Revision` therefore fingerprints named persistence-contract revisions in addition to the
live route manifest. A new client detects an older server before that server can silently ignore the
new fields.

## The brochure is HTML source, rendered to PDF (2026-08-22)

The workflow promotional brochure was a ReportLab PDF whose generator had been lost, so every
change was binary surgery on the shipped artifact. It is now generated from
`marketing/brochure/brochure.html` by `scripts/build-brochure.ps1`, and the PDF in `output/pdf/`
is a build artifact like any other.

The recovered source is HTML wrapping one `<svg viewBox="0 0 960 540">` per slide rather than a
new generator script. SVG positions text by baseline and images by box, which is exactly what a
PDF content stream does, so the recovery is a coordinate-for-coordinate translation rather than a
reinterpretation. A number in the source is the number the PDF gets. Chromium supplies font
subsetting and `ToUnicode` maps, so the output stays selectable and searchable without the project
owning a font pipeline.

`tools/BrochureDecompile` performed the one-time recovery and is kept for provenance. It
understands only the subset of the content stream language that ReportLab emitted for this file,
and recognises two of its idioms deliberately: rounded-rectangle bezier runs become `<rect rx>`,
and the stacked stripes that faked each background wash become a `<linearGradient>`. Recovering
those as literal beziers and eighty rectangles would have been faithful and useless.

Slide 1's background was a screenshot of the login screen on its desktop wallpaper, so the bodhi
leaf and the sign-in dialog were pixels in a JPEG. Both are now removed from the plate
(`tools/BrochureBackdrop`, a Laplace inpaint that is only sound because the wallpaper is a smooth
gradient), and the leaf is placed by the slide. Its position is two numbers in the markup.

Marketing artwork is not clinical data and carries none of the platform's tenancy or audit
obligations, so this pipeline deliberately stays outside the application's architecture. It is a
build script and a checked-in source file, nothing more.

## Shutdown flushes send only unsaved work (2026-08-23)

The desktop keeps a last-confirmed-content baseline for each visible agenda draft and for the
selected Person's journal. Ten-minute autosave, account switching, selection changes, and shutdown
compare against that baseline before calling persistence. Loading or merely viewing text therefore
cannot create a cloud write or turn an expired access token into a save failure during exit.

An agenda write rejected with `401` is classified separately from connectivity and concurrency
failures. The API rejected it before the endpoint ran, so no ambiguous retry is needed. The client
stops the timer after the first rejection, leaves both drafts visible, and announces one accessible
session-expiry warning. Reinitialization after a new sign-in establishes new baselines and resumes
autosave. This preserves the 30-minute short-lived-token boundary rather than disguising the defect
by lengthening access-token lifetime.

An active desktop session renews its access token through the ordinarily protected API group five
minutes before expiry. Renewal is not a second anonymous credential: JWT validation and
`ValidatedActorFilter` run first, the server reloads the user, and the new token preserves the
original `sati_auth_time`. Renewal ends after twelve hours from credential entry even if the app
remains busy. This gives a normal workday continuity without converting a stolen 30-minute token
into an indefinitely renewable session.

A refused renewal ends the session; it is not a failed request. Renewal authenticates with the
token it is replacing, so once the server rejects it no later attempt can succeed — the token is
already too old, or the twelve-hour cap has passed. `CloudApiClient` therefore latches on the first
rejection, raises `SessionEnded` once, and answers every later authenticated call locally with
`CloudSessionEndedException`. Setting a new token clears the latch, so signing in again reopens the
same client. Previously each screen retried the renewal and received its own 401, which read as many
unrelated failures instead of one ended session and left the desktop hammering the API.

An active session is renewed on the token's schedule, not on the chance that a request lands in the
window. Renewal is only possible inside the five minutes before expiry, and an idle desktop issues
no requests at all — the ten-minute agenda timer returns without calling when nothing is dirty — so
a session could die with the user still at their desk. `SessionKeepAlive` wakes at
`expiry - RenewalMargin` instead. A fixed poll is not a substitute and is worse than none: a
twenty-minute interval waits at minute twenty and next wakes at minute forty, holding a token that
died at thirty.

Renewal is gated on user input rather than on the process being alive. An ungated keep-alive would
hold an unattended workstation signed in for the twelve hours the server permits, which is a real
change in posture and not one this defect justified. Gated, the rule is the one the product already
implied: an actively used session continues to the twelve-hour cap, an untouched one lapses after a
token lifetime and asks for credentials. The idle allowance is measured over the gap between
renewals, not the token lifetime; measured over the lifetime, only `lifetime - margin` can have
elapsed at the first renewal, so the gate could never close and the effective timeout would silently
double.

An ended session must be stated, never implied by absence. The switch-user directory is the case
that proved it: the load failed, the dialog cleared its list, and the account picker came up empty
with no explanation — an empty list is a claim that there are no accounts, which is a different and
false statement. Cloud services translate the transport failure into `Sati.Data.SessionExpiredException`
so a view model can name the condition without referencing the transport. `ISessionLifetime` carries
the same fact to the shell, which asks for credentials in place; local Production is served by a
never-raising implementation, because an EF session against a database the client already reaches is
bounded by the process rather than by a credential. Signing back in as the same person deliberately
reinitializes nothing: the loaded screens are still that person's, and replacing the visible agenda
drafts with what the server last stored would discard the unsaved text the pause existed to protect.

## A note is shown in one panel, and viewing is a locked mode of editing (2026-08-23)

The notes log showed a selected note twice: read-only in a Note Detail panel and, after a
double-click, editable in the shared entry module. That is two independent renderings of the same
clinical record, and nothing kept them agreeing. The detail panel is gone. The entry panel is the
only place a note is read or written, with viewing expressed as a locked mode of the same fields
rather than a separate screen.

**Rejected:** keeping the detail panel and merely narrowing it. It duplicated client, type, date,
status, units, return reason, and narrative; every future note field would have had to be added in
two places, and a mismatch between them would be invisible until someone noticed the two panels
disagreeing about a record that has one true value.

**Rejected:** disabling the whole form when locked. A disabled `TextBox` in WPF greys its text,
refuses focus, and cannot be scrolled or copied — the reader's job would be harder than before the
change. Text fields go read-only instead; only the controls that would change the record are
disabled.

**Rejected:** treating the lock as a permission. It is a mistake-guard on a clinical record, not an
authorization control, and it is not described as one anywhere. The API decides who may change a
note.

The padlock is deliberately not the only cue: the heading reads New Note, View Note, or Edit Note
and is a live region, so the mode is announced rather than only drawn.

### Selection may not silently discard a case manager's unsaved work

Making grid selection drive the panel means a single click can now replace what is in it. A
half-written visit note is real work, so every path that would replace panel contents — selecting a
row, double-clicking one, and re-locking an open edit — first asks through `TryReleaseDraft()`.
Declining snaps the grid selection back rather than leaving the highlighted row and the panel
describing different notes.

**Rejected:** deciding "unsaved" by diffing the panel against the saved note. Loading writes every
field and the visit attendees load asynchronously, so the diff would report changes the case
manager never made and would prompt on ordinary clicking-around. An explicit flag set by the field
callbacks and cleared by the loader says what actually happened.

The prompt is an injected `DiscardChangesPrompt` delegate rather than a window constructed in the
view model, so the confirmation is a decision the tests can supply an answer for instead of a
dialog they would hang on.

### The double-click decision lives in the module, not in each host

Both the notes log and the dashboard turn a double-click into "open this note for editing", and
both must first decide whether the panel's current contents may be replaced. Written twice, that
decision drifted immediately: the notes log asked before discarding a draft and the dashboard did
not. `NoteEntryViewModel.OpenForEdit` now owns it and both hosts are one line.

**Rejected:** fixing the dashboard by adding the same three branches there. It would have been
correct on the day it was written and silently wrong the next time only one of the two was
touched. A guard that has to be remembered in two places is a guard that will be missing from one.

### One WPF Application per test process, owned by the harness

Verifying that a locked note is genuinely read-only means loading the view for real: reading XAML
as XML proves a `Setter` is declared, not that `{StaticResource {x:Type TextBox}}` resolves or that
a `RelativeSource` binding reaches the property it names. Both of those were load-bearing here —
the attendee checkboxes sit inside an `ItemsControl` whose items are attendee view models, so the
obvious `{Binding IsLocked}` would have found nothing and left them editable.

WPF allows one `Application` per AppDomain and the flag enforcing it is never cleared, not even by
`Shutdown()`. A test assembly with two creators therefore fails in whichever one happens to run
second, which reads as a flaky unrelated test rather than as a duplicated singleton.
`WpfUiHarness` is the single owner; the pre-existing feature-view smoke test was moved onto it.

**Rejected:** letting the new view tests build their own `Application` beside the smoke test's.
It passed in isolation and failed in the full run, which is the worst possible failure mode — the
kind that gets diagnosed as "the test suite is flaky" instead of as a real constraint being
violated.

### Returning to a new note keeps the client

`ReturnToNewNote()` drops the loaded note and clears the fields but leaves `SelectedPerson` alone.
`Clear()` — which also nulls the client — stays as the full reset and is bound to nothing.

The distinction is not cosmetic. On the dashboard the note module's `SelectedPerson` is mirrored
onto the page and scopes the notes grid, the compliance checkboxes, and the form rows. A New Note
button that nulled it would blank the entire page around the panel, which is not what anyone means
by "start a new note". On both hosts the next thing a case manager usually does is write another
note for the same person; saving has always left the client in place for that reason, and now
takes the same path.

**Rejected:** giving the two hosts different behavior — clear the client on the notes log, keep it
on the dashboard. The module would have needed to know which page it was on, and the case manager
would have had to learn that the same button means two things.

### One way back, always visible, in the module rather than the pages

The New Note button lives in `NoteEntryView`'s header, so both hosts get it from the module instead
of each page declaring its own. Escape runs the same command, bound on the module so it works from
anywhere in the form and repeated on each host page so it also works from that page's grid. Hosts
drop their own grid highlight off the `EditorCleared` event; the module does not know grids exist.

**Rejected:** hiding the button until it would do something. An affordance that comes and goes has
to be rediscovered every time, and one that appears mid-form reorders keyboard focus underneath
someone tabbing through it. It is always visible and simply disabled on a panel that is already
blank.

**Rejected:** keeping the notes log's Deselect Note button beside it. It existed to stop the old
detail panel showing one note while the editor held another. With a single panel, "un-highlight the
row but keep showing its note" describes nothing a case manager wants, and two buttons that both
appear to clear the selection but differ in whether they reset the panel is exactly the redundancy
this page was being cleaned up to remove.

### A displayed note is checked for staleness at unlock, not on a timer

The note panel copies a record's fields in when it loads, so a note changed by a supervisor or
another session goes on being displayed as it was. The check for that runs at one moment: when the
padlock opens.

That is where the cost changes. Reading a slightly old version is a nuisance. *Editing* one means
either overwriting someone else's change or writing a full narrative and losing it to a conflict at
the moment of saving — which is exactly what `ReconcileNoteConflictAsync` was left to clean up
after. Checking at unlock moves that discovery to before the work instead of after it.

**Rejected:** polling while a note is displayed. It would put a repeating read on the server for
every open panel, in both hosts, to catch an uncommon event — and on the Demo path each of those is
an authenticated round trip that the database-wait feedback subsystem then has to explain to the
user. The save-time check already backstops anything the unlock check misses.

**Rejected:** blocking the unlock on the read. A Demo round trip can take seconds; a padlock that
freezes the panel when clicked would be worse than the problem. It is fire-and-forget behind an
instant unlock, guarded by a `LatestRequestTracker` so a slow reply for a note the panel has since
moved off cannot publish over the one now on screen.

**Rejected:** replacing what is on screen unconditionally when the server copy differs. If the case
manager has already typed, their work is theirs; the banner warns and leaves it alone. Only an
untouched panel is reloaded to the current version, where doing so costs nothing and starts the
edit from the record as it actually stands.

A check that cannot reach the server says so and leaves the note editable. Its message carries no
exception text: nothing about a note, a host, or a failure belongs in something a user reads, and
the save path still refuses a stale write.

### Personal typing shortcuts are local UI preferences, not agency Settings

Win+Shift+1 through Win+Shift+0 insert personal snippets only into the note narrative and the two
Scratchpad editors. The mappings live in the current Windows profile, separated again by Sati user
and Demo/Production environment. They are capped at 200 characters and are available to every role.
That makes them presentation/input assistance; they do not decide permission, workflow, billability,
or official record status, so an API round trip and Admin-only agency Settings authorization would
be the wrong ownership boundary.

Windows reserves Win+number combinations for taskbar actions. The low-level keyboard hook therefore
consumes the chosen Win+Shift+number gesture only when the Sati shell is active, an explicitly marked
editable target has focus, and that number has non-empty text. Everywhere else the event is passed
to Windows unchanged. Snippet contents are never written to diagnostic logs.

**Rejected:** a global `RegisterHotKey`. Windows-key combinations are reserved, and a global hotkey
would also steal the gesture when the user was working outside Sati. A normal WPF `KeyBinding` is too
late for a shell-reserved gesture and would behave differently across Windows configurations.

### Multi-select Visit documentation extends the note JSON instead of reinterpreting enums

Visit Setting, Appearance, Participation, and Health/Safety now render as checkboxes and may retain
several applicable selections. `VisitDocumentation` adds collection properties but retains the four
legacy singular enum properties. Current clients prefer a populated collection and fall back to the
singular value when opening older JSON; new saves populate both, with the first checked choice in the
legacy field. This avoids a database migration and keeps old notes readable without changing the
numeric meaning of already stored enum values.

**Rejected:** turning the existing sequential enums into `[Flags]`. Existing JSON stores their numeric
values; reassigning those numbers as bit flags would silently reinterpret historical clinical notes.

### Billing compliance is derived from dates and configured once per agency

A document affects billing only when its due date has passed and it was not completed as of the
date being evaluated. The due date itself remains billable, the gap begins the next day, and the
completion date is billable. This is owned by `BillingComplianceGate` in `Sati.Contracts.V1` and is
used for both today's compliance decision and historical service-date windows.

The participating types are an agency `Settings` flags value editable only by an Admin. The default
preserves the prior intended scope: 90-day reviews, PCP, Comprehensive Assessment,
Reclassification, and Safety Plan. Privacy Practices and the three release types are available but
start disabled. Settings updates retain the existing concurrency and audit behavior, and the API
validates that no unknown bits are persisted.

`Form.IsCompliant` remains useful workflow/presentation state, but it is not billing truth. A future
generated form may correctly be unfinished without being overdue, and legacy data can carry a stale
flag that disagrees with `CompletedDate`. Due and completion dates therefore win. A missing effective
date is tracked as a profile/data-quality concern rather than mislabeled as overdue paperwork.

**Rejected:** checking every current-cycle annual before its due date. That was the direct cause of
2027 documents blocking clients during 2026.

**Rejected:** separate hardcoded lists for today's gate, historical billing, the API, and reports.
They had already drifted: Safety Plan participated in one path but not the historical window.

This is Sati's configurable product policy, not a claim that MaineCare or OADS has approved every
default or boundary. External billing requirements still require agency, payer, and legal review.

### Dashboard document tabs are doorways into one implementation

AT Requests now lives on the case-manager dashboard navigation beside Clients and Notes. Authorized
Rep and Releases sit beside it. The latter two host the same `DhhsFormsViewModel` and
`AgencyReleaseViewModel` instances already used by the selected consumer on the Clients page; the
Clients-page DHHS Forms, Agency Release, and AT Requests workspaces remain available.

**Rejected:** copying the form functionality into new dashboard-specific view models. Two versions
of consent selections, PDF generation, consumer selection, or release safeguards would inevitably
drift and would create a healthcare-record correctness risk for a purely navigational request.

### Client creation is one transaction, and failure messages state save certainty

The Person, initial forms, first lifecycle version, and audit event are one creation graph. Local
persistence validates the graph before tracking it and commits it with one `SaveChangesAsync`; the
API follows the same validation rule and one-commit boundary. A relational rejection therefore
leaves none of the four behind. The API returns the tracked graph after commit instead of making a
second read that could fail after the record was already durable.

The UI distinguishes three outcomes: definitely not saved, definitely saved with a read-only refresh
failure, and unknown because a cloud request may have reached the server before the connection was
lost. Every message says what was saved, what went wrong, and the best next action. The unknown case
requires refreshing before retrying so recovery does not manufacture a duplicate Person. Technical
diagnostics use a support reference and do not expose exception text or Person data.

`PersonSaveRules` in `Sati.Contracts.V1` owns persistence validation for both hosts. Desktop
annotations remain immediate form feedback, but they are not the authority. The shared rule checks
database length bounds, dates, supported values, representative-payee requirements, and a complete,
unique, internally consistent initial form set. Local creation also derives agency ownership from the
signed-in actor and rejects a caller-supplied different owner.

**Rejected:** allowing an async command, constructor background load, or selection-triggered load to
surface an unobserved exception to WPF. A routine save or refresh problem must not terminate Sati.

**Rejected:** deleting old forms before the user confirms replacement forms and before the Person
update succeeds. Cancellation or a later failure would turn an edit into unreported data loss.

**Rejected:** reporting every network failure as simply "not saved." Once a request may have been
sent, that claim is unsafe; refresh-first recovery is required.

### Test-consumer deletion requires a durable creation marker and an Admin attestation

The Admin dashboard may permanently remove one consumer and their owned records only after the
record was marked as synthetic test data when an Admin created it **and** the deleting Admin
explicitly affirms that it was created for testing. The marker is creation-only and immutable;
ordinary users cannot set it and an update cannot convert a real consumer into deletable test data.
The warning directs duplicate and inactive-consumer cases to Cancel and the help menu. The
attestation is versioned in `Sati.Contracts.V1`, the selected Person revision is required, and the
local and API paths repeat the marker, Admin, and agency checks. UI visibility is not the permission
control.

The operation explicitly deletes forms and their synthetic-only attestation rows, notes, contacts,
consumer-provider links, quarterly reviews
and appointments, Comprehensive Assessments, AT requests and items, and Person lifecycle versions
in one serializable transaction. A billing claim line blocks the operation before any delete.
`AuditEvent` rows remain, and success appends `test-data.consumer-deleted` with the Person ID,
attestation version, and counts but no consumer name or narrative.

Removing `PersonVersion` and AT-request rows is a deliberate exception for attested synthetic data:
both may contain copies or snapshots of the test consumer, and their restrictive foreign keys
otherwise prevent the Person removal. This does not create an ordinary-client deletion policy and
does not supersede the unfinished retention/legal-hold work.

The `IsTestData` migration defaults every existing row to false. It backfills existing rows only
when `dbo.SatiDatabaseIdentity` exists and identifies the database as exactly `SatiDemo` / `Demo`,
where all consumers are defined to be synthetic. Production and legacy local databases remain
unmarked because guessing from names, dates, or other profile content would be unreliable. Their
existing consumers therefore cannot use this destructive command; an Admin may create a new,
explicitly marked test consumer when testing is needed.

**Rejected:** using either the marker or the attestation alone. A mutable or caller-forgeable marker
could reclassify a real consumer, while attestation alone asks one click to establish a historical
fact the application could have recorded at creation. Requiring both, plus authorization, agency,
revision, financial-record, transaction, and audit controls, gives each control one narrow job.

**Rejected:** deleting claim lines, billing periods, EDI generations, or audit events as part of
consumer cleanup. Those records have independent financial or evidentiary value even when the
source data began as a test.

## 2026-08-28 — Provider affiliation is one parent link, not three typed tiers

*Design decision recorded ahead of implementation. No code exists for this yet.*

Medical directory entries gain two columns: `Provider.MedicalKind`
(`Individual | Practice | Network`, nullable, required when `Type == Healthcare`) and
`Provider.ParentProviderId`, a self-reference.

The tiers are real rather than a UI convenience — they are the same split the federal identifier
system already makes, individual NPIs being Type 1 and organizational NPIs Type 2. `Provider.Npi`
and `BillingRules.IsValidNpi` therefore serve both without change.

**Affiliation is a single parent, not `PracticeId` + `NetworkId`.** Two typed FKs cannot express a
hospitalist who belongs to a network with no practice between, so they would force `NetworkId` onto
individuals as well — at which point an individual's network can disagree with their practice's
network, and the model contains a contradiction by construction. One parent has no such state.

Legal parents, enforced in `Sati.Contracts.V1` so the desktop and the API cannot disagree:

| Child | May parent to |
|---|---|
| Individual | Practice, Network, or nothing |
| Practice | Network, or nothing |
| Network | another Network, or nothing |

Network→Network is what lets three tier *names* survive four-level reality: MaineHealth owns Maine
Medical Partners, which owns practices. Individual→Individual is rejected — a nurse practitioner
under a supervising physician is a supervision relationship, not an affiliation, and folding it in
would corrupt every ancestor walk.

`ParentProviderId` is deliberately **not** gated to healthcare in the schema. Waiver providers have
the same shape (an agency owning programs owning direct-support staff), and the expensive-to-retrofit
part is the link, the cycle guard, and the resolution walk — not the vocabulary. `MedicalKind` stays
medical-specific because a second vocabulary is cheap to add later; the structure is not.

Resolution returns the ancestor chain generically and the medical UI labels each entry by its
`MedicalKind`. Saving validates the tier rule, rejects a parent that already has the child as an
ancestor, and bounds the walk by depth so a cycle introduced by concurrent edits cannot hang a
reader.

**Rejected:** flattening the tiers into `ProviderType` as `HealthcareIndividual` /
`HealthcarePractice` / `HealthcareNetwork`. `ProviderDto.Type` crosses the wire as a string, so this
would break the existing `Healthcare` value for no modelling gain. A separate nullable enum is
additive, and matches how `Npi` and `MaineCareProviderId` were already added as optional parameters.

**Rejected:** renaming `ProviderType.Healthcare` to `Medical` to match how case managers speak. Same
wire-compatibility cost. "Medical" is a display label.

**Consequence — duplicates stop being merely untidy.** Two case managers each typing "MaineHealth"
is cosmetic today. Once providers have parents it silently splits the tree, with half the practices
hanging off each row and no view that reveals it. The admin-curated directory governance already
deferred in `AGENDA.md` is promoted by this design from cleanup to prerequisite.

Per the 2026-08-15 decision, directory rows remain one agency's local knowledge, so the hierarchy is
local knowledge too and two agencies may legitimately disagree about who owns whom. The future
canonical organization registry is where that resolves; nothing here should try to resolve it early.

## 2026-08-28 — A consumer's provider list stores the link, never the resolved chain

*Design decision recorded ahead of implementation. No code exists for this yet.*

A new `PersonProvider` child collection links a consumer to a directory entry. It stores the
`ProviderId` and the attributes of the *relationship* — role, an at-most-one primary-care flag,
start and end dates, active state, release-on-file, display order — and stores **no copy of the
practice or network**.

Practice and network are derived by walking `ParentProviderId` at read time and rendered read-only.
Copying them onto the consumer row would mean that when a physician changes practices, every profile
naming her keeps showing the old one, silently and with no signal that it went stale. Deriving them
means the directory is corrected once and every profile follows. The fields are read-only in the UI
for the same reason: an editable derived value is a copy wearing a different costume.

This follows the split the codebase already draws twice — `PersonContact` is live profile data while
notes snapshot attendees, and an `ATRequest` snapshots vendor fields at select-time because it is a
document. Live profile derives; **documents snapshot the resolved triple at generation**, which is
what the deferred Comprehensive Assessment and PCP provider-selection items require.

`ProviderId` may point at any tier. A consumer whose relationship is with a walk-in clinic rather
than a named clinician selects the practice, and the derived chain simply starts higher. Blocking
that would model a workflow that does not exist.

**No cap on the number of providers.** An eight-row limit was considered and dropped: it is a
document constraint dressed as a data rule, and a medically complex consumer with eleven specialists
would have the eleventh recorded nowhere. Where a form has a fixed number of rows, the form takes
that many in the case manager's explicit order. A high sanity bound may guard against runaway input,
but it is not a product rule and no workflow should ever meet it.

Profiles stay tidy through **state, not truncation**: the default view shows active links only, with
the primary care provider pinned first, and past providers collapse behind a disclosure. Ending a
relationship sets an end date rather than deleting a row, because who was treating someone in a
given year is exactly the kind of question a case record has to be able to answer.

**Consequence — three existing fields are superseded and must be reconciled, not left alongside.**
`Person.PrimaryCareProvider` and `Person.HealthcareSystemName` are free text and a settings-driven
string list, and `PersonContact.Kind == HealthcareProvider` carries a free-text `Organization`. Left
in place, the same fact would live in four locations, which the one-named-owner rule forbids.
`HealthcareSystemName` already documents this seam and anticipates a name-match backfill.

Reconciliation keeps the string columns, adds the link beside them, matches what matches, and
surfaces the remainder for a case manager to link by hand. Unmatched free text is never deleted —
it is the only record of what someone actually typed.

**Rejected:** a medical-only consumer↔provider link table. `AGENDA.md` already carries an open
waiver-side need for the same association — the AT dropdown cannot pre-select a consumer's own
agency, and `Provider.OfferedServices` stays inert until it can. One table serves both; two would be
the same defect the one-named-owner rule exists to prevent.

## 2026-08-28 — `EndDate` is the only fact that says a provider link is current

Implementing the consumer provider list raised a choice the design had left open: whether a link
carries both an `IsActive` flag and an `EndDate`, as `PersonContact` does.

It carries only `EndDate`. Two columns meaning the same thing drift, and the drift is silent —
a row marked inactive with no end date, or ended with the flag still true, are both writable and
neither is obviously wrong at a glance. `ConsumerProviderRules.IsCurrent` names the rule so it
cannot come to mean two things in two places, and `PersonProvider.IsActive` is a computed
projection rather than a stored column.

A future-dated end is deliberately **not** current. A transfer recorded ahead of time is a real
workflow, but "current" has to be answerable without a clock: a rule whose result depends on when
it is asked cannot be enforced identically on a client and a server.

Both filtered unique indexes — one current primary care provider, one current link per provider —
filter on `EndDate IS NULL` for the same reason. An ended relationship constrains nothing: a
consumer may have had several primary care providers over the years, and may return to a provider
they left, which is exactly the history a second row is there to record.

**Rejected:** mirroring `PersonContact.IsActive` for consistency. The consistency would be with a
shape that was already carrying the ambiguity, not with a decision anyone had made.

## 2026-08-28 — A directory entry on a consumer's record cannot be deleted, and the refusal counts rather than names

An API test written for the consumer provider list failed on its first run: deleting a directory
entry a consumer was currently seeing succeeded. The foreign key would have caught it in
production, where the schema comes from the desktop migrations — as a raw constraint violation
surfacing to an Admin as an unexplained failure.

Both delete paths now refuse explicitly, before touching any other state, and count **ended links
as well as current ones**: the row still references the entry, and keeping that history readable is
the whole reason the row was not deleted when the relationship ended.

The refusal reports **a count and never consumer names**. An Admin curating the provider directory
has no need to know which consumers see which clinician, and a message is a disclosure channel like
any other. This is the same reasoning as `AuditCsv` neutralizing on the way out: the constraint
belongs where the value leaves, not only where it enters.

**Rejected:** letting the foreign key be the only guard. It produces the right outcome and the
wrong experience, and an error a user cannot act on is a defect even when the data is safe.

**Rejected:** naming the consumers so the Admin can go and clear the links. That is the same
argument that justifies every convenience disclosure, and the count plus the provider name is
enough to act on.

## 2026-08-28 — Removing a consumer provider link is separate from ending one

The list has both a `DELETE` route and an ordinary update that sets `EndDate`, and they mean
different things. Ending records that a real relationship stopped; removing corrects a link entered
against the wrong consumer.

Collapsing them would force one of two bad outcomes: either a typo is permanent, or ending a
relationship destroys the history the row exists to hold. The interface labels them accordingly and
the remove button's tooltip says what it is for.

Neither writes an audit event today, which matches how `PersonContact` behaves and is tracked in
`AGENDA.md` rather than quietly accepted — a removal is the one operation here that destroys a
record, and it is the obvious candidate for the first audited profile-child event.

**Deferred:** a free-text note on the link ("only sees him for the injections"). Genuinely useful,
but it is PHI-bearing free text with export and retention consequences and no consumer yet. Adding
a column is cheap later; adding it now without deciding those questions is not.

## 2026-08-28 — The legacy provider fields are linked by hand, never backfilled

`Person.PrimaryCareProvider` and `Person.HealthcareSystemName` are free text that predates the
directory. The obvious way to reconcile them is a migration that matches names and writes the
links. That is not what happens.

**Nothing is written automatically.** `LegacyProviderLinking` proposes; a case manager confirms,
one consumer at a time, from the provider panel on that consumer's profile. A bulk name-match write
across live consumer medical records is exactly the operation that should not run unreviewed, and
the failure mode is asymmetric: an unlinked value is visibly unfinished, a wrong link looks
finished. A consumer silently attached to the wrong physician is a clinical record defect that
nothing in the interface would ever flag.

**Matching is exact after trimming, case-insensitive, and nothing else.** No edit distance, no
token overlap, no prefix matching. "Dr. Reed" and "Dr. Reedy" are different statements of fact, and
so are "Dr. Reed" and "Dr. Reed, MD" — the second may well be the same person, but only somebody
who knows the caseload can say so. The tests name these cases explicitly because they are precisely
what a fuzzy matcher would get wrong while appearing to work.

**An ambiguous name is refused rather than resolved.** Directory names are unique per agency only
by identifier, so two "Dr. Reed" rows are possible. Linking to one would attach the consumer to
whichever sorted first. The panel says how many entries share the name and directs the case manager
to merge them in the directory — which is the actual defect.

**No schema change was needed.** The target of `PrimaryCareProvider` is a `PersonProvider` row with
`IsPrimaryCare` set, not a new foreign key on `Person`; the target of `HealthcareSystemName` is the
network already derived from that provider's chain. Adding either as a column would have created
the fourth copy this work exists to remove.

**The legacy strings are never cleared**, before or after linking. They are the only record of what
somebody actually typed, and a link is an addition beside them rather than a replacement. Where the
typed system name disagrees with the derived network, the panel says so rather than preferring
either: one of the two is stale and only a person knows which.

**Rejected:** a migration with a name-match backfill and a report of what it did. The report arrives
after the writes, which is the wrong order for an operation nobody can eyeball first.

**Rejected:** linking the healthcare system as a relationship of its own. A consumer's relationship
is with a clinician or a practice; the network follows from that. A second link would reintroduce
the disagreement between an individual's network and their practice's network that one parent link
exists to prevent.

**Deferred:** an agency-wide view of how many consumers still hold unlinked text. The per-consumer
prompt is enough to finish the work; a queue would make it easier to plan, and is tracked rather
than built.

## 2026-08-28 — `PersonContactKind.HealthcareProvider` is redefined, not retired

With clinicians now recorded as directory links, the contact kind could have been removed. It is
kept and re-scoped: it now means a person to contact **at** a healthcare provider — an office
manager, a care coordinator, the nurse who returns calls. Its display text says so.

Retiring it would orphan rows that are real people somebody deliberately recorded, and "who do I
actually phone at that office" is a question the provider directory does not answer and should not
try to. The two are different facts about different kinds of entity, and the ambiguity was in the
old label rather than in the data.

## 2026-08-28 — A document freezes the provider chain; everything else derives it

The consumer profile resolves a provider's practice and network on every read, so correcting a
directory entry reaches every consumer at once. A Comprehensive Assessment does the opposite.

`AssessmentNeed` now carries `ProviderPracticeSnapshot` and `ProviderNetworkSnapshot` beside the
existing `ProviderNameSnapshot` and `ProviderId`, and `ProviderAffiliation.Snapshot` is the shared
function that produces them. Choosing a provider on a need is what freezes the chain; nothing
recomputes it afterwards. An assessment approved in March has to keep saying what it said in March,
even after the physician moves practices — a document that silently rewrites itself when reference
data changes is not a record of anything.

This is the only place in Sati where the chain is copied. Getting the direction wrong is silent in
both directions: a profile that copies goes stale invisibly, and a document that derives rewrites
history invisibly. The tests assert the difference explicitly rather than assuming it.

`ProviderId` is kept alongside the frozen strings so an entry can still be traced back to the
directory, but it is a reference for humans, not a lookup the renderer performs.

**The free-text provider box on a need is replaced by a picker** over the consumer's own current
provider list, closing the deferred "replace the temporary provider-name entry" item. Only current
links are offered: a need written today should not propose somebody the consumer stopped seeing
last year. A need whose provider was typed before the directory existed, or who has since left the
consumer's list, still renders exactly what it recorded — the document keeps what it froze rather
than being rewritten to match the present.

**Rejected:** resolving the chain when the assessment is rendered, from `ProviderId`. It would keep
one copy of the truth, and it would silently change approved documents.

**Rejected:** dropping `ProviderNameSnapshot` now that a picker supplies the name. Every assessment
written before this change has only that field, and it is the whole record for those needs.

## 2026-08-28 — Provider-directory editing is broad, destructive curation is Admin-only

The provider directory is one agency-wide rolodex. A case manager on the phone with a new
specialist must be able to add the entry immediately, and supervisors/directors must be able to
correct it. `ProviderDirectoryRules.CanCreateOrEdit` therefore permits CaseManager, Supervisor,
Director, and Admin in both local and API paths. Delete and merge remain Admin-only because they
remove a shared row other users' consumers, affiliations, and settings may reference.

**A same-name match warns and never blocks.** Names are normalized by trimming, collapsing internal
whitespace, and ignoring case, but they are not identities: two real organizations can share one.
The form explains the split-tree risk and leaves the decision to the person who can verify it.
Durable NPI/MaineCare identifier conflicts continue to block.

**Named contacts are not the general phone line.** `Provider.PrimaryContact` and `Phone` describe
the organization's main directory contact. `ProviderContact` describes several actual people who
work there (referral coordinator, billing contact, office manager), with one optional primary.
Combining them would make a one-to-many fact overwrite a one-to-one fact.

**Merge moves live references and never rewrites documents.** In one serializable transaction it
moves affiliated children, consumer-provider links, named contacts, and the agency passthrough
default; adopts identifiers and a parent only where the survivor has none; then removes the
duplicate and records `provider.merged`. Tier mismatches, conflicting durable identifiers,
affiliation loops, cross-agency entries, and a consumer currently linked to both entries are
refused before any write. The refusal counts consumers and never names them.

`AssessmentNeed.ProviderId` and its name/practice/network snapshots are deliberately excluded.
They are part of a document that froze what was selected; repointing them would silently change
what an assessment says. This asymmetry is the point of the merge: live directory relationships
follow the survivor, records of what was documented do not.

**Rejected:** making all directory writes Admin-only. It turns normal phone-call work into an
administrative queue and caused Demo and local Production to disagree about the same button.

**Rejected:** silently choosing one of two current consumer links during merge. The two rows may
carry different role, release, and date facts. Sati directs the case manager to end or correct one
instead of destroying relationship history under an Admin curation command.

## 2026-08-29 — Synthetic provenance is data, and exchange history is append-only

Demo billing history uses the same bounded read contracts as future real exchange history, but every
seeded row carries `IsSynthetic = true` and both grids expose that fact as a dedicated non-color
column. A banner alone is not sufficient provenance: copied rows, screenshots, exports, and future
consumers must not have to infer which environment produced a financial-looking outcome.

Submission activity is an event stream rather than another mutable status column on
`BillingPeriod`. Generated, transmitted, failed, 999, and 277CA facts occur at different times, and
a later response must not erase an earlier failure or retry. Remittance claim outcomes are likewise
append-only; reversal is a new outcome, not an edit to the original payment.

The first slice stores bounded operational explanations and claim-level amounts, not raw inbound X12
or note narratives. There is no inbound mutation route yet. Real 999/277CA/835 parsing, validation,
matching, posting, reconciliation, retention, and legal-hold policy remain separate work, and the
Demo catalog must not be described as clearinghouse connectivity or payer certification.

**Rejected:** hard-coded WPF sample rows. That would make the distributed client invent financial
facts and would exercise neither tenant authorization nor the API contract.

**Rejected:** overloading `BillingPeriod.Status` with the latest external response. One value cannot
preserve retries, transport failures, functional acknowledgments, claim acknowledgments, and later
reversals without destroying chronology.

## 2026-08-30 — Deposit reconciliation is a separate anchor, and PLB is never hidden

An 835 can describe several claim payments plus provider-level adjustments that do not belong to
one ClaimLine. `RemittanceDeposit` therefore stores the payment reference, claim-payment total,
signed PLB amount and description, 835 payment amount, and optional EFT amount as one append-only
reconciliation read model. `DepositReconciliationRules` is the single owner of the four states:
awaiting EFT, matched to the penny, EFT mismatch, and internally unbalanced remittance.

The desktop displays these values together and never calls a deposit reconciled merely because an
835 was received. A future EFT or 835 importer must append a new immutable observation and retain
the prior one; it must not overwrite an original remittance. The current Demo rows are synthetic
and intentionally cover a takeback, a missing EFT, an EFT mismatch, and a remittance arithmetic
mismatch.

**Rejected:** repeating an EFT total on every claim outcome. That makes the same deposit drift when
one claim is corrected and hides provider-level adjustments that have no claim reference.

## 2026-08-30 — The migration chain belongs to a platform-neutral persistence assembly

`Sati.Persistence` targets plain `net10.0` and owns the entity model, `SatiContext`, its design-time
factory, and the migration source and snapshot. The WPF client and API may reference it without
pulling in `UseWPF` or a Windows target, so a future migrator is not forced onto Windows before its
hosting decision is made. The WPF-only `WorkdayTile` remains in the desktop; placing an
`ObservableObject` in the persistence project would reverse the boundary this move establishes.

The desktop's local EF services and guarded startup update remain in the WPF project. They receive
the same `SatiContext` type from the new assembly, so backup-before-migrate ordering and Local
Production behavior do not change. The API continues to use `ApiDbContext` for requests. Referencing
`Sati.Persistence` gives shared schema tooling a cross-platform home; it does not silently replace
the API's deliberately scoped model.

The migration files moved physically with their assembly owner so the next ordinary
`dotnet ef migrations add` lands beside the existing chain. A hand-authored migration without
`MigrationAttribute` is not part of EF's chain even if its filename looks correct. The missing
metadata on `TenantScopeSettingsAndProviders` was therefore repaired, and a boundary test now
requires all 80 ids to be discoverable without any WPF assembly reference.

**Rejected:** leaving the files compiled by the WPF project and creating a Windows migrator. That
would make the Phase 3 hosting decision implicitly and permanently. Also rejected: switching API
requests wholesale to `SatiContext` during this move; that is a much larger authorization and query-
shape change than extracting migration ownership requires.

## 2026-08-30 — Demo schema changes run from inside the App Service, not from a workstation

The temporary SQL firewall rule was never about publishing the API. `az webapp deploy` pushes a zip
to App Service and never touches SQL; two Demo publications this week opened no rule at all. The
rule exists only because *applying* a migration meant connecting to `SatiDemo` from a workstation,
and no workstation has standing access. The release playbook put that step inside its "Demo API
publication" section, which made the two read as one thing.

Reading drift stopped needing the rule when the comparison moved into the API. Applying a change
stops needing it when the thing that applies it runs somewhere already on the allow-list. That is
what `demo-history-reconciliation` is: a triggered WebJob, shipped inside the API package at
`App_Data/jobs/triggered/`, running from the same three outbound addresses the SQL server already
admits.

**The cost is honest and bounded.** A WebJob runs under the App Service's managed identity, and
identity is scoped to the resource rather than the process, so granting it DDL rights means the
internet-facing API effectively holds them too. A compromise of the API could then alter the Demo
schema. That is accepted while `SatiDemo` holds only synthetic data, and `AGENDA.md` Phase 3 records
the gate: before `SatiProduction` moves to the cloud, the runner moves to a Container Apps Job with
its own identity, genuinely out of the API's reach.

**That cost is not owed yet, and the first write-up said it was.** The reconciliation issues only
`INSERT` and `DELETE` against `dbo.__EFMigrationsHistory` plus catalog reads — no `CREATE`, `ALTER`,
or `DROP`. Writing rows is `db_datawriter`, which the identity already needs to serve the API, so
this job most likely requires no new grant at all. `db_ddladmin` is owed when `Sati.Migrator`
applies real schema migrations, and not before. The prerequisite was written down as DDL because
that is what the *phase* eventually needs; stating it as the *job's* precondition would have widened
a production-facing identity to solve a problem that may not exist. Least privilege is decided per
operation, not per phase.

Choosing the cheap host now costs almost nothing later, because the expensive work is already done.
Phase 1.5 freed the migration chain from the WPF target, so what runs is host-agnostic; hosting is a
thin, swappable layer. Doing the Container Apps Job first would have meant a Dockerfile, a registry,
an environment, and a fourth standing allow-list entry for its egress, to migrate a synthetic
database a few times a month.

Two safety properties are deliberate. The job defaults to the rollback-only dry run and requires the
app setting `SATI_RECONCILIATION_MODE` to be exactly `apply` before it will change anything, so
triggering it by accident is inert. And `-UseManagedIdentity` throws when the App Service identity
endpoint is absent rather than falling back to integrated security, which off-host would silently
connect as the signed-in developer and defeat the point.

**Rejected:** migrating on API startup. Every instance would race, there is no plan step, no
authorization gate, and a bad migration takes the service down with it. `SchemaDriftHealthCheck`
already says the API does not migrate, and co-locating a separately triggered process in the same
package does not change that. Also rejected: an Admin API route that applies migrations. It keeps
the same DDL grant as the WebJob and adds network reachability, which is strictly worse.

## 2026-08-30 — A catalog proof cannot tell "absent" from "invisible"

`Apply-DemoHistoryReconciliation.ps1` refused for most of an evening because it reported that
`dbo.Users.AgencyId` had no constant default of 1. It did have one. `DF__Users__AgencyId__57DD0BE4`,
definition `((1))`, created 2026-08-11 and untouched since.

The App Service managed identity held `db_datareader` and `db_datawriter`. Neither carries
`VIEW DEFINITION`, and without it a principal reads a table and its columns but sees no rows for that
table in `sys.default_constraints`. The proof's `IF NOT EXISTS` was therefore true, and it reported a
permission boundary as schema drift. `GRANT ALTER ON OBJECT::dbo.Users` implies `VIEW DEFINITION`;
the same proof passed minutes later against a database nobody had changed.

**The rule this establishes: any assertion read through `sys.*` must either run as a principal with
`VIEW DEFINITION` on what it inspects, or first prove it can see the class of object it is about to
judge.** An `IF NOT EXISTS` over a catalog view answers "I cannot see one", and that is not the same
proposition as "there is not one". Failing closed on invisibility is safe in the narrow sense that
nothing was written — but it is not harmless, because a false diagnosis is acted upon. This one
produced a corrective DDL script, a second WebJob with schema-change capability, and a standing
`ALTER` grant, all for a problem that did not exist.

The reconciliation's proofs were otherwise vindicated. Every other assertion — columns, indexes,
foreign keys, identity, primary keys, the history table's own key — was correct, and the refusal to
write a history row it could not justify was exactly right. The defect is in what the proof concludes
from a negative catalog read, not in its insistence on proving semantics rather than names.

**Rejected:** relaxing the default-constraint assertion so the run would pass. The assertion is
correct and the constraint genuinely matters to what `AddAgencyId` means; the problem was the
principal's sight, not the standard. Also rejected: leaving the diagnosis at "the schema diverged"
once the constraint's `create_date` showed it predated every run that evening. A finding that cannot
survive its own timestamps is not a finding.

## SatiProduction migration-history reconciliation — 2026-08-30

Sati 1.2.32 refused to start against `SatiProduction` with SQL 2705, "Column name 'AgencyId' in
table 'Settings' is specified more than once". The startup guard did what it exists for: it took a
backup first, refused rather than half-applying, named the offending object, and left the records
unchanged.

**The cause was not the 1.2.32 change set.** `20260812090000_TenantScopeSettingsAndProviders` was
authored without its `[Migration]` and `[DbContext]` attributes, so EF never enumerated it and never
recorded it, while its effects reached the database by another route. Restoring those attributes
during the persistence move made EF see the migration for the first time, and the first thing it did
was try to apply one whose columns already existed. The drift had been latent for eighteen days; the
attribute repair is what surfaced it.

`scripts/Apply-ProductionHistoryReconciliation.ps1` writes the one missing history row after proving
the migration's whole end state is present — both `AgencyId` columns as required `int`, both indexes
by key column and ordinal, both foreign keys by the columns they map rather than by constraint name.
It creates, alters, and drops nothing.

Six other migrations also lacked history rows on the rehearsal database and were deliberately left
alone: their objects are genuinely absent, so they are pending rather than drifted and EF must apply
them normally. Writing history rows for those would tell EF work had been done that had not been,
which is how this class of problem starts.

Rehearsed against this machine's own `SatiProduction`, which carried the identical drift: dry run
clean, one row applied, second run wrote zero. The live database went from 78 to 79 history rows,
leaving one genuinely pending migration for the desktop to apply with its own backup and guard.

**The general lesson, alongside the catalog-visibility one above: a migration file without its
attributes is not in the chain, and a database can therefore carry a migration's effects with no
record that it ran.** Neither `dotnet ef migrations list` nor a history-table comparison will show
it, because both work from what EF enumerates. The only thing that catches it is comparing declared
effects against the actual schema.

## 2026-08-30 — Startup repairs the provable half of a history disagreement

`LocalDatabaseUpdater` used to refuse every disagreement between migration history and
schema, and said why in its own comment: it needs judgement about which side is right, and
guessing on a database full of consumer records is not something a startup path should do
unattended. That reasoning was right and is why nothing was damaged when this finally
happened for real on three machines in one evening.

It was also doing more work than the argument required. "Which side is right" is a
judgement only when the two sides actually disagree about something. When every effect a
pending migration declares is already present in the schema — every column, index,
foreign key and primary key, each checked by what it maps rather than by its name — the
schema has already answered. Recording that the migration ran is then a statement of fact,
and the recording is an insert into `__EFMigrationsHistory` that touches no schema and no
consumer data.

So the startup path now repairs exactly that case and nothing else.
`MigrationEffectAnalyzer` classifies each pending migration before anything is written:
every effect present is `AlreadyPresent` and is recorded; no effect present is
`NotApplied` and is migrated normally; anything else is `PartiallyPresent` or
`Indeterminate` and still stops, now naming the migration instead of surfacing SQL 2705
about a duplicate column.

**What the analyzer will not claim.** Raw SQL steps and data changes cannot be inspected,
so they are reported as unverifiable and left out of the verdict rather than assumed in
either direction. `TenantScopeSettingsAndProviders` — the migration that caused all of
this — contains such a step, a backfill. Its structural evidence happens to settle the
question anyway, because the migration alters both columns to `NOT NULL` afterwards and a
column cannot be made `NOT NULL` while nulls remain. That is a property of this migration,
not a general guarantee, which is why the unverifiable steps are still reported. An
operation type the analyzer does not recognise counts as unverifiable too, so an
unfamiliar migration reports `Indeterminate` rather than a confident wrong answer.

**Rejected:** repairing `PartiallyPresent`. Which half is missing decides what should
happen, and no amount of care makes an unattended guess about that acceptable against
consumer records. Also rejected: requiring zero unverifiable steps before repairing, which
sounds stricter but would have excluded the only case that actually occurs and left the
feature useless.

Verified end to end against a real SQL Server database rather than only with fakes: a
scratch database built from the chain, drifted by removing one history row, then run
through the real updater — `Applied`, drift recorded, nothing pending, and a second run
reporting `AlreadyCurrent`.

**Noticed while building the rehearsal:** the migration chain does not replay on an empty
database. A migration reads `dbo.SatiDatabaseIdentity`, which is created outside the chain,
so `MigrateAsync` against a fresh database fails on `Invalid object name`. Real installs
create that table first, so nothing is broken today, but it means the chain alone cannot
reconstruct a database. The unmerged `second-machine-setup` branch carries a commit named
for exactly this. Tracked in AGENDA.md.

## 2026-08-30 — A claim remains visible from promotion through payment

The billing queue owns approved notes that have not become claim lines. The submissions home owns
every billing period that contains claim lines, including a draft with no submission events. This
makes promotion a move between two visible lists rather than the point where unbilled work can
disappear.

`BillingSubmissionProgress.NotSubmitted` represents that eventless interval. It is constructed
from a period's claim lines by `BillingSubmissionsViewModel`; it is deliberately never returned by
`BillingSubmissionProgressRules.Classify`, because no submission stage means "not submitted." Once
the first event exists, the append-only event stream supplies the batch's progress as before.

The date shown for an unsubmitted period is its oldest `ClaimLine.DateOfService`. Timely-filing risk
runs from service delivery, so this is the age a biller needs to see. A billing-period creation
timestamp would measure when Sati grouped the work rather than how old the work is, and adding one
would require a migration without improving this decision. No `CreatedAt` column or migration was
added.

**Rejected:** leaving draft periods on the Queue after claim-line promotion. That would make the
same note appear to be both awaiting promotion and already promoted, and it would give two screens
authority over one lifecycle state.

## 2026-08-30 — Agency authorization is per-user permissions, with billing independent

Agency users carry a persisted `[Flags]` permission set owned by `UserPermissions` and interpreted
only by `UserPermissionRules` in `Sati.Contracts.V1`. Case management, supervision,
administration, and billing are independent capabilities. This permits a case manager to bill
without gaining user management, destructive test-data cleanup, audit export, operations, or
schema-report access. The old `Role` column remains temporarily as non-authoritative compatibility
metadata and for the separate `PlatformOperator` identity; agency authorization never reads it.

The API resolves permissions from the database in `ValidatedActorFilter` on every request rather
than trusting a permission claim in the JWT. Thus a grant or revocation takes effect on the next
request. Every billing endpoint denies without billing permission, regardless of an Admin legacy
label. `PlatformOperator` has no agency permissions and retains only its narrow cross-tenant
incident surface.

Stateful billing service methods accept a small immutable `AgencyActor` containing user id, agency
id, and permissions, not a persistence `User` or an ambient login service. The local implementation
re-confirms all three fields against the database before doing work. The API constructs the actor
from validated server state; it never trusts a caller-supplied user or agency as the actor. This
makes authorization an explicit dependency without coupling domain code to WPF session state or
exposing the user entity as a contract.

The migration backfills legacy access: CaseManager gets case management; Supervisor gets case
management and supervision; Director adds administration; Admin gets all four. Unknown bits and an
empty set deny by default. New user management edits permissions directly, and desktop visibility
follows the same predicates while remaining only a presentation aid.

**Rejected:** four unrelated boolean columns, because combinations and future extension become
scattered rules; permissions in the token, because revocation would wait for expiry; passing the
full logged-in `User`, because it couples services to persistence and ambient UI state; and a
general role/claim framework, which adds machinery the four known capabilities do not require.

## 2026-08-31 — Agency-wide supervision is its own capability, and user management is enforced in the service

Three findings from the line-by-line review of the per-user permissions conversion
(`API_SECURITY_AUDIT.md`, third pass). All three are fixed here.

**Director backfills to agency-wide supervision, not administration.** The permission model
conflated two different powers under `Administration`: reaching every case manager in the agency,
and holding the audit export, settings, destructive test-data deletion, and provider merge. The
legacy `Director` label held the first without the second — under the old role string every
administration gate read `Role != "Admin"`, which denied Director outright. Mapping Director to
administration therefore granted, on upgrade, roughly twenty-five gates it never had. The fix is
a fifth capability, `AgencyWideSupervision`, carrying the supervisory reach; `Director` maps to
case management + supervision + agency-wide supervision, and `Admin` to all five. Administration
implies agency-wide supervision, because an administrator who can export the whole agency's audit
trail is not meaningfully restrained from reading its notes. `canReviewAgency` and the two other
broadening sites now read the capability rather than administration.

This is the fifth bit the original entry said the four known capabilities did not require. It
earns its place because a concrete backfill could not be expressed without it, not because the
model wanted generality.

**The correction ships as a second migration, not an edit.** `AddUserPermissions` is left exactly
as written. Editing the body of a migration already recorded in `__EFMigrationsHistory` is skipped
on upgraded databases and applied on fresh ones, which is precisely how fresh and upgraded
deployments diverge — the failure mode this project already has reconciliation tooling for.
`SeparateAgencyWideSupervision` corrects Director 7→19 and Admin 15→31, scoped to rows still
carrying the exact value the first migration wrote, so a deliberate edit made in between survives.

**There is deliberately no "you may not grant what you do not hold" rule.** It looks like an
escalation control and is not one: whoever may create a user also chooses that user's initial
password, so an administrator without billing can already mint a billing user and sign in as it.
The subset test would have blocked an administrator from creating an ordinary case manager while
stopping nothing. Administration is the root capability by design. The rule that does carry weight
is the non-administrator branch — anyone without administration may write only a
case-management-only user assigned to themself — which is the real successor to the old ladder's
"only an administrator may create or assign an administrator", and which the Director remap now
puts Directors back underneath.

**Desktop user management is authorized in `UserService`, not the view model.** `CreateAsync` and
`UpdateAsync` previously took a `User` and wrote it with no actor, no permission gate, and no
agency scoping. That was survivable only while `NewUserViewModel` hard-coded `UserRole.CaseManager`;
once the conversion let it assemble an arbitrary permission set from checkboxes, the only restraint
on local Production — where no API sits behind the service — was `CanAssignExpandedPermissions`, a
view-model boolean. Both methods now take an `AgencyActor`, re-confirm it against the database the
way `ValidateBillingActorAsync` does, and delegate the decision to
`Sati.Contracts.V1.UserManagementRules`, shared with `Sati.Api` so the two cannot drift. Self-service
profile editing moved to `UpdateOwnContactDetailsAsync`, which takes the two fields a user may
change about themselves and cannot express a permission, agency, supervisor, or label change at all.

**Rejected:** re-adding a Director-specific special case in the validator, because the ladder it
belonged to is gone and the permission set can express the same thing structurally; and narrowing
Director to plain supervision, which removes the escalation but silently costs every existing
Director the agency-wide review they actually had.

### Addendum — what the WebJob does and does not remove

Recorded because it caused a real mistaken belief during the 1.2.34 release. `AGENDA.md` had
summarised the 1.2.32 WebJob as meaning "applying a Demo schema change no longer needs a temporary
exact-IP SQL firewall rule". That sentence outran what shipped, and the same release section
elsewhere says plainly that 1.2.32 contained no schema change and opened no rule, so the claim was
never exercised.

The accurate boundary is the one the job's own header draws. `demo-history-reconciliation` writes
only to `dbo.__EFMigrationsHistory` and reads catalog views. It issues no `CREATE`, `ALTER`, or
`DROP`, which is precisely why it needs `db_datawriter` rather than `db_ddladmin` and why it was
safe to ship inside the internet-facing API. Reconciling history from inside the App Service does
not generalise to applying DDL from inside it.

So until `Sati.Migrator` exists, a schema-adding release still opens the rule, and 1.2.34 did:
`AddUserPermissions` adds a column. The AGENDA line is corrected in place rather than deleted, with
the correction visible, because a reader who remembers the original sentence needs to find out why
it was wrong rather than wonder whether they imagined it.

## 2026-08-31 — Quarterly evidence does not complete the quarterly attestation

The 90-Day Reviews workspace keeps `ReviewItem` and `Form` separate. Requested, Received, and
Logged dates say that supporting evidence was gathered and recorded. The corresponding `QnR`
form says that the case manager attests the quarterly review itself occurred. A provider document
arriving or being logged cannot make a consumer billable; only the explicit form transition can.

The Reviews workspace therefore shows both states and offers an attestation control beside the
evidence. Its completion-date picker is blank and required. It does not default to `DueDate` or
silently stamp today, because `CompletedDate` defines the historical billing-block window and an
invented on-time date can make late-period service appear billable. An explicitly entered late
date is preserved exactly. Future dates are rejected by the shared `FormCompletionRules` owner in
the UI, Local persistence, and API, so bypassing the desktop cannot create contradictory state.

At this point the dashboard and Clients quick toggles still recorded `DueDate` as their documented
on-time assumption. The 2026-09-03 all-form attestation decision below supersedes that temporary
exception and replaces both toggles with the blank shared capture.

All form-compliance changes now converge on one dashboard refresh cascade. Checkbox properties,
the matrix, and upcoming/late-review events refresh together; external workspace changes reload
the dashboard's person snapshot first. This is an awaited callback rather than an ordinary async
event so a save command cannot report completion while dependent screens still show stale state.

**Rejected:** auto-completing `QnR` when all evidence is Logged; deriving completion from the last
Logged date; pre-filling the due date in the evidence-rich Reviews workflow; and validating only
in WPF. Each either confuses evidence with attestation, invents a billing fact, or permits a direct
API caller to bypass record-integrity rules.

## 2026-09-03 — Form evidence never completes a form; explicit attestations do

The quarterly decision now applies to all twelve form types. A form-tagged note is evidence that
can appear in a derived pending-attestation list; saving, editing, moving, or deleting that note
does not mutate `Form.CompletedDate`. The pending projection resolves the form from the note's
person, type, and event-date cycle rather than the current dashboard selection and today's cycle.

All ordinary completion paths use one shared attestation control. Its date starts blank, must not
be in the future or before the form's cycle start, and is passed unchanged to the persistence
boundary. The dashboard checkbox, task board, Clients workspace, Reviews workspace, and bulk tool
no longer invent `Today` or `DueDate`. The admission confirmation remains a creation-only exception:
it captures a per-row date before a new Person graph has ever been saved.

Every persisted completion or reasoned revocation appends a `FormAttestation` row and a
PHI-minimized `form.attested` or `form.attestation-revoked` audit event in the same transaction.
`Form.CompletedDate` remains the fast, authoritative projection used by billing and presentation;
`Form.IsCompliant` remains derived from it. Ledger rows cannot be changed or deleted, their Form
foreign key is restricted rather than cascading, and a form with attestation history cannot be
deleted. Existing completed forms receive a System row with reason `pre-attestation record` in the
`AddFormAttestations` migration without changing their recorded completion dates.

`CompletedDate` is an optimistic-concurrency token. Two sessions starting from the same outstanding
form cannot both attest successfully: one commits, and the other receives the typed
`form_attestation_changed` conflict. All three new routes validate the persisted actor and call
`TenantAccess.CanAccessUserAsync` before a caller-controlled caseload id reaches a feature query.

`PUT /api/v1/forms/{id}` is now an opened-date endpoint in practice: a requested completion change
is rejected. This closes the direct bypass before document prerequisites land. At this point the
next design slice remained blocked on annual-document choices; the following decision records
Josh's answers rather than inventing them.

## 2026-09-03 — Provisional Privacy Practices default and versioned templates

Josh subsequently authorized generic Privacy Practices wording while the actual agency template
is unavailable. The seeded Sati-default version is visibly provisional and requires later agency,
privacy, and legal review. Its cycle label is a preparation date, not a claim about the legal
effective date of an approved notice. Generating the notice does not record receipt or complete
the Privacy Practices form; the separate acknowledgment gate remains a later implementation step.

Published template source is immutable. An agency Administrator appends the next version rather
than editing an existing one. The latest non-retired agency version wins over the latest Sati
default, and each artifact retains the exact owner/key/version used at generation. Default rows
are seeded through controlled migrations, not editable through an agency API. Template retirement
is reserved in the schema but no retirement mutation is exposed in this slice.

The source is a deliberately constrained text format: headings, paragraphs, bullets, pipe tables,
page breaks, and a closed token list. It is neither HTML nor executable code. Validation lives in
`Sati.Contracts.V1`; the shared `Sati.Forms` MigraDoc composer only renders accepted source.
`DOCUMENT_TEMPLATES.md` describes the format and the unresolved production-review requirements.

## 2026-09-03 — Document prerequisites are server facts, with a narrow technical override

Josh resolved the three choices blocking the document-prerequisite slice. Sati owns a distinct
Medical Release generator, Reclassification requires a completed Comprehensive Assessment in the
same compliance cycle, and a Supervisor may override an unmet prerequisite only for a technical
problem with a required explanation. The override is stored with the attestation and emits its own
`form.prerequisite-overridden` audit event; it is not a consumer signature, a billing override, or
permission to bypass the attestation-date rules.

`DocumentArtifact` stores metadata rather than PDF bytes. Generated documents record kind, cycle,
origin, generator, timestamp, filename, byte count, SHA-256, and known blank fields. An externally
prepared document records the same cycle identity plus a required note. Regeneration supersedes the
prior live row, and a filtered unique index permits only one live artifact per person, kind, and
cycle. A Draft is recorded but never satisfies a release prerequisite.

`AnnualDocumentCatalog` and `FormAttestationRules` in `Sati.Contracts.V1` are the shared rule
owners. The API re-derives artifacts, assessment state, actor role, and tenant scope before accepting
an attestation; caller assertions are not trusted. Agency, DHHS, and Medical release generation now
write artifact metadata in the same transaction as their PHI-minimized audit event. The Medical
Release uses Sati-owned wording and the existing release-choice structure; legal/program review is
still required before representing it as an accepted official form.

## 2026-09-01 — The initial login agenda is once daily and local

The sign-in agenda appears at most once per local calendar day for each Sati user in each
environment. This resolves the handoff's open cadence question in favor of the recommended
once-daily behavior. Reauthentication, restarts, and account switching must not repeatedly put a
modal between a case manager and the caseload. A personal Appearance-tab toggle disables it.

Both values live in one local, environment-and-user-keyed JSON file. They deliberately do not live
on agency `Settings`, where one person's choice would affect colleagues, and they do not add a
`User` column and migration for presentation state that does not need to roam. The tradeoff is
explicit: a person using two computers chooses the setting on each, and may see the agenda once on
each machine in the same day.

The agenda is not a compliance workflow. It surfaces all unattested overdue forms, shows at most
the oldest five and the true count, and identifies which additionally block billing. It never bulk
completes or synthesizes a completion date. `LateReview` forward events are removed from the agenda
because the unbounded overdue source already owns the same forms; showing both would manufacture
two apparent tasks from one record. A quiet-period Comprehensive Assessment suggestion keys from
the form's unattested state even when its assessment entity is Approved, preserving the existing
evidence-versus-attestation boundary.

Selected items originally appended ordinary human-editable lines through the current Today's Work
view model. No identifiers, hidden markers, or parseable task payloads were embedded in that text.
The 2026-09-05 structured Today's Work decision below supersedes only this selected-item storage
choice; the once-daily cadence, local preference, recommendation sources, and no-compliance-write
boundary remain in force.

Two copy corrections were accepted during implementation. Four Set E variants originally omitted
the `{1}` forward-window value despite the handoff's own test requiring it in every Set E variant;
they now state the guaranteed forward period. Also, forward `LateReview` rows are deliberately
deduplicated against the overdue section rather than presenting one form twice.

## 2026-09-01 — The database owns "one form per person, type, and due date"

`dbo.Forms` now carries a unique index on `(PersonId, Type, DueDate)`. That invariant used
to be enforced only by `Person.AddMissingFormsForCycle` reading the person's own `Forms`
collection before inserting — a check-then-insert with nothing holding the gap. Before
`57af6fa`, `GetAllPeopleAsync` ran that on every caseload load on its own `DbContext`, and
startup issued those loads concurrently, so three loaders each passed the check and each
inserted a full set. Every form in `SatiProduction` generated before that commit exists
three times.

The code fix that stopped it — serializing the loads, gating the write off — was correct
and is unchanged. It is also not sufficient: it prevents that one caller from racing,
while the invariant it protects has no owner. `ViewModels/NewClientViewModel` was a second
writer producing the same shape by a completely different route, and neither the model nor
the database would have refused either one. Only a constraint can, so the constraint is
where the rule now lives.

**Consequence — the writers must survive losing.** `GetAllPeopleAsync` catches the
unique-violation `DbUpdateException`, discards its own losing inserts and re-reads, because
losing that race means the rows it wanted are in the database already. A crash on a benign
concurrent write would be a worse bug than the one being fixed.

`Form.Type` was `nvarchar(max)` and had to be narrowed to be indexable. 40 characters
against a longest enum name of 23.

**Rejected: deriving `IsCompliant` from `CompletedDate` as part of this change.** It is a
real defect — 147 rows already hold `IsCompliant` true with a null or future completion
date, which renders as checked while the gate reads incomplete — and it produces a symptom
identical to the duplicates. It is also a different defect with a different blast radius,
and collapsing it into this one would have made a data repair and a semantic change to
"born compliant" annual documents indistinguishable in the same release. Tracked separately
in `AGENDA.md`.

## 2026-09-01 — Duplicate form rows merge unattended; conflicting completion dates do not

`FormDuplicateRepair` collapses duplicate rows automatically, at startup, with no dry run
and no typed-back confirmation — deliberately unlike `FormBulkCompletion` and
`FormDueDateBackfill`, which demand both.

The difference is what the operation can destroy. Those two **invent** data: a completion
date the record never held. This one invents nothing. It merges the union of what the
copies already assert and deletes rows asserting nothing the survivor does not. The
survivor is chosen as the copy already carrying the most state — a completion date first,
then compliance, then lowest Id — so no merged row is ever *constructed*, only kept. That
is what makes it safe to run with nobody watching, and it is the only reason it is.

**A group holding two or more different completion dates is not merged at all.**
`CompletedDate` is date-keyed into `BillingComplianceGate.IsBillingWindowBlocked`, so
picking a survivor decides whether service dates between the two candidates were billable.
That is a billing determination, not a mechanical one. Those groups are reported and left
untouched, the index migration then refuses with a message naming them, and startup stops
until a person resolves them. Stopping is correct: the alternative is guessing at a
billing fact on real client records.

Note what is deliberately *not* a conflict — some copies holding a date and the rest
holding none. That is the ordinary shape, where one copy was edited and the others are
untouched generation defaults, and the union contains exactly one completion fact.

**Ordering is forced, not chosen.** The repair runs after the pre-migration backup, so the
prior state is recoverable, and before `MigrateAsync`, because the index cannot bind while
duplicates exist. That leaves exactly one correct position in `LocalDatabaseUpdater`, and
it is asserted rather than trusted.

**Rejected: running the repair as a Settings maintenance action instead.** It was the first
plan, on the grounds that deleting billing-relevant rows deserves a human present. The
sequencing killed it: the migration is in the chain, the chain applies at startup, and a
failed migration shuts the app down before the login window — so a repair reachable only
from Settings would be unreachable exactly when it was needed. The evidence requirement is
met instead by an `AuditEvent` per removed row, recorded under `ActorUserId = 0` because no
one is signed in when it runs.

## 2026-09-01 — Compliance is the completion date, and nothing else

`Form.IsCompliant` is now `CompletedDate.HasValue`. The stored column is gone
(`AddDerivedFormCompliance`), and so is the `isCompliant` constructor parameter: a
caller that believes a document is in force must say since when.

The column was a second field for a fact the date already held, kept in step by
convention. Convention lost. 147 rows in `SatiProduction` held the flag set with no
date, and because every screen read the flag while `BillingComplianceGate` reads only
the date, those rows rendered complete and blocked billing simultaneously — the same
symptom as the duplicate rows, from an unrelated cause, and equally impossible to see
from the screen. Person 1044's Comprehensive Assessment is one of them.

**The backfill picks the date the code already believed in.** Those rows came from
`AddMissingFormsForCycle`, which created current-cycle annual documents flagged
compliant on the reasoning that the cycle had started, therefore the documents were
signed. That reasoning is sound and its date is knowable: the cycle start — precisely
what the sibling path `GenerateFormList` was already stamping. The migration completes
an assertion recorded inconsistently rather than inventing a new one, writes a
`form.compliance-date-backfilled` audit event per row, and `Person.InForceSince` is now
the single owner of the rule both paths express.

**Reviews are never backfilled.** A quarterly review is an attestation that work
happened; no date can be inferred for work nobody recorded. Neither is a cycle that has
not started, nor a person with no effective date — inventing one there is how this
class of bug began.

**Consequence — a second question needed its own name.** "A completion is recorded" and
"this document is in force as of today" are different, and they differ exactly when a
completion date has not arrived yet. `IsCompliant` answers the first; the new
`Form.IsSatisfiedAsOf(date)` answers the second, using the same predicate as
`BillingComplianceGate.IsIncompleteAndOverdue`. Every reader whose answer depends on
today — the caseload matrix, `UpcomingEvents`, task rows, `GetComplianceStatus` — now
asks the second, so a screen can no longer call a form complete while the gate blocks
on it. Checkbox bindings keep asking the first, which is what a checkbox means.

**Rejected: making the gate treat a future completion date as satisfied.** It would have
collapsed the two questions into one and removed the need for `IsSatisfiedAsOf`, but the
gate's answer is the correct one — a document completed on a date that has not arrived
is not in force — and it is date-keyed into historical billing. Changing it would have
made past service dates billable on the strength of a future date.

## 2026-09-01 — Cycle form generation is on again, because the constraint now holds

`PersonService.GetAllPeopleAsync` generates missing cycle forms on load again;
`EnableEnsureCycleFormsOnLoad` is gone.

It was switched off in `57af6fa` because it raced: the membership check reads the
person's own `Forms`, concurrent loads both passed it, and both inserted. Switching it
off stopped the duplication and left a quieter problem in its place — **nothing else
generates forms for an ongoing caseload.** Clients only still had records because the
racing runs had pre-created the current *and* next cycle before the flag went off.
Those run out through 2027–2028, after which compliance records would simply stop
appearing, with no error and no empty state to notice.

Two fixes made re-enabling safe rather than merely tempting: `IX_Forms_PersonId_Type_DueDate`
decides the race in the database, and `GetAllPeopleAsync` treats losing it as a re-read
instead of an exception. The generator is idempotent and its check is in-memory over
already-loaded forms, so a caseload with nothing missing costs one comparison and no
write.

**The guard was never the fix.** It suppressed a symptom of an unenforced invariant, and
kept suppressing it after the invariant acquired an owner. A feature flag holding back a
race is a reminder to go fix the race.

## 2026-09-01 — Every cycle gets forms, and only the current one is assumed satisfied

`EnsureCurrentCycleForms` now generates a form set for every cycle from the effective
date through the cycle after the current one, rather than only the current-and-next
pair. `Person.InForceSince` marks a cycle's annual documents satisfied only when that
cycle is the one containing today.

Those two changes are one decision, because either alone is wrong.

**Generating every cycle** closes a hole where a backdated admission had no forms at all
for the intervening years. A form that was never created cannot be enforced —
`BillingComplianceGate` iterates the rows that exist and has nothing to fail — so an
entire year silently carried no compliance requirements. Absent is not the same as
satisfied, and only the row makes the difference visible.

**Restricting the in-force assumption to the current cycle** is what keeps that
generation honest. The previous rule marked any already-started cycle satisfied from its
start date, which was harmless while only the current cycle was ever generated. Applied
across a client's whole tenure it would have asserted compliance nobody attested, for
every historical year at once — a far worse defect than the gap it was closing, and the
same mistake in kind as the 147 dateless-compliant rows.

Sati has no record of whether a closed year's documents were renewed. A later cycle
beginning proves nothing: cycles turn over on the anniversary date, not because anything
was signed. So a closed cycle is generated outstanding, an unknown reads as unknown, and
a real historical gap surfaces as an open document instead of an invented completion
date. This follows the precedent already set for quarterly reviews — do not bulk-close
and do not invent dates, accept an honest backlog.

**Consequence — a backdated admission produces open documents on purpose.** The creation
dialog is where a case manager records what actually happened for those years; nothing
else should guess.

**Bounded at 25 cycles**, dropping the oldest end so the current and next cycles are
always present and what remains is a contiguous run. A mistyped effective date decades
back would otherwise generate hundreds of rows per client. The per-cycle existence check
also became one pass over `Forms` instead of one per (cycle, type), since this now runs
across a whole tenure on every caseload load.

## 2026-09-01 — 1080p and smaller get compact starting state, not a globally shrunken UI

Sati decides compact display mode once when `ShellWindow` obtains its native handle. The monitor
hosting that window is compact when either physical dimension is at or below 1920 × 1080. Exactly
at the boundary the change is silent and starts the Clients workspace with its horizontal compact
selector. Below the boundary, a once-per-run notice names the detected resolution, explains the
adjustments, and recommends 1080p or higher.

Compact mode tightens shell and Clients spacing, narrows the consumer-record rail, condenses the top
navigation, and relies on automatic scrollbars wherever content still overflows. Sub-1080p mode also
collapses Today's Work. The existing chevrons remain active, so the mode does not prevent a user from
reopening either optional panel. Layout rounding and display-optimized ClearType are enabled on the
shell to reduce fractional-pixel softness without changing font size.

**Rejected: applying a global `ScaleTransform` to the application.** It would make more pixels fit,
but would do so by shrinking text, focus indicators, and click targets together. That is an
accessibility regression and conflicts with Windows' own DPI scaling. Responsive starting state and
overflow preserve reachability without making the interface harder to read or operate.

## 2026-09-02 — Existing-profile Credible import is explicit, reviewed, and single-record

An agency Admin may enable `Settings.AllowCredibleProfileUpdates`, which defaults false. The option
exposes the existing Credible review action while one consumer is deliberately open for editing.
Accepted mapped fields fill that edit form and the ordinary Save changes action remains the only
writer. Missing and declined fields do not clear existing information, Sati-only fields are not
mapped, and a different nonblank Credible client id refuses the import before changing the form.

The option does not make bulk folder import update matches. A reviewed single record has a visible
target and a second explicit save; silently replacing hundreds of clinical profiles has neither
property and requires a separate design, authorization, audit, and recovery decision.

## 2026-09-02 — The VR assistant title is agency reference text, not consumer data

A consumer who is open with Vocational Rehabilitation may record two assigned names: the
Vocational Rehabilitation Counselor and the counselor's assistant. Those names belong to the
consumer profile and follow the same validation, concurrency, audit, and immutable-version path as
other demographic fields. Turning off `OpenWithVR` hides the fields but does not destroy them.

The assistant role's wording changes independently of the person assigned to it, so the displayed
title lives once in agency Settings as `VrAssistantTitle`, with `VSA` as the compatibility default.
Changing that title updates the label across consumer profiles and does not rewrite every consumer
row or create misleading person-history entries.

## 2026-09-02 — Easy Eyes is a personal opt-in presentation mode

Easy Eyes belongs to the signed-in worker, Windows profile, and selected environment rather than
agency Settings. It defaults off. One worker can therefore enlarge and simplify their interface
without changing every colleague's display, and no clinical, operational, or tenant-owned record
is created for an accessibility preference.

The mode deliberately applies a 1.3 layout scale to the main working surface. This differs from the
compact-display decision above: automatic compact mode must never shrink the interface merely to
fit a monitor, while Easy Eyes is an explicit request to make text, focus indicators, and click
targets larger together. It also hides only the displayed Narrative columns in the two note grids
and forces the existing horizontal Clients selector; underlying note content and the user's normal
compact-selector choice are preserved when the mode is switched off.

---

## The inactivity screen is a privacy screen, not a lock (2026-09-03)

Sati blurs its own window after a configurable idle period and clears on any input. It was
tempting to call this a lock, because it looks like one.

It is not one, and saying so would be the dangerous part. It does not lock Windows, holds no
credential, and anyone at the keyboard clears it by moving the mouse. In a setting where the real
risk is PHI readable across a room, hiding the screen is genuinely useful; implying the machine is
secured would invite someone to walk away from an unlocked workstation. So the overlay itself, the
Settings help text, and the release notes all state plainly that it does not lock Windows.

**Rejected:** a PIN in this release. A PIN that can be bypassed by killing the process is
security theater, and doing it properly means a credential store, lockout policy, and a recovery
path that a case manager can use at 7pm with a client waiting. That is its own piece of work.
What did ship is the seam: `TryDismiss` is the single exit from the overlay and
`RequiresUnlockChallenge` is the flag that gates it. Every waking path already routes through that
one method, so adding the challenge later is a change in one place rather than an audit of
callers.

**Consequence:** the waking keystroke or click is consumed rather than delivered. That is required
for a future PIN, and it is right today too — the click that brings Sati back should not also press
a button on a screen the user could not read.

## The waking mouse-move has to be a real move (2026-09-03)

Showing the overlay changes what sits under the cursor, and WPF raises a `MouseMove` for that
alone. The first implementation therefore woke itself the instant it appeared. Bare mouse moves now
count as activity only when the pointer actually travelled, which is why `ShellWindow` tracks the
last pointer position rather than trusting the event.

## Button fill became its own palette token (2026-09-03)

`PrimaryButton` filled itself with `AccentBrush`, the same brush that paints accent text. Making
the orange palettes' buttons lighter would have lightened their accent type too.

Every theme now supplies `AccentButtonBrush` and its hover, pressed, and foreground partners. In
thirteen themes those are copies of the accent values, so nothing moved. In Blue-Gray Pearl and
Cedar Grove the button fill is a much lighter orange of the same hue, paired with dark text at
roughly 7.9:1 contrast.

**Rejected:** defining fallbacks in `App.xaml`. Theme dictionaries are merged and swapped whole,
and a resource defined directly on the application's own dictionary wins over the merged theme, so
a fallback there would have made themes unable to override it. Every theme carries the keys
instead, and a structure test enforces that.

## The case note template never removes text (2026-09-03)

Build Case Note Template writes the ticked meeting facts above the case manager's own words and
moves those words below a Meeting Narrative header.

Pressing it twice therefore stacks two templates rather than replacing the first. That was the
deliberate choice. Replacing would mean deciding that everything above the header is machine
output and safe to discard — but a case manager who edited a generated line would lose that edit,
silently, in a clinical record. Visible duplication that the user can delete is a better failure
than invisible deletion. A test pins the behavior so it is not "fixed" by accident.

The template renders `CaseNoteFactCompiler.VisitFacts` rather than phrasing the checkboxes itself,
so the template and the local-AI draft cannot describe the same selection two different ways.

## The follow-up suggestion asks a different question than the dashboard (2026-09-03)

The suggested-follow-up row under the note narrative had been built on
`UpcomingEventService.GenerateEvents`, which reports only forms inside their open/late window. With
the default zero-day review window, a quarterly review is "open" on exactly its due date, so for a
client whose coverage started recently the row was blank for months. It was reported as a feature
that never appeared, and it effectively never did.

The dashboard question is "what is actionable right now". The note panel's question is "what is
coming up next for this client". `NextFormSuggestion` answers the second, ignoring the window and
falling back only when `GenerateEvents` has nothing. Both read the same form table, the same
`GetCurrentCycleForm`, and the same `IsSatisfiedAsOf`, so neither can name a form the compliance
gate considers satisfied.

The existing tests missed this because they drove the panel with a stub event service. They proved
the panel reacted; nothing proved the real generator ever produced anything.

## The API package must be zipped by .NET 10, not by Windows PowerShell (2026-09-03)

`ZipFile.CreateFromDirectory` running under Windows PowerShell 5.1 writes entry names with
backslashes, because it is the .NET Framework implementation. App Service extracts those literally,
producing a file named `App_Data\jobs\triggered\...` instead of the nested path, which silently
costs the package its WebJob. The 1.2.41 package was built that way once, caught by the entry
inspection, and discarded before it reached Azure.

The packaging step now runs under .NET 10 and normalizes every entry name to a forward slash, and
the backslash count is asserted rather than assumed. That check was already in the release
evidence for a reason; this is the failure it was written for.
# 2026-09-03 — Safety plans require supervisor approval before final status

Sati uses one shared seven-section safety-plan structure. The assigned case manager may draft and submit it; a non-author supervisor with actual caseload access must approve it before it is final or satisfies the annual Safety Plan prerequisite. Agency equality alone does not grant review access. Return reasons and plan narrative are retained in the plan record and are not copied into audit metadata.

## 2026-09-03 — Annual packet, exact-copy receipts, and staff-sent requests

Josh confirmed: medical-records requests are **download only; staff send them**, addressed to the
current linked primary-care provider and included only after the cycle's medical release is
attested. Missing address/phone inherit from the provider's organization chain; no fax or delivery
integration was added. Staff must verify recipient, requested scope/date range and authorization.

Packet generation uses the existing metadata-only artifact policy. A completed or external release
cannot be reconstructed into its exact signed/saved bytes from a hash, so those documents are
omitted explicitly in the manifest with retrieval instructions; they are not replaced by blank
drafts. The packet generates identity-only drafts when no completed release is recorded. The safety
plan is rendered from saved structured content and its source id/version are recorded. Downloading
a PDF/ZIP never attests a form. No draft-release request store or sending job was introduced.

Each new privacy PDF is a new artifact. Receipt or documented good-faith effort references that
exact artifact, so generating it again requires another acknowledgment; previous receipts remain
historical evidence. The verifier compares both SHA-256 and byte count with any accessible recorded
artifact, including superseded history, without uploading file contents.

The packet-opening window is its own agency setting (30 days by default, 0–180 supported), not a
derivation from per-form due/open offsets. Anniversary calculation uses the original enrollment
date to preserve February 29 across leap years. Packet prerequisite reads and artifact replacement
share one serializable transaction. Reminders are read-time UI state, not persisted notifications.

## 2026-09-03 — Explicit API writes are not automatically replayed

The release-prep tests reproduced HTTP 500 on packet generation when SQLite used EF's same retry/
transaction guard as deployed SQL Server. `SingleAttemptWriteFilter` establishes an execution scope
before protected write endpoints begin explicit transactions. It deliberately makes one attempt:
an ambiguous commit must not silently create another receipt/artifact/audit event. Read-only
endpoints retain retry behavior. This does not implement client idempotency keys or promise that a
failed response means no write committed; clients should reload before retrying. The factory now
uses a retry-shaped strategy in every API integration test so the mismatch cannot hide again.

## 2026-09-03 — Ordinary-client deletion within a 20-day window, and a real (narrow) legal-hold registry

A workflow demo surfaced two gaps: a batch import created duplicate consumers with no way to
merge or remove one, and the "delete a consumer created in error" command Josh remembered
designing turned out to be authorized (`HANDOFF_CLIENT_DELETION_POLICY.md`, 2026-08-31) but never
built. This entry records what shipped, which extends and **supersedes** the test-consumer
decision's sentence that the marker-and-attestation command "does not create an ordinary-client
deletion policy" — it now does, bounded by time rather than by a creation-time marker.

**The window is 20 days, not the 14 the handoff doc specified.** Josh widened it when asked
directly during implementation; `ConsumerDeletionRules.DeletionWindowDays` and every reference in
the handoff doc were updated together so the two do not disagree.

**Rule 3 (deletion) and Rule 4 (archive) are both built**, exactly as HANDOFF_CLIENT_DELETION_POLICY.md
designed: `Person.CreatedAtUtc` (immutable, set once at creation — `private set`, not merely a
runtime check, so an edit-built `Person` cannot carry it at all) and `Person.Status`
(`Active`/`NoLongerServed`/`Deceased`/Admin-only `Ghost`) gate the two commands, and both are
excluded from `GetAllPeopleAsync` and everything downstream of it once archived. `ConsumerDeletionRules`
owns the window and the A1 billing-integrity predicate (a pure function over a counts record, per
the handoff doc's own requirement) — draft and synthetic billing artifacts remain deletable inside
the window; only billing that reached a payer blocks. The deletion command's audit event
(`consumer.deleted-in-window`) is an itemized tombstone — id, date, and type per note, claim line,
form, review item, assessment, AT request, contact, and `PersonVersion` — with narrative, name,
MaineCareId, birth date, and address deliberately excluded; a test asserts sentinel PHI values
never reach the audit metadata.

**The legal-hold registry is real, not the interim always-`Unavailable` stub the handoff doc
specified**, per Josh's explicit direction: an unreleased row in a new `LegalHold` table blocks
deletion; any query failure still maps to `Unavailable`, never `Clear`, so the fail-closed
property the handoff doc required is unchanged — only the "always fail closed because nothing is
implemented yet" stub was replaced with "fail closed by construction, backed by a real table."

**Three deliberate, tracked narrowings, not oversights:**

- The registry is scoped to gating this one command. It is not `OPERATIONS.md`'s general
  record-class/scope hold model and does not by itself satisfy that gate for any other retention
  or purge job.
- Hold release is single-admin. `OPERATIONS.md`'s legal-hold gate requires a second approver for
  release; building that now would have meant either delaying a capability Josh needed immediately
  or shipping dual control without the UI/workflow to use it well. AGENDA.md tracks it as follow-up
  work, not a silently accepted gap.
- The deletion tombstone's required Admin reason is an intentional, narrow exception to
  `AUDIT_EVENTS.md`'s own rule against copying free-text reasons into `MetadataJson` — caught during
  the docs pass when the same bug turned up (and was fixed) in legal-hold placement, where the
  reason durably survives on the `LegalHold` row instead. A deleted Person leaves no row for it to
  survive on; dropping it would leave an irreversible action with no recorded justification at all.
  `AUDIT_EVENTS.md` carries the full rationale and the mitigation (an on-screen warning against
  typing identifying detail into the field, not server-side enforcement).

**Rejected:** shipping strictly to the handoff doc's original interim stub. An Admin would have
had the full window/billing/archive machinery built and still been unable to delete anything,
which does not solve the problem the demo surfaced. Building a real registry — deliberately
narrow, explicitly short of the dual-control requirement, both documented rather than assumed —
was judged the better trade for an internal compliance control with one caller today.

## 2026-09-05 - Case Management navigation

Case Management opens directly onto its feature tabs: Overview, Clients, Notes, Caseload Matrix,
Calendar, Statistics, Reviews, Providers, Help, and Documents. The redundant Dashboard navigation
row is removed. Help offers Guidance and Reference in a sidebar; Documents offers AT Requests,
Authorized Rep, and Releases. These remain doorways into the existing view models and document
workspaces. Top-level Case Management, Supervision, and Billing navigation is unchanged.

## 2026-09-05 - Client save outcome and screen refresh

The client Submit flow marks persistence as confirmed immediately after AddPersonAsync or
EditPersonAsync returns. Later collection, selection, and screen-refresh exceptions must report
that the save succeeded and the screen needs refreshing. They cannot be classified as unknown
save outcomes or rolled-back database transactions. Unconfirmed edits use edit-specific wording
that applies to both LocalDB and cloud sessions.

## 2026-09-05 - Paged supervisory review and explicit threshold approval

Pending Approvals loads 10 logged notes at a time, split into compliant and held sections after
bounded database retrieval. Stable increasing note IDs and a fixed maximum ID prevent approving
an earlier page from skipping later rows, and keep a running batch from chasing newly added notes.
The selected case-manager filter is applied within the authorized database query. Both LocalDB and
the API retain the legacy unpaged reads for compatibility; this screen no longer calls them.

Approval runs only when the supervisor clicks "Approve all within threshold". Default maximum:
4 units per note, inclusive. Changing the field, opening the page, and scrolling never approve.
The batch walks the current filter, including unloaded notes, with one ordinary approval transaction
per note. Successful approvals retain actor, timestamp, revision and audit; audit metadata also
records the chosen threshold and batch origin. It is deliberately a partial-success operation:
invalid/noncompliant/stale notes remain; unexpected errors stop the batch and report confirmed
counts plus the possible unconfirmed final request. Leaving the page stops further requests.

`NoteReviewRules` owns automatic-approval eligibility: positive duration, supported service-note
type (not Reminder), nonblank bounded narrative, nonfuture service date, inclusive rounded-unit
limit, and valid optional time window. The persistence boundary rechecks it together with existing
reviewer scope, logged status, revision and compliance, and checks service-time conflicts. These
are structured checks; they do not assess clinical narrative quality. No compliance override is
introduced. Existing manual approval and override workflows retain their established behavior.

## 2026-09-05 - Adaptive display modes with a central Work Agenda

Design and acceptance reference: `DISPLAY_MODES_DESIGN.md`. Josh explicitly chose Work Agenda as
the default center workspace.

Use available window layout space, including the effect of Windows scaling and Easy Eyes, to
choose Wide, Balanced or Compact presentation. Compact falls back to one selected workspace
when two comfortable panes cannot fit. Supporting features move into labeled selectors rather
than becoming inaccessible. Work Agenda is the initial central workspace; an explicit Focus note
action expands the same current note without replacing its state or changing its permissions.

Keep the one-switch Easy Eyes experience and existing 1.3 enlargement for this implementation.
Adaptive allocation supplies the missing room. Missing center preferences default to Agenda;
explicit saved choices remain respected. All preferences remain local personal presentation state.

The implementation supersedes the physical-resolution startup policy. `OverviewLayoutPolicy` uses
the effective Overview viewport with 1080/1440/2100 width thresholds, an 840 height threshold, and a
48-unit growth margin. The view places stable live hosts into Wide, Balanced, two-pane Compact, or
one-pane Compact arrangements. Supporting work moves through a labeled Workspace selector, and
Focus note uses the same note and Work Agenda controls.

`ShellWindow` owns exactly one live `ScratchpadView` and moves it between Overview and the shell-side
host. `NoteEntryView` changes the visibility of its existing detail and writing rows for short
windows, preserving draft and editor state. Notes, Forms, and Deadlines now identify unselected,
loading, failed, and successful-empty states where their existing load boundary can establish the
difference. No persistence, permissions, billing, or clinical rules changed.

Rejected: retaining the startup monitor dialog alongside responsive layout. Physical resolution
does not describe usable WPF space after window resizing, DPI scaling, or Easy Eyes, and two active
mode engines could contradict each other. `DisplayLayoutService` and `DisplayAdjustmentDialog` were
removed. Navigation strips retain bounded horizontal overflow at extreme widths; replacing that
fallback with a selector remains a separate presentation refinement.

## 2026-09-05 - Fixed Overview roles and a bounded Statistics report

This decision supersedes the workspace-selector, Focus note, Forms-summary, duplicate Notes-panel,
and center-preference portions of the adaptive-display decision above. At normal desktop widths,
Overview has fixed roles: Current note on the left, Work Agenda in the center, Upcoming Due Dates on
the right, and Productivity below Work Agenda when height permits. Below 1080 effective units the
three primary panels stack. Easy Eyes remains the one user-facing enlargement switch. The obsolete
center preference and its local preference service are removed; an old preference file may remain
on a workstation but is no longer read or changed.

The compact note header no longer repeats the selected client's name. It reports the nearest
outstanding form as upcoming, ready to open, open, or overdue, including the relevant opening,
opened, and due dates. The ordinary client picker remains the source of selected-client identity.
Full note history, form work, reviews, and detailed productivity remain available in their existing
Case Management destinations.

Statistics no longer retrieves full yearly note entities to calculate a date-bounded monthly sum.
`IProductivityReportService` projects only EventDate and Minutes for the signed-in worker and returns
monthly units. Demo uses an authenticated API route whose scope comes only from the validated actor;
the request cannot name another user. Local Production derives the same scope from the session.
The route response has no narrative or person fields. Independent report reads begin together, the
Statistics view opens before they finish, and only the latest date-window request may publish data.
The established Logged + Approved unit rule and all incentive and billing-loss calculations remain
unchanged.

## 2026-09-05 - Today's Work uses Scheduled notes as its structured plan

Today's Work keeps its dated free-text Scratchpad and adds a structured view over today's Scheduled
notes. Note type determines the visible group: Form is Paperwork, Visit is Visits, Phone and legacy
Contact are Calls, Email is Emails, and remaining types are Freeform. Every structured row therefore
already has the client, type, status, date, revision, tenant scope, authorization checks, and audit
behavior the note lifecycle supplies. A second task table would duplicate those facts and create a
new synchronization problem, so none is introduced.

The sign-in agenda turns each selected form into a Scheduled Form note for today with a 15-minute
editable estimate and the exact form type. It leaves the scratchpad text untouched and recognizes an
exact prior insert when a partial save is retried. Scheduled notes are no longer offered by the
sign-in recommendation list because they appear automatically on their due day.

Starting an item prepares the same note row as an unsaved Pending draft in the adjacent current-note
panel. It uses today's date, preserves the client and specific type, brackets the planned text as a
clear replacement prompt, and defaults the start to the first five-minute-grid opening that fits its
minutes. The row changes in persistence only when Save succeeds. Canceling or navigating away after
the ordinary discard prompt leaves the stored plan Scheduled, and a successful save cannot create a
duplicate note.

Future service notes retain their chosen type, optional form type, and estimated minutes. The shared
`NoteSchedulingPolicy` fixes their status at Scheduled and clears actual start time, justification,
and completed-visit facts. This resolves the earlier soft-reservation question for future plans:
estimated duration is useful planning data, while a service start becomes real when work begins.
Explicit Reminders keep their separate non-service behavior.

`Phone` and `Email` are appended enum values; existing integer values are unchanged and the legacy
`Contact` value remains readable. New-entry controls no longer offer ambiguous Contact. When an old
Scheduled Contact is started it defaults to Phone, the more common historical case, and remains
editable before save because old narratives cannot be classified reliably after the fact.

## Team chat — safer defaults after design review (2026-09-05)

The user authorized implementing Claude's handoff with the safest practical corrections.
`TEAM_CHAT_REVIEW.md` maps defects to corrections. Membership is explicit; Admin manages it, but
reading requires membership plus existing consumer access for consumer-scoped rooms. No automatic
agency room, independent moderator role or historical disclosure on joining is introduced. General
coordination has a no-client-details policy, not a claim of reliable free-text classification.

The original five-minute client-read audit loses final reads and cannot prove human reading.
Record exact server body releases durably before returning them; seen markers serve unread UI only.
Original messages and later redactions are immutable. Client merge uses server message IDs;
retry identity is scoped to room and author and cannot be reused for a different body.

Timestamp overlaps and identity watermarks cannot prove recovery across late commits and old
redactions. A concurrency-checked room revision orders all changes. Contentless WebSocket notices
prompt the audited HTTP read and are not required for correctness, avoiding a second PHI path.

Chat is disabled unless explicitly enabled in the validated Demo/testing environment. There is no
Local Production chat or real-data activation. Matching existing encryption is not a risk
assessment; no legal-compliance claim is made. No automatic deletion runs pending the schedule and
complete preservation, discovery/export, backup, account and operational work in `TEAM_CHAT_GUIDE.md`.

Room and page responses identify each membership episode. A rejoin invalidates earlier local text
and pending work even if the client missed the intervening removal. The authorized consumer's name
and record identifier distinguish client discussions from merely user-chosen room names. Background
chat traffic cannot renew an idle session. Room-detail edits retain the version originally shown;
periodic refresh cannot silently make old edits current. Consumer deletion refuses retained chat,
and migration rollback refuses populated rooms instead of erasing evidence.

## 2026-09-06 — Electronic signatures preserve originals and signer-specific evidence

The signature handoff is implemented as an opt-in synthetic feature. The environment gate allows
only the validated Demo/Testing pair, and staff issuance additionally requires a consumer marked
as test data at creation. All live legal-clearance claims in the original proposal are superseded
by `SIGNATURE_PORTAL_REVIEW.md`. Safety-plan and state DHHS signing remain blocked pending their
program decisions; the medical-records request letter has no consumer signature.

The exact previously generated PDF must match the current complete artifact's hash and byte count.
No supervisor may bypass missing fields. Its frozen bytes remain separately retained; the derived
signed copy and paginated evidence certificate have their own immutable package and hash. Signing
does not fill the existing unique artifact slot, supersede the original, attest ordinary form
completion, satisfy every team signer, or imply billing approval.

Each request freezes one signer, capacity, reviewed authority basis, delivery address, disclosure
and intent. Current name/address snapshots are verified when staff confirms issuance or replacement.
A new request gets its own 8–12 digit code protected by PBKDF2 and a wrapped random per-request HMAC
key. The portal may unwrap only the PIN key, never the separate invitation outbox key. Consent is
the signer's explicit per-session act after authentication and actual document access. No raw code,
working invitation, IP address or user-agent description is retained as signature evidence.

The public portal has a separate deployment/identity, narrow canonical context, explicit SQL role,
private container read access, secure request-bound session cookies and CSRF protection. SQL locks
serialize code checks before deriving a candidate; revisions guard workflow changes. Five failed
codes lock durably. Replacement requires a different code and closes old links and sessions.
The code must still match an existing request on idempotent replay; a new code cannot silently
change what a previous uncertain submission established. Logout invalidates server sessions.

A committed signing decision precedes recoverable package preparation and notification processing.
Workers are separately opt-in. Durable leases and provider operation IDs prevent automatic repeat
email submission after an uncertain response. Provider success is not claimed as inbox delivery.
After signing, the invitation's `/s/` access is consumed; `/r/` plus the same code can create a
receipt-only session within the original expiry. Later free/accessible copies use the agency's
reviewed staff process. Authorization withdrawal is separate from withdrawal of electronic consent
and never rewrites signed history. No automatic purge is introduced.

Relevant signer identity/contact changes stop unfinished requests and old external receipt access
within the same authorized edit transaction. The signed outcome, staff copies and already-submitted
mail facts remain intact; access revocation is distinct from withdrawing a medical authorization.
Every portal action and download binds to its displayed session as well as its secure cookie,
preventing one browser tab from silently acting on another tab's document. Deadline checks repeat
after awaited work before evidence or disclosure is committed.

`SIGNATURE_PORTAL_GUIDE.md`, `Sati.Portal/README.md`, and `SIGNATURE_PORTAL_VALIDATION.md` distinguish
completed local work from deployment, legal, program, accessibility and operations prerequisites.

## 2026-09-06 — Team chat is a room dock with open rooms as tabs

The chat workspace was rebuilt as presentation only. No service, contract, authorization, audit or
concurrency behaviour changed, and no rule moved out of `Sati.Contracts.V1.ChatAccess`.

Rooms live in a collapsible dock on the left. Choosing one opens it as a tab, and each tab holds
that room's pane: the transcript under two subordinate tabs for the latest messages and older
history, then the composer. Collapsing the dock leaves a rail carrying the toggle and the unread
room count, so a hidden list still says there is something to read. The earlier `GridSplitter`
sidebar, its separate below-800px layout, and the room drop-down that briefly replaced both are all
gone; code-behind now sizes only the workspace floor.

Tabs are a way back to a room, not several live conversations. The view model keeps one selection
and one loaded transcript, so every existing guarantee about cursors, membership episodes and
snapshot boundaries continues to hold unchanged; the per-room draft is what makes returning to a
tab feel continuous. Opening a tab and selecting a room are the same act, routed through
`SelectedRoom`.

`OpenRooms` holds the same instances as `Rooms`, and `ForgetRoom`/`ForgetAllRooms` are the only way
either loses one. A room the account can no longer reach leaves the tabs, the room list, the saved
draft and any pending send together. Before this there were four hand-written removal sites —
access withdrawn on refresh, chat disabled, a 403 or 404 from the server, and sign-out — and a tab
strip added on top of them would have kept showing a transcript the server had already refused.

The transcript's subordinate tabs run the history commands rather than deciding what is displayed.
A tab that cannot act snaps back to the loaded view and writes the reason to the status line, so a
header can never claim a page the view model has not loaded. History remains read-only, and the
composer stays disabled while it is shown.

Every message reads from the same left margin. Alternating sides wasted the middle of a wide pane
and made long bodies wrap early, so authorship is carried by the accent fill and the written-out
author name instead of by position.

A filled surface in this view is painted with `AccentBrush` and lettered with `OnAccentBrush`, and
with nothing else. Those two keys invert together across the themes — the accent is dark on
`SunlitShell` and light on `MidnightOpal` — so the pair is the one combination legible on all
fifteen. Every state also carries a word: unread shows a count, archived says "Archived", a hidden
message says it is hidden and stays visually drab. Selection in the dock, the tab strips and the
transcript is drawn with an accent rule and font weight rather than a filled block, following the
rule already documented on `NavTabButton`.

`ChatMessageItem` is a value record, so `IsOwnMessage` and `StartsGroup` are fixed when the item is
constructed and cannot be recomputed by the view. Every write to the message list therefore passes
the item the new one will sit under, and trimming the head re-renders the survivor so the transcript
always opens on a complete group. `ChatMessageItem.StartsNewGroup` is the one owner of when a post
repeats its author: the user chose grouping by a single author within five minutes, with a hidden
message standing alone on both sides because its body carries its own redaction date and the posts
around it must not appear to continue it. Elapsed time is compared as an absolute duration, so a
clock that runs backwards between two posts breaks the group rather than folding a later message
into an earlier one. Authorship compares the server-supplied author id against the signed-in account
and is never taken from anything else the row carries. `AccessibleName` still carries author and
time for every row, so folding a byline into the group above never hides authorship from a screen
reader.

Redaction moved out of a permanently visible expander. It appears in the transcript only once a
supervisor or administrator has selected a message, and still states that the original is retained.
Membership and room administration moved behind a "Room details" disclosure so they stop competing
with the transcript for vertical space. Browsing history and having a message selected are
independent states, so the two occupy separate rows and can never overlap.

The composer and the transcript now live inside the selected tab's template and are rebuilt when
the tab changes, so code-behind can no longer hold them as fields. It locates them by the same
automation names assistive technology uses, which keeps one set of names authoritative for both.

## 2026-09-06 — Account switching is an opaque two-phase privacy boundary

The shell raises one opaque, input-blocking shield before either replacement-credential dialog is
displayed. The outgoing account remains intact underneath only long enough to save its scratchpads
and selected-client journal or to cancel safely. Once a replacement account authenticates, every
shell-lifetime clinical, supervisory, billing, administrator, scratchpad and chat surface is cleared
synchronously before the session identity changes. The replacement workspace loads behind the
shield, and the shell reveals it only after role navigation completes.

Clearing visible collections is not sufficient because an earlier asynchronous request can finish
later. User-scoped singleton view models therefore invalidate outstanding request identities and
compare the current session-user instance before publishing data, errors, or busy state. Account
switch tests hold an old request open across the identity change and prove that it cannot publish.

**Rejected:** navigating first and letting each page eventually clear itself; changing the session
before clearing; a translucent overlay that still exposes names; and relying on API authorization
alone to correct an on-screen confidentiality leak.

## 2026-09-06 — Demo mock submission consumes the retained generated 837P

The Demo billing workspace may simulate a clearinghouse response only after this desktop session
has generated a test 837P for the selected range. The API finds that retained immutable generation,
records a synthetic `Transmitted` event referencing its filename, and feeds the selected synthetic
999, 277CA and 835 documents through the ordinary ingestion path. One generation may be submitted
once; another scenario requires another explicit test generation.

The simulator remains absent in effect on Production and cannot accept a production-mode file. It
is workflow scaffolding, not transport, X12 certification, payer acceptance, or evidence of a real
payment. Regenerating a merely similar file inside the submission endpoint was rejected because it
would not prove that the acknowledged content was the content the user generated and chose to send.

## 2026-09-06 — Billing-period selection is a draft work queue with exact-row readiness

The primary Billing Period selector contains only claim-bearing draft periods. Submitting and
locking a period removes it from that selector because no further action can be taken on it there;
the immutable period remains available in the 837 generation range and Submission Home for its
later exchange and response workflow. Hiding locked periods everywhere was rejected because it
would make the next billing stage inaccessible.

The area beside the selector previews every exact frozen claim row and its readiness errors. A
single `Sati.Contracts.V1.ProfessionalClaimReadiness` rule owns those checks for both desktop-local
and API flows. It runs immediately after a row is constructed and again for loading, locking, and
837P generation. Candidate-note validation remains an earlier gate, but it cannot prove that the
subsequently constructed financial snapshot is valid. The exact-row gate closes that gap and also
makes malformed legacy or synthetic rows visible before the biller presses Submit & Lock.

**Rejected:** treating UI visibility as the gate; duplicating preview and generator rules; silently
dropping invalid rows; and leaving submitted periods in the draft selector merely to support later
837 generation.

## 2026-09-06 — Installer implementation consoles stay hidden behind one progress surface

PowerShell remains an internal installer implementation detail for now, but no installation or
registered uninstall entry launches it through a visible console. The Demo self-extractor calls a
small path-restricted Windows Script Host bridge with window style zero. The Local bootstrap uses a
console-free child process with both `CreateNoWindow` and hidden-window arguments. Both install
scripts display the same modeless Sati-branded indeterminate progress window from an STA runspace,
which remains responsive while file, shortcut, and prerequisite work continues.

The progress surface cannot cancel midway because a partial LocalDB/application installation is a
worse outcome than waiting. Test-mode installer acceptance suppresses presentation but exercises
the same payload and exit-code path. A genuine Windows elevation prompt is not hidden: when the
Microsoft-signed LocalDB prerequisite is absent, Windows must still ask the user before the elevated
MSI runs.

**Rejected:** accepting the black window as unavoidable; hiding all feedback during a potentially
long LocalDB install; creating a second full self-contained graphical runtime inside the Demo
package; and suppressing the Windows security-consent prompt.

## 2026-09-06 — Decorative themes pattern the shell, not the work

Ironworks Matte, Paisley, Art Nouveau, and Mid-Century Modern are complete theme dictionaries, not
background images or partial color overlays. Their motifs are tiled WPF vector drawings on
`WindowBackgroundBrush` and `NavBackgroundBrush`, so they scale cleanly, add negligible package
weight, and do not require image attribution or network access.

The content surfaces beneath notes, forms, grids, dialogs, and buttons remain solid or gently
graded. This keeps clinical and billing work readable and prevents decorative lines from being
mistaken for field boundaries. Every palette still supplies the full interchangeable theme
contract, including separate primary-button tokens and local-AI colors. Runtime rendering covers
all four themes, and primary-button foreground/background pairs must meet at least 4.5:1 contrast.

**Rejected:** wallpaper behind form content; raster textures that blur under scaling and enlarge
the installer; making the motif carry status meaning; and adding a decorative dictionary that
inherits missing colors from whichever theme happened to be loaded before it.
