# Audit events

*Current as of 2026-08-28.*

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
- `settings.updated`
- `scratchpad.updated`
- `billing-claim-line.created`, `billing-period.submitted`, `billing-edi.generated`
- `at-request.published`, `at-request.reopened`
- `audit.exported`
- `platform-incidents.viewed`, `incident-status.updated`

The two AT request actions bracket a document of record. `at-request.published` records the case
manager's attestation; `at-request.reopened` records that an attestation was discarded, and carries
the discarded signer and timestamp in its metadata so the trail does not simply go quiet. Reopening
is its own action rather than an implicit consequence of a status change, because a reviewer reading
the trail should not have to infer that a signature was removed.

`note.reassigned` records a successful correction from one client to another on the same case
manager's own caseload. Its metadata contains only the previous and new Person IDs; client names
and note content remain out of the general audit envelope.

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
- Both application database contexts reject tracked updates or deletes of `AuditEvent` rows.
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
- Both database contexts reject application attempts to update or delete Person history rows.
- `GET /api/v1/people/{personId}/history` and `.pdf` are Admin-only, agency-scoped, marked
  `no-store`, and their use is itself recorded in the lightweight audit log.
- Existing People cannot acquire history retroactively. Their first edit or history request adds a
  clearly labeled `TrackingBaseline` snapshot of the then-current record; tracking is complete from
  that point forward.

The PDF is an auditor-friendly rendering of the same append-only ledger. It includes confidential
handling language, record and revision identifiers, chronological actor/timestamp details, and
the old and new value for every changed field. Related notes, contacts, forms, assessments, and
billing items retain their own histories and are not silently folded into the Person profile ledger.

The WPF client's Admin tab is the supported human-facing entry point. It summarizes agency usage,
renders recent `AuditEvent` activity, shows Person versions and field changes, and invokes the protected PDF export. It also reports database/retention status and creates a
reason-gated, bounded agency audit CSV whose use is itself recorded. The UI does not broaden access: cloud requests remain subject to the API's
Admin and tenant checks, and the transitional local service repeats the Admin/agency restrictions.
