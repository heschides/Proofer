# Operations and records governance

*Current as of 2026-08-15. Retention enforcement remains `PolicyOnly`; nothing in this document
describes an automated deletion process that exists today.*

This runbook describes the operational controls represented in the Admin dashboard and the
controls that must exist before Sati is treated as a production system. It is intentionally
explicit about what is policy and what is automated.

## Retention classes

Team chat's synthetic-only activation and outstanding operational controls are described below.

| Record class | Current policy | Enforcement | Reason |
|---|---:|---|---|
| `AuditEvent` | 2,555 days (7 years) | Policy only | Security and compliance activity index; intentionally excludes narrative PHI. |
| `EdiGeneration` replay content | 90 days | Policy only | Short retry/reconciliation window for protected 837P content. |
| `PersonVersion` | Retain with the Person record | No automated deletion | Required to reconstruct the Person lifecycle; contains PHI. |
| Clinical and billing source records | Agency/legal policy required | No automated deletion | Must not be destroyed by a generic cleanup job. |
| Team chat messages, changes and memberships | Agency/legal schedule required | No automated deletion | Originals and redactions retained; linked consumer deletion restricted. General/misfiled content and backups still require complete discovery/hold procedures. |
| `Person` (ordinary consumer), created within 20 days | Deletable by Admin action only | Manually triggered, gated, not automated | See "Consumer deletion within the creation window" below — a per-record command, not a background job. |
| `Person` (ordinary consumer), beyond 20 days or already archived | No deletion; archive-only | No automated deletion | `Person.Status` (`NoLongerServed`/`Deceased`/`Ghost`) removes a consumer from active caseload and compliance surfaces without destroying data. |

The API validates configured audit retention between 365 and 3,650 days and EDI replay retention
between 30 and 365 days. The Admin dashboard reports the configured values and
`RetentionEnforcementMode = PolicyOnly`. No background process currently deletes these records.

## Legal hold gate

### Team chat: synthetic testing only (2026-09-05)

`Chat:Enabled` defaults false. `ChatFeature` additionally requires the startup-validated pair
`Demo`/`SatiDemo` or `Testing`/`SatiApiTests`; a switch cannot authorize Production. Do not change
a database identity to bypass this. Local Production has no chat service.

`AddTeamChat` adds five tables. Cloud schema changes require separately authorized controlled
deployment. No cloud migration, deployment, security/firewall setting or real PHI is part of this
implementation. Hosting needs WebSocket support for prompt notices; HTTP polling works without it.
Only contentless notices are held in process, so missing a notice cannot bypass authorization or
lose persisted changes. Rehearse multi-instance operation before depending on its latency.

Monitor request failures, rejected writes, reconnect counts, database/audit growth and restore
health without message text, tokens, names or redaction reasons. Route alerts to a named owner.
Automatic purge, broad chat legal holds, controlled original-body export and complete account
disablement/session revocation remain unfinished. Direct SQL bypasses application guards;
least-privilege grants/denies and controlled recovery remain real-data prerequisites.
See `TEAM_CHAT_GUIDE.md`.

### General retention prerequisites

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
mode for **automated, background retention jobs**. That remains true today — nothing scheduled or
bulk exists.

### Consumer deletion within the creation window

*Added 2026-09-03. See `HANDOFF_CLIENT_DELETION_POLICY.md` and `DECISIONS.md`.*

Separately from the automated-retention question above, Sati.Api and the transitional
desktop-local service now expose one **manually triggered, per-record** destructive command: an
Admin may permanently delete an ordinary consumer created within the last 20 days
(`ConsumerDeletionRules.DeletionWindowDays`), gated on:

- The creation-time window, from immutable `Person.CreatedAtUtc`.
- A1's billing-integrity check — refuses only when billing for the consumer actually reached a
  payer; draft and synthetic billing artifacts remain deletable.
- A real, minimal `ILegalHoldRegistry` (`LocalLegalHoldRegistry` / `ApiLegalHoldRegistry`) over a
  new `LegalHold` table. An unreleased hold blocks deletion; any query failure reports
  `Unavailable`, never `Clear` — the same fail-closed shape items 1-5 above describe, just scoped
  to this one command rather than to general retention.

This registry is **not** the retention workflow items 1-5 above describe in full:

- It has no record-class or agency-wide scope — it only answers "is this one person held."
- Release is **single-admin**, not the dual-control release item 3 requires. Tracked in
  `AGENDA.md`.
- There is no dry-run report or backup reconciliation (items 4-5) — this command is a
  human-confirmed, one-record-at-a-time action, not a bulk job, and item 4's per-record
  equivalent is a typed-name confirmation with a summary of what will be removed.

Do not treat this command's existence as satisfying the legal-hold gate for any future automated
retention job. It answers a narrower question for a narrower purpose.

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

## Demo schema changes without a firewall rule

`SatiDemo`'s SQL allow-list admits only `sati-demo-api-satilogica`'s three outbound addresses. A
migration run from a workstation therefore needs a temporary exact-IP rule, which is a security
setting nobody but the operator may add. The `demo-history-reconciliation` triggered WebJob exists
so that step is not needed: it runs inside the App Service, from addresses already on the list.

Publishing the API ships the job. Running it is separate and manual.

### What permission this job actually needs

Less than a migrator will. The reconciliation only runs `INSERT` and `DELETE` against
`dbo.__EFMigrationsHistory` and reads `sys.*` catalog views for its proofs. It issues no `CREATE`,
`ALTER`, or `DROP`. Writing rows to a table is `db_datawriter`, which the App Service identity
already needs in order to serve the API at all, so this job most likely requires **no new grant**.

Find out by running the dry run rather than by granting first. It is rollback-only, and if the
identity is short a permission it fails on the write with a clear error and changes nothing.
Granting `db_ddladmin` speculatively widens a production-facing identity to solve a problem that may
not exist.

`db_ddladmin` becomes necessary when `Sati.Migrator` applies real schema migrations, not before. If
it is needed, run this against `SatiDemo` as the server's Entra admin:

```sql
ALTER ROLE db_ddladmin ADD MEMBER [sati-demo-api-satilogica-46417];
```

To see what it already holds:

```sql
SELECT r.name AS role_name
FROM sys.database_role_members rm
JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
WHERE m.name = 'sati-demo-api-satilogica-46417';
```

### The database user is not named after the App Service

The App Service is `sati-demo-api-satilogica`. Its contained database user in `SatiDemo` is
**`sati-demo-api-satilogica-46417`** — same identity, different name, and the suffix is not
guessable. Any `GRANT` or `ALTER ROLE` written against the resource name fails with "Cannot find the
user", which reads like a permissions problem and is not one.

Confirmed 2026-08-30: type `EXTERNAL_USER`, holding `db_datareader, db_datawriter`. It is a user
rather than a group, so grants to it widen nothing beyond the API. To re-derive the name if it ever
changes:

```sql
SELECT name, type_desc FROM sys.database_principals WHERE type IN ('E','X') ORDER BY name;
```

The connection carries no credentials — `ConnectionStrings__SatiDemo` uses
`Authentication=Active Directory Default` with no user or password, so the App Service managed
identity is the only thing authenticating.

Running either statement means connecting to `SatiDemo` yourself, which needs one temporary
exact-IP firewall rule. That is the one remaining opening, and it is one-time rather than
per-release.

**Any such grant is a security setting a person makes deliberately.** No release workflow, script,
or agent performs it. While the identity holds `db_ddladmin`, a compromise of the public API could
alter the Demo schema — an accepted, recorded trade while Demo holds only synthetic data.
`AGENDA.md` Phase 3 records the gate: before `SatiProduction` moves to the cloud, the runner moves
to its own identity.

### What the identity needs, and why it is not ALTER

The proofs read constraint definitions out of `sys.default_constraints`. A principal holding only
`db_datareader` and `db_datawriter` cannot see those rows — neither role carries `VIEW DEFINITION` —
so the proof reports a constraint it cannot see as one that does not exist. That is what happened on
2026-08-30, and it produced a whole corrective-DDL job for a constraint that had been in place since
2026-08-11.

So the standing grant is:

```sql
GRANT VIEW DEFINITION ON OBJECT::dbo.Users TO [sati-demo-api-satilogica-46417];
```

`ALTER` also implies `VIEW DEFINITION`, which is why granting `ALTER` made the proof pass — but
`ALTER` lets the identity change the table, and nothing here needs that. If `ALTER` is present from
the 2026-08-30 session, narrow it. **Grant `VIEW DEFINITION` before revoking `ALTER`**, or the proofs
start failing again for the same invisible reason:

```sql
GRANT VIEW DEFINITION ON OBJECT::dbo.Users TO [sati-demo-api-satilogica-46417];
REVOKE ALTER ON OBJECT::dbo.Users FROM [sati-demo-api-satilogica-46417];
```

Like every grant here, a person makes it; no job or agent does.

A `demo-users-agencyid-default` WebJob briefly existed to add that constraint. It was removed once
the constraint turned out to be present all along: it could perform DDL against a live database and
had no remaining reason to exist. If a genuine constraint divergence ever appears, write the
corrective script then, rather than keeping a schema-change job idling against the possibility.

### Rehearse before the first live run

`Apply-DemoHistoryReconciliation.ps1` states in its own notes that it must first be rehearsed
against a restored copy. `-WhatIfOnly` rolls its transaction back, but it still connects to the live
database and takes serializable locks, so the dry run is not free. Restore a copy and rehearse
there, or record the decision to accept that risk against synthetic Demo data.

### Running it

1. Confirm the mode. The job reads the app setting `SATI_RECONCILIATION_MODE`. Anything other than
   the exact string `apply` — including absent, empty, or misspelled — is a rollback-only dry run.
2. Trigger the dry run and read the log:

   ```powershell
   az webapp webjob triggered run --name sati-demo-api-satilogica `
       --resource-group rg-sati-demo --webjob-name demo-history-reconciliation
   az webapp log deployment show --name sati-demo-api-satilogica --resource-group rg-sati-demo
   ```

3. Only if the dry run is correct, set `SATI_RECONCILIATION_MODE=apply`, trigger again, then **set
   it back immediately**. Leaving it on `apply` turns an accidental trigger into a history change.
4. Trigger once more, still on `apply`, to prove idempotency, then clear the setting.
5. Confirm `/health/ready` is still `Healthy` and `GET /api/v1/admin/schema-drift` reports what you
   expect.
6. Confirm the allow-list is unchanged — three `sati-demo-api-outbound-*` rules, nothing else:

   ```powershell
   az sql server firewall-rule list --server sati-demo-satilogica-central --resource-group rg-sati-demo --output table
   ```

The job is manual-only and carries no `settings.job` schedule. A migration-history change is a
decision somebody makes, not something that happens on a timer.

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

## Electronic signature operations

The feature and its server workers remain off by default. Follow `Sati.Portal/README.md` for
separate identities, exact environment configuration, private storage, distinct versioned keys,
manual role grants, trusted proxies, token-free hosting logs and controlled synthetic recipients.
Follow `SIGNATURE_PORTAL_GUIDE.md` for legal, program and staff procedures. No real signature,
deployment, cloud migration, email or DNS change was performed while implementing this feature.

Retain frozen originals, request identity/disclosure/intent, consent, events, completion, signed
package, authorization/external-access withdrawal and delivery history as one evidence set.
Preserve wrapped-key versions and prove recovery of the database and blob files together. The
application refuses history deletion and populated schema rollback; this is not a complete
retention/hold lifecycle. Orphaned immutable blobs after an interrupted commit need reviewed
reconciliation, not an automatic purge. Existing person-only preservation tools are not claimed
to cover every signature/blob/backup scope.

Staff see invitation and receipt notification states separately. A provider-accepted message
is not established inbox delivery, and a previously submitted email cannot be recalled by
revoking a link. Delayed/uncertain submissions use the saved operation identifier for polling.
After the retry limit, assign an operator to investigate the visible review state. Configure
external alert routing for both unprepared signed copies and failed notifications; content-free
warnings alone do not notify an on-call person.

Consumers can obtain copies through bounded authenticated receipt access or an authorized staff
process afterward. Maintain a free accessible/paper-copy procedure. Identity/address/authority
changes stop old external access without altering the signature or pretending to revoke medical
authorization. Incident review must cover misdirected mail and copied files already released.
Browser/screen-reader/document acceptance, deployed sender authentication and delivery-event
monitoring remain uncompleted launch evidence, as listed in `SIGNATURE_PORTAL_VALIDATION.md`.
