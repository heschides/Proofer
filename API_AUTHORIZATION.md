# API authorization and tenant ownership

*Route inventory current as of 2026-08-15, covering all 90 protected routes. Every route added,
removed, or rescoped must be reflected here in the same change.*

This is the route inventory for the protected `/api/v1` API. The unauthenticated
`POST /api/v1/auth/login` route and health checks are intentionally outside this table.

`POST /api/v1/auth/login` carries one invariant that is not visible from the route table: it must
spend the same key-derivation work whether or not the username exists, so that sign-in cannot be
used to enumerate accounts. `PasswordVerifier.VerifyMissingUser` exists solely for that path and
must not be removed as dead code.

Every protected route has two layers of protection:

1. JWT authentication establishes a claimed user, role, and agency.
2. `ValidatedActorFilter` confirms that the claimed user still exists with that exact role and
   agency before the endpoint runs. `TenantAccess` supplies the shared actor, caseload, and
   supervisory checks used by feature endpoints.

“Own user” means the authenticated user. “Own caseload” means a person assigned to that user and
the same agency. “Accessible case manager” means the actor themself, their assigned case manager
when acting as Supervisor, or any case manager in the agency when acting as Director/Admin.

| Feature | Protected route | Authoritative tenant owner | Access rule |
|---|---|---|---|
| Profile | `GET /me` | User's `AgencyId` | Own user only. |
| Profile | `GET /users/switchable` | User's `AgencyId` | Authenticated actor; response is restricted to the actor's agency. |
| Audit | `GET /audit-events` | Audit event's `AgencyId` | Admin only; response is restricted to actor agency, a bounded date window, and at most 500 rows. |
| Admin | `GET /admin/overview` | Actor's `AgencyId` | Admin only; every count is computed within the actor's agency. |
| Admin | `GET /admin/people` | Person and assigned user's `AgencyId` | Admin only; both ownership markers must equal actor agency. |
| Admin | `GET /admin/activity` | Audit event's `AgencyId` | Admin only; bounded activity feed with actor display names from the same agency. |
| Admin | `GET /admin/operations` | Actor's `AgencyId` | Admin only; reports database health, retained audit/EDI counts, oldest-record timestamps, and the configured retention policy for the actor's agency. |
| Admin | `POST /admin/audit-export.csv` | Audit event's `AgencyId` | Admin only; agency derived from the actor and never from the caller. Requires a 10–250 character reason and a window of at most 366 days, caps at 10,000 rows, marks the response `no-store`, and records one `audit.exported` event. Exported values are neutralized against spreadsheet formula evaluation. |
| Users | `POST /users` | New user's `AgencyId` | Supervisor/Director/Admin; requested agency must equal actor agency, with role hierarchy checks. |
| Users | `PUT /users/{userId}` | Target user's `AgencyId` | Supervisor/Director/Admin in the same agency; supervisors only manage assigned case managers. |
| Users | `PUT /users/{userId}/password` | Target user's `AgencyId` | Same rule as user update. |
| Users | `PUT /users/me/password` | User's `AgencyId` | Own user plus current-password verification. |
| Supervisor | `GET /supervisor/supervisees` | Case manager user's `AgencyId` | Supervisor sees assigned case managers; Director/Admin role is accepted but the current route returns directly assigned users. |
| Supervisor | `GET /supervisor/notes` | Note person's owning user's `AgencyId` | Supervisor sees assigned case managers; Director/Admin sees agency case managers. |
| Supervisor | `POST /supervisor/notes/{noteId}/approve` | Note person's owning user's `AgencyId` | Same supervisory scope; server owns approval transition and requires the caller's expected Note revision. |
| Supervisor | `POST /supervisor/notes/{noteId}/approve-override` | Note person's owning user's `AgencyId` | Same supervisory scope; reason and expected revision required; server records approver. |
| Supervisor | `POST /supervisor/notes/{noteId}/return` | Note person's owning user's `AgencyId` | Same supervisory scope; reason and expected revision required; server records returner. |
| Caseload | `GET /caseload` | Target user's `AgencyId` | Accessible case manager only. |
| Caseload | `GET /people/{personId}/journal` | Person's assigned user and agency | Own caseload only. |
| Caseload | `PUT /people/{personId}/journal` | Person's assigned user and agency | Own caseload only. |
| People | `POST /people` | Actor's user and agency | Creates only in the actor's own caseload and agency. |
| People | `PUT /people/{personId}` | Person's assigned user and agency | Own caseload only. |
| Person audit | `GET /people/{personId}/history` | Person and assigned user's `AgencyId` | Admin only; both Person and assigned user must belong to actor agency. Response is not cacheable. |
| Person audit | `GET /people/{personId}/history.pdf` | Person and assigned user's `AgencyId` | Admin only in actor agency; generated PDF remains behind the same check and is not cacheable. |
| Contacts | `GET /people/{personId}/contacts` | Contact's person, assigned user, and agency | Own caseload only. |
| Contacts | `POST /people/{personId}/contacts` | Contact's person, assigned user, and agency | Own caseload only. |
| Contacts | `PUT /people/{personId}/contacts/{contactId}` | Contact's person and assigned user | Own caseload only; contact must belong to route person. |
| Contacts | `DELETE /contacts/{contactId}` | Contact's person and assigned user | Own caseload only; soft delete. |
| Reviews | `GET /reviews` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `GET /people/{personId}/reviews` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `POST /reviews/ensure-current` | Each review person's assigned user and agency | Processes only people belonging to accessible case managers; inaccessible IDs are skipped. |
| Reviews | `PUT /reviews/{reviewItemId}/stage` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `PUT /reviews/{reviewItemId}/appointment` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `GET /people/{personId}/appointments/latest` | Appointment review's person, assigned user, and agency | Accessible case manager only. |
| Assessments | `POST /people/{personId}/assessments/draft` | Person's assigned user and agency | Assigned case manager alone may author; `authorUserId` must equal actor. |
| Assessments | `PUT /assessments/{assessmentId}/document` | Assessment author plus owned person | Author alone may edit; approved/superseded versions are locked. |
| Assessments | `POST /assessments/{assessmentId}/submit` | Assessment author plus owned person | Author alone may submit their editable draft. |
| Assessments | `GET /people/{personId}/pcp-source` | Person's assigned user and agency | Accessible case manager; read-only supervisory access is allowed. |
| Providers | `GET /providers` | Provider's `AgencyId` | Authenticated actor; response restricted to actor agency. |
| Providers | `POST /providers` | Actor's `AgencyId` | Admin only; agency is assigned server-side. |
| Providers | `PUT /providers/{id}` | Provider's `AgencyId` | Admin only in same agency. |
| Providers | `DELETE /providers/{id}` | Provider's `AgencyId` | Admin only in same agency. |
| AT | `GET /at-requests` | Request person's assigned user and agency | Accessible case manager only. |
| AT | `GET /people/{personId}/at-requests` | Request person's assigned user and agency | Accessible case manager only. The list follows the client rather than the current caseload holder, so a transfer does not orphan filed requests. |
| AT | `GET /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only. |
| AT | `GET /at-requests/{id}/snapshot` | Request person's assigned user and agency | Accessible case manager only; generated binary stays behind the same check. |
| AT | `POST /at-requests` | Request person's assigned user and agency | Accessible case manager only; person controls ownership. |
| AT | `PUT /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only; person cannot be changed and the expected aggregate revision is required. |
| AT | `DELETE /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only; stale or omitted revisions are rejected, and a published request is refused. |
| AT | `POST /at-requests/{id}/publish` | Request person's assigned user and agency | Accessible case manager only. The attestation signer is taken from the validated actor; no request field can name a different signer. Completeness is decided by `AtRequestPublication`. Refused if already published or if the revision is stale. |
| AT | `POST /at-requests/{id}/reopen` | Request person's assigned user and agency | Accessible case manager only. Discards the attestation, returns the request to `Development`, and records the discarded signer in the audit event. Refused if the revision is stale. |
| AI context | `POST /people/{personId}/ai-context` | Person's assigned user and agency | Accessible requesting user; person and requested user must agree. |
| Notes | `POST /notes` | Note person's assigned user and agency | Own caseload only; note agency is assigned server-side. |
| Notes | `PUT /notes/{id}` | Note person's assigned user and agency | Own caseload only; server enforces editable workflow states and rejects stale revisions. |
| Notes | `DELETE /notes/{id}` | Note person's assigned user and agency | Own caseload only; server enforces deletable workflow states and rejects stale revisions. |
| Notes | `GET /people/{personId}/notes` | Note person's assigned user and agency | Own caseload only. |
| Notes | `GET /notes/monthly` | Target user's `AgencyId` | Accessible case manager only. |
| Notes | `GET /notes/day` | Target user's `AgencyId` | Accessible case manager only; returns one date across that user's whole caseload for the service-time overlap rule. |
| Notes | `GET /notes/year/{year}` | Own user and caseload | Own user only. |
| Notes | `POST /notes/abandon-overdue` | Own user and caseload | Own user only; only that user's eligible notes are transitioned, with each revision incremented. |
| Settings | `GET /settings` | Settings `AgencyId` | Actor's agency only. |
| Settings | `PUT /settings` | Settings `AgencyId` | Admin only in actor's agency; provider references must share the agency. |
| Scratchpad | `GET /scratchpad/today` | Scratchpad `UserId` | Own user only. |
| Scratchpad | `GET /scratchpad/history` | Scratchpad `UserId` | Own user only. |
| Scratchpad | `PUT /scratchpad` | Scratchpad `UserId` | Own user only. |
| Scratchpad | `POST /scratchpad/{scratchpadId}/comments` | Scratchpad `UserId` | Own historical scratchpad only. |
| Exempt dates | `GET /exempt-dates/{year}` | Exempt date `UserId` | Own user only. |
| Exempt dates | `POST /exempt-dates` | Exempt date `UserId` | Created for actor server-side. |
| Exempt dates | `DELETE /exempt-dates/{id}` | Exempt date `UserId` | Own user only. |
| Incentives | `GET /incentives/{year}/{month}` | Incentive user's `AgencyId` | Accessible case manager only. |
| Incentives | `GET /incentives/history` | Incentive `UserId` | Own user only. |
| Incentives | `PUT /incentives/{id}` | Incentive `UserId` | Own user only. |
| Incentives | `POST /incentives/eligible-days` | Settings `AgencyId` | Calculates with actor-agency settings. |
| Incentives | `POST /incentives/remaining-days` | Settings `AgencyId` | Calculates with actor-agency settings. |
| Reports | `GET /reports/consumer-billing-loss` | Each person's assigned user and agency | Own caseload only. |
| Billing | `POST /billing/periods/{year}/{month}` | Billing period user's `AgencyId` | Admin only; target user must be in actor agency. |
| Billing | `GET /billing/periods` | Billing period user's `AgencyId` | Admin only; response joined to actor agency. |
| Billing | `GET /billing/configuration` | Authenticated actor's `AgencyId` | Admin only; returns only the actor agency's payer/provider defaults. |
| Billing | `PUT /billing/configuration` | Authenticated actor's `AgencyId` | Admin only; writes and audits only the actor agency's configuration. |
| Billing | `GET /billing/candidates` | Note person's owning user's `AgencyId` | Admin only; candidates joined to actor agency. |
| Billing | `POST /billing/claim-lines` | Source note person's owning user's `AgencyId` | Admin only; source note must be approved and in actor agency. |
| Billing | `GET /billing/claim-lines/draft` | Billing period user's `AgencyId` | Admin only; period owner joined to actor agency. |
| Billing | `POST /billing/periods/{periodId}/submit` | Billing period user's `AgencyId` | Admin only in same agency. |
| Billing | `POST /billing/periods/{periodId}/edi` | Billing period user's `AgencyId` | Admin only; period, every source note/person, and generated file must remain in actor agency. |
| Forms | `POST /forms/delete` | Form person's assigned user and agency | Own caseload only; all requested IDs must be owned. |
| Forms | `PUT /forms/{id}` | Form person's assigned user and agency | Own caseload only. |
| Incidents | `POST /incidents` | Authenticated actor's `AgencyId` | Reports only a validated PHI-minimized envelope; agency is derived from the token, never the request. |
| Incidents | `GET /admin/incidents` | Incident `AgencyId` | Admin only; actor agency only. |
| Incidents | `PUT /admin/incidents/{incidentId}/status` | Incident `AgencyId` | Admin only; same-agency status transition is audited. |
| Platform health | `GET /platform/incidents` | All incident agencies | `PlatformOperator` only; every cross-tenant view is audited. Ordinary Admin is forbidden. |

`PlatformOperator` is provisioned outside agency user-management workflows. It is excluded from
agency counts, switch-user results, selectable agency roles, and agency user editing.

## Required regression coverage

`Sati.Api.Tests/TenantAuthorizationTests.cs` exercises the real HTTP and JWT pipeline against two
isolated agencies. It must retain rejection tests for authentication, users, people, providers,
reports, billing exports, AT requests and snapshots, assessments, and supervisor actions whenever
these routes are refactored.

It additionally covers two properties that are invisible in the table above and easy to regress:

- **Sign-in costs the same whether or not the account exists**
  (`SignInSpendsTheSameWorkWhetherOrNotTheAccountExists`). Confirmed to fail against the unfixed
  handler before being kept.
- **The audit export is inert in a spreadsheet** (`AuditExportNeutralizesSpreadsheetFormulas`),
  with the underlying rule covered in `Sati.Tests/AuditCsvTests.cs`.

The table was reconciled against the routes registered in `ApiEndpoints.cs` on 2026-08-15 and
matched exactly at 87 routes. Re-run that comparison when routes change; a route inventory that
silently falls behind the code is worse than no inventory.
