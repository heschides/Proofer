# Sati Repository Security Audit

**Audit date:** September 3, 2026<br>
**Repository revision reviewed:** `1e64cf7d24f299f25b31b21ee124fc69f9d42605`<br>
**Assessment type:** Static repository security review with local build, automated tests, dependency analysis, and secret-pattern review

## Executive summary

This audit identified **7 High, 5 Medium, and 3 Low-severity findings**. No Critical vulnerability, such as unauthenticated remote-code execution or a directly exploitable cross-tenant disclosure, was confirmed.

Sati already contains meaningful security controls: the API revalidates the claimed user, agency, role, and permissions against the database; Entity Framework queries are parameterized; SSNs use authenticated encryption; Demo configuration does not contain SQL credentials; password comparison is constant-time; and CSV formula injection is neutralized. The reviewed source and Git history did not contain an obvious real password, token, private key, account key, or production connection string.

The most important remaining risks concern authorization after permissions are changed, compliance-record integrity, session revocation, local direct-database access, maintenance utilities, and installer authenticity. Until these are addressed, Sati should not be represented as ready for hosted multi-tenant Production or as HIPAA compliant.

### Finding summary

| ID | Severity | Finding | Primary security property |
|---|---|---|---|
| SATI-SEC-001 | High | Local authentication reconstructs permissions from legacy roles | Authorization / least privilege |
| SATI-SEC-002 | High | Twelve API routes ignore the actor's current CaseManagement permission | Authorization / revocation |
| SATI-SEC-003 | High | Compliance forms can be hard-deleted or overwritten without versioning and audit | Integrity / billing compliance |
| SATI-SEC-004 | High | Maintenance utilities lack service-side authorization and export PHI to Desktop | Privilege boundary / data handling |
| SATI-SEC-005 | High | Password changes do not revoke sessions; accounts cannot be safely disabled | Authentication / session lifecycle |
| SATI-SEC-006 | High | Distributed installers are not Authenticode-signed | Software supply chain |
| SATI-SEC-007 | High | Local Production ultimately trusts the Windows account rather than Sati login | Architecture / privilege boundary |
| SATI-SEC-008 | Medium | Tenant isolation is manually repeated rather than structurally enforced | Multi-tenancy |
| SATI-SEC-009 | Medium | Audit immutability, retention, and monitoring controls are incomplete | Auditability / non-repudiation |
| SATI-SEC-010 | Medium | Authenticated API resource consumption is insufficiently bounded | Availability |
| SATI-SEC-011 | Medium | Password hashing work factor is below current OWASP guidance | Credential protection |
| SATI-SEC-012 | Medium | Dependency and build-chain governance is incomplete | Dependency risk / reproducibility |
| SATI-SEC-013 | Low | Switchable-user discovery over-discloses workforce information | Information disclosure |
| SATI-SEC-014 | Low | A tracked SQL maintenance artifact performs an unconditional form update | Operational safety |
| SATI-SEC-015 | Low | Response hardening and security documentation have drifted | Defense in depth |

## Scope and method

The review covered the entire tracked repository, including:

- WPF desktop source, ViewModels, data services, and XAML bindings.
- ASP.NET Core API endpoints, middleware, authentication, authorization, rate limiting, contracts, and persistence.
- Entity Framework models, mappings, migrations, direct SQL, concurrency behavior, and audit/version controls.
- Environment selection, secret handling, SSN encryption, Key Vault integration, and local DPAPI behavior.
- Import, export, CSV, EDI/X12, HTML parsing, filesystem, installer, and PowerShell code.
- NuGet package declarations and resolved dependency graphs.
- Repository and Git-history searches for likely secrets and unsafe primitives.
- Architecture, authorization, operational, regulatory, and environment documentation.

The review mapped findings to common OWASP-style classes, particularly broken access control, authentication failures, insecure design, security misconfiguration, vulnerable components, integrity failures, and insufficient logging/monitoring.

### Validation performed

- `dotnet build Sati.slnx --configuration Release --no-restore`
  - Result: successful, 0 errors and 9 warnings.
- `dotnet test Sati.slnx --configuration Release --no-build --no-restore`
  - Result: 1,513 passed, 1 skipped, 0 failed.
- `dotnet list Sati.slnx package --vulnerable --include-transitive`
  - Result: NuGet reported no vulnerable packages, but also emitted `NU1900` because its advisory feed was unavailable. This result is therefore inconclusive on its own.
- Independent OSV queries for all resolved NuGet assets.
  - Result: 207 unique package/version pairs checked; no OSV advisory was returned as of September 3, 2026.
- Deprecated-package analysis.
  - Result: transitive `Azure.Identity 1.14.2` is deprecated, and transitive `Microsoft.Identity.Client 4.73.1` is deprecated with a critical-bug classification. Direct runtime reachability was not established by this audit.
- Secret-pattern review of tracked source/configuration/scripts and Git history.
  - Result: no obvious real credential, private key, token, account key, or production connection string found.

### Severity model

- **Critical:** Direct and practical system-wide compromise, unauthenticated remote execution, or equivalent catastrophic impact.
- **High:** Practical compromise of PHI, authorization, official records, privileged sessions, or trusted software delivery.
- **Medium:** Important weakness requiring preconditions, limited access, or an additional failure, but capable of significant impact.
- **Low:** Defense-in-depth, information-disclosure, documentation, or operational foot-gun with limited direct exploitability.

## Detailed findings

### SATI-SEC-001 — Local authentication reconstructs permissions from legacy roles

**Severity:** High<br>
**OWASP-style categories:** A01 Broken Access Control; A07 Identification and Authentication Failures<br>
**Likely CWE mappings:** CWE-269 Improper Privilege Management; CWE-863 Incorrect Authorization

#### Evidence

- `Data/AuthService.cs:40-49` constructs the signed-in `User` without passing the stored `userEntity.Permissions` value.
- `Sati.Persistence/Models/User.cs:37-53` initializes omitted permissions using `UserPermissionRules.FromLegacyRole(role.ToString())`.
- `Sati.Contracts/V1/UserPermissions.cs:79-86` maps legacy roles to broad default permission sets:
  - CaseManager becomes CaseManagement.
  - Supervisor becomes CaseManagement plus Supervision.
  - Director also gains AgencyWideAccess.
  - Admin gains all permissions.
- `ViewModels/ShellViewModel.cs:104-120` uses the reconstructed session permissions to expose feature workspaces.
- Local services such as `Data/PersonService.cs:22-29` authorize writes using the current in-memory session permission.
- `Data/DhhsFormService.cs` can reveal decrypted SSNs for owned clients but does not independently confirm the actor's current database CaseManagement permission.

#### Exploit scenario

An agency changes a case manager to billing-only but leaves the legacy `Role` value and caseload assignment in place during reassignment. At the next Local Production login, Sati discards the billing-only permission set and reconstructs CaseManagement access from the role. The user can regain case-management functions that the administrator intended to revoke, including access to sensitive client data where a local service trusts the reconstructed session.

The inverse failure also occurs: a billing-only permission can disappear when the role is reconstructed. This is both a privilege-escalation and an authorization-consistency defect.

#### Remediation

1. Preserve the exact persisted `Permissions` value when creating the local authenticated session.
2. Use a dedicated safe session projection that includes identity, role, agency, and exact permissions but never password hash or salt.
3. Revalidate the actor's current database identity, agency, and permissions inside local services that reveal SSNs or alter sensitive records.
4. Add tests for billing-only, administration-only, supervision-only, CaseManagement-plus-billing, and other non-legacy combinations.
5. Mutation-test the user factory so the tests fail if the permission argument is removed again.

### SATI-SEC-002 — CaseManagement revocation is ineffective on twelve API routes

**Severity:** High<br>
**OWASP-style category:** A01 Broken Access Control<br>
**Likely CWE mappings:** CWE-862 Missing Authorization; CWE-863 Incorrect Authorization

#### Evidence

`Sati.Api/Security/TenantAccess.cs:62-76` contains the correct `OwnsPersonAsync` guard. It requires current CaseManagement permission, current identity, record ownership, and matching agency. The following routes instead use partial owner predicates and do not require the actor's current CaseManagement permission:

- `Sati.Api/ApiEndpoints.cs:1183-1195` — get person journal.
- `Sati.Api/ApiEndpoints.cs:1197-1229` — update person journal.
- `Sati.Api/ApiEndpoints.cs:1238-1274` — add journal entry.
- `Sati.Api/ApiEndpoints.cs:1339-1360` — update person.
- `Sati.Api/ApiEndpoints.cs:1907-1929` — update contact.
- `Sati.Api/ApiEndpoints.cs:1933-1949` — delete contact.
- `Sati.Api/ApiEndpoints.cs:2940-2962` — update note.
- `Sati.Api/ApiEndpoints.cs:3033-3055` — delete note.
- `Sati.Api/ApiEndpoints.cs:3122-3139` — get notes by year.
- `Sati.Api/ApiEndpoints.cs:3142-3157` — abandon overdue notes.
- `Sati.Api/ApiEndpoints.cs:4268-4295` — delete forms.
- `Sati.Api/ApiEndpoints.cs:4298-4325` — update form.

`ValidatedActorFilter` correctly reloads the actor's current permissions. The vulnerability occurs because these routes ignore those validated permissions.

#### Exploit scenario

A user's CaseManagement permission is revoked while clients remain assigned to that user. With a still-valid or newly issued token, the actor can continue reading journals and notes and changing people, contacts, notes, and forms. The routes see that the person is still assigned to the actor but do not check that the actor is still allowed to perform case-management work.

#### Remediation

1. Require both feature capability and record-level scope for every client-owned operation.
2. Replace handwritten owner predicates with the centralized `TenantAccess.OwnsPersonAsync` rule or a successor with equivalent checks.
3. Group endpoints behind feature-specific authorization policies so a missing capability check is difficult to introduce.
4. Make permission removal atomically reassign the caseload or immediately suspend record access.
5. Add tests that revoke CaseManagement while ownership remains and assert that all twelve routes deny access.

### SATI-SEC-003 — Compliance forms can be erased or silently rewritten

**Severity:** High<br>
**OWASP-style categories:** A01 Broken Access Control; A04 Insecure Design<br>
**Likely CWE mappings:** CWE-284 Improper Access Control; CWE-367 Time-of-check Time-of-use Race Condition

#### Evidence

- `Sati.Api/ApiEndpoints.cs:4268-4295` permits bulk hard-deletion of up to 100 forms using `ExecuteDeleteAsync`.
- The deletion path lacks a current CaseManagement check, agency check, state restriction, audit event, immutable version, amendment, or revision token.
- `Sati.Api/ApiEndpoints.cs:4298-4325` overwrites completion/open dates without optimistic concurrency or audit history.
- `Sati.Api/Data/ApiDbContext.cs:532-547` shows that `ServerForm` has no `Revision` property.
- `Sati.Contracts/V1/BillingComplianceGate.cs:42-84` evaluates forms that are present. It does not independently reconstruct missing required cycles.
- Claim creation loads the forms that remain at `Sati.Api/ApiEndpoints.cs:4030-4039` before applying the gate.
- Local `Data/FormService.cs:18-43` also performs update/removal operations without comparable authorization, audit, or concurrency protection.

#### Exploit scenario

A user deletes an overdue required form and then creates a billing claim. Because the deleted requirement is absent from the collection, the billing compliance gate may have no overdue form from which to produce a blocking reason. A user can also overwrite completion dates, intentionally or through a stale screen, without retaining the previous official value.

#### Remediation

1. Remove the general-purpose deletion route if, as current call-site review suggests, it is unused by the normal UI.
2. For legitimate corrections, use audited tombstones, immutable versions, or amendments instead of hard deletion.
3. Reconstruct required compliance cycles independently of existing rows and fail closed when a required record is absent.
4. Add a revision token to forms and return a typed `409 Conflict` on stale updates.
5. Audit every form completion, reset, amendment, administrative repair, and deletion-like state transition.
6. Use the shared Maine business-date abstraction instead of direct `DateTime.Today` calculations.

### SATI-SEC-004 — Maintenance utilities lack service-side authorization and export PHI to Desktop

**Severity:** High<br>
**OWASP-style categories:** A01 Broken Access Control; A04 Insecure Design; A09 Security Logging and Monitoring Failures<br>
**Likely CWE mappings:** CWE-602 Client-Side Enforcement of Server-Side Security; CWE-200 Exposure of Sensitive Information

#### Evidence

- `Data/FormBulkCompletion.cs:51-88` loads and can modify forms across users and agencies without accepting or validating an actor, permission, or agency boundary.
- `Data/FormDueDateBackfill.cs:74-125` loads people and forms across the database with the same missing service boundary.
- `Views/SettingsWindow.xaml:1020-1110` hides the maintenance UI based on `CanManageAgencySettings`.
- `ViewModels/SettingsViewModel.cs:446-583` invokes the operations without repeating authorization in the command/service path.
- `Data/FormBulkCompletion.cs:124-134` writes client full names, form types, and dates to the Windows Desktop.
- `Data/FormDueDateBackfill.cs:238-269` writes person identifiers and dates to the Desktop.
- `OPERATIONS.md:241-266` says PHI-bearing exports must not be written to Desktop, Documents, or consumer synchronization roots.

#### Exploit scenario

A modified client, reflected command invocation, or future UI wiring bypasses the hidden tab and invokes a mass compliance update. Even when an authorized administrator runs only a dry run, the generated report can be automatically uploaded by consumer OneDrive or another Desktop synchronization service.

#### Remediation

1. Delete one-time migration or backfill utilities after their controlled use.
2. If retained, require and revalidate an administrative `AgencyActor` in the service itself.
3. Scope every query to the authorized tenant and audit all mutations.
4. Require explicit dry-run review, backup verification, and legal-hold checks before execution.
5. Write reports to a protected `%LOCALAPPDATA%\Sati\OperationalReports`-style directory, redact names when possible, and reject known consumer synchronization paths.

### SATI-SEC-005 — Password changes do not revoke sessions, and accounts cannot be safely disabled

**Severity:** High<br>
**OWASP-style category:** A07 Identification and Authentication Failures<br>
**Likely CWE mappings:** CWE-613 Insufficient Session Expiration; CWE-620 Unverified Password Change

#### Evidence

- `Sati.Api/Security/TokenIssuer.cs:15-35` emits a JWT `jti` but does not create a server-side session or revocation record.
- `Sati.Api/Infrastructure/ApiOptions.cs:5-12` defaults to a 30-minute token with a renewable session lasting as long as 720 minutes.
- `Sati.Api/Program.cs:37-40` permits configuration up to 1,440 minutes.
- `Sati.Api/ApiEndpoints.cs:777-804` renews sessions based on authentication time and current user/agency existence, but has no password/security-stamp comparison.
- `Sati.Api/ApiEndpoints.cs:891-930` replaces password hashes without changing a token version or invalidating sessions.
- `Sati.Api/Data/ApiDbContext.cs:448-460` has no `IsActive`, `DisabledAt`, or security-stamp field on `ServerUser`.
- `Sati.Contracts/V1/UserManagementRules.cs:69-71` disallows an empty permission set, so it cannot serve as a normal account-disable operation.
- `AGENDA.md` separately tracks session revocation and MFA as unfinished work.

#### Exploit scenario

An attacker obtains an administrator's bearer token. The administrator or help desk resets the password, but the stolen token remains valid and can still be renewed until the maximum session age. Offboarding also lacks a clear operation that immediately prevents all future authentication and token use.

#### Remediation

1. Add `IsActive` and a `SecurityStamp` or monotonically increasing `TokenVersion` to users.
2. Increment the version on password resets, role/permission/agency changes, account disablement, and other security-sensitive events.
3. Compare the token version on every validated request and renewal.
4. Store hashed refresh/session records with device metadata and support per-session and all-session revocation.
5. Require MFA for privileged roles and reauthentication for SSN reveals, exports, password changes, and administration.
6. Prefer a mature OIDC identity provider such as an appropriate Microsoft Entra offering rather than expanding custom password/session infrastructure indefinitely.

### SATI-SEC-006 — Distributed installers are not Authenticode-signed

**Severity:** High<br>
**OWASP-style category:** A08 Software and Data Integrity Failures<br>
**Likely CWE mapping:** CWE-494 Download of Code Without Integrity Check

#### Evidence

- `installer/README.md:70` explicitly states that generated installers are not code-signed and will show an Unknown Publisher warning.
- `installer/Build-LocalInstaller.ps1:139-141` produces an adjacent SHA-256 file.
- `installer/Sati.LocalBootstrap/Program.cs:20-36` extracts an embedded payload and executes PowerShell with `ExecutionPolicy Bypass`.
- The LocalDB prerequisite's Microsoft signature is checked, but this does not authenticate the outer Sati installer or its application payload.

#### Exploit scenario

An attacker who can modify a distribution share, email attachment, or transfer location replaces the installer and its adjacent checksum. A user has already been trained to expect an Unknown Publisher warning, accepts it, and executes attacker-controlled PowerShell on a workstation that may contain PHI and Local Production database files.

#### Remediation

1. Authenticode-sign the outer installer and shipped executables/scripts using a protected organizational certificate or managed signing service.
2. Timestamp signatures and make signature verification part of release publication and acceptance testing.
3. Have the bootstrapper verify a signed manifest or payload signatures before execution.
4. Distribute releases through an access-controlled HTTPS/update channel.
5. Treat the adjacent checksum only as accidental-corruption detection unless the expected checksum is distributed through an independent authenticated channel.

### SATI-SEC-007 — Local Production trusts the Windows account rather than Sati login

**Severity:** High<br>
**OWASP-style categories:** A01 Broken Access Control; A04 Insecure Design<br>
**Likely CWE mappings:** CWE-250 Execution with Unnecessary Privileges; CWE-269 Improper Privilege Management

#### Evidence

- `Data/DataEnvironment.cs:47-61` resolves the Local Production SQL connection.
- `App.xaml.cs:390-434` registers local Entity Framework services and connects the WPF client directly to SQL Server/LocalDB.
- `DATABASE_ENVIRONMENTS.md` documents the Local Production EF/SQL path.
- `installer/README.md:7-10` requires Windows Integrated Security.
- `Data/DpapiKeyWrapper.cs:18-27` correctly explains that DPAPI does not protect against another process running as the same Windows user.
- `Data/DpapiKeyWrapper.cs:55,79` uses current-user DPAPI for local SSN key wrapping.

#### Exploit scenario

Malware, a database administration tool, or another person using the same Windows profile connects to the LocalDB database as the Windows user. It bypasses Sati's login, feature permissions, and application audit path. Software executing as that user can also invoke DPAPI and recover the local encryption key.

#### Remediation

1. Continue the planned transition to API-mediated Production; do not distribute SQL credentials or direct database access to clients.
2. Until that transition is complete, require one Windows account per human and prohibit shared profiles.
3. Use BitLocker, protected hibernation settings, endpoint detection, application allowlisting, least-privilege Windows accounts, and protected backups.
4. Restrict database files and principals as far as the local architecture permits.
5. Document that Windows authentication is the primary Local Production security boundary and that Sati's login cannot defend against the same Windows principal.

### SATI-SEC-008 — Tenant isolation is manually repeated rather than structurally enforced

**Severity:** Medium; potentially High impact if combined with a missed predicate<br>
**OWASP-style category:** A01 Broken Access Control<br>
**Likely CWE mapping:** CWE-863 Incorrect Authorization

#### Evidence

- `Sati.Api/Data/ApiDbContext.cs` contains no global tenant `HasQueryFilter`.
- API routes manually repeat `AgencyId` and owner predicates.
- Several routes identified in SATI-SEC-002 omit an explicit person-agency check and rely on ownership consistency.
- The database model does not structurally guarantee in every relationship that a person's `AgencyId` matches the owner's agency.
- `DATABASE_ENVIRONMENTS.md:117-118` records broad `db_datareader` and `db_datawriter` access for the Demo API identity.
- `AGENDA.md` still tracks the tenant model and centralized tenant query enforcement as open work.

#### Exploit scenario

An inconsistent record enters through an import or repair operation, or a future developer forgets one agency predicate. Because the API database identity can see every tenant row, the application mistake can become a cross-tenant read or write.

No stable direct cross-tenant exploit was confirmed in the current source, so this is rated Medium rather than High.

#### Remediation

1. Introduce a scoped tenant context and fail-closed global query filters or a repository boundary.
2. Add database constraints or triggers for denormalized agency ownership relationships.
3. Consider SQL row-level security with server-controlled session context as defense in depth.
4. Use a separate principal and code path for cross-tenant platform support.
5. Test with intentionally inconsistent tenant data and mutation-test removed agency predicates.

### SATI-SEC-009 — Audit immutability, retention, and monitoring controls are incomplete

**Severity:** Medium; High impact for official records<br>
**OWASP-style categories:** A08 Software and Data Integrity Failures; A09 Security Logging and Monitoring Failures

#### Evidence

- `Sati.Api/Data/ApiDbContext.cs:416-443` prevents tracked EF updates/deletes of selected immutable records.
- `Sati.Persistence/SatiContext.cs:49-76` implements a similar local protection.
- Those protections can be bypassed by direct SQL, bulk database operations, or a compromised database identity.
- `OPERATIONS.md:69-71` describes database-level denial of update/delete as the intended control.
- `OPERATIONS.md:288-296` records that the grants/denials, external monitoring, and restore drills are not yet fully applied.
- Form deletion/update and several other sensitive routes do not create corresponding audit events.
- Legal-hold enforcement remains deferred; policy text alone does not enforce retention.

#### Exploit scenario

A compromised runtime identity, same-profile Local Production user, or future bulk operation modifies or deletes audit/compliance evidence outside the tracked EF path. Missing external alerts delay detection, while incomplete retention and restore controls make reconstruction uncertain.

#### Remediation

1. Apply database-level `DENY UPDATE, DELETE` to immutable audit/version tables for runtime identities.
2. Use a separate insert-only stored procedure or principal if necessary.
3. Replicate critical audit records to an independently controlled append-only or WORM-capable destination.
4. Audit sensitive reads, deletes, SSN reveals, exports, overrides, permission changes, and administrative repairs.
5. Implement legal-hold enforcement, external alert routing, and recurring restore drills before hosted Production.

### SATI-SEC-010 — Authenticated API resource consumption is insufficiently bounded

**Severity:** Medium<br>
**OWASP-style category:** A04 Insecure Design<br>
**Likely CWE mapping:** CWE-400 Uncontrolled Resource Consumption

#### Evidence

- `Sati.Api/Program.cs:111-136` configures a named limiter for login.
- The API does not apply an equivalent global/per-user concurrency and request limiter across authenticated endpoints.
- `Sati.Api/ApiEndpoints.cs:3811-3849` accepts a billing response document after checking only that it is nonblank.
- `Sati.Contracts/V1/ClaimResponseReader.cs:283-287` splits the full document into nested arrays, creating multiple in-memory copies.
- Several caseload endpoints materialize complete note and form sets rather than using bounded pagination/projections.
- No endpoint-specific business limit for X12 characters, segments, elements, or claim count was found.

#### Exploit scenario

An authenticated billing user repeatedly submits large, near-server-limit JSON/X12 documents. JSON deserialization, string storage, and repeated split operations consume CPU and memory. Concurrent submissions can reduce availability for all tenants. Large caseload queries create a similar, less concentrated risk.

#### Remediation

1. Establish business-derived endpoint request limits substantially below general server defaults.
2. Use a bounded or streaming X12 parser.
3. Limit segments, elements, claims, and nesting before persistence.
4. Apply per-actor and per-IP rate/concurrency partitions with bounded queues.
5. Propagate cancellation and timeouts through expensive work.
6. Paginate large record collections and project only necessary fields.

### SATI-SEC-011 — Password hashing work factor is below current OWASP guidance

**Severity:** Medium<br>
**OWASP-style category:** A02 Cryptographic Failures<br>
**Likely CWE mapping:** CWE-916 Use of Password Hash With Insufficient Computational Effort

#### Evidence

- `Sati.Api/Security/PasswordVerifier.cs:8-12` uses PBKDF2-HMAC-SHA256 with 100,000 iterations, a 16-byte salt, and a 32-byte result.
- `Data/PasswordHasher.cs:10-12` uses the same parameters locally.
- Fixed-time comparison and the unknown-user decoy path are positive controls.
- `Sati.Api/ApiEndpoints.cs:5038` permits passwords as short as eight characters.
- Current OWASP Password Storage Cheat Sheet guidance recommends 600,000 iterations when PBKDF2-HMAC-SHA256 is required.

#### Exploit scenario

If a database or backup is stolen, the 100,000-iteration work factor makes each offline guess approximately six times cheaper than the cited current PBKDF2 baseline. User-selected eight-character passwords can further reduce resistance.

#### Remediation

1. Prefer Argon2id when platform and compliance constraints allow it.
2. Otherwise raise PBKDF2-HMAC-SHA256 to a benchmarked contemporary work factor, currently at least the OWASP baseline.
3. Store an algorithm/version/work-factor identifier with every hash and rehash after successful login.
4. Encourage long passphrases and check proposed passwords against a breached-password corpus.
5. Add MFA rather than relying on increasingly complex composition rules.

Reference: <https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html>

### SATI-SEC-012 — Dependency and build-chain governance is incomplete

**Severity:** Medium<br>
**OWASP-style categories:** A06 Vulnerable and Outdated Components; A08 Software and Data Integrity Failures

#### Evidence

- The reviewed project files pin direct NuGet package versions, including `Sati.csproj:102-117`, `Sati.Api/Sati.Api.csproj:13-17`, and `Sati.Persistence/Sati.Persistence.csproj:13-18`.
- The built-in NuGet vulnerability check emitted `NU1900` because it could not reach its advisory source.
- An independent OSV check returned no advisories for 207 resolved package/version pairs as of the audit date.
- Resolved dependency graphs include deprecated transitive `Azure.Identity 1.14.2` and `Microsoft.Identity.Client 4.73.1`; the latter is marked deprecated for critical bugs.
- The repository has no `packages.lock.json`, `Directory.Packages.props`, `global.json`, or tracked CI workflow.

#### Exploit scenario

A restore resolves a changed transitive graph, or a newly published advisory is missed because the advisory service remains unavailable. Without locked restore and CI enforcement, a developer or release machine can build a dependency graph that was not reviewed or tested.

No currently exploitable dependency vulnerability was confirmed, so this finding does not claim that the deprecated transitive libraries are reachable in a vulnerable way.

#### Remediation

1. Add lock files and enforce locked restore for release builds.
2. Centralize versions with `Directory.Packages.props`.
3. Pin the supported SDK through `global.json`.
4. Add CI for restore, build, tests, NuGet and OSV advisory checks, static analysis, secret scanning, and SBOM generation.
5. Upgrade or remove the dependency chain resolving the deprecated identity libraries.
6. Keep development/design-time packages out of runtime dependency graphs where possible.

### SATI-SEC-013 — Switchable-user discovery over-discloses workforce information

**Severity:** Low<br>
**OWASP-style category:** A01 Broken Access Control<br>
**Likely CWE mapping:** CWE-200 Exposure of Sensitive Information

#### Evidence

- `Sati.Api/ApiEndpoints.cs:819-830` returns all switchable users in the actor's agency to any authenticated actor.
- `Sati.Contracts/V1/ContractMapper.cs:25-34` maps the full user profile.
- `Sati.Contracts/V1/Contracts.cs:14-23` includes username, role, permissions, supervisor identifier, agency, email, and phone.

#### Exploit scenario

A compromised low-privilege account enumerates privileged usernames, roles, permissions, reporting relationships, and contact information. This improves password-spraying and targeted-phishing accuracy.

#### Remediation

Return a narrow purpose-built DTO containing only the fields required for user switching, document the intended audience, and gate broader directory data behind a justified permission. Monitor unusual repeated enumeration.

### SATI-SEC-014 — Tracked SQL artifact performs an unconditional form update

**Severity:** Low<br>
**OWASP-style category:** A04 Insecure Design

#### Evidence

`SQLQuery2.sql:1-2` contains an unconditional `UPDATE Forms SET IsCompliant = 1` with no `WHERE` clause, transaction, database-identity assertion, environment guard, or dry-run mode.

The current schema may no longer contain that column, reducing immediate applicability, but the script remains dangerous against compatible historical databases.

#### Exploit scenario

An operator runs the file in the wrong query window or against an older database. Every form is marked compliant, destroying the reliability of compliance reporting.

#### Remediation

Delete obsolete operational SQL from the repository. If a repair script is required, give it a descriptive name and include database-identity validation, an explicit transaction, a preview query, backup requirements, and a narrowly scoped predicate.

### SATI-SEC-015 — Response hardening and security documentation have drifted

**Severity:** Low<br>
**OWASP-style categories:** A05 Security Misconfiguration; A09 Security Logging and Monitoring Failures

#### Evidence

- `PreventSensitiveResponseCaching` in `Sati.Api/ApiEndpoints.cs:5284-5288` is applied to selected routes rather than globally to authenticated PHI responses.
- `Sati.Api/Program.cs:162-171` performs HTTPS redirection but does not configure application HSTS. Azure/App Service edge configuration may provide it, but that was outside this repository audit.
- `Sati.Api/Security/Actor.cs:13-27` reads the first validated-permission claim.
- `Sati.Api/Security/TenantAccess.cs:103-109` appends a validated claim rather than first removing any existing claim of the same type. The current token issuer does not emit that claim, so this is hardening against a future duplicate-claim regression rather than a current exploit.
- The actor's display name can remain token-derived while role, agency, and permissions are database-revalidated, allowing audit labels to be stale after an administrative rename.
- `API_AUTHORIZATION.md`, `API_SECURITY_AUDIT.md`, `Data/DhhsFormService.cs` comments, `AGENDA.md`, and the current route behavior contain contradictory statements about capability enforcement and local SSN behavior.

#### Exploit scenario

An intermediary or client cache retains sensitive JSON longer than expected, or future token changes introduce a duplicate permission claim that shadows the database-validated value. Separately, maintainers rely on inaccurate security documentation and repeat an already-known authorization error.

#### Remediation

1. Apply `Cache-Control: no-store` and related headers centrally to authenticated sensitive responses.
2. Configure and verify HSTS at either the edge or application, and document the authoritative location.
3. Remove all existing validated-permission claims before inserting the database-derived claim, or store the validated actor in `HttpContext.Items` rather than claims.
4. Populate audit identity display data from the validated database row.
5. Update authorization inventories and SSN comments immediately after correcting the implementation.

## Positive controls observed

The audit did not identify a confirmed SQL injection, command injection, path traversal, SSRF, XXE, unsafe binary deserialization, browser XSS/CSRF surface, custom certificate-validation bypass, or committed production credential.

Specific positive controls include:

- `ValidatedActorFilter` reloads the claimed user and confirms identity, role, agency, and permissions on each API request.
- JWT validation checks issuer, audience, signature, and lifetime with a short clock skew.
- Login uses per-username guarding, a global rate limiter, a missing-user decoy hash, and fixed-time comparison.
- Entity Framework LINQ queries are parameterized. The reviewed dynamic backup SQL escapes identifiers and literals, and its inputs are configuration/application-owned rather than caller-controlled.
- API SSNs use AES-GCM envelopes bound to tenant, record, and field context, with keys protected through Azure Key Vault and managed identity.
- Local SSN key wrapping uses current-user DPAPI and accurately documents the same-Windows-user limitation.
- Demo environment mapping is hard-coded to an HTTPS API and carries no SQL credential.
- Demo packaging excludes local `appsettings.json`; the file is Git-ignored.
- CSV output uses shared formula-injection neutralization.
- API exception handling avoids returning unrestricted stack traces or sensitive narratives and uses correlation identifiers.
- HTML import uses an inert parser without a resource loader or script execution.
- Cloud EDI downloads normalize filenames before constructing response metadata.

These controls reduce exposure but do not compensate for the authorization and integrity defects described above.

## Recommended remediation sequence

### Immediate: before the next security-sensitive release

1. Fix exact-permission preservation in Local Production authentication.
2. Apply CaseManagement authorization to all twelve affected API routes.
3. Remove form hard deletion and add form concurrency/audit protection.
4. Add regression tests that demonstrate permission revocation works while records remain assigned.
5. Remove or secure the form bulk-completion and due-date-backfill utilities.

### Near term: before hosted Production or broader PHI use

1. Implement account disablement, token-version revocation, and server-side session records.
2. Require MFA for privileged access and step-up authentication for sensitive actions.
3. Authenticode-sign installers and verify payload authenticity.
4. Establish a structural tenant enforcement layer and corresponding database constraints.
5. Apply database-level immutability permissions and external audit alerting.
6. Bound request sizes, concurrency, X12 parsing, and large result sets.

### Platform hardening

1. Complete the API-mediated Production transition.
2. Enforce legal holds, retention jobs, restore drills, and independent monitoring.
3. Modernize password hashing with versioned migration.
4. Lock and continuously scan dependencies; generate an SBOM for releases.
5. Narrow workforce discovery responses and remove obsolete operational SQL.
6. Reconcile security documentation with executable behavior after every security fix.

## Limitations

This was a source and local-build assessment. It did not include:

- Dynamic penetration testing against a deployed API.
- Live Azure resource, managed-identity, Key Vault, SQL grant, networking, App Service, firewall, or log-routing inspection.
- Installer distribution-share access-control testing or certificate validation because the installer is currently unsigned.
- Endpoint forensics, malware analysis, or Windows workstation configuration review.
- Review of real Production data or PHI.
- A HIPAA, MaineCare, OADS, contractual, or legal compliance determination.

Passing tests demonstrate that expected behavior remains internally consistent; they do not prove that the expected behavior is secure. Similarly, a point-in-time dependency scan cannot prove that dependencies are free of undisclosed vulnerabilities.

## Conclusion

Sati has a substantially stronger foundation than a typical early desktop-to-cloud migration: it has safe network contracts, per-request actor validation, encrypted sensitive fields, distinct environments, versioning for several important records, and broad automated tests. The remaining problems are nevertheless material because they occur at the exact boundaries that determine who may access PHI and whether clinical or billing evidence can be trusted.

The permission-reconstruction defect, API permission-revocation gaps, and mutable/deletable compliance forms should be treated as the first engineering priority. Session revocation, signed delivery, structural tenant isolation, and database-enforced audit immutability are the next prerequisites for a defensible hosted Production boundary.
