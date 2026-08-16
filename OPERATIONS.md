# Operations and records governance

*Current as of 2026-08-15. Retention enforcement remains `PolicyOnly`; nothing in this document
describes an automated deletion process that exists today.*

This runbook describes the operational controls represented in the Admin dashboard and the
controls that must exist before Sati is treated as a production system. It is intentionally
explicit about what is policy and what is automated.

## Retention classes

| Record class | Current policy | Enforcement | Reason |
|---|---:|---|---|
| `AuditEvent` | 2,555 days (7 years) | Policy only | Security and compliance activity index; intentionally excludes narrative PHI. |
| `EdiGeneration` replay content | 90 days | Policy only | Short retry/reconciliation window for protected 837P content. |
| `PersonVersion` | Retain with the Person record | No automated deletion | Required to reconstruct the Person lifecycle; contains PHI. |
| Clinical and billing source records | Agency/legal policy required | No automated deletion | Must not be destroyed by a generic cleanup job. |

The API validates configured audit retention between 365 and 3,650 days and EDI replay retention
between 30 and 365 days. The Admin dashboard reports the configured values and
`RetentionEnforcementMode = PolicyOnly`. No background process currently deletes these records.

## Legal hold gate

Automated retention must not be enabled until a legal-hold registry and the following workflow
exist:

1. An authorized administrator identifies the agency, record class, scope, reason, case/reference,
   issuer, and effective date of a hold.
2. Every purge query excludes records covered by an active hold before selecting deletion targets.
3. Creating, changing, or releasing a hold is append-only audited. Release requires a second
   authorized approver and records the release rationale.
4. A dry-run report shows record counts and oldest/newest timestamps before a destructive job can
   execute. The job records its policy version, cutoff, hold exclusions, and deleted counts.
5. Backup retention and deletion are reconciled with the same hold; deleting live rows while a
   held backup expires would not satisfy the hold.

Until those controls are implemented and reviewed, `PolicyOnly` is the only permitted enforcement
mode.

## Controlled audit export

- Only an authenticated Admin can export audit activity.
- The server derives the agency from the authenticated actor; callers cannot choose an agency.
- A business reason of 10-250 characters and a window no longer than 366 days are required.
- Exports are capped at 10,000 rows, returned as UTF-8 CSV, and marked `no-store`.
- The reason appears in the exported artifact for its reviewer but is not copied into audit
  metadata. The export itself creates one `audit.exported` event with window and row count.
- A CSV is sensitive operational data. Save it only to an agency-approved encrypted location and
  transfer it through an approved channel.
- Exported values are neutralized for spreadsheet import. RFC 4180 quoting makes a field parse
  correctly but does not stop Excel, LibreOffice, or Sheets from evaluating a value that begins with
  `=`, `+`, `-`, `@`, tab, or carriage return. Any such value is written with a leading apostrophe so
  the reader treats it as text, with the original characters preserved after it. The format has one
  owner, `Sati.Contracts.V1.AuditCsv`, shared by the API export and the desktop's local export. See
  `API_SECURITY_AUDIT.md` for why this crosses a privilege boundary rather than being cosmetic.

## Production SQL principals

Production deployment must use separate identities:

| Identity | Minimum responsibility |
|---|---|
| API runtime | Connect, select, insert, and only the updates/deletes required by API workflows. No schema-owner or DDL rights. |
| Migration job | Apply reviewed migrations during deployment. Not used by the running API. |
| Backup operator | Run and verify backups/restores without application credentials. |
| Read-only support | Time-limited, approved diagnostic access; no direct PHI access by default. |

Database permissions should deny application updates/deletes to `AuditEvents` and
`PersonVersions` in addition to the application-level append-only check. Secrets belong in the
deployment secret store, never in appsettings, source control, logs, or support tickets.

## Health, monitoring, and alerts

The API exposes liveness and readiness health checks, validates the expected database/environment
at startup, emits structured logs, and carries a correlation ID through protected operations. The
Admin operations panel confirms that agency-scoped database queries succeed and reports retained
audit/EDI counts and oldest-record timestamps.

Before a production pilot, wire these signals into the chosen monitoring platform and alert on:

- readiness failure or repeated process restarts;
- elevated HTTP 5xx or database timeout rates;
- unusual authentication-failure/rate-limit volume;
- export spikes and repeated authorization failures;
- database storage, connection, CPU, and backup-job thresholds;
- retention dry-run/job failures after enforcement is implemented.

Alerts must route to a named owner with severity, acknowledgement, escalation, and after-hours
expectations. A dashboard without notification routing is visibility, not an alerting system.

## Demo operator check

1. Sign in as an Admin and open **Admin**.
2. Confirm Database is `Healthy`, the retention values are visible, and enforcement says
   `PolicyOnly`.
3. Enter an appropriate export reason and download the last-30-day CSV.
4. Confirm the recent activity list adds **Exported audit activity**.
5. Do not present policy-only retention as automated deletion or legal-hold enforcement.

## Developer workstation rules

Sati is developed on the same laptops that hold real client records, which makes the
workstation part of the compliance surface rather than an implementation detail. These rules
came out of a 2026-08-15 sweep that found a mixed-era database and several development
artifacts sitting in a personal consumer OneDrive.

### Where databases may live

`%LOCALAPPDATA%\Sati\Databases` for live databases, `%LOCALAPPDATA%\Sati\Archive` for
retired ones. Never a profile root, never Desktop or Documents, and never any directory
inside a sync root.

The mechanism that caused the problem is worth naming, because it is silent: **OneDrive syncs
Desktop and Documents by default.** Anything saved to either is in Microsoft's cloud within
seconds, under whichever account is signed in — which may be a personal one with no BAA.
`%LOCALAPPDATA%` is not synced, which is why it is the right home.

### Real client data does not leave the machine it belongs on

- Never copy a Production database to another person's machine, including a business
  partner's. `SatiDemo` exists so that nobody ever needs real records to develop against.
- Real client records belong to the employing provider agency, not to SatiLogica or
  RobinBradleyAMS. Where they may be stored is that agency's policy decision, not a
  SatiLogica one.

### Exports and recordings are records too

A screen recording of Sati showing real clients is PHI. So is a CSV export, a generated AT
request PDF, a backfill log that enumerates people, and a crash dump taken with a caseload
loaded. These are easy to overlook because none of them feels like a database:

- Do development demos, screen recordings, and screenshots against `SatiDemo` only.
- Write operational logs and exports outside sync roots, and delete them when the task ends.
- Check what is on screen before recording.

### Disk encryption

BitLocker on, verified rather than assumed — `manage-bde -status C:` must report
**Protection On** and **100%**. Store the recovery key somewhere that is not the laptop.

Two limits to understand:

- **Used Space Only** encryption, the Windows default, leaves free space unencrypted. Sectors
  released by deleting a database are not covered, so deletion is not erasure.
- BitLocker protects a powered-off disk. It does nothing for a running, logged-in machine, and
  sleep keeps the key in memory. A laptop that travels with client data should hibernate or
  shut down, not sleep.

### Windows account separation

Development and real case management should not share a Windows profile, because they do not
share a cloud account, a risk profile, or an audience. A separate profile gives each its own
OneDrive, its own Desktop and Documents, and its own default save paths — which removes the
class of mistake above rather than relying on remembering the rule each time.

## Remaining production work

- Decide the retention answer for the mixed-era archives in `%LOCALAPPDATA%\Sati\Archive`.
  They predate the Production/Demo split and may hold real records, so disposing of them is a
  records decision under the retention classes above, not workstation cleanup.
- Implement the legal-hold registry, dual-control release, dry-run, and destructive job.
- Apply the production SQL grants/denies and verify them in deployment tests.
- Connect logs/health/database signals to an external metrics and paging platform.
- Add backup restore drills, incident response exercises, and evidence retention.
