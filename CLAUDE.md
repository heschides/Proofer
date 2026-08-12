# Sati — Project Briefing for Coding Assistants

Read this file, `ARCHITECTURE.md`, `DECISIONS.md`, and the relevant section of `AGENDA.md`
before making architectural changes.

## What Sati is

Sati is a Maine-focused human-services case-management platform built by Josh, a social-services
case manager and software developer. It began as a WPF desktop application used for real daily
work. Its longer direction is a cloud-based, multi-tenant platform capable of supporting provider
agencies, supervisors, billing staff, reviewers, and eventually state-facing workflows.

The current client targets .NET 10 and uses WPF, CommunityToolkit.Mvvm, EF Core, SQL Server, and
Microsoft.Extensions.Hosting. The repository currently contains one application project. That is
the current implementation, not the intended final solution structure.

## Product direction

Treat the following as governing constraints:

1. Sati is becoming an API-mediated platform with WPF as one client.
2. Distributed clients must not connect directly to Azure SQL or contain database credentials.
3. The API is authoritative for identity, authorization, tenant isolation, validation,
   transactions, audit history, migrations, and integrations.
4. Azure-hosted services should use managed identities and least-privilege access.
5. Submitted clinical and financial records require immutable versions and amendments rather
   than silent overwrites.
6. Tenant isolation, auditability, concurrency, recovery, observability, and automated testing
   are platform foundations, not post-launch enhancements.
7. Demo and Production must remain separate in databases, credentials, service identities,
   deployments, logs, backups, and administrative access. The desktop bootstrap chooser may
   select either environment only through the validated, hard-coded environment mapping.

Do not solve a cloud feature by placing an Azure SQL connection string in the WPF application.
Do not add new direct EF dependencies to ViewModels or distributed-client services.

## Current product areas

- Client caseloads and demographic records
- Service notes and documentation workflow
- Compliance forms, annual cycles, and quarterly reviews
- Upcoming deadlines and calendar events
- Supervisor dashboards, queues, and approval workflow
- Productivity, incentives, settings, scheduling, and exempt dates
- Comprehensive Assessment authoring
- Contacts and support teams
- Provider directory and assistive-technology requests
- Early billing, claim-line, and 837P generation
- Local AI-assisted note drafting with explicit human acceptance

The long-term market direction includes Maine waiver and targeted case-management organizations.
Potential future scope includes person-centered plans, incident management, EVV, mobile/offline
documentation, authorization/utilization, full claim and remittance lifecycle, reporting, and
state/payer/provider integrations. Do not imply those capabilities already exist.

## Current architecture

Most persistence operations are behind interfaces in `Data/`, with implementations that create a
short-lived `SatiContext` from `IDbContextFactory<SatiContext>` per method. This is useful as a
transition seam.

The target transition is:

```text
WPF ViewModel -> IFeatureService -> HttpFeatureService -> Sati.Api
                                                     -> domain/application service
                                                     -> EF Core -> Azure SQL
```

Existing EF service implementations can move behind the API. Do not expose EF entities directly
as network contracts. Introduce narrowly scoped DTOs, especially around `User`, whose persistence
model currently contains password hash and salt fields that must never leave the server.

Methods that accept `userId`, `agencyId`, or similar caller-controlled scope values require
authorization redesign. The server should derive authoritative actor and tenant scope from the
authenticated session.

Pure presentation and local concerns may stay in the client. Any calculation that controls
persistence, permission, approval, billability, or official record status belongs server-side.

## Data environments

- The bootstrap chooser runs before the splash screen or any database connection.
- `My work` maps only to `SatiProduction`; `Demo` maps only to `SatiDemo`.
- Database name and `dbo.SatiDatabaseIdentity` are checked against that selection before login.
- A selected Demo session displays a permanent Demo indicator.
- The original mixed local database is retained as an archive and is not a runtime target.

See `DATABASE_ENVIRONMENTS.md`. Production data must never be copied, queried, transformed, or
uploaded as part of Demo work without explicit authorization.

## Near-term priority

The cloud platform foundation in `AGENDA.md` takes precedence over broad feature expansion:

1. introduce the ASP.NET Core API and safe contracts;
2. move authentication server-side and issue short-lived tokens;
3. formalize tenant ownership and capability-based authorization;
4. add audit events, record versions, and optimistic concurrency;
5. migrate desktop service implementations from EF to HTTP;
6. add automated tests and controlled migrations;
7. host synthetic Demo data in Azure with managed identity and nightly reset;
8. package and test the Demo client on clean machines.

Feature work may proceed when it reinforces these boundaries or is explicitly prioritized.

## Engineering rules

- Preserve unrelated user changes in the dirty worktree.
- Use constructor dependency injection; do not introduce service-locator access.
- Keep ViewModels unaware of Views.
- Use factories for window creation where a window remains the correct UI primitive.
- Keep database contexts short-lived and server-side as the API transition proceeds.
- Put authoritative business rules in one named owner and document cascade points.
- Do not use UI visibility as security.
- Do not log unrestricted note narratives, passwords, tokens, connection strings, or other
  sensitive content.
- Add tests with new authorization, tenancy, billing, audit, migration, and concurrency work.
- Record durable design choices in `DECISIONS.md` and deferred work in `AGENDA.md`.
- Update `ARCHITECTURE.md` when ownership or boundaries change.

## Healthcare and regulatory posture

Sati is not currently represented as HIPAA compliant or production-ready. Azure hosting and use of
HIPAA-eligible services do not themselves establish compliance. Consult `REGULATORY_CONCERNS.md`
before work involving real PHI, OADS review, MaineCare claims, signatures, records retention,
cross-agency access, AI, exports, or external integrations.

The platform must be designed to produce evidence of access control, integrity, auditability,
availability, risk management, and incident response. Regulatory conclusions require review by
qualified counsel, agency stakeholders, and the appropriate Maine authorities.

## Working with Josh

- Lead with the architectural reason and then explain the implementation.
- Be direct when an assumption is unsafe or conceptually incorrect.
- Avoid flattering language and avoid overstating readiness.
- Match complexity to the real risk, but do not treat healthcare security as optional simplicity.
- Preserve accessible design: meaningful automation names, keyboard navigation, screen-reader
  support, non-color status cues, and sensible focus order.
- Track deferred items instead of silently dropping them.

Sati is simultaneously a working tool and the seed of a much larger product. Protect the working
system while deliberately moving the platform boundary in the intended direction.

*Last updated: August 8, 2026.*
