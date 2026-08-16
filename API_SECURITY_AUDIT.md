# API security audit — 2026-08-14

Scope: the authorization surface of `Sati.Api`, the sensitive-data boundary between the server and
distributed clients, and the artifacts the platform hands to a reviewer. Driven by the two risks
`CLAUDE.md` names as open — caller-controlled scope values, and `User` fields that must never leave
the server.

This is a point-in-time review of the code as of this date, not a certification. It does not
substitute for an independent assessment, and it does not establish HIPAA compliance.

---

## Fixed

### 0. Unauthenticated account creation on the sign-in screen — added 2026-08-15

The sign-in window offered "Create an account", enabled whenever the environment was not
API-backed. That condition is true for local Production. Anyone able to launch Sati could
create themselves a `CaseManager` account with no credentials, no approval, and no record
of who authorised it, and could assign themselves a supervisor chosen from a dropdown built
by enumerating every staff account — so the same surface also disclosed the staff directory
to an unauthenticated party.

This is account creation, not privilege escalation: the created role was hard-coded to
`CaseManager`, so it granted an authenticated foothold and an empty caseload rather than
administrative rights. In a system holding PHI, an unapproved identity that appears in
supervisor queues and audit trails is still a serious defect.

**Fixed by removing the path, not the button.** `CanCreateAccount`, the `OpenNewUserWindow`
command, and the `OpenNewUserRequested` event are gone, and `LoginWindow` no longer takes
the `NewUserWindow` factory. Hiding the control would have left a bindable command on an
unauthenticated surface. Creating users now happens only in User Management, behind an
authenticated Supervisor/Director/Admin session, where the server-side role rules already
stop a Supervisor creating anything but their own case managers.

The one legitimate need this served — an installation with nobody to sign in as — is met by
first-run administrator setup, which creates exactly one account and only while none exists.

`SignInSurfaceTests` pins it shut by reflecting over the view model's public surface, because
the defect was the existence of a reachable command rather than a wrong value. Confirmed to
fail against the reintroduced property.

### 1. Spreadsheet formula injection in the audit export — privilege-boundary crossing

**Where:** `Sati.Api/Endpoints/ApiEndpoints.cs` (`/admin/audit-export.csv`) and the duplicate
implementation in `Data/AdminService.cs`.

Both wrote CSV fields with RFC 4180 quoting only. Quoting makes a field *parse* correctly; it does
not stop a spreadsheet from *evaluating* it. Excel, LibreOffice, and Sheets strip the surrounding
quotes on import and then treat a leading `=`, `+`, `-`, `@`, tab, or carriage return as the start
of a formula.

The `ActorDisplayName` column is the live vector. A **Supervisor** may set the display name of any
case manager they supervise (`POST /users`, `PUT /users/{userId}`), and only an **Admin** may run
the export — so a lower-privileged user could place executable content into a higher-privileged
user's file. Display-name validation was length-only. `ExportReason` is caller-supplied text as
well.

This matters more than a generic CSV-injection finding because the audit export is the artifact
offered as evidence of access control. It should not be able to carry anything active.

**Fixed by** moving the file format into one owner, `Sati.Contracts.V1.AuditCsv`, used by both the
API and the desktop export, which also removes the drift risk of two hand-built copies. Values that
begin with a formula trigger are prefixed with an apostrophe — the spreadsheet convention for
literal text — and the original characters are preserved after it so the record stays faithful.
Neutralization is applied to every column, not only the ones untrusted today, so adding a column
cannot silently reopen the hole.

**Covered by** `Sati.Tests/AuditCsvTests.cs` (payloads, the Supervisor→Admin case, the assumption
that machine-written columns never trigger neutralization) and an end-to-end assertion in
`Sati.Api.Tests/TenantAuthorizationTests.cs`.

### 2. Username enumeration by timing on sign-in

**Where:** `POST /api/v1/auth/login`.

When no user row matched, the handler returned without calling the password verifier, skipping
100,000 PBKDF2 iterations. A missing account therefore answered measurably faster than a wrong
password. Measured on this machine before the fix: **2.9 ms versus 9.9 ms**. That is a reliable
oracle for which clinician accounts exist, and it feeds the per-username lockout below.

**Fixed by** `PasswordVerifier.VerifyMissingUser`, which performs the same derivation against a
fixed decoy credential and always returns false. The handler calls it on the not-found path.

**Covered by** `SignInSpendsTheSameWorkWhetherOrNotTheAccountExists`, which was confirmed to fail
against the unfixed code before being kept.

---

## Reviewed and accepted — no change made

- **Per-username sign-in lockout is a targeted denial-of-service vector.** `LoginAttemptGuard`
  allows 12 attempts per username per minute, so anyone who knows a username can keep that person
  locked out. Every lockout design trades this against credential stuffing, and changing the policy
  is a product decision, not a defect fix. Worth a deliberate decision before production.
- **`LoginAttemptGuard` state is per-process.** On more than one API instance the effective limit
  multiplies by the instance count. Fine for Demo; needs shared state before a scaled deployment.
- **`POST /at-requests` and a few sibling routes resolve a person by id and then authorize on the
  person's owning user, rather than using `TenantAccess.OwnsPersonAsync`,** which also asserts
  `person.AgencyId == actor.AgencyId`. Cross-agency access is still blocked because
  `CanAccessUserAsync` requires the target user to be in the actor's agency. It would only matter
  for a person row whose agency disagrees with its owner's agency, which the write paths do not
  currently allow. Noted rather than changed because the fix touches several routes and deserves a
  test for the inconsistent-row case.

---

## Reviewed and found sound

- **Tenant scoping across all 87 protected routes.** Every route taking a caller-supplied `userId`
  (`/caseload`, `/notes/monthly`, `/notes/day`, `/at-requests`, `/reviews`, `/incentives`) gates on
  `TenantAccess.CanAccessUserAsync` before use. Admin routes check `actor.Role == "Admin"` and
  scope every query to `actor.AgencyId`. `ValidatedActorFilter` re-confirms the claimed identity,
  role, and agency against the database on every request, so a token does not outlive a role change.
- **`PlatformOperator` containment.** Confined to the platform surface by an allow-list that fails
  closed on any unrecognized path.
- **No credential material in contracts.** `UserProfileDto` carries no hash or salt; nothing maps
  `PasswordHash`/`Salt` into a response.
- **Incident reporting carries no PHI.** `IncidentReportRequest` transmits only a SHA-256 shape
  fingerprint built from exception types, HResults, and target member names — never messages,
  arguments, or stack traces. `AppErrorLog.SafeArea` strips the operation label to alphanumerics.
  Raw stack traces stay in the workstation-local JSONL log, and that log omits `Exception.Message`.
- **Logging.** No narrative, journal, password, token, or connection string reaches a log statement.
  `Microsoft.EntityFrameworkCore.Database.Command` is pinned to `Warning`, which keeps SQL parameter
  values — the likeliest accidental PHI channel — out of the logs.
- **Sign-in hygiene otherwise.** Identical `401` for unknown user and wrong password, no user
  enumeration in the response body, PBKDF2-SHA256 at 100,000 iterations, constant-time comparison,
  a per-request rate limiter, and an audit event on success.
- **Startup fails closed.** A missing connection string, a signing key under 32 characters, or an
  out-of-range token lifetime or retention setting all throw at boot rather than degrading.
- **Error handling.** The global handler returns a correlation id and a generic message; exception
  detail never reaches the client.

---

## Not covered by the 2026-08-14 pass

Transport and hosting configuration, Azure identity and key management, dependency vulnerability
scanning, the desktop client's local database posture, and the EDI/claim submission path.

---

## Second pass — 2026-08-15

Covered the gaps listed above plus a fresh sweep for injection, authorization, and secret
handling. One finding, recorded as item 0. Everything below was checked and found sound; it
is listed so a later reviewer knows it was looked at rather than skipped.

| Area | Result |
|---|---|
| EDI/claim path (previously uncovered) | Sound. Both the desktop and API generators validate every element through the shared `BillingRules.IsSafeX12Element`, which rejects the X12 delimiters `* ~ : ^` — the same injection class as the CSV finding, closed in one owner rather than two. |
| EDI output file path | Sound. The filename is built server-side from a parsed GUID and period numbers; no caller input reaches `Path.Combine`. |
| Dependency vulnerabilities (previously uncovered) | Clean. `dotnet list package --vulnerable --include-transitive` reports none across all five projects. |
| Transport/hosting (previously uncovered) | Sound. HTTPS redirection, `X-Content-Type-Options: nosniff`, no CORS surface. |
| Raw SQL | No injection surface. The only two `CommandText` uses are fixed strings for database-identity checks with no interpolation. |
| Anonymous endpoints | Only `POST /auth/login` (rate-limited) and health checks. |
| Mass assignment on user creation | Sound. Client-supplied `AgencyId` is discarded and replaced with the actor's. |
| Role escalation on user creation | Sound. A Supervisor may create only `CaseManager`s assigned to themselves; a Director may not create an `Admin`; `PlatformOperator` is not an assignable role. |
| By-id lookups | Sound. Every one either carries an `AgencyId` predicate or is followed by a `TenantAccess` check. |
| JWT validation | Sound. Issuer, audience, signing key, and lifetime all validated, HMAC-SHA256, 30-second clock skew. |
| Hardcoded secrets | None found in source, config, or scripts. |
| Error logging | Sound by design. `AppErrorLog` records exception types, HResults, target sites, and XAML positions — never `Exception.Message` — so narratives cannot reach the log through an exception. |
| Cross-tenant incident telemetry | Sound. `IncidentReportRequest` carries only a reference, source, severity, operation, release, exception fingerprint, and timestamp. No message, no PHI. |

### Still not covered

Azure identity and key management, and the desktop client's local database posture. The local
posture is the weaker of the two: a desktop Production install trusts whoever holds Windows
credentials on the machine, and local services are scoped by convention rather than enforced
tenancy, on the reasoning that anyone who can reach the database directly has already won.
That reasoning holds for a single-operator install and stops holding the moment a local
database is shared between workstations.
