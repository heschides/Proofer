# Sati - Decisions

*Living document. The "why" behind choices that no diagram preserves. ARCHITECTURE.md
says what owns what; this says why it was built that way and what was rejected. Newest
sections at the bottom. Last updated: 2026-08-13.*

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
dataset and stored demonstration logins. A scheduled Azure job restores that baseline nightly under
a reset-specific managed identity.

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
