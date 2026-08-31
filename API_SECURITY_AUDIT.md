# API security audit — 2026-08-14, 2026-08-15, 2026-08-30, 2026-08-31

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

- **Tenant scoping across all 112 protected routes.** Every route taking a caller-supplied `userId`
  (`/caseload`, `/notes/monthly`, `/notes/day`, `/at-requests`, `/reviews`, `/incentives`) gates on
  `TenantAccess.CanAccessUserAsync` before use. Administration routes check the current database-
  resolved administration permission and
  scope every query to `actor.AgencyId`. `ValidatedActorFilter` re-confirms the claimed identity and
  agency and resolves current permissions from the database on every request, so a token does not
  preserve a revoked permission.
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
| Permission escalation on user creation | Sound. A user with supervision but not administration permission may create only a case-management-only user assigned to themself; `PlatformOperator` is not an assignable agency permission set. |
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

---

## Third pass — 2026-08-30: the per-user permissions conversion

Scope: commit `2bf5187` ("Replace the single role string with per-user permissions"), 56 files.
That commit shipped with an explicit caveat — it verified completeness, deny-by-default, the
resolution point, `PlatformOperator` orthogonality, and the backfill, but **not** that each gate
received the right permission, that no route lost a tenant check, or that the new tests fail
against ungated code. This pass closes exactly those three questions and extends them to the
desktop-local services.

Method: the route inventory was diffed mechanically against `ApiEndpoints.cs` rather than read;
gate coverage was established by **mutation** — each class of gate was replaced with a constant and
the suites re-run, so "covered" below means a test actually failed when the gate was removed.
Baselines: 278 API tests, 907 desktop tests, all green before and after. The worktree was restored
after every mutation.

### Answers to the three open questions

**Did any route lose tenant scoping during the conversion? No.** Every removed line in the commit is
a gate expression or a role comparison; no `AgencyId` predicate and no `TenantAccess` call was
dropped. The nine removed lines that mention `AgencyId` are all paired halves of gate substitutions
and were re-added with the predicate intact. The filter's `IsCurrentActorAsync` call was replaced by
an equivalent inline query on the same three fields.

**Does the route inventory still match the code? The set does; the count sentence does not.**
`API_AUTHORIZATION.md` and `ApiEndpoints.cs` agree on all 114 protected routes with zero differences
in either direction. The prose still said "112 protected routes" in two places. Corrected below.

**Do the new tests fail against ungated code? The billing ones do. Most of the rest do not.** See
the coverage table.

**`Actor.From` still derives from the token alone.** `TokenIssuer` never emits
`sati_validated_permissions`; the only writer is `ValidatedActorFilter`, from the database row. No
caller-supplied value reaches an actor field on any route.

### Gate coverage, established by mutation

| Gate class | Sites | Mutation result |
|---|---|---|
| Billing (API) | 14 | **Covered.** 3 tests fail, including `EveryBillingRouteDeniesAnAdministratorWithoutBillingPermission`, which pins all 14. |
| Administration (API) | 23 | **Partial.** 12 tests fail. Uncovered: `GET /admin/incidents`, `PUT /admin/incidents/{id}/status`, and the escalation guards in `PUT /users/{userId}`, `PUT /users/{userId}/password`, and `ValidateUserRequestAsync`. |
| Supervision (API) | 9 | **Uncovered.** All 278 tests pass with every supervisor gate disabled. |
| Case management (API) | 3 plus the new `TenantAccess` clauses | **Uncovered.** All 278 pass with them disabled. |
| `ProviderDirectoryRules.CanDeleteOrMerge` | 4 | **Covered.** 2 tests fail. |
| `ProviderDirectoryRules.CanCreateOrEdit` | 7 | **Uncovered.** |
| Desktop billing actor validation | 1 | **Covered.** `BillingUsesTheCurrentPermissionInsteadOfTheRoleLabel` fails. |
| Desktop `SettingsAccessPolicy` | 1 | **Covered.** 4 cases fail. |
| Desktop reviewer gate (`SupervisorService` / `LocalTenantAccess.IsReviewer`) | 3 | **Covered.** `ReviewIsRefusedAcrossAssignmentAndAcrossAgency` fails. |
| Desktop `AdminService`, `PersonService`, `ComprehensiveAssessmentService`, `LocalTenantAccess` self-access | 4 | **Uncovered.** All 907 pass with all four disabled. |

`Sati.Tests/UserPermissionTests.cs` tests the rule helpers and the migration SQL, not the gates. It
passes whether or not any route is gated, which is correct for what it is but does not discharge the
project rule.

Important qualifier on the supervision result: removing those nine gates does not by itself open a
hole on most routes, because the queries beneath them are separately data-scoped
(`SupervisorId == actor.UserId`, `owner.AgencyId == actor.AgencyId`), so a case manager sees an
empty result rather than someone else's. The exception is `POST /users`, where the gate is the only
thing standing between a case manager and creating users. "Untested" and "vulnerable" are different
claims, and only the first is established for the other eight.

### Findings

#### 3. The Director backfill is a privilege increase, not a preservation — FIXED 2026-08-31

`Director` maps to `7` (case management, supervision, **administration**). Before this commit every
administration gate read `actor.Role != "Admin"`, which denied Director;
`ProviderDirectoryRules.CanDeleteOrMerge` was `role is "Admin"`, which denied Director; and the
desktop `SettingsAccessPolicy` was `role == UserRole.Admin`, which denied Director. After it, every
existing Director passes all of them.

Concretely, on upgrade a Director gains: the agency audit trail and the audit CSV export; the admin
overview, operations, activity, people, and schema-drift reports; destructive test-data deletion;
the Demo SSN seed; both admin incident routes; agency settings writes; person history and its PDF;
provider deletion and merge; and the ability to create people carrying the test-data marker.

The commit message says "The backfill maps existing roles to their current access rather than to a
narrower set. Nobody loses anything on upgrade." Both sentences are true and neither is the issue —
nothing records that Director *gains* twenty-five gates. `DECISIONS.md` states the mapping
("Director adds administration") without characterising it as an increase.

This needs a decision, not a patch: either accept it explicitly in `DECISIONS.md` with the list
above, or map Director to `CaseManagement | Supervision` and let agencies grant administration
deliberately. It should not ship as an unremarked side effect of a backfill.

#### 4. "Only an administrator may create or assign an administrator" was dropped — FIXED 2026-08-31

The old `ValidateUserRequestAsync` carried two rules. The Supervisor rule survived the conversion;
the second did not:

    - if (actor.Role == "Director" && request.Role == "Admin")
    -     errors["role"] = ["Only an administrator may create or assign an administrator."];

There is no equivalent in the new code. Combined with finding 3, every former Director can now mint
full administrators and grant billing permission.

The general form is worse than the Director case. `PUT /users/{userId}` resolves the target as any
user in the actor's agency, including the actor, and the only remaining guard is skipped outright
for anyone holding administration. An administration-only user may therefore edit their own record
and self-grant `Billing`. The separation this whole change exists to create — billing access that
does not carry administrative access, and vice versa — is enforced in one direction only. No test
covers any of this.

#### 5. Desktop-local user management is enforced in the view model — FIXED 2026-08-31

`UserService.CreateAsync` and `UserService.UpdateAsync` take a `User`, write it, and check nothing:
no actor parameter, no permission gate, no agency scoping. `UpdateAsync` copies all scalar values
through `CurrentValues.SetValues`, `Permissions` and `Role` included.

Before this commit that was tolerable, because `NewUserViewModel` hard-coded `UserRole.CaseManager`
into `User.Create(...)` — the desktop path could not express anything else. It now assembles an
arbitrary permission set from four checkboxes, and the only restraint is
`CanAssignExpandedPermissions`, a view-model boolean. `UserManagementViewModel.SaveAsync` likewise
holds the supervisor restriction in the view model.

On local Production there is no API behind these, so this is the whole enforcement. It contradicts
the engineering rule "Do not use UI visibility as security", the class comment on `LocalTenantAccess`
("local services must not assume the API is their only caller"), and the AGENDA item checked off in
this same commit: "UI visibility follows the permission, but the API enforces independently."

The fix is the one the billing services already took in this commit: `CreateAsync` and `UpdateAsync`
should accept an `AgencyActor` and re-confirm it, mirroring `ValidateUserRequestAsync`.

#### 6. Four caseload routes scope by owner without an agency predicate — pre-existing, open

`GET /people/{personId}/journal`, `PUT /people/{personId}`,
`PUT /people/{personId}/contacts/{contactId}`, and `DELETE /contacts/{contactId}` filter on
`person.UserId == actor.UserId` instead of calling `TenantAccess.OwnsPersonAsync`. They therefore
omit `person.AgencyId == actor.AgencyId` and, now, the new `actor.HasCaseManagerPermissions` clause
that `OwnsPersonAsync` gained in this commit.

**Not a conversion regression** — the commit does not touch those lines. Same class as the
`POST /at-requests` item already accepted above, and not exploitable while a person's agency agrees
with its owner's. Recorded here because the conversion added a case-management requirement to
`OwnsPersonAsync` that these four routes silently do not inherit.

#### 7. The validated-permissions claim is shadowable by construction — low, open

`ValidatedActorFilter` appends `sati_validated_permissions` to the existing `ClaimsIdentity`, and
`Actor.From` reads it with `FindFirstValue`, which returns the *first* match. Today this is safe:
`TokenIssuer` never emits that claim and the JWT is server-signed, so the filter is the only writer.
It is one issued claim away from a token value silently outranking the database value. Prefer
`HttpContext.Items`, or assert the claim is absent before adding it.

### Also corrected in this pass

- `API_AUTHORIZATION.md`: the two "112 routes" statements are now 114. The table itself was already
  complete and correct.
- `API_AUTHORIZATION.md`: the `POST /people` row did not mention the case-management permission the
  conversion added.

### Noted, no change

- **Signed documents freeze a derived label.** `SignedByRole = actor.Role` on AT attestations and the
  agency-release PDF now stamp `LegacyLabel(permissions)`, which collapses to
  Admin/Supervisor/CaseManager. A billing-only signer is stamped "CaseManager". These are regulatory
  artifacts and the label no longer describes the signer's authority. Consider
  `UserPermissionRules.Describe(...)`.
- **Desktop billing actors are session-bound by convention.** `ValidateBillingActorAsync` confirms
  the supplied actor matches a real user row on id, agency, and permissions, but not that it is the
  signed-in user; `BillingService` no longer takes `ISessionService`. Every call site passes
  `sessionService.CurrentUser`. Within one desktop process this is not a boundary, but the binding
  moved from enforced-by-construction to enforced-by-convention.
- **The Supervisor rows in `API_AUTHORIZATION.md` say administration "broadens" supervisory scope.**
  True only for someone who already holds supervision; an administration-only user is refused
  outright. The wording is defensible and the behaviour deliberate, but it is worth knowing.

### Found sound

- All 14 billing routes deny without billing permission; verified by mutation, not by reading.
- Deny-by-default holds throughout. `IsSupported` rejects unknown bits, and the filter refuses a user
  whose persisted set is unsupported.
- `PlatformOperator` remains orthogonal and unmintable: `LegacyLabel` cannot return it, so no user
  management path can produce one, and the allow-list still fails closed.
- Permission changes that cross a legacy-label boundary invalidate outstanding tokens, because the
  filter matches on `Role`. Changes within a label take effect on the next request; test-verified by
  `BillingPermissionRevocationTakesEffectBeforeTheTokenExpires`.

### Outstanding before this ships

1. ~~Decide the Director mapping (finding 3) and record it either way.~~ Done 2026-08-31.
2. ~~Restore an administration-assignment rule (finding 4), with a test that fails without it.~~
   Done 2026-08-31, structurally rather than as a special case.
3. ~~Move desktop user create/update enforcement out of the view models (finding 5).~~
   Done 2026-08-31.
4. Add denial tests for the supervision and case-management gates, and for the two admin incident
   routes. The bar is the project's own: confirm each fails against the ungated code. **Still
   outstanding.**

Items 1, 2, and 3 are the ones that should block a release. The commit's own caveat — "Do not
include this in a release until it has had one" — is discharged as to scope by this pass, and the
answer is that three things need fixing first. See the resolution section below; item 4 remains.

---

## Resolution of findings 3, 4, and 5 — 2026-08-31

All three release blockers are fixed. Design rationale is in `DECISIONS.md`, 2026-08-31; this
records what changed and, more importantly, what proves it.

### What changed

| Finding | Fix |
|---|---|
| 3 — Director gained ~25 gates | New `UserPermissions.AgencyWideSupervision` capability. `Director` backfills to case management + supervision + agency-wide supervision; `Admin` to all five. Administration implies agency-wide supervision. The six sites where administration was standing in for supervisory reach (`canReviewAgency` ×3, `LoadReviewableNoteAsync`, `TenantAccess.CanAccessUserAsync`, `LocalTenantAccess`) now read the capability. Corrected by a second migration, `SeparateAgencyWideSupervision`, rather than an edit to the applied one. |
| 4 — assignment rule dropped | Resolved structurally rather than by re-adding a special case. Director no longer holds administration, so it falls into the non-administrator branch and may write only a case-management-only user assigned to itself — it cannot mint an administrator. The subset "may not grant what you do not hold" rule was considered and deliberately rejected; see `DECISIONS.md` for why it is not a boundary. |
| 5 — desktop enforced in the view model | `UserService.CreateAsync` / `UpdateAsync` / `ResetPasswordAsync` now take an `AgencyActor`, re-confirm it against the database, and delegate to the shared `Sati.Contracts.V1.UserManagementRules`. Agency and the legacy label are assigned server-side rather than accepted. Self-service profile editing moved to `UpdateOwnContactDetailsAsync`, which cannot express a permission change. The view-model booleans remain, demoted to presentation. |

### What proves it

Each new test was run against the unfixed code and confirmed to fail, per the project rule.
Baselines: 287 API tests, 930 desktop tests, green before and after.

| Mutation applied to the fixed code | Result |
|---|---|
| `FromLegacyRole("Director")` reverted to include `Administration` | **8 tests fail** — all seven `TheLegacyDirectorLabelDoesNotReachTheAdministrationRoutes` cases plus `TheLegacyDirectorLabelCannotCreateAnAdministrator`. |
| The three broadening sites reverted to `HasAdminPermissions` | **1 test fails** — `TheLegacyDirectorLabelKeepsAgencyWideSupervisoryReach`, which is the half that proves the narrowing cost Directors nothing. |
| Desktop `Refuse(...)` and `RequireManageable(...)` neutralized | **9 of 16 `LocalUserManagementAuthorizationTests` fail.** |
| Desktop actor re-confirmation weakened *and* the caller-supplied permission set trusted | **`AFabricatedActorPermissionSetIsRefused` fails.** |

One honest note on the last two rows. Weakening `ValidateActorAsync` alone changed nothing,
because it returns the database row and every downstream decision authorizes off that row rather
than the supplied values. `ACaseManagerCannotCreateUsersAtAll` likewise survives either single
mutation, because the precheck and `DescribeGrantRefusal` each independently refuse it. That is
defense in depth working, not weak tests — but it does mean those two gates are individually
non-load-bearing, and a future edit could remove one without any test noticing.

### Still open from the third pass

Findings 6 (four caseload routes scoped by owner without an agency predicate — pre-existing) and
7 (the validated-permissions claim is shadowable by construction — low) are unchanged. The
coverage gaps in the mutation table also remain: the nine supervision gates and the
case-management gates are still largely untested, as are `GET /admin/incidents`,
`PUT /admin/incidents/{id}/status`, and `ProviderDirectoryRules.CanCreateOrEdit`.

### Found while fixing, unrelated to the audit

- **A month-end bug in `NotePipelineTests.ASubmittedPeriodAcceptsNoFurtherClaimLines`.** The test
  seeded its second note at `BillableDate.AddDays(1)` where `BillableDate` is `DateTime.Today`.
  A billing period is keyed by the service date's month and year, so on the last day of a month
  the second note opens a fresh September draft period and the submitted-period guard never fires.
  The suite failed on 2026-08-31 for this reason and would fail on the last day of any month.
  Fixed by keeping both notes in the same period. Pre-existing; nothing to do with permissions.
- **No API route for self-service profile editing.** `CloudUserService.UpdateOwnContactDetailsAsync`
  goes through `PUT /api/v1/users/{id}`, which requires supervision or administration, so against a
  hosted database an ordinary case manager cannot change their own email or phone. Unchanged
  behaviour, surfaced by giving the operation its own name. Tracked in `AGENDA.md`.
