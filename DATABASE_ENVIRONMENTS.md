# Production and Demo Data Environments

Sati maintains deliberately separate Production and Demo identities. This separation protects
real working data during development and prepares the synthetic Demo environment for Azure.

| Startup choice | Transport | Required target | Required marker |
|---|---|---|---|
| `My work` | Local EF/SQL (`SatiProduction`) | `SatiProduction` | `Production` |
| `Demo` | HTTPS API | `https://sati-demo-api-satilogica.azurewebsites.net/` | `SatiDemo` / `Demo` |

The environment chooser is the first window shown, before the splash screen and before Sati builds
its service host or opens a database connection. User credentials never select or redirect the
database. Local Production startup fails before migrations or authentication when the configured
database name or database-resident identity marker does not match the explicit startup selection.
Demo builds no EF context and never receive an Azure SQL connection string; the API validates the
database marker during its own startup. Closing the chooser opens neither environment and exits Sati.

## Local environments

The provisioning script preserves the original mixed `Sati` database, creates a checked backup,
restores isolated copies, and filters them by owning user's agency:

```powershell
.\scripts\Provision-LocalDataEnvironments.ps1
```

- `SatiProduction` retains production-owned users and clients.
- `SatiDemo` retains the synthetic demonstration users and clients.
- Ownership follows `Person.UserId -> User.AgencyId`; the denormalized `Person.AgencyId` value is
  not authoritative for this split.
- The original `Sati` database remains unchanged as a source archive.
- Backups are stored under `%LOCALAPPDATA%\Sati\DatabaseSnapshots`.
- The script refuses to overwrite either target database.

During development, either build can be started normally and the data environment selected in the
first window:

```powershell
dotnet run --configuration Debug
```

The `Demo` build configuration remains available when a separately named `Sati.Demo.exe` artifact
is useful, but it also requires an explicit environment selection and does not bypass identity
validation.

## Deployed Demo cloud boundary

The Demo path now uses the required boundary:

```text
Sati.Demo client -> authenticated HTTPS API -> Azure SQL SatiDemo
```

Implemented properties:

- The client contains the HTTPS API address, never an Azure SQL password.
- The API validates Sati credentials and issues a short-lived session token.
- Password hashes and salts remain server-side.
- The API derives user, role, agency, and allowed records from the authenticated session rather
  than trusting caller-supplied IDs.
- The Azure API connects to SQL using managed identity.
- Azure SQL accepts connections from the hosted service boundary, not arbitrary tester devices.
- The Demo client does not register an EF context and does not run database migrations.
- The database retains the `dbo.SatiDatabaseIdentity` marker as an additional environment guard.

Current HTTP-backed Demo workflows are authentication, caseload and person summaries, journals,
case notes, settings reads, scratchpads, exempt dates, incentives, and form state updates. Features
that have not yet received an authorized API endpoint fail explicitly in Demo; they do not fall back
to LocalDB or direct Azure SQL.

## Canonical Demo and nightly reset

Demo migrations define schema. A separate, versioned Demo seed defines the canonical superhero
and sitcom dataset. Do not use production migrations as a nightly data-reset mechanism.

The scheduled reset should:

1. stop or reject new mutations;
2. restore or reseed the canonical Demo data;
3. reset stored demonstration logins;
4. validate tenant ownership, record counts, and the `Demo` marker;
5. record reset success or failure; and
6. reopen mutations only after validation succeeds.

The reset job should run in Azure under its own managed identity with only the permissions needed
for reset operations. Its permissions should be separate from the normal API identity.

## Current Azure Demo database

Provisioned and validated on August 11, 2026:

| Setting | Value |
|---|---|
| Subscription | `Azure subscription 1` |
| Resource group | `rg-sati-demo` |
| Logical server | `sati-demo-satilogica-central.database.windows.net` |
| Database | `SatiDemo` |
| Region | Central US |
| Authentication | Microsoft Entra-only; `Joshua White` is the server Entra administrator |
| Compute | Free-limit General Purpose serverless, `GP_S_Gen5_2`, 0.5 minimum vCore |
| Cost guard | `AutoPause` when the monthly free allowance is exhausted |
| Network | Public endpoint with three exact App Service outbound-IP rules; tester IPs are not allowed |

The imported database was read back through an exact-IP temporary firewall rule and matched the
verified local source: `Demo` identity marker, 15 users, 167 people, and 3,769 notes. On August 12,
2026, the controlled Admin/audit deployment brought the database to 55 migrations, reconciled all
legacy Person and Note ownership to the owning user's agency, and verified zero null or mismatched
tenant assignments. Every temporary import, migration, and validation firewall rule was removed
after use.

The API is hosted on Windows App Service Free F1 in Central US at
`https://sati-demo-api-satilogica.azurewebsites.net/`. Its system-assigned managed identity is a
contained database user with only `db_datareader` and `db_datawriter`. The API uses a managed-
identity connection; its token-signing key is an App Service setting and is not stored in source.
Both `/health/live` and `/health/ready` returned HTTP 200 after the Admin/audit API deployment on
August 12, 2026. Authenticated verification also covered the agency-scoped Admin overview, all 167
Person list rows, first-view lifecycle baseline creation, recent activity, and PDF export; anonymous
Admin access returned HTTP 401.

On August 13, 2026, the additive billing configuration/snapshot migration was applied to Azure
`SatiDemo`, and ten clearly marked synthetic billing scenarios were seeded for Sandbox Mode:
three ready and seven independently blocked. The real billing service verified the expected 3/7
split and partial-unit values before the matching API package was deployed. Post-deploy liveness and
readiness returned HTTP 200, the release endpoint returned 1.2.1, and the new anonymous billing-
configuration request returned HTTP 401. Temporary exact-IP migration rules were removed after use.

Later on August 13, the billing-complete desktop client and API were versioned together as 1.2.2.
The exact 1.2.2 installer passed isolated install, public-configuration, version, 15-second launch,
and cleanup checks. Azure OneDeploy deployment `204b1019a81746508f7e3585723b5ec6`
succeeded for the matching API package; after the normal restart interval, `/health/live` and
`/health/ready` returned HTTP 200 and `/health/version` returned 1.2.2. Authenticated Admin
preflight and clean external-machine evidence remain explicit final Demo gates.

Later on August 13, the tenant-safe incident pipeline was applied as migration 62 and the matching
API/client release 1.2.3 was deployed with OneDeploy deployment
`8e1d1e6de769418c87fc15588237d28d`. Live, ready, and version checks returned HTTP 200/1.2.3. A
separately provisioned `global-admin` account returned the `PlatformOperator` role, read four
cross-agency health rows, and was correctly denied an agency provider route. Its randomly generated
password is retained only in a Windows user-bound encrypted credential file outside the repository.
The temporary migration firewall rule and a stale client-IP rule were removed; only the three App
Service outbound-IP rules remain. The 1.2.3 installer passed isolated version, public-configuration,
15-second launch, responsiveness, and cleanup checks. External-machine and presenter attestations
remain separate final evidence gates.

Later on August 13, release 1.2.5 corrected the missing-telemetry failure mode and clarified the
note review handoff. Guarded migration `20260813210000_AddIncidentScope` was applied to `SatiDemo`
as migration 63; its temporary single-IP firewall rule was verified and removed immediately. API
1.2.5 was deployed with OneDeploy deployment `4d16d2e572094579b0e427f8fdaac55f`.
Live, ready, version, Global Admin role, four-agency visibility, and agency-route denial checks
passed. With no received incidents, platform health now reports No telemetry rather than 100.
The exact 1.2.5 installer passed isolated installation, version 1.2.5.0, 15-second responsiveness,
and cleanup checks; SHA-256 is
`86a16127cb595cae70af8e709de681e2afabf00561aed65b5decb2f196a17737`.

Release 1.2.6 follows 1.2.5 with two focused corrections: the desktop defers its second
save-on-close request until the Windows dispatcher is idle, and the platform operator may use
only the authenticated self-service password endpoint in addition to its existing platform
surface. API 1.2.6 was deployed with OneDeploy deployment
`60119571fdb84ab1a1e6dac52cacf93f`; live, ready, exact-version, platform-role,
agency-route-denial, and non-mutating self-password-route checks passed. The exact 1.2.6
installer passed isolated installation, version 1.2.6.0, 15-second responsiveness, and cleanup;
SHA-256 is `5bba56cf90f80a44089a7d47fe9a0ca330272ec33841c4d8c8c63d492b1e0929`.

Release 1.2.7 corrected Global Admin account switching without weakening the platform boundary:
the desktop now opens neutral username/password entry rather than requesting an agency user
directory, and ordinary picker initialization failures remain contained in the dialog. API 1.2.7
was deployed with OneDeploy deployment `d4afc62157994bc49ff75696c148d8cc`; live, ready, and
exact-version checks passed. The encrypted verification credential returned 401 after the user's
password-change attempt, consistent with that separate credential retaining the prior password,
so the authenticated Global Admin script awaits an intentional credential refresh. The exact
1.2.7 installer passed isolated installation, version 1.2.7.0, 15-second responsiveness, and
cleanup; SHA-256 is `92f37711eb9c208c19d9424d7b6a4d7b4efbed8a4bb93f39eeb5d37485552752`.

Release 1.2.8 added explicit supervisor-name display to both administrative and personal user
profiles and replaced the legacy application artwork with a validated multi-resolution Windows
icon. API 1.2.8 was deployed with OneDeploy deployment `b8ed5a41ad0e40efbde8b13a9d2f7543`;
live, ready, and exact-version checks passed. The exact 1.2.8 installer passed isolated
installation, version 1.2.8.0, 15-second responsiveness, and cleanup; SHA-256 is
`85b0aa688860638d77d4197ac203ce3daf82d50e2ebb651bf9dcf51cc633e4e6`.

Release 1.2.9 is locally source-complete but is not yet the live Azure pair. The runtime durability
pass made theme resources component-relative, rendered the parameterless feature views on an STA
WPF thread, and strengthened installer acceptance to require five responsive launches followed by
normal window shutdown and exit code zero. Debug, Release, Demo, and current NuGet vulnerability
checks passed. The rebuilt local installer SHA-256 is
`c5d1e6bbaa1bd563d1b812ac0d8a283ce5e6a3895fd8c29fe66a7b95180ef9d8`. Do not collect final
acceptance evidence until Azure authentication is refreshed and API 1.2.9 is deployed and verified.

Release 1.2.10 adds explicit billing-compliance reasons, clearer note-workflow guidance, serialized
journal saves during user switching, and the bright geometric watercolor Bodhi-leaf icon. The exact
local installer passed isolated installation plus five responsive, normal open/close cycles; SHA-256
is `c36ad62e01170568e06c268d9807ea139c4bc93b3f2d83ca3c7f28b1709b5a3b`. The live Demo API still
reported 1.2.8 on August 14, 2026, so deploy and verify API 1.2.10 before collecting final matched-pair
company-demo evidence.

Release 1.2.11 gives the icon's ivory container fully transparent, more generously rounded outer
corners while preserving the watercolor leaf. The exact local installer passed isolated installation
plus five responsive, normal open/close cycles; SHA-256 is
`b2956b02862d42a50daedb068055670d79b3bbc7de4993fd18a2e21cc3115c4f`. The live Demo API remains
behind this packaged release, so deploy and verify the matching API before final matched-pair evidence.
## Azure migration checklist

- [x] Confirm every Demo record is synthetic.
- [ ] Extract canonical seed data from the current Demo database.
- [x] Provision Azure SQL and the hosted API in the intended region and subscription.
- [x] Establish managed identity and least-privilege database permissions.
- [ ] Deploy schema and the `Demo` identity marker through a controlled pipeline.
- [x] Import and validate the current synthetic Demo data.
- [x] Implement token-based API authentication and authorization for the initial Demo surface.
- [x] Move the initial Demo workflows to HTTP-backed service implementations.
- [x] Remove client-side `Database.Migrate()` and EF registration from Demo.
- [ ] Configure the nightly reset and failure alerts.
- [ ] Test from a clean computer outside the development network.
- [x] Verify that the Demo client configuration contains no database credential or Azure SQL connection.
- [ ] Exercise backup, restore, and environment-rejection procedures.

Production cloud deployment is a later, separately approved operation. Nothing in Demo deployment
authorizes movement of the real working database to Azure.
