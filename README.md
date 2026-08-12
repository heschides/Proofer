# Sati — Human Services Infrastructure for Maine

Sati is a case-management and human-services platform being built from direct experience
with Maine waiver and targeted case-management workflows. Its purpose is to make daily
documentation, compliance, person-centered planning, supervision, and billing easier to
understand and harder to get wrong.

The name comes from the Pali word for mindfulness and remembrance: keeping what matters
present and accounted for.

## Product direction

Sati began as a Windows desktop application backed by SQL Server LocalDB. That application
is in active daily use and remains the product-development client, but it is not the final
deployment architecture.

Sati is intentionally evolving toward a cloud-based, multi-tenant platform for Maine human
services organizations:

```text
Windows, web, and future mobile clients
                 |
              HTTPS API
                 |
      domain services and background jobs
                 |
              Azure SQL
```

The API will be the only authority permitted to access production cloud data. Installed
clients will never contain a shared database password or connect directly to Azure SQL.
Azure-hosted services will use managed identities wherever possible.

The long-term goal is not merely remote database hosting. It is a healthcare-grade platform
with:

- explicit agency and tenant isolation;
- server-enforced authorization and separation of duties;
- immutable submitted records, amendments, signatures, and complete audit history;
- reliable MaineCare billing and remittance workflows;
- configurable case-management and person-centered-planning workflows;
- secure integrations with state, payer, provider, and health-information systems;
- web and mobile access where the work requires it;
- operational monitoring, tested backups, disaster recovery, and controlled deployment.

This direction is ambitious. Sati is not yet a production replacement for Credible, Therap,
or Maine's state systems. The existing application is the domain foundation from which that
platform is being built.

## Current capabilities

- Caseload and client record management
- Service notes and documentation workflows
- Compliance forms, annual cycles, and 90-day reviews
- Upcoming events and deadline monitoring
- Supervisory queues, approvals, and team views
- Productivity, incentives, scheduling, and workday exclusions
- Comprehensive Assessment authoring
- Provider directory and assistive-technology requests
- Early billing, claim-line, and 837P generation work
- Local AI-assisted case-note drafting with explicit human acceptance
- Separate local Production and Demo data environments

## Architectural commitments

The following are product constraints, not optional enhancements:

1. **No direct cloud database access from distributed clients.** All cloud data access goes
   through an authenticated API.
2. **The server is authoritative.** Authorization, validation, tenant isolation, transactions,
   audit events, and workflow transitions are enforced below the UI.
3. **Clinical and financial history is preserved.** Submitted records are versioned and amended,
   not silently overwritten.
4. **Tenant boundaries are structural.** Agency isolation cannot depend on individual developers
   remembering to add a query filter.
5. **Migrations are deployment operations.** Installed clients do not modify cloud schemas.
6. **Security and compliance are systems, not claims.** Technical controls, policies, risk
   analysis, incident response, and evidence must develop together.
7. **Automated tests are required for expansion.** Authorization, tenant isolation, billing,
   migrations, and record integrity need regression coverage before production use.

See [ARCHITECTURE.md](ARCHITECTURE.md), [DECISIONS.md](DECISIONS.md),
[AGENDA.md](AGENDA.md), and [REGULATORY_CONCERNS.md](REGULATORY_CONCERNS.md).

## Current technology

- **Client:** WPF on .NET 10 for Windows
- **Presentation:** MVVM with CommunityToolkit.Mvvm
- **Current data access:** Local Production uses EF Core 10/LocalDB; Demo uses the deployed HTTPS API
- **Composition:** Microsoft.Extensions.Hosting and constructor dependency injection
- **Local databases:** SQL Server LocalDB with isolated Production and Demo development identities
- **Demo cloud:** ASP.NET Core API on App Service Free F1, Azure SQL, and managed identity
- **Target platform:** the same API boundary plus background jobs and
  centralized observability

The current service interfaces form the migration seam: desktop ViewModels retain their contracts
while Demo registers HTTP-backed implementations. Demo does not register an EF context or receive
an Azure SQL connection string. Unmigrated Demo workflows fail explicitly rather than falling back
to direct database access.

## Local development

Requirements:

- .NET 10 SDK
- Visual Studio with Windows desktop development support
- SQL Server LocalDB

Start Sati normally during development:

```powershell
dotnet run --configuration Debug
```

The first window asks whether to open your isolated working data or the synthetic demonstration
environment. This happens before the splash screen or any data connection. A selected Demo session
displays a permanent `DEMO` indicator and connects only to the deployed HTTPS API. Local Production
validates its database identity before migrations; the Demo API validates `SatiDemo` during server
startup. See [DATABASE_ENVIRONMENTS.md](DATABASE_ENVIRONMENTS.md) for details.

## Status

Sati is under active development. Its current priority is establishing the platform boundary:

1. ASP.NET Core API and safe authentication tokens;
2. formal tenant ownership and server-side authorization;
3. append-only audit events, record versions, and concurrency control;
4. HTTP implementations of the existing data-service interfaces;
5. Azure-hosted Demo with a canonical nightly reset;
6. automated tests, clean-machine packaging, and controlled releases.

Feature development continues, but new work should reinforce rather than bypass these
foundations.

## About

Sati is built by Josh, a Maine social-services case manager and software developer. The project
combines practical workflow improvement with a longer-term effort to strengthen human-services
infrastructure in Maine.
