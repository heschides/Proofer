# Audit events

*Current as of 2026-09-03.*

Sati records a small, append-only event when a protected action succeeds. The event answers
“who did what, to which record, for which agency, and during which request?” It is not a second
copy of the clinical or financial record.

## Envelope

| Field | Purpose |
|---|---|
| `EventId` | Globally unique event identifier. |
| `AgencyId` | Tenant boundary and primary audit-query scope. |
| `ActorUserId` | Authenticated user who completed the action. |
| `Action` | Stable machine-readable action name. |
| `ResourceType` / `ResourceId` | Minimal pointer to the affected record. The numeric ID may be absent on a create performed in the same database save. |
| `OccurredAtUtc` | Server timestamp. |
| `CorrelationId` | Connects the audit event to structured API logs without copying request content. |
| `MetadataJson` | Reserved for small, reviewed, non-narrative metadata; defaults to `{}`. |

Passwords, note narratives, assessment documents, names, reasons, tokens, billing identifiers, and
request bodies must not be copied into `MetadataJson`. The authoritative record remains the source
for content; the audit event is only its activity index.

## Recorded actions

- `authentication.succeeded`
- `user.created`, `user.updated`, `user.password-reset`, `user.password-changed`
- `note.reassigned`, `note.approved`, `note.approval-overridden`, `note.returned`
- `assessment.created`, `assessment.updated`, `assessment.submitted`
- `person.created`, `person.updated`, `person.journal-updated`, `person.journal-reminder-added`
- `person-history.viewed`, `person-history-pdf.generated`
- `consumer.archived`, `consumer.unarchived`
- `legal-hold.placed`, `legal-hold.released`
- `consumer.deleted-in-window`
- `settings.updated`
- `scratchpad.updated`
- `billing-claim-line.created`, `billing-period.submitted`, `billing-edi.generated`
- `at-request.published`, `at-request.reopened`
- `form.attested`, `form.attestation-revoked`, `form.prerequisite-overridden`
- `document.generated`, `document.recorded-external`
- `document-template.published`
- `provider.merged`
- `audit.exported`
- `platform-incidents.viewed`, `incident-status.updated`

The two AT request actions bracket a document of record. `at-request.published` records the case
manager's attestation; `at-request.reopened` records that an attestation was discarded, and carries
the discarded signer and timestamp in its metadata so the trail does not simply go quiet. Reopening
is its own action rather than an implicit consequence of a status change, because a reviewer reading
the trail should not have to infer that a signature was removed.

The two form actions bracket the live compliance projection. `form.attested` records form type,
cycle start, the explicitly entered completion date, actor kind, and prerequisite artifact ids;
`form.attestation-revoked` records form type and actor kind. A revocation's required explanation
stays on the protected append-only `FormAttestation` row rather than being copied into general
audit metadata. The ledger row, `Form.CompletedDate` projection, and audit event share one EF Core
transaction. Existing completions backfilled by migration carry a System attestation reason of
`pre-attestation record`; the historical completion date itself is not changed.

`document.generated` records document kind, cycle start, and origin; it never carries PDF bytes,
consumer names, release selections, or other document content. `document.recorded-external`
records kind and cycle start, while the required verification/location note remains only on the
protected `DocumentArtifact`. `form.prerequisite-overridden` records form type and the kinds of
unmet prerequisites. The Supervisor's required technical-problem explanation remains on the
protected attestation ledger row rather than being copied into broad audit metadata.

`document-template.published` records the agency, document kind, and newly assigned version.
Template source and merged consumer values are excluded. Privacy-document generation additionally
records template owner, key, and version in `document.generated` and on the artifact. Published
template rows reject tracked edits/deletes in both contexts; replacing wording appends a version.

`note.reassigned` records a successful correction from one client to another on the same case
manager's own caseload. Its metadata contains only the previous and new Person IDs; client names
and note content remain out of the general audit envelope.

`provider.merged` records an Admin's atomic consolidation of two entries in the shared agency
directory. Its resource is the surviving Provider ID; metadata contains only the absorbed Provider
ID and counts of affiliated entries, consumer links, and named provider contacts moved. It carries
no provider names, consumer IDs, or document content. Assessment snapshots are not rewritten by
the merge.

The two incident actions cover the operational telemetry surface: every cross-tenant dashboard read
by a `PlatformOperator` is recorded, and an agency Admin changing an incident's lifecycle status is
recorded with the incident id and new status. Reading one's own agency incident dashboard is not
audited; crossing a tenant boundary to read another agency's is.

The state change and its event share one EF Core `SaveChanges` transaction. If either fails, neither
is committed. EDI generation records the event before the file response is returned.

The transitional local assessment and supervisor services apply the same rule and derive the actor
from the signed-in session. Caller-supplied author/reviewer IDs cannot change event attribution or
broaden assignment or agency scope.

## Access and immutability

- Only an Admin can call `GET /api/v1/audit-events`.
- The API always restricts the query to the actor's agency and limits the date window and row count.
- Both application database contexts reject tracked updates or deletes of `AuditEvent` and
  `FormAttestation` rows. The attestation-to-form relationship is restricted rather than cascading.
- Production SQL-principal separation, retention classes, the legal-hold gate, export controls, and
  monitoring expectations are defined in `OPERATIONS.md`. Enforcement remains `PolicyOnly` until
  legal-hold controls exist; application-level append-only enforcement does not make a database
  administrator powerless.

## Concurrency and duplicate protection introduced with this slice

Comprehensive Assessments carry a `Revision` concurrency token. Save and submit requests include
the revision the user opened. A stale revision returns HTTP 409 and does not overwrite the newer
record. The successful response supplies the next revision to the client.

Notes use the same fail-closed revision contract for edits, deletes, supervisor decisions, and
automated abandonment. Every successful transition increments the Note revision. A stale caller
receives `409 stale_note`; the desktop keeps an in-progress narrative draft, reloads the latest
saved Note, and identifies fields that differ before the user decides whether to save again.
Older clients that omit the expected revision do not receive a compatibility bypass.

AT requests use one parent `Revision` for the request and all line items. Updating vendor details,
money, dates, status, or the item collection replaces that aggregate in one EF transaction and
increments the revision. Stale updates and deletes receive `409 stale_at_request`, including older
clients that omit the expected revision. The desktop AT screen does not yet expose persistence;
its typed conflict is ready for the deliberately deferred Save/Open/Delete + PDF-publishing slice.

Scratchpads use a per-user daily `Revision`. Stale autosaves receive
`409 stale_scratchpad`; older clients that omit the expected revision fail closed. The desktop
keeps the attempted text visible, stops repeat autosaves, and requires an explicit reload of the
newer saved copy. An unchanged autosave returns the current revision without writing a row or an
audit event.

Claim lines have a unique database index on `NoteId`. Creating the monthly period, claim line, and
audit event happens in one save, and a repeated command returns HTTP 409 instead of charging the
same service note twice.

EDI generation uses a caller-supplied GUID retry key. The exact generated filename and 837P content
are committed with the success audit event behind a unique tenant/actor/key index. Repeating the
same period and mode with the same key replays that response without another write or event;
reusing the key for different inputs returns `409 idempotency_key_reused`. Billing-period submission
is naturally idempotent: repeating an already-successful submit returns the stored submitted state,
and `BillingPeriod.Status` is a concurrency token for simultaneous requests.

`EdiGeneration` contains protected billing content, unlike the PHI-minimized `AuditEvent` envelope.
The pending retention/legal-hold policy must explicitly cover these replay records and their access.

## Person lifecycle history

The general `AuditEvent` envelope says that an operation happened without copying PHI into the
activity log. Person lifecycle history serves a different, narrowly scoped purpose: it preserves
the actual Person profile values needed to reconstruct each successful revision.

- Each new Person starts at revision 1 with a complete compressed snapshot.
- Each successful profile or journal edit adds one immutable `PersonVersion` containing the full
  resulting snapshot plus a field-by-field before/after change list, actor, UTC timestamp, agency,
  and request correlation ID.
- The Person row is an optimistic-concurrency record. A stale client revision receives HTTP 409
  and cannot silently replace a newer edit.
- Both database contexts reject tracked application attempts to update or delete Person history
  rows. The sole named exception is the Admin test-consumer deletion command described below,
  which removes the synthetic consumer's PHI-bearing versions while preserving the independent
  audit trail.
- `GET /api/v1/people/{personId}/history` and `.pdf` are Admin-only, agency-scoped, marked
  `no-store`, and their use is itself recorded in the lightweight audit log.
- Existing People cannot acquire history retroactively. Their first edit or history request adds a
  clearly labeled `TrackingBaseline` snapshot of the then-current record; tracking is complete from
  that point forward.

The PDF is an auditor-friendly rendering of the same append-only ledger. It includes confidential
handling language, record and revision identifiers, chronological actor/timestamp details, and
the old and new value for every changed field. Related notes, contacts, forms, assessments, and
billing items retain their own histories and are not silently folded into the Person profile ledger.

## Archive status and legal holds

`consumer.archived` and `consumer.unarchived` record a change to `Person.Status` (`Active`,
`NoLongerServed`, `Deceased`, or the Admin-only `Ghost`). Only fired when the new status actually
differs from the current one. Metadata carries the person id and the previous/new status values;
the optional status note stays on the `Person` row itself, alongside the rest of the profile, and
is not duplicated into general audit metadata — the same "stays on the authoritative row" pattern
`form.attestation-revoked` uses. Archiving is non-destructive: an archived Person keeps its full
history and can be reactivated. `Sati.Contracts.V1.PersonStatusRules` is the sole owner of who may
set which status.

`legal-hold.placed` and `legal-hold.released` record changes to the minimal `LegalHold` registry
introduced 2026-09-03 to gate the deletion command below (see `HANDOFF_CLIENT_DELETION_POLICY.md`
and `OPERATIONS.md` for the registry's deliberately narrow scope and its single-admin-release
shortfall against `OPERATIONS.md`'s general dual-control requirement). Metadata carries only the
person id and, for a release, the hold id — never the placement reason or release note. Both stay
on the `LegalHold` row, which is retained (marked released, never deleted) for exactly the same
reason `FormAttestation` rows are retained rather than duplicated into `MetadataJson`.

## Admin deletion of consumer test data

The Admin dashboard exposes one deliberately narrow destructive command for a consumer marked as
synthetic test data when an Admin created it. It is not the path for duplicates, inactive consumers,
or ordinary retention work. The local and API implementations both require that immutable marker,
the exact versioned deletion attestation, Admin role, actor-agency ownership, and the selected Person
revision.

Deletion runs in one serializable transaction and explicitly removes the consumer-owned forms and
their synthetic-only attestation rows,
notes, contacts, consumer-provider links, reviews and appointments, assessments, AT requests and
their items, and Person versions before removing the Person row. A claim line referencing any of
the consumer's notes blocks the entire operation. The existing `AuditEvent` ledger is never deleted;
a successful operation appends `test-data.consumer-deleted` with the Person ID, attestation version,
and record counts—including consumer-provider links—but no name, narrative, or profile value.
Removing Person versions here is a narrow test-data exception because those compressed rows contain
copies of the synthetic profile; it does not weaken the append-only rule for real lifecycle history.

The WPF client's Admin tab is the supported human-facing entry point. It summarizes agency usage,
renders recent `AuditEvent` activity, shows Person versions and field changes, and invokes the protected PDF export. It also reports database/retention status and creates a
reason-gated, bounded agency audit CSV whose use is itself recorded. The UI does not broaden access: cloud requests remain subject to the API's
Admin and tenant checks, and the transitional local service repeats the Admin/agency restrictions.

## Admin deletion of an ordinary consumer within the creation window

A second, distinct destructive command, added 2026-09-03, covers what the test-data command above
does not: an ordinary consumer created in error (most often a duplicate produced by import) with no
synthetic-test marker. It does not reuse the test-data attestation — an older client that only knows
that version cannot invoke this broader command. Full gate rationale lives in
`HANDOFF_CLIENT_DELETION_POLICY.md`; this section covers only the audit contract.

Three gates must all pass before any row changes, checked in this order: the Person must still be
within `ConsumerDeletionRules.DeletionWindowDays` (20 days) of its immutable `CreatedAtUtc`; the
consumer's legal-hold status from `ILegalHoldRegistry` must be exactly `Clear` — `Active`,
`Unavailable`, and a registry exception all refuse before touching a row; and none of the consumer's
claim lines may belong to a `BillingPeriod` whose billing actually reached a payer (a transmitted,
non-synthetic `BillingSubmissionEvent`; a non-synthetic `RemittanceClaimOutcome`; or a submitted or
non-Draft `BillingPeriod`). Draft and synthetic billing, notes, assessments, AT requests, and
contacts are all deletable inside the window — that permissiveness is the point of a time-boxed
correction window, not an oversight.

Deletion runs in one serializable transaction and removes the same class of consumer-owned records
as the test-data command, plus claim lines (permitted here because A1 already refused anything but
draft/synthetic billing) and document artifacts. The existing `AuditEvent` ledger is never deleted;
a successful operation appends `consumer.deleted-in-window` as an itemized tombstone rather than
bare counts — record counts are included too, but the point of the itemization is that this is the
one remaining evidence the record existed. Per note: id, event date, status, minutes, and note type.
Per claim line: id, date of service, procedure code and modifier, units, charge amount, and billing
period id. Per form: id, type, and due date. Per review item: id, category, and requested date. Per
assessment: id, status, and creation date. Per AT request: id, status, and submitted date. Per
contact: id and kind. Per `PersonVersion`: id, change kind, and changed-at timestamp. The event also
carries the attestation version, the deletion timestamp, the consumer's `CreatedAtUtc`, and the three
billing-integrity facts that were checked. None of this carries the consumer's name, `MaineCareId`,
`EvergreenId`, birth date, address, or any note/assessment narrative — matching the exclusion
discipline the test-data tombstone above already follows, verified by a dedicated test that plants
sentinel strings in a note narrative and a `MaineCareId` and asserts neither appears anywhere in the
resulting `MetadataJson`.

The one deliberate exception to this file's Envelope rule against copying "reasons" into
`MetadataJson`: the Admin's required free-text reason for the deletion is included in the tombstone.
Every other reason/explanation field in this system (form-attestation revocation, audit-CSV export)
stays off the general audit log because it durably survives on a protected row that isn't going
anywhere. A deleted Person has no such row left to hold it, and dropping the reason entirely would
leave an irreversible action with no recorded justification at all — worse, on balance, than the
narrow risk of an Admin typing identifying detail into it. The Admin dashboard's reason field carries
an explicit warning against including the client's name or other identifying detail
(`Views/AdminDashboardView.xaml`), but nothing server-side scrubs the text; treat this field as
Admin-authored operational metadata, not as PHI-safe by construction.

# Safety-plan workflow events

`safety-plan.created`, `safety-plan.updated`, `safety-plan.submitted`, `safety-plan.approved`, and `safety-plan.returned` identify the lifecycle transition and safety-plan record. They intentionally do not copy plan narrative or a return reason into audit metadata.

## Annual packet and receipt events — 2026-09-03

- `document.acknowledged`: exact artifact id and validated actor; receipt date/effort explanation
  remain on the protected append-only acknowledgment row, never duplicated in audit metadata.
- `document.verified`: artifact id and boolean match outcome. No uploaded bytes, filename or
  narrative is logged; both stored hash and byte count must match.
- `annual-packet.saved`: consumer id and actor, in the same transaction as the generated artifact
  records. Means the server generated a download, not that the user saved it or sent anything.
- Standalone Safety Plan PDFs use `document.generated` with source id/version provenance. Packet
  constituents are itemized by their artifact rows/manifest, with one packet audit event.

Authorized consumer deletion includes SafetyPlans and DocumentAcknowledgments, before artifacts
and the Person are removed. Their counts are included in the retained audit tombstone; no safety
plan content or receipt explanation is copied into the general ledger.
