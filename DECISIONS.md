# Sati - Decisions

*Living document. The "why" behind choices that no diagram preserves. ARCHITECTURE.md
says what owns what; this says why it was built that way and what was rejected. Newest
sections at the bottom. Last updated: 2026-08-26.*

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

`Sati.Contracts.V1.NoteSchedulingPolicy` owns the conversion. A future date wins
over caller-selected type and status, preserves only the date and narrative, and
removes minutes, start time, form type, visit documentation, and case-manager
justification. The desktop applies the rule immediately for understandable UI;
the local `NoteService` and API apply it again before persistence. The API uses
the agency date from `ApiClock`, so a forged or older distributed client cannot
turn future work into submitted or billable documentation.

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
