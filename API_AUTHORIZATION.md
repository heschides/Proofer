# API authorization and tenant ownership

This is the route inventory for the protected `/api/v1` API. The unauthenticated
`POST /api/v1/auth/login` route and health checks are intentionally outside this table.

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
| AT | `GET /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only. |
| AT | `GET /at-requests/{id}/snapshot` | Request person's assigned user and agency | Accessible case manager only; generated binary stays behind the same check. |
| AT | `POST /at-requests` | Request person's assigned user and agency | Accessible case manager only; person controls ownership. |
| AT | `PUT /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only; person cannot be changed and the expected aggregate revision is required. |
| AT | `DELETE /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only; stale or omitted revisions are rejected. |
| AI context | `POST /people/{personId}/ai-context` | Person's assigned user and agency | Accessible requesting user; person and requested user must agree. |
| Notes | `POST /notes` | Note person's assigned user and agency | Own caseload only; note agency is assigned server-side. |
| Notes | `PUT /notes/{id}` | Note person's assigned user and agency | Own caseload only; server enforces editable workflow states and rejects stale revisions. |
| Notes | `DELETE /notes/{id}` | Note person's assigned user and agency | Own caseload only; server enforces deletable workflow states and rejects stale revisions. |
| Notes | `GET /people/{personId}/notes` | Note person's assigned user and agency | Own caseload only. |
| Notes | `GET /notes/monthly` | Target user's `AgencyId` | Accessible case manager only. |
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

## Required regression coverage

`Sati.Api.Tests/TenantAuthorizationTests.cs` exercises the real HTTP and JWT pipeline against two
isolated agencies. It must retain rejection tests for authentication, users, people, providers,
reports, billing exports, AT requests and snapshots, assessments, and supervisor actions whenever
these routes are refactored.
