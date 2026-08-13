# Operations and records governance

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

## Remaining production work

- Implement the legal-hold registry, dual-control release, dry-run, and destructive job.
- Apply the production SQL grants/denies and verify them in deployment tests.
- Connect logs/health/database signals to an external metrics and paging platform.
- Add backup restore drills, incident response exercises, and evidence retention.
