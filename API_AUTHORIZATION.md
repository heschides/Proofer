# API authorization and tenant ownership

*Route inventory current as of 2026-08-28, covering all 112 protected routes. Every route added,
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
| Profile | `POST /auth/renew` | User's `AgencyId` | Own validated user only; preserves the original authentication time and refuses renewal after the configured maximum session window. |
| Profile | `GET /me` | User's `AgencyId` | Own user only. |
| Profile | `GET /users/switchable` | User's `AgencyId` | Authenticated actor; response is restricted to the actor's agency. |
| Audit | `GET /audit-events` | Audit event's `AgencyId` | Admin only; response is restricted to actor agency, a bounded date window, and at most 500 rows. |
| Admin | `GET /admin/overview` | Actor's `AgencyId` | Admin only; every count is computed within the actor's agency. |
| Admin | `GET /admin/people` | Person and assigned user's `AgencyId` | Admin only; both ownership markers must equal actor agency. |
| Admin | `GET /admin/schema-drift` | Not tenant-scoped — deployment metadata | Admin only; returns table and column names plus applied migration ids for the connected database. No row data, so no consumer information. Schema shape is operational detail about the deployment, not something every signed-in case manager needs to enumerate. |
| Admin | `POST /admin/test-data/consumers/{personId}/delete` | Person and assigned user's `AgencyId` | Admin only; both ownership markers must equal actor agency and the Person must carry the immutable creation-time test-data marker. Requires the exact versioned test-only attestation and expected Person revision, runs the complete dependent-record cleanup in one serializable transaction, and retains a PHI-minimized `test-data.consumer-deleted` audit event. Any billing claim line for the consumer blocks the operation, and missing/cross-agency records return 404. |
| Admin | `POST /admin/demo/seed-ssns` | Person's `AgencyId` | Admin only; enabled in effect only when the API's startup-validated identity is exactly `SatiDemo` / `Demo`. Generates deterministic synthetic values server-side, encrypts through the configured Demo Key Vault, remains within actor agency, and records `person.ssn-updated` for every Person. It does not broaden the ordinary own-caseload SSN routes. |
| Admin | `GET /admin/activity` | Audit event's `AgencyId` | Admin only; bounded activity feed with actor display names from the same agency. |
| Admin | `GET /admin/operations` | Actor's `AgencyId` | Admin only; reports database health, retained audit/EDI counts, oldest-record timestamps, and the configured retention policy for the actor's agency. |
| Admin | `POST /admin/audit-export.csv` | Audit event's `AgencyId` | Admin only; agency derived from the actor and never from the caller. Requires a 10–250 character reason and a window of at most 366 days, caps at 10,000 rows, marks the response `no-store`, and records one `audit.exported` event. Exported values are neutralized against spreadsheet formula evaluation. |
| Users | `POST /users` | New user's `AgencyId` | Supervisor/Director/Admin; requested agency must equal actor agency, with role hierarchy checks. |
| Users | `PUT /users/{userId}` | Target user's `AgencyId` | Supervisor/Director/Admin in the same agency; supervisors only manage assigned case managers. |
| Users | `PUT /users/{userId}/password` | Target user's `AgencyId` | Same rule as user update. |
| Users | `PUT /users/me/password` | User's `AgencyId` | Own user plus current-password verification. |
| Supervisor | `GET /supervisor/supervisees` | Case manager user's `AgencyId` | Supervisor sees assigned case managers; Director/Admin role is accepted but the current route returns directly assigned users. |
| Supervisor | `GET /supervisor/notes` | Note person's own and owning user's `AgencyId` | Supervisor sees assigned case managers; Director/Admin sees agency case managers. |
| Supervisor | `POST /supervisor/notes/{noteId}/approve` | Note person's own and owning user's `AgencyId` | Same supervisory scope; server owns approval transition and requires the caller's expected Note revision. |
| Supervisor | `POST /supervisor/notes/{noteId}/approve-override` | Note person's own and owning user's `AgencyId` | Same supervisory scope; reason and expected revision required; server records approver. |
| Supervisor | `POST /supervisor/notes/{noteId}/return` | Note person's own and owning user's `AgencyId` | Same supervisory scope; reason and expected revision required; server records returner. |
| Caseload | `GET /caseload` | Target user's `AgencyId` | Accessible case manager only. |
| Caseload | `GET /people/{personId}/journal` | Person's assigned user and agency | Own caseload only. |
| Caseload | `PUT /people/{personId}/journal` | Person's assigned user and agency | Own caseload only. |
| Caseload | `POST /people/{personId}/journal/entries` | Person's assigned user and agency | Own caseload only; same gate as the journal `PUT`. Server prepends the stamped entry and stamps from `ApiClock`, so the caller supplies only the text. |
| People | `POST /people` | Actor's user and agency | Creates only in the actor's own caseload and agency. `IsTestData=true` is accepted only from a validated Admin and is otherwise rejected. |
| People | `PUT /people/{personId}` | Person's assigned user and agency | Own caseload only. The creation-time test-data classification is immutable, including for Admins. |
| SSN | `GET /people/{personId}/ssn` | Person's assigned user and agency | Own caseload only. Returns the mask and an on-file flag; no route anywhere returns a plaintext SSN. Response is not cacheable. |
| SSN | `PUT /people/{personId}/ssn` | Person's assigned user and agency | Own caseload only. Shape-checked before encryption; audited as `person.ssn-updated` without the value. Null or empty clears every stored part including the last four. |
| DHHS forms | `POST /people/{personId}/forms.pdf` | Person's assigned user and agency | Own caseload only. The ONLY operation permitted to decrypt an SSN; records `person.ssn-decrypted` alongside `dhhs-form.generated`. Consent selections are taken only from the request body, never derived. Response is not cacheable. |
| Agency release | `POST /people/{personId}/agency-release.pdf` | Person's assigned user and agency | Own caseload only. Consumer and staff identities are derived server-side; the request carries only recipient details and explicit authorization choices. Records `agency-release.generated`; response is not cacheable. No SSN is read. |
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
| Consumer providers | `GET /people/{personId}/providers` | Person's assigned user and agency | Accessible case manager only. Returns ended links as well as current ones; the caller decides what to show. |
| Consumer providers | `POST /people/{personId}/providers` | Person's assigned user and agency, plus provider's `AgencyId` | Accessible case manager only. A caller-supplied `providerId` is resolved only against the actor's own agency, so a directory entry from another tenant is rejected as absent. |
| Consumer providers | `PUT /people/{personId}/providers/{linkId}` | Person's assigned user and agency, plus provider's `AgencyId` | Accessible case manager only. The link must belong to the consumer named in the route, so a link id cannot select the scope it is then checked against. |
| Consumer providers | `DELETE /people/{personId}/providers/{linkId}` | Person's assigned user and agency | Accessible case manager only; same link-belongs-to-consumer check. Removal is for a mis-entry — ending a real relationship is a `PUT` that sets `EndDate` and keeps the row. |
| Providers | `GET /providers` | Provider's `AgencyId` | Authenticated actor; response restricted to actor agency. |
| Providers | `POST /providers` | Actor's `AgencyId` | CaseManager, Supervisor, Director, or Admin; agency is assigned server-side. A caller-supplied `parentProviderId` is resolved only against the actor's own agency, so a parent in another tenant is rejected as absent. |
| Providers | `PUT /providers/{id}` | Provider's `AgencyId` | CaseManager, Supervisor, Director, or Admin in the same agency. Same agency-scoped resolution of `parentProviderId`; an entry cannot be repointed across a tenant boundary. |
| Providers | `DELETE /providers/{id}` | Provider's `AgencyId` | Admin only in same agency. Refused with `provider_has_affiliated_entries` while entries are affiliated beneath it, and with `provider_on_consumer_records` while any consumer record references it — a count only, never consumer names. Both checks run before any other state is touched. |
| Provider contacts | `GET /providers/{providerId}/contacts` | Provider's `AgencyId` | Authenticated actor in the same agency. These are shared-directory contacts and carry no consumer identity. |
| Provider contacts | `POST /providers/{providerId}/contacts` | Provider's `AgencyId` | CaseManager, Supervisor, Director, or Admin in the same agency. Provider id is resolved inside the actor's agency before the contact is written. |
| Provider contacts | `PUT /providers/{providerId}/contacts/{contactId}` | Provider's `AgencyId`, contact's `ProviderId` | CaseManager, Supervisor, Director, or Admin in the same agency; contact must belong to the provider named in the route. |
| Provider contacts | `DELETE /providers/{providerId}/contacts/{contactId}` | Provider's `AgencyId`, contact's `ProviderId` | CaseManager, Supervisor, Director, or Admin in the same agency; same contact-belongs-to-provider check. |
| Provider merge | `POST /providers/{survivingId}/merge` | Both providers' `AgencyId` | Admin only; both entries must be in the actor's agency. Runs atomically, refuses identifier/tier/loop/current-consumer-link conflicts, moves live references, leaves assessment snapshots untouched, and records `provider.merged` without names or consumer IDs. |
| AT | `GET /at-requests` | Request person's assigned user and agency | Accessible case manager only. |
| AT | `GET /people/{personId}/at-requests` | Request person's assigned user and agency | Accessible case manager only. The list follows the client rather than the current caseload holder, so a transfer does not orphan filed requests. |
| AT | `GET /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only. |
| AT | `GET /at-requests/{id}/snapshot` | Request person's assigned user and agency | Accessible case manager only; generated binary stays behind the same check. |
| AT | `POST /at-requests` | Request person's assigned user and agency | Accessible case manager only; person controls ownership. |
| AT | `PUT /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only; person cannot be changed and the expected aggregate revision is required. |
| AT | `DELETE /at-requests/{id}` | Request person's assigned user and agency | Accessible case manager only; stale or omitted revisions are rejected, and a published request is refused. |
| AT | `POST /at-requests/{id}/publish` | Request person's assigned user and agency | Accessible case manager only. The attestation signer is taken from the validated actor; no request field can name a different signer. Completeness is decided by `AtRequestPublication`. Refused if already published or if the revision is stale. |
| AT | `POST /at-requests/{id}/reopen` | Request person's assigned user and agency | Accessible case manager only. Discards the attestation, returns the request to `Development`, and records the discarded signer in the audit event. Refused if the revision is stale. |
| AI context | `GET /people/{personId}/ai-context` | Person's assigned user and agency | Own caseload only; actor identity is derived from the validated session and the response contains selected-client identity only. |
| Notes | `POST /notes` | Note person's assigned user and agency | Own caseload only; note agency is assigned server-side. |
| Notes | `PUT /notes/{id}` | Current and requested note persons' assigned user and agency | Both the stored note and any requested reassignment target must belong to the actor's own caseload and agency; server enforces the `NoteWorkflow` transition table, rejects stale revisions, and audits a successful reassignment. |
| Notes | `DELETE /notes/{id}` | Note person's assigned user and agency | Own caseload only; server enforces deletable workflow states and rejects stale revisions. |
| Notes | `GET /people/{personId}/notes` | Note person's assigned user and agency | Own caseload only. |
| Notes | `GET /notes/monthly` | Target user's `AgencyId` | Accessible case manager only. |
| Notes | `GET /notes/day` | Target user's `AgencyId` | Accessible case manager only; returns one date across that user's whole caseload for the service-time overlap rule. |
| Notes | `GET /notes/year/{year}` | Own user and caseload | Own user only. |
| Notes | `POST /notes/abandon-overdue` | Own user and caseload | Own user only; only that user's eligible notes are transitioned, with each revision incremented. |
| Settings | `GET /settings` | Settings `AgencyId` | Actor's agency only. |
| Settings | `PUT /settings` | Settings `AgencyId` | Admin only in actor's agency; provider references must share the agency. |
| Scratchpad | `GET /scratchpad/today` | Scratchpad `UserId` | Own user only. |
| Scratchpad | `GET /scratchpad/tomorrow` | Scratchpad `UserId` | Own user only; server resolves the next workday, including Friday-to-Monday rollover. |
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
| Billing | `POST /billing/periods/{periodId}/responses` | Billing period user's `AgencyId` | Admin only; the period is resolved through its owning user's agency, so a response cannot be attached to another tenant's history. No tenant is ever read from the document. `IsSynthetic` comes from the document's ISA15 usage indicator, not from configuration. |
| Billing | `POST /billing/periods/{periodId}/mock-clearinghouse` | Billing period user's `AgencyId` | Admin only, and additionally restricted to a validated `SatiDemo`/`Demo` deployment or the isolated test host. Returns 404 elsewhere so the route is absent in effect on Production. Fabricates test interchanges only and ingests them through the same path as a real response. |
| Billing | `GET /billing/submissions` | Event `AgencyId` plus billing period user's `AgencyId` | Admin only; both ownership markers must equal actor agency. Synthetic provenance is explicit. |
| Billing | `GET /billing/remittances` | Outcome `AgencyId` | Admin only; returns bounded claim-level outcomes for actor agency, without raw 835 or note narrative. |
| Billing | `GET /billing/remittance-deposits` | Deposit `AgencyId` | Admin only; returns bounded 835/EFT reconciliation totals for actor agency. Provider-level (PLB) adjustments are explicit; no bank credentials are returned. |
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
