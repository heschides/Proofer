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
verified local source: `Demo` identity marker, 15 users, 167 people, 3,769 notes, 50 migrations,
and zero people owned outside the `Sandbox Mode` agency. The temporary import and validation
firewall rules were removed after use.

The API is hosted on Windows App Service Free F1 in Central US at
`https://sati-demo-api-satilogica.azurewebsites.net/`. Its system-assigned managed identity is a
contained database user with only `db_datareader` and `db_datawriter`. The API uses a managed-
identity connection; its token-signing key is an App Service setting and is not stored in source.
Both `/health/live` and `/health/ready` returned HTTP 200 after deployment on August 11, 2026.

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
