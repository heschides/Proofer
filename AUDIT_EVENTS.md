# Audit events

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
- `note.approved`, `note.approval-overridden`, `note.returned`
- `assessment.created`, `assessment.updated`, `assessment.submitted`
- `person.created`, `person.updated`, `person.journal-updated`
- `person.history-viewed`, `person.history-pdf-generated`
- `settings.updated`
- `billing-claim-line.created`, `billing-period.submitted`, `billing-edi.generated`

The state change and its event share one EF Core `SaveChanges` transaction. If either fails, neither
is committed. EDI generation records the event before the file response is returned.

## Access and immutability

- Only an Admin can call `GET /api/v1/audit-events`.
- The API always restricts the query to the actor's agency and limits the date window and row count.
- Both application database contexts reject tracked updates or deletes of `AuditEvent` rows.
- Production still needs SQL-principal permissions and a documented retention/legal-hold process;
  application-level append-only enforcement does not make a database administrator powerless.

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

Claim lines have a unique database index on `NoteId`. Creating the monthly period, claim line, and
audit event happens in one save, and a repeated command returns HTTP 409 instead of charging the
same service note twice.

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
renders recent `AuditEvent` activity, shows Person versions and field changes, and invokes the
protected PDF export. The UI does not broaden access: cloud requests remain subject to the API's
Admin and tenant checks, and the transitional local service repeats the Admin/agency restrictions.
