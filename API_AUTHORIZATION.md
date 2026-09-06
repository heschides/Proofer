# API authorization and tenant ownership

*Route inventory mechanically reconciled 2026-09-06: 169 protected routes. The table matches
`ApiSurface.Routes` after excluding health and anonymous login, and `ApiSurfaceTests` checks that
manifest against live endpoint registration. Every route added, removed, or rescoped must be
reflected here in the same change.*

This is the route inventory for the protected `/api/v1` API. The unauthenticated
`POST /api/v1/auth/login` route and health checks are intentionally outside this table.

The separate `Sati.Portal` host is also outside this staff-route count. Its neutral `/`, `/s/{token}`,
`/r/{token}` pages and `GET /portal/bootstrap` contain no consumer record. `POST /portal/auth`
requires the private request token and correct signing code, plus HTTPS, bounded JSON, rate limits
and CSRF. `GET /portal/state` and `GET /portal/document.pdf` resolve only a durable, request-bound
session cookie. `POST /portal/consent`, `/portal/sign`, `/portal/decision`, `/portal/extend` and
`/portal/logout` additionally require CSRF; receipt cookies cannot sign, consent, extend a signing
session or make unfinished-request decisions. Every protected operation rechecks expiry, request
status, durable authentication version and relevant source validity. The portal has no routes
accepting a caller-supplied agency, person or artifact ID and no `/api/v1` routes.

Decisions and PDF downloads additionally require the displayed page's session binding. This
non-secret correlation value cannot authenticate or select a different request; it must match
the session already selected by the cookie. State refresh validates it when supplied. Relevant
signer changes stop external receipt access and invalidate old sessions without erasing a signed
decision. Expiry is rechecked after awaited work and before the protected decision or release.

`POST /api/v1/auth/login` carries one invariant that is not visible from the route table: it must
spend the same key-derivation work whether or not the username exists, so that sign-in cannot be
used to enumerate accounts. `PasswordVerifier.VerifyMissingUser` exists solely for that path and
must not be removed as dead code.

Every protected route has two layers of protection:

1. JWT authentication establishes a claimed user, legacy identity label, and agency.
2. `ValidatedActorFilter` confirms that identity and agency against the database, then resolves the
   current persisted permission set before every endpoint runs. Permissions are deliberately not
   trusted from the token, so revocation takes effect immediately. `TenantAccess` supplies the
   shared actor, caseload, and supervisory checks used by feature endpoints.

“Own user” means the authenticated user. “Own caseload” means a person assigned to that user and
the same agency. “Accessible case manager” means the actor themself when they have case-management
permission, an assigned case manager when they have supervision permission, or any case manager in
the agency when they also have agency-wide supervision.

Agency-wide supervision is a capability in its own right, distinct from administration, and
administration implies it. The two were briefly conflated: the legacy `Director` label held
agency-wide review WITHOUT any administration route, so a backfill that mapped it to
administration handed every existing Director the audit export, settings writes, destructive
test-data deletion, and provider merge. See `DECISIONS.md`, 2026-08-31.

| Feature | Protected route | Authoritative tenant owner | Access rule |
|---|---|---|---|
| Signatures | `GET /signatures/availability` | Validated actor | Reports environment capability only; disabled by default. |
| Signatures | `GET /signatures/catalog` | Validated actor | Shared document-purpose catalog; no consumer information or legal clearance. |
| Signatures | `GET /people/{personId}/signature-signers` | Person agency and owning user agency | Existing live `TenantAccess.CanAccessUserAsync`, marked test consumer, active same-consumer representative contacts; release audited. |
| Signatures | `GET /people/{personId}/signature-requests` | Person/request agency and owning user agency | Same live consumer-access rule; bounded retained history and audited release. |
| Signatures | `POST /people/{personId}/documents/{artifactId}/freeze` | Person/artifact agency and owning user agency | Test consumer, live consumer access, current complete generated allowed-kind artifact, explicit review and exact hash/size; audited freeze in the same transaction. |
| Signatures | `POST /signature-requests` | Person/frozen document/request agency | Test consumer and live access; server-resolved current signer/contact; exact confirmed name/email snapshot, authority explanation, code policy and idempotency checks. |
| Signatures | `POST /signature-requests/{requestId}/replace` | Request/person agency and owning user agency | Same current signer/access checks, expected revision, reason, fresh different code and new request identity; old request/session revoked. |
| Signatures | `POST /signature-requests/{requestId}/revoke` | Request/person agency and owning user agency | Live access, test-consumer gate, expected revision and reason; unfinished requests only, evidence retained. |
| Signatures | `POST /signature-requests/{requestId}/withdraw-authorization` | Request/person agency and owning user agency | Live access, expected revision and reason; signed authorization purpose only. Preserves the signature and records withdrawal separately. |
| Signatures | `GET /signature-requests/{requestId}/original.pdf` | Request/person/frozen document agency | Live consumer access, exact retained hash/size verification and audit committed before bytes are returned. |
| Signatures | `GET /signature-requests/{requestId}/signed.pdf` | Request/person/package agency | Same release checks; an immutable prepared package must exist. |
| Chat | `GET /chat/availability` | Validated deployment identity | Validated agency user; only whether synthetic chat is enabled. PlatformOperator remains excluded by the path allowlist. |
| Chat | `GET /chat/candidates` | Actor agency and optional consumer | Admin; eligible agency users with existing consumer access when specified. No platform-support identities. |
| Chat | `GET /chat/rooms` | Room and membership agencies | Eligible active member with matching room/user/agency; consumer rooms also require current caseload access. |
| Chat | `POST /chat/rooms` | Actor agency | Admin; explicit eligible same-agency members, all authorized for optional consumer scope. No inherited history. |
| Chat | `PUT /chat/rooms/{roomId}` | Room agency | Admin plus optional consumer access, expected revision. Name/description only. Management does not grant reading. |
| Chat | `POST /chat/rooms/{roomId}/archive` | Room agency | Same management scope, expected revision. Records retained, posting closed. |
| Chat | `GET /chat/rooms/{roomId}/members` | Room, member and user agencies | Authorized active member with current consumer access where applicable. |
| Chat | `POST /chat/rooms/{roomId}/members` | Room and target user agencies | Admin management scope; target eligible and authorized for consumer scope. Expected revision, member limits, new visibility boundary. |
| Chat | `DELETE /chat/rooms/{roomId}/members/{userId}` | Room and membership agencies | Admin management scope or authorized member leaving themself; expected revision. Closes retained membership interval. |
| Chat | `GET /chat/rooms/{roomId}/messages` | Room, member, message and change agencies | Authorized active member plus current consumer access; history after join only. Bounded ordered changes, current redacted form, exact release evidence committed before nonempty response. |
| Chat | `POST /chat/rooms/{roomId}/messages` | Room and actor agency | Authorized member, consumer access and unarchived room. Expected revision, bounded body, user rate limit and scoped exact retry identity. |
| Chat | `POST /chat/rooms/{roomId}/read` | Room, marker and actor agency | Authorized member; marker bounded to visible room sequence. Presentation acknowledgment, not proof of human reading. |
| Chat | `POST /chat/messages/{messageId}/redact` | Message and room agency | Authorized member with supervision or administration, current consumer access and message visibility. Reason and expected revision; retained amendment. |
| Chat | `GET /chat/stream` | Actor agency and eligible live membership | HTTPS WebSocket, authenticated actor and token/session lease. Contentless notices only, periodic revalidation, bounded connections. |
| Profile | `POST /auth/renew` | User's `AgencyId` | Own validated user only; preserves the original authentication time and refuses renewal after the configured maximum session window. |
| Profile | `GET /me` | User's `AgencyId` | Own user only. |
| Profile | `GET /users/switchable` | User's `AgencyId` | Authenticated actor; response is restricted to the actor's agency. |
| Audit | `GET /audit-events` | Audit event's `AgencyId` | Administration permission; response is restricted to actor agency, a bounded date window, and at most 500 rows. |
| Admin | `GET /admin/overview` | Actor's `AgencyId` | Administration permission; every count is computed within the actor's agency. |
| Admin | `GET /admin/people` | Person and assigned user's `AgencyId` | Administration permission; both ownership markers must equal actor agency. |
| Admin | `GET /admin/schema-drift` | Not tenant-scoped — deployment metadata | Administration permission; returns table and column names plus applied migration ids for the connected database. No row data, so no consumer information. Schema shape is operational detail about the deployment, not something every signed-in case manager needs to enumerate. |
| Admin | `POST /admin/test-data/consumers/{personId}/delete` | Person and assigned user's `AgencyId` | Administration permission; both ownership markers must equal actor agency and the Person must carry the immutable creation-time test-data marker. Requires the exact versioned test-only attestation and expected Person revision, runs the complete dependent-record cleanup in one serializable transaction, and retains a PHI-minimized `test-data.consumer-deleted` audit event. Any billing claim line for the consumer blocks the operation, and missing/cross-agency records return 404. |
| Admin | `POST /admin/consumers/{personId}/delete-in-window` | Person's `AgencyId` | Administration permission in actor agency. Distinct from the test-data command above: no creation-time marker is required, but the Person must be within `ConsumerDeletionRules.DeletionWindowDays` (20 days) of its immutable `CreatedAtUtc`, no claim line for the consumer may belong to a `BillingPeriod` with billing that actually reached a payer (A1), and `ILegalHoldRegistry` must return exactly `Clear` — `Active`, `Unavailable`, or a registry exception all refuse before any row changes. Requires the exact versioned rule-3 attestation (distinct from the test-data one, so an older client cannot invoke the newer command) and expected Person revision. Retains an itemized `consumer.deleted-in-window` audit tombstone — ids, dates, and types per related record, never narrative, name, MaineCareId, birth date, or address. |
| Admin | `GET /admin/legal-holds` | Hold's `AgencyId` | Administration permission; actor agency only. Query string carries only a `personId`, never a name. |
| Admin | `POST /admin/legal-holds` | Actor's `AgencyId`, target Person's `AgencyId` | Administration permission; the named Person must be in actor agency. Records `legal-hold.placed`. |
| Admin | `POST /admin/legal-holds/{legalHoldId}/release` | Hold's `AgencyId` | Administration permission in actor agency; refuses an already-released hold. Release is single-admin for v1 — see `OPERATIONS.md` and `AGENDA.md` for the tracked dual-control gap. Records `legal-hold.released`. |
| Admin | `POST /admin/demo/seed-ssns` | Person's `AgencyId` | Administration permission; enabled in effect only when the API's startup-validated identity is exactly `SatiDemo` / `Demo`. Generates deterministic synthetic values server-side, encrypts through the configured Demo Key Vault, remains within actor agency, and records `person.ssn-updated` for every Person. It does not broaden the ordinary own-caseload SSN routes. |
| Admin | `GET /admin/activity` | Audit event's `AgencyId` | Administration permission; bounded activity feed with actor display names from the same agency. |
| Admin | `GET /admin/operations` | Actor's `AgencyId` | Administration permission; reports database health, retained audit/EDI counts, oldest-record timestamps, and the configured retention policy for the actor's agency. |
| Admin | `POST /admin/audit-export.csv` | Audit event's `AgencyId` | Administration permission; agency derived from the actor and never from the caller. Requires a 10–250 character reason and a window of at most 366 days, caps at 10,000 rows, marks the response `no-store`, and records one `audit.exported` event. Exported values are neutralized against spreadsheet formula evaluation. |
| Users | `POST /users` | New user's `AgencyId` | Supervision or administration permission; requested agency must equal actor agency. A non-administrator may create only a case-management-only user assigned to themself. |
| Users | `PUT /users/{userId}` | Target user's `AgencyId` | Supervision or administration permission in the same agency; non-administrators only manage assigned case-management-only users. |
| Users | `PUT /users/{userId}/password` | Target user's `AgencyId` | Same rule as user update. |
| Users | `PUT /users/me/password` | User's `AgencyId` | Own user plus current-password verification. |
| Supervisor | `GET /supervisor/supervisees` | Case manager user's `AgencyId` | Supervision permission sees assigned users with case-management permission; the current route returns directly assigned users. |
| Supervisor | `GET /supervisor/notes` | Note person's own and owning user's `AgencyId` | Supervision permission sees assigned case managers; agency-wide supervision broadens that to every case manager in the agency. Administration implies agency-wide supervision but does not on its own substitute for supervision. |
| Supervisor | `GET /supervisor/notes/page` | Note person's own and owning user's `AgencyId` | Existing paged review route: same supervision and caseload scope, bounded by the review page size and captured upper note ID. Added to this inventory during chat reconciliation; not a new chat route. |
| Supervisor | `GET /supervisor/notes/filters` | Case manager and person's `AgencyId` | Returns only case-manager and client choices inside the caller's supervisory scope. |
| Supervisor | `POST /supervisor/notes/{noteId}/approve` | Note person's own and owning user's `AgencyId` | Same supervisory scope; server owns approval transition and requires the caller's expected Note revision. |
| Supervisor | `POST /supervisor/notes/{noteId}/approve-override` | Note person's own and owning user's `AgencyId` | Same supervisory scope; reason and expected revision required; server records approver. |
| Supervisor | `POST /supervisor/notes/{noteId}/return` | Note person's own and owning user's `AgencyId` | Same supervisory scope; reason and expected revision required; server records returner. |
| Caseload | `GET /caseload` | Target user's `AgencyId` | Accessible case manager only. |
| Caseload | `GET /people/{personId}/journal` | Person's assigned user and agency | Own caseload only. |
| Caseload | `PUT /people/{personId}/journal` | Person's assigned user and agency | Own caseload only. |
| Caseload | `POST /people/{personId}/journal/entries` | Person's assigned user and agency | Own caseload only; same gate as the journal `PUT`. Server prepends the stamped entry and stamps from `ApiClock`, so the caller supplies only the text. |
| People | `POST /people` | Actor's user and agency | Case-management permission; creates only in the actor's own caseload and agency. `IsTestData=true` additionally requires administration permission and is otherwise rejected. |
| People | `PUT /people/{personId}` | Person's assigned user and agency | Own caseload only, with current persisted ownership/permission rechecked in the write transaction. The creation-time test-data classification is immutable. Relevant signer changes atomically cancel unfinished invitations or stop signed-copy portal access. |
| People | `PUT /people/{personId}/owner` | Person's agency, plus the current owner and the target user, all read from the database | Supervision permission. `CaseloadTransferRules` gates both ends: the actor must reach the current owner (their own caseload or a supervisee's) **and** the target, where reach requires the same agency, case-management permission, and either agency-wide supervision or a direct supervisor link. The request carries only a target id and a revision token — it cannot assert who the current owner is. Authorization is decided before the revision, so a refused caller learns nothing about the record's state. Records `person.reassigned` with the two user ids and nothing else; the move also lands in the consumer's own history as a `Reassigned` version. Stale revision answers `stale_person` 409. |
| People | `POST /people/credible-matches` | Actor's agency; the owner of each matched consumer | Case-management permission. Answers which Credible client ids the agency already holds, for import dedupe. Agency-scoped rather than caseload-scoped, because the duplicate an importing supervisor most needs to catch sits on a case manager's caseload. Returns no person id and no name — only the ids that matched, plus the owner's display name where `CaseloadTransferRules` says the caller could already see that caseload, so a plain case manager learns an id is taken without learning whose consumer it is. A POST, not a query string: these identify real people and a query string reaches access logs. Capped at 500 ids; response is not cacheable. |
| SSN | `GET /people/{personId}/ssn` | Person's assigned user and agency | Own caseload only. Returns the mask and an on-file flag; no route anywhere returns a plaintext SSN. Response is not cacheable. |
| SSN | `PUT /people/{personId}/ssn` | Person's assigned user and agency | Own caseload only. Shape-checked before encryption; audited as `person.ssn-updated` without the value. Null or empty clears every stored part including the last four. |
| DHHS forms | `POST /people/{personId}/forms.pdf` | Person's assigned user and agency | Own caseload only. The ONLY operation permitted to decrypt an SSN; records `person.ssn-decrypted` alongside `dhhs-form.generated`. Consent selections are taken only from the request body, never derived. Response is not cacheable. |
| Agency release | `POST /people/{personId}/agency-release.pdf` | Person's assigned user and agency | Own caseload only. Consumer and staff identities are derived server-side; the request carries only recipient details and explicit authorization choices. Records `agency-release.generated`; response is not cacheable. No SSN is read. |
| Person audit | `GET /people/{personId}/history` | Person and assigned user's `AgencyId` | Administration permission; both Person and assigned user must belong to actor agency. Response is not cacheable. |
| Person audit | `GET /people/{personId}/history.pdf` | Person and assigned user's `AgencyId` | Administration permission in actor agency; generated PDF remains behind the same check and is not cacheable. |
| Contacts | `GET /people/{personId}/contacts` | Contact's person, assigned user, and agency | Own caseload only. |
| Contacts | `POST /people/{personId}/contacts` | Contact's person, assigned user, and agency | Own caseload only. |
| Contacts | `PUT /people/{personId}/contacts/{contactId}` | Contact's person and assigned user | Own caseload and current permission rechecked inside the write transaction; contact must belong to route person. Identity/address/capacity/active changes atomically invalidate outstanding external signing access. |
| Contacts | `DELETE /contacts/{contactId}` | Contact's person and assigned user | Own caseload and current permission rechecked inside the write transaction; soft archive and signing-access invalidation commit together. |
| Reviews | `GET /reviews` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `GET /people/{personId}/reviews` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `POST /reviews/ensure-current` | Each review person's assigned user and agency | Processes only people belonging to accessible case managers; inaccessible IDs are skipped. |
| Reviews | `PUT /reviews/{reviewItemId}/stage` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `PUT /reviews/{reviewItemId}/appointment` | Review person's assigned user and agency | Accessible case manager only. |
| Reviews | `GET /people/{personId}/appointments/latest` | Appointment review's person, assigned user, and agency | Accessible case manager only. |
| Assessments | `POST /people/{personId}/assessments/draft` | Person's assigned user and agency | Assigned case manager alone may author; `authorUserId` must equal actor. |
| Assessments | `GET /people/{personId}/assessments/latest` | Person's assigned user and agency | Read-only agenda progress source; assigned case manager alone, no draft creation. |
| Assessments | `PUT /assessments/{assessmentId}/document` | Assessment author plus owned person | Author alone may edit; approved/superseded versions are locked. |
| Assessments | `POST /assessments/{assessmentId}/submit` | Assessment author plus owned person | Author alone may submit their editable draft. |
| Assessments | `GET /people/{personId}/pcp-source` | Person's assigned user and agency | Accessible case manager; read-only supervisory access is allowed. |
| Consumer providers | `GET /people/{personId}/providers` | Person's assigned user and agency | Accessible case manager only. Returns ended links as well as current ones; the caller decides what to show. |
| Consumer providers | `POST /people/{personId}/providers` | Person's assigned user and agency, plus provider's `AgencyId` | Accessible case manager only. A caller-supplied `providerId` is resolved only against the actor's own agency, so a directory entry from another tenant is rejected as absent. |
| Consumer providers | `PUT /people/{personId}/providers/{linkId}` | Person's assigned user and agency, plus provider's `AgencyId` | Accessible case manager only. The link must belong to the consumer named in the route, so a link id cannot select the scope it is then checked against. |
| Consumer providers | `DELETE /people/{personId}/providers/{linkId}` | Person's assigned user and agency | Accessible case manager only; same link-belongs-to-consumer check. Removal is for a mis-entry — ending a real relationship is a `PUT` that sets `EndDate` and keeps the row. |
| Providers | `GET /providers` | Provider's `AgencyId` | Authenticated actor; response restricted to actor agency. |
| Providers | `POST /providers` | Actor's `AgencyId` | Case-management permission; agency is assigned server-side. A caller-supplied `parentProviderId` is resolved only against the actor's own agency, so a parent in another tenant is rejected as absent. |
| Providers | `PUT /providers/{id}` | Provider's `AgencyId` | Case-management permission in the same agency. Same agency-scoped resolution of `parentProviderId`; an entry cannot be repointed across a tenant boundary. |
| Providers | `DELETE /providers/{id}` | Provider's `AgencyId` | Administration permission in same agency. Refused with `provider_has_affiliated_entries` while entries are affiliated beneath it, and with `provider_on_consumer_records` while any consumer record references it — a count only, never consumer names. Both checks run before any other state is touched. |
| Provider contacts | `GET /providers/{providerId}/contacts` | Provider's `AgencyId` | Authenticated actor in the same agency. These are shared-directory contacts and carry no consumer identity. |
| Provider contacts | `POST /providers/{providerId}/contacts` | Provider's `AgencyId` | Case-management permission in the same agency. Provider id is resolved inside the actor's agency before the contact is written. |
| Provider contacts | `PUT /providers/{providerId}/contacts/{contactId}` | Provider's `AgencyId`, contact's `ProviderId` | Case-management permission in the same agency; contact must belong to the provider named in the route. |
| Provider contacts | `DELETE /providers/{providerId}/contacts/{contactId}` | Provider's `AgencyId`, contact's `ProviderId` | Case-management permission in the same agency; same contact-belongs-to-provider check. |
| Provider merge | `POST /providers/{survivingId}/merge` | Both providers' `AgencyId` | Administration permission; both entries must be in the actor's agency. Runs atomically, refuses identifier/tier/loop/current-consumer-link conflicts, moves live references, leaves assessment snapshots untouched, and records `provider.merged` without names or consumer IDs. |
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
| Settings | `PUT /settings` | Settings `AgencyId` | Administration permission in actor's agency; provider references must share the agency. |
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
| Reports | `GET /reports/productivity-units` | Validated actor's user and agency | Own caseload only; both Person and Note agency markers must match, the request accepts no user id, and the response contains narrative-free monthly aggregates. |
| Billing | `POST /billing/periods/{year}/{month}` | Billing period user's `AgencyId` | Billing permission; target user must be in actor agency. |
| Billing | `GET /billing/periods` | Billing period user's `AgencyId` | Billing permission; response joined to actor agency. |
| Billing | `POST /billing/periods/{periodId}/responses` | Billing period user's `AgencyId` | Billing permission; the period is resolved through its owning user's agency, so a response cannot be attached to another tenant's history. No tenant is ever read from the document. `IsSynthetic` comes from the document's ISA15 usage indicator, not from configuration. |
| Billing | `POST /billing/periods/{periodId}/mock-clearinghouse` | Billing period user's `AgencyId` | Billing permission, and additionally restricted to a validated `SatiDemo`/`Demo` deployment or the isolated test host. Returns 404 elsewhere so the route is absent in effect on Production. Requires a retained test 837P, consumes its exact immutable content once, records a synthetic `Transmitted` event, and ingests fabricated responses through the same path as a real response. |
| Billing | `GET /billing/submissions` | Event `AgencyId` plus billing period user's `AgencyId` | Billing permission; both ownership markers must equal actor agency. Synthetic provenance is explicit. |
| Billing | `GET /billing/remittances` | Outcome `AgencyId` | Billing permission; returns bounded claim-level outcomes for actor agency, without raw 835 or note narrative. |
| Billing | `GET /billing/remittance-deposits` | Deposit `AgencyId` | Billing permission; returns bounded 835/EFT reconciliation totals for actor agency. Provider-level (PLB) adjustments are explicit; no bank credentials are returned. |
| Billing | `GET /billing/configuration` | Authenticated actor's `AgencyId` | Billing permission; returns only the actor agency's payer/provider defaults. |
| Billing | `PUT /billing/configuration` | Authenticated actor's `AgencyId` | Billing permission; writes and audits only the actor agency's configuration. |
| Billing | `GET /billing/candidates` | Note person's owning user's `AgencyId` | Billing permission; candidates joined to actor agency. |
| Billing | `POST /billing/claim-lines` | Source note person's owning user's `AgencyId` | Billing permission; source note must be approved and in actor agency. |
| Billing | `GET /billing/claim-lines/draft` | Billing period user's `AgencyId` | Billing permission; period owner joined to actor agency. |
| Billing | `POST /billing/periods/{periodId}/submit` | Billing period user's `AgencyId` | Billing permission in same agency. |
| Billing | `POST /billing/periods/{periodId}/edi` | Billing period user's `AgencyId` | Billing permission; period, every source note/person, and generated file must remain in actor agency. |
| Forms | `POST /forms/delete` | Form person's assigned user and agency | Own caseload only; all requested IDs must be owned. Refuses any form carrying append-only attestation history. |
| Forms | `PUT /forms/{id}` | Form person's assigned user and agency | Own caseload only. Changes `OpenedDate`; any attempt to change `CompletedDate` is rejected because completion belongs to attestation/revocation. |
| Forms | `POST /people/{personId}/forms/{type}/attestation` | Form person's assigned user and agency | Accessible case manager only through `TenantAccess.CanAccessUserAsync`; Form id, person id, and type must identify the same row. Server rechecks date/cycle/evidence rules, writes actor from validated identity, and returns typed 409 on a concurrent attestation change. |
| Forms | `POST /people/{personId}/forms/{type}/attestation/revoke` | Form person's assigned user and agency | Same accessible-caseload gate; a nonblank reason is required and the actor is server-derived. A successful live revocation is append-only and audited. |
| Forms | `GET /people/{personId}/forms/{type}/prerequisite` | Form person's assigned user and agency | Same accessible-caseload gate; form id, person id, and type must match before the API derives the live prerequisite state. |
| Forms | `GET /people/{personId}/attestations/pending` | Person's assigned user and agency | Accessible case manager only through `TenantAccess.CanAccessUserAsync`; derives suggestions from eligible notes and forms after the gate. |
| Annual documents | `POST /people/{personId}/documents/{kind}` | Person's assigned user and agency | Assigned case manager for Agency/Medical releases. Privacy/Safety Plan rendering also admits reviewers authorized by `TenantAccess.CanAccessUserAsync`, never every supervisor merely sharing an agency. Identity, template/source version, approval status and artifact provenance are derived server-side. Privacy generation does not create acknowledgment or completion. |
| Annual documents | `GET /people/{personId}/annual-documents` | Person's agency and assigned user | Same-agency accessible caseload through `TenantAccess.CanAccessUserAsync`; window, live artifacts, receipt IDs and preparation reminder are derived server-side. |
| Annual documents | `POST /people/{personId}/annual-packet` | Person's agency and assigned user | Assigned case manager through `OwnsPersonAsync`; validates the anniversary/window, reads authorization and medical-release attestation and records artifacts in one serializable transaction. Downloads only. |
| Annual documents | `POST /people/{personId}/documents/privacy-practices/acknowledgment` | Person and artifact's agency | Accessible caseload; exact live generated Privacy Practices artifact must belong to that person/agency. Validated receipt date or good-faith effort; actor server-derived, append-only. |
| Annual documents | `POST /people/{personId}/documents/verify` | Person and artifact's agency | Accessible caseload; historical or live artifact must belong to person/agency. Compares SHA-256 and length; no file bytes uploaded. |
| Safety plans | `GET /people/{personId}/safety-plans/latest` | Person's agency and assigned user | Accessible caseload, including authorized supervisors; validates cycle and returns only its latest version. |
| Safety plans | `POST /people/{personId}/safety-plans/draft` | Person's agency and assigned user | Assigned case manager; caller author must equal validated actor. New immutable-version row only after Approved/Returned; existing Draft/ReadyForReview is returned unchanged. |
| Safety plans | `PUT /safety-plans/{planId}/document` | Plan person's agency and assigned user | Assigned case manager and original author; Draft only; shared schema validation and expected revision required. |
| Safety plans | `POST /safety-plans/{planId}/submit` | Plan person's agency and assigned user | Same author gate; validated caller author, complete Draft, and expected revision. |
| Safety plans | `POST /safety-plans/{planId}/approve` | Plan person's agency and assigned user | Supervisor permission plus `CanAccessUserAsync`; cannot review own plan. ReadyForReview and expected revision required. |
| Safety plans | `POST /safety-plans/{planId}/return` | Plan person's agency and assigned user | Same supervisor/nonself/caseload gate; ReadyForReview, expected revision and reason required. |
| People | `PUT /people/{personId}/status` | Person's agency and assigned user | Shared `PersonStatusRules`: own case manager may set allowed lifecycle statuses; Ghost requires administration. Tenant scope, expected revision, history and audit enforced. |
| Annual documents | `POST /people/{personId}/documents/{kind}/external` | Person's assigned user and agency | Accessible case manager only; validates a supported prerequisite kind, cycle anniversary, and required external-record note before recording the artifact. |
| Annual documents | `GET /people/{personId}/documents` | Person's assigned user and agency | Accessible case manager only; lists live artifact metadata for one requested cycle after tenant validation. |
| Document templates | `GET /agencies/{agencyId}/templates/{kind}` | Actor's agency | Administration permission only; requested agency must equal actor agency. Returns that agency's versions and Sati-default versions, never another agency's. |
| Document templates | `POST /agencies/{agencyId}/templates/{kind}` | Actor's agency | Same Administration/agency gate; validates closed tokens and source bounds, derives author/version/timestamp, appends an immutable version plus audit event, and returns typed 409 on a version collision. Cannot publish a global default. |
| Incidents | `POST /incidents` | Authenticated actor's `AgencyId` | Reports only a validated PHI-minimized envelope; agency is derived from the token, never the request. |
| Incidents | `GET /admin/incidents` | Incident `AgencyId` | Administration permission; actor agency only. |
| Incidents | `PUT /admin/incidents/{incidentId}/status` | Incident `AgencyId` | Administration permission; same-agency status transition is audited. |
| Platform health | `GET /platform/incidents` | All incident agencies | `PlatformOperator` only; every cross-tenant view is audited. Agency administration permission does not grant access. |

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

The gate coverage behind this table is uneven, and the 2026-08-30 permissions audit measured it by
mutation rather than by reading. The billing, provider delete/merge, settings, user-management, and
Director-scope gates fail their tests when removed. The remaining supervision and case-management
gates still do not — no test in either suite notices when those are disabled, though most sit above
separately data-scoped queries, so removing one usually yields an empty result rather than a leak.
See `API_SECURITY_AUDIT.md`, third pass, before relying on a green suite as evidence for those rows.

The 2026-09-03 reconciliation includes the partial endpoint files `SafetyPlanEndpoints.cs` and
`AnnualPacketEndpoints.cs`, not just `ApiEndpoints.cs`. The 169 protected routes match in both
directions after normalizing route parameter constraints. Re-run this comparison on route changes.

Safety-plan regressions were proven to fail with the old same-agency-only supervisor check and
with the revision check removed. Packet isolation fails with its ownership gate removed. Receipt
immutability fails when its append-only guard is removed, in both API and local persistence.

### Supervisor review page (2026-09-05)

`GET /api/v1/supervisor/notes/page?afterId=&throughId=&userId=&personId=&fromDate=&toDate=&searchTerm=` requires supervisor permission.
A supplied userId passes `TenantAccess.CanAccessUserAsync` before reaching the query. The query
also validates a supplied personId against that same review scope and restricts every result to the
actor's agency and supervised case managers, or agency-wide supervision when granted. Date and text
filters only narrow that authorized set. Cursor values only narrow that authorized set. Page size is fixed at 10,
and the first page begins with the newest submitted note while the captured upper ID keeps later pages stable.
The existing `POST /supervisor/notes/{noteId}/approve` optionally accepts `MaximumUnits`; the server
rechecks threshold eligibility and service-time conflicts before the same revision-checked,
audited approval. The threshold never grants additional access or a compliance override.
Mutation verification: removing the new page's supervision gate makes the demoted-supervisor test
fail; disabling threshold eligibility makes zero-minute and over-threshold approval tests fail.
