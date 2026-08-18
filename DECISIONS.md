# Sati - Decisions

*Living document. The "why" behind choices that no diagram preserves. ARCHITECTURE.md
says what owns what; this says why it was built that way and what was rejected. Newest
sections at the bottom. Last updated: 2026-08-15.*

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

**Not yet configured as of 2026-08-15.** The design is settled; the job is not running. Demo data
persists between demonstrations until it is reset by hand. `DATABASE_ENVIRONMENTS.md` carries the
required reset sequence and `AGENDA.md` tracks the work. Do not describe Demo as self-resetting.

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
(`Services/LocalAi/ConsumerSessionBoundary.cs`) records the consumer of the last completed request
and reports whether the next request targets someone else. When it does, the formatter unloads and
reloads the model before generating — a full reset, not a best-effort clear — so no consumer's
formatting call can begin on a model instance that just carried a different consumer's context.
Consecutive requests for the same consumer skip the reload, so formatting several notes for one
client in a row stays fast. `LocalAiConsumerIsolationTests` covers the reset-boundary decision logic
and proves, against a seeded database, that one consumer's note content never appears in another
consumer's assembled context.

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

**Removal condition.** This is transitional. When no reachable deployment predates
the journal-entries route, delete the `catch` in
`CloudPersonService.AddJournalReminderAsync`, the `UsedLegacyJournalWrite` flag and
the warning it drives, and `Sati.Tests/JournalReminderFallbackTests.cs` — they were
written to go together. Tracked in `AGENDA.md`.

**Found on the way:** `CloudApiClient.GetAsync<string?>` rejects a null result as
"an empty response", and a journal that has never been written returns an empty
body — so loading the journal of any such client raised "the journal could not be
loaded from the cloud" rather than showing an empty journal. `GetStringOrNullAsync`
expresses that shape, and `GetJournalAsync` now uses it.
