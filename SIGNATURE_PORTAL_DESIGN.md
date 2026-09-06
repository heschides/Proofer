# Electronic signature portal — design

*Designed 2026-09-05 against release 1.2.47. Status: design only. No code has been written.*

Read `CLAUDE.md`, `REGULATORY_CONCERNS.md`, and the "Electronic signature portal — vetted
direction" section of `AGENDA.md` before implementing this. That AGENDA section was reviewed
2026-08-07 with policy citations and is **binding on this design**; where the two disagree, AGENDA
wins and this document is wrong. `HANDOFF_SIGNATURE_PORTAL.md` is the brief for whoever builds it.

Scope decided with Josh on 2026-09-05: all six `AnnualDocumentKind` values, a separate
`Sati.Portal` application, one signer per request.

---

## 1. What this is

A consumer or guardian receives an email, opens a link to a Sati-hosted page, enters a PIN
established in advance with their case manager, reads the exact document, and signs it. Sati then
produces a signed PDF and an evidence record.

Email is the delivery channel only. Sati owns the identity check, the document, the intent-to-sign
action, the evidence, and the resulting artifact. Sati does not operate inbound mail and never
treats an email reply as a signature.

### The honest framing

DocuSign's value is not the signature image. It is the **certificate of completion**: an
append-only evidence record binding a specific person, authenticated a specific way, to a specific
byte-identical document, at a specific time, having been shown specific consent language. The
image is decoration. Build the evidence record first and the signature becomes a rendering
detail.

This design is much smaller than DocuSign because the documents are Sati's own, there is one
signer, there are no drag-and-drop signature fields, and there is no template designer. It is
**not** smaller in the parts that carry legal weight.

### What this is not

It is not a claim that any given document, signed this way, satisfies a specific Maine, OADS, or
MaineCare requirement. Section 3 sets out which kinds are cleared and which are gated, and the
gate is enforced in code.

---

## 2. Four gaps this feature has to fill before it can exist

None of these exist in the repository today. Each is a real subsystem, and underestimating them is
the main risk to the "very simple" framing.

| Gap | Current state | What it needs |
|---|---|---|
| **Document bytes** | `DocumentArtifact` stores a SHA-256 hash and byte count. Its own comment says "PDF bytes are never stored here." | Byte storage. You cannot show a consumer a document you did not keep. |
| **Email** | No SMTP, SendGrid, `MailMessage`, or any sender anywhere in the solution. | A transactional email service under a HIPAA business associate agreement. |
| **A public surface** | Every `/api/v1` route is `RequireAuthorization()` plus `ValidatedActorFilter`. Nothing in Sati is reachable without an agency credential. | An internet-facing application with a different threat model. |
| **A signature block to fill** | Only `AgencyReleasePdfGenerator` draws signature lines. The medical release, safety plan, and template composer draw none. | A standard appended signature page, so five generators do not need editing. See section 8. |

---

## 3. Six document kinds, six different meanings

Signing all six was chosen deliberately, and the design refuses to pretend they mean the same
thing. A single "signed" flag across six kinds would be the defect.

New owner in `Sati.Contracts.V1`: `SignatureMeaningCatalog`. One entry per kind, carrying the
signature's meaning, the capacities permitted to sign it, and its policy status.

| Kind | What a signature means | Permitted signer capacity | Policy status |
|---|---|---|---|
| `ReleaseAgency` | Authorization to disclose, under the agency's own wording. | Consumer, guardian, authorized representative. | **Cleared.** Sati-owned wording, agency is the relying party. |
| `ReleaseMedical` | Authorization to disclose medical information. | Consumer, guardian, authorized representative. | **Cleared**, subject to the existing `REGULATORY_CONCERNS.md` caveat that this is Sati-owned wording and not an official form. |
| `PrivacyPractices` | **Acknowledgment of receipt.** Not authorization, not agreement. | Consumer, guardian, authorized representative. | **Cleared** as an acknowledgment. The existing `DocumentAcknowledgment` record remains the compliance projection; see 3.1. |
| `SafetyPlan` | Agreement to the plan's content. | Consumer, guardian. | **Gated.** No settled requirement that a consumer signs a safety plan at all, and `REGULATORY_CONCERNS.md` already flags the schema as needing agency review. |
| `ReleaseDhhs` | Authorization on a **state-owned form**. | Consumer, guardian, authorized representative. | **Gated.** Nobody has confirmed DHHS accepts an electronically signed version of its own form. |
| `MedicalRecordsRequest` | Nothing. See below. | None. | **Not applicable.** |

### The records request does not get signed

It is a letter from the agency to a provider, and its authority comes from the consumer's already
signed medical release. A consumer signature on the request itself would be a second, weaker
authorization sitting beside the real one, and would invite a provider to rely on the wrong
document. `SignatureMeaningCatalog` returns "not signable" for this kind and the API refuses to
create a request for it. This is a rule, not an omission.

### Enforcing the gate in code

`SignatureMeaningCatalog` exposes `PolicyStatus` per kind: `Cleared`, `GatedPendingConfirmation`,
`NotSignable`. `POST /signature-requests` refuses anything not `Cleared` with a message naming the
outstanding confirmation.

The gated kinds ship with **all** their plumbing built and tested. Flipping one to `Cleared` after
Josh has the written answer is a one-line change plus a test, not a project. That is what "all six
kinds" buys: the gate is a policy switch, not missing code.

The two gates are already open items in `AGENDA.md`'s vetted direction. Do not close either one by
editing this catalog without written confirmation recorded in `REGULATORY_CONCERNS.md`.

### 3.1 Acknowledgment is not authorization

The privacy notice's signature records that the consumer received it. It must not be rendered,
described, or stored in a way that reads as agreement to anything. `SignatureMeaningCatalog`
carries the distinction, the appended evidence page prints the acknowledgment wording rather than
the authorization wording, and the existing `DocumentAcknowledgment` row remains what the annual
packet consults. An electronic acknowledgment sets `ReceivedOn`; it does not replace the record.

---

## 4. The trust boundary — `Sati.Portal`

A separate ASP.NET Core application, its own Azure App Service, its own managed identity, its own
deployment pipeline. It is the only internet-facing surface that serves a page to someone without
an agency credential.

```text
  WPF client ──► Sati.Api ──────────┐
  (agency staff, JWT)               │      Azure SQL
                                    ├──►   (two SQL users, different grants)
  Consumer browser ──► Sati.Portal ─┘
  (token + PIN)              │
                             └──► Blob container (read-only identity)
```

### The control that actually matters

`Sati.Portal` connects to Azure SQL as **its own SQL user with explicit table-level grants**, not
as the API's identity:

| Grant | Objects |
|---|---|
| `SELECT` | `SignatureRequest`, `SignatureEvent`, `SignatureConsent`, and a **view** over `DocumentArtifact` exposing only id, kind, hash, byte count, and blob reference. |
| `INSERT` | `SignatureEvent`, `SignatureConsent`. |
| `UPDATE` | `SignatureRequest` state columns only. |
| Nothing at all | `People`, `Notes`, `Users`, `AuditEvents`, `ChatMessage`, every other table. |

This is the point of the separate application. Least privilege enforced by the database, not by
application code, means a SQL injection, a deserialization bug, or a plain logic error in the
portal cannot read a caseload. Code-level checks are the second layer, not the first.

Verify the grants with a test that connects as the portal user and asserts that
`SELECT TOP 1 * FROM People` is refused. A grant list nobody tests is a comment.

### The portal is deliberately dull

No client-side framework, no CDN dependencies, no analytics, no third-party script of any kind.
Server-rendered pages, a strict Content-Security-Policy, `no-store` on every response that carries
document content. The attack surface of a page is proportional to what is on it.

### Rejected: anonymous routes inside `Sati.Api`

One misordered endpoint filter, one `AllowAnonymous` on a group instead of an endpoint, and the
authenticated API is on the open internet. `ValidatedActorFilter`'s own `PlatformOperator` path
allowlist is evidence of how much care a mixed-trust route table already demands. Adding an
untrusted audience to it is the wrong direction.

---

## 5. Identity, and the honest limits of a PIN

### The threat model

To sign, an attacker needs **both** a high-entropy single-use link and a PIN they were never sent.
Neither alone is sufficient. That is the whole security argument, and everything below protects one
half of it.

### The PIN's real problem

A six-digit PIN has one million possibilities. `PasswordVerifier` uses PBKDF2-SHA256 at 100,000
iterations, which is correct for passwords and **does not save a PIN**: an attacker holding a
leaked database can exhaust a six-digit space offline regardless of iteration count. A PIN's
security cannot come from its hash.

Three things carry it instead:

1. **An HMAC pepper from Key Vault**, applied before hashing, using the existing `IKeyWrapper`
   seam. A leaked database without the vault key yields nothing to attack offline. This is the
   difference between a PIN that is defensible and one that is not.
2. **Online rate limiting and lockout.** Five failed attempts locks the request; unlocking requires
   a case manager, who confirms identity by another means and records it. Failures are counted per
   request and per source, and every one is an evidence row.
3. **The token.** The PIN is never the only factor. A guessed PIN with no link is useless.

Minimum six digits, no repeated-digit or sequential values, and never the consumer's birth date or
the last four of an identifier. Validate in `Sati.Contracts.V1.SigningPinRules` so the portal and
the API agree.

### Establishing the PIN

The consumer types it themselves, on the case manager's screen, at a meeting they were already
having. The field is masked, the case manager does not see it, and Sati stores only the peppered
hash. This is the same interaction as setting a PIN at a bank counter.

Two consequences the design accepts:

- **Staff cannot recover a forgotten PIN, only reset it.** A reset is a case-manager action
  requiring identity confirmation by another means, recorded with a reason as an evidence row. Per
  the vetted direction, recovery must not be easier to exploit than the normal flow.
- **The PIN is never sent by email**, never printed in a notification, never shown in the desktop
  client after establishment, and never logged. It is established out of band or the second factor
  is not a second factor.

### Shared mailboxes, and why one request means one person

A household email may reach a consumer, a spouse, and an adult child. The link plus PIN pair is
what identifies the signer, not the mailbox. The design therefore forbids:

- Reusing one request for two signers.
- Treating delivery to a shared address as evidence that a particular person signed.
- Any flow where the same PIN would sign for two people.

A guardian signs their **own** request, under their own PIN, in their own name and capacity, and
never as though they were the consumer. `SignerCapacity` is recorded on the request and printed on
the evidence page.

### Authority to sign as guardian

`PersonContact` already carries `Guardian` and `AuthorizedRepresentative` kinds. Selecting one as
signer requires the case manager to affirm that authority is documented and to record what
document establishes it. Sati does not verify guardianship; it records the assertion and who made
it. Say that plainly rather than implying verification.

---

## 6. The link token

- 256 bits from `RandomNumberGenerator`, base64url, in the URL path.
- **Stored as a SHA-256 hash.** A leaked database does not yield working links. No pepper needed
  here; 256 bits is not brute-forceable.
- **72-hour expiry**, configurable per agency between 24 and 168 hours.
- Revocable by the case manager at any time, immediately.
- **Nothing meaningful in the URL.** No person id, no document id, no MaineCare identifier, no
  agency name, no cycle date. The token is the entire path segment.
- Rate-limited by token and by source address.
- A token that has completed, been declined, expired, or been revoked returns the same neutral
  page as one that never existed. Distinguishing them tells an attacker which tokens are real.
- Reissuing sends a **new** token and revokes the old one. A resend never revives a dead link.

Single-use applies to **issuance**, not to viewing. After the PIN succeeds, the portal issues a
short-lived session cookie scoped to that request so a consumer can scroll a long release without
re-entering the PIN. The cookie is `HttpOnly`, `Secure`, `SameSite=Strict`, and expires in 30
minutes.

---

## 7. The email

**Azure Communication Services Email**, under Microsoft's HIPAA business associate agreement,
authenticated with the API's managed identity, on an agency-controlled sending domain with SPF,
DKIM, and DMARC configured. Outbound only. No inbound mailbox, ever.

### Content

Content-free by default. No consumer name, no document kind, no cycle date, no identifier of any
sort. The body says that a document is ready to review and sign, gives the link, says the PIN was
set in person, and says what to do if the message was unexpected.

**The phishing tension, and how it is resolved.** A completely generic email is indistinguishable
from a phishing attempt, and an email nobody clicks is a broken feature. The resolution is out of
band: at PIN establishment the case manager tells the consumer that an email is coming, roughly
when, and what it will look like. That solves recognition without putting "you are a client of a
disability services agency" in an unencrypted mailbox.

Naming the agency in the email is a per-agency setting, defaulting to **off**, with the tradeoff
stated in the settings help text. It is the agency's disclosure decision, not Sati's.

### Delivery events

Sent, delivered, bounced, and suppressed are recorded as evidence rows and surfaced to the case
manager. A bounce must be visible: silently failing to reach a consumer while the desktop shows
"sent" is the failure mode that wastes a month of a compliance cycle.

A bounce never retargets a request to a different address. Correcting an address revokes the
request and issues a new one, so the evidence trail shows both.

---

## 8. Freezing, and producing the signed document

### Freeze

A signature request can only be created against a **frozen** artifact: bytes stored, SHA-256
recorded, and the artifact marked immutable. Regenerating the document afterwards produces a new
artifact and supersedes the old one, exactly as `DocumentArtifact.MarkSuperseded` already models.

**An open request against a superseded artifact is revoked automatically**, with a reason. Signing
a document that has since been replaced is the single worst outcome this feature can produce, and
it must be structurally impossible rather than merely discouraged.

Blocking incomplete documents is a MaineCare condition in the electronic-signature notice. The
existing `BlankFieldsJson` on `DocumentArtifact` already records unfilled fields; a request is
refused when it is non-empty, unless a supervisor overrides with a recorded reason.

### Byte storage

Azure Blob Storage, private container, no public access, managed identity, Microsoft-managed
encryption at rest. Path is `{agencyId}/{artifactId}/{sha256}.pdf`, so the path itself carries no
consumer identifier. `Sati.Api` writes; `Sati.Portal` reads through a read-only identity.

Blobs are never deleted by the application. Retention follows `OPERATIONS.md`, which is still
`PolicyOnly`, and legal hold applies through the existing `ILegalHoldRegistry` because a document
is consumer-scoped.

**Rejected:** storing PDFs in SQL. Multi-megabyte `varbinary(max)` rows would bloat every backup,
every Demo reset, and the migration chain, for a workload that is pure sequential read.

### The signed artifact

At completion, Sati produces a **new** PDF: the frozen bytes, unchanged, with a signature and
evidence page appended.

The frozen bytes are never modified. That is what makes "the signer saw exactly this" provable
against the recorded hash. PdfSharp is already a dependency and page-append is straightforward.

The appended page carries:

- Document kind, cycle, artifact id, and the **SHA-256 of the frozen bytes**.
- Signer name, capacity, and the email address the request was delivered to.
- The exact consent and intent wording shown at the moment of signing, quoted.
- Signing timestamp with time zone, authentication method, and the request id.
- A statement of what the signature is and is not, in the manner of
  `AtRequestPublication.AttestationScopeNotice`.
- The full evidence timeline: issued, delivered, first viewed, PIN attempts, signed.

The signed artifact gets its own `DocumentArtifact` row with its own hash, linked to the frozen
one. Both are retained. Hash verification must actually run somewhere and not merely be stored —
`REGULATORY_CONCERNS.md` already notes that a stored hash nobody checks proves nothing.

Where a generator already draws an ink signature line, as the agency release does, that line stays
blank on the frozen copy and the appended page states that it is superseded by the electronic
signature recorded there. Rendering those generators in an "electronic signature pending" mode is
a follow-up, not v1.

---

## 9. Consent to transact electronically

This is the requirement most home-grown signing systems miss, and it is not optional.

Before a consumer's first electronic signature, Sati must capture their agreement to transact
electronically, per Maine UETA and the ESIGN consumer-consent provisions. The disclosure states:

- That signing electronically is **voluntary**, and that paper and in-person signing remain
  available at no disadvantage. Electronic transactions must never be a condition of receiving
  services.
- How to withdraw consent, and that withdrawal does not invalidate signatures already given.
- How to obtain a paper copy, and at what cost, which for an agency's own consumer should be none.
- The hardware and software needed to access and retain the documents.
- How to update the email address.

Captured once per consumer per agency as a `SignatureConsent` row, with the disclosure version and
its full text frozen onto the row. Revisions create a new version and require fresh consent.
Withdrawal is its own row; it revokes open requests and does not touch completed ones.

**A signature captured without a current consent record is not usable evidence.** The API refuses
to create a request when consent is absent or withdrawn, and the portal re-presents the disclosure
as the first screen if consent is unrecorded.

---

## 10. The signing session

Five screens, plain language, one decision each.

1. **Welcome.** No PHI. States that a document is waiting and that a PIN is required. Offers "I
   was not expecting this" with an agency phone number.
2. **PIN.** Masked entry, attempts remaining shown after the first failure, lockout at five with a
   plain instruction to call the case manager.
3. **Consent**, if not already recorded. Section 9's disclosure, with an explicit accept.
4. **Review.** The document rendered in the page and downloadable. Long documents must be
   scrollable and the signing control is disabled until the end is reached or the download is
   taken. A signature obtained without the document having been displayed is not a signature.
5. **Decide.** Three outcomes, equally weighted visually:
   - **Sign.** Typed full name, an explicit checkbox affirming intent, and the frozen consent
     wording immediately above the button. The typed name must reasonably match the expected
     signer; a mismatch warns rather than blocks, and the entered name is recorded as typed.
   - **Decline.** With an optional reason. Ends the request. A signature flow with no way to
     refuse is not consent.
   - **Request changes.** With a required comment. Returns it to the case manager without
     signing.

Then a confirmation screen with the signed PDF available to download, and the same copy emailed as
a link into the portal rather than as an attachment.

### Intent is an affirmative act

UETA recognizes an electronic process adopted **with intent to sign**. The signature is the
combination of the typed name, the checkbox, and the button, with the consent wording visible.
Never a pre-checked box, never a signature inferred from viewing, never a "by continuing you
agree" banner.

### Accessibility is a requirement, not a polish item

Keyboard-only operation, screen-reader labels on every control, visible focus, WCAG AA contrast,
usable at 320 pixels wide and at 200 percent zoom, no time limit that expires mid-read, plain
language at roughly an eighth-grade reading level, and an accessible downloadable PDF. The people
signing these documents include people with disabilities; that is the entire consumer population
of this product.

### The assisted path

Some consumers cannot operate a portal, share an email account, have no device, or use supported
decision making. For them the case manager records a paper or in-person signature exactly as today
through `DocumentArtifact.External`. The electronic path never becomes the only path, and the UI
must not present it as the default.

---

## 11. Data model

New file `Sati.Persistence/Models/Signatures.cs` with server twins in `ApiDbContext`.

```text
SignatureRequest
  Id                      int, identity
  AgencyId                int, required
  PersonId                int, required
  DocumentArtifactId      int, required        -- the frozen artifact
  FrozenSha256            char(64), required   -- copied at freeze; compared before signing
  Kind                    int, required        -- AnnualDocumentKind
  SignerCapacity          int, required        -- Consumer, Guardian, AuthorizedRepresentative
  SignerContactId         int, null            -- PersonContact, when not the consumer
  SignerName              nvarchar(120)        -- expected name, snapshotted
  DeliveryEmail           nvarchar(254)        -- snapshotted at issue
  TokenSha256             char(64), required
  PinHash                 nvarchar(64)         -- PBKDF2 over HMAC-peppered PIN
  PinSalt                 nvarchar(32)
  PinKeyId                nvarchar(128)        -- pepper key version, for rotation
  FailedPinAttempts       int, required
  LockedAtUtc             datetime2, null
  State                   int, required        -- Issued, Viewed, Signed, Declined,
                                               -- ChangesRequested, Expired, Revoked
  ConsentId               int, required        -- the SignatureConsent relied on
  IssuedAtUtc             datetime2, required
  IssuedByUserId          int, required
  ExpiresAtUtc            datetime2, required
  CompletedAtUtc          datetime2, null
  SignedArtifactId        int, null            -- the produced signed document
  TypedSignerName         nvarchar(120), null  -- as actually typed
  ConsentStatementShown   nvarchar(2000), null -- frozen wording at the moment of signing
  DeclineReason           nvarchar(500), null
  RevokedReason           nvarchar(240), null
  unique index (TokenSha256)
  index (AgencyId, PersonId, State)
  index (DocumentArtifactId)

SignatureEvent                                  -- append-only. No updates, no deletes.
  Id                      bigint, identity
  SignatureRequestId      int, required
  AgencyId                int, required
  OccurredAtUtc           datetime2, required
  Kind                    int, required        -- Issued, EmailSent, EmailDelivered,
                                               -- EmailBounced, Viewed, PinFailed, PinLocked,
                                               -- PinReset, ConsentAccepted, DocumentDisplayed,
                                               -- Signed, Declined, ChangesRequested,
                                               -- Expired, Revoked, Reissued
  ActorKind               int, required        -- Signer, Staff, System
  ActorUserId             int, null            -- staff only
  DetailJson              nvarchar(1000)       -- no PHI, no PIN, no token
  index (SignatureRequestId, OccurredAtUtc)

SignatureConsent                                -- ESIGN/UETA consent to transact electronically
  Id                      int, identity
  AgencyId                int, required
  PersonId                int, required
  SignerCapacity          int, required
  SignerContactId         int, null
  DisclosureVersion       int, required
  DisclosureText          nvarchar(max), required  -- frozen, not looked up at render
  AcceptedAtUtc           datetime2, null
  WithdrawnAtUtc          datetime2, null
  WithdrawnReason         nvarchar(240), null
  index (AgencyId, PersonId, SignerCapacity)

DocumentArtifact                                -- additions to the existing table
  BlobPath                nvarchar(400), null   -- null for pre-existing rows
  IsFrozen                bit, required default 0
  SignedFromArtifactId    int, null             -- set on a produced signed artifact
```

`SignatureEvent` is append-only in the manner of `AuditEvent`. Enforce it: the API's EF
`SaveChanges` rejects a tracked modify or delete on this type, as published document templates
already do.

The consent disclosure text is frozen onto the row rather than looked up. The reasoning is
`AtRequestPublication`'s, verbatim: consent that floats to whatever the current wording happens to
be consents to nothing in particular.

---

## 12. Routes

### `Sati.Api` — staff-facing, inside the existing `/api/v1` group

| Route | Access rule |
|---|---|
| `POST /people/{personId:int}/documents/{artifactId:int}/freeze` | Own caseload. Stores bytes, sets `IsFrozen`, refuses if `BlankFieldsJson` is non-empty without a supervisor override. |
| `POST /people/{personId:int}/signature-consent` | Own caseload. Records acceptance or withdrawal against a disclosure version. |
| `POST /people/{personId:int}/signing-pin` | Own caseload. Establishes or resets a PIN. Never returns it. A reset requires a recorded reason. |
| `POST /signature-requests` | Own caseload. Refuses a non-`Cleared` kind, a non-frozen artifact, a missing consent, or a superseded artifact. |
| `GET /people/{personId:int}/signature-requests` | Own caseload. State and evidence timeline, never the token or PIN. |
| `POST /signature-requests/{id:int}/resend` | Own caseload. New token, old one revoked. |
| `POST /signature-requests/{id:int}/revoke` | Own caseload. Requires a reason. |
| `POST /signature-requests/{id:int}/unlock` | Own caseload. Clears PIN lockout with a recorded identity-confirmation reason. |
| `GET /signature-requests/{id:int}/signed.pdf` | Own caseload. The produced signed artifact. |

Nine routes into `ApiSurface.Routes` and `API_AUTHORIZATION.md` in the same change.

### `Sati.Portal` — public, token-addressed

| Route | Notes |
|---|---|
| `GET /s/{token}` | Welcome. Neutral response for every unusable token. |
| `POST /s/{token}/pin` | Rate-limited, lockout at five, every attempt an evidence row. |
| `GET /s/{token}/consent`, `POST /s/{token}/consent` | Section 9. |
| `GET /s/{token}/document` | Streams the frozen PDF. `no-store`. Records `DocumentDisplayed`. |
| `POST /s/{token}/sign` | Typed name, intent checkbox, frozen consent wording. |
| `POST /s/{token}/decline`, `POST /s/{token}/changes` | The other two outcomes. |
| `GET /s/{token}/signed.pdf` | The produced signed artifact, after completion only. |

No route in the portal accepts a person id, an artifact id, or an agency id. Everything is
resolved from the token server-side. A parameter the caller can influence is a parameter that has
to be authorized, and the portal's answer is not to have any.

---

## 13. Audit

Two records with different jobs, and conflating them is a mistake.

`SignatureEvent` is the **evidence record**: complete, append-only, per-request, and the thing that
would be produced if a signature were ever challenged. It is granular by design.

`AuditEvent` gets the **staff actions**, at the existing granularity:

| Action | Metadata |
|---|---|
| `signature.consent-recorded` | Capacity, disclosure version. |
| `signature.consent-withdrawn` | Capacity. |
| `signature.pin-established` | Capacity. Whether it was a reset. |
| `signature.request-issued` | Kind, artifact id, capacity. Never the email address. |
| `signature.request-revoked` | Kind, artifact id. |
| `signature.request-unlocked` | Failed-attempt count at unlock. |
| `signature.completed` | Kind, artifact id, outcome, signed artifact id. |
| `document.frozen` | Kind, cycle start, hash. |

No PIN, no token, no email body, no document content in any metadata, log line, or incident record.
The delivery email address is on the request row where it is evidence; it is not copied into
general audit metadata.

Consumer-side actions do not create `AuditEvent` rows. A consumer is not an agency actor, and
`AuditEvent.ActorUserId` has no honest value for them. That is precisely why `SignatureEvent`
exists.

---

## 14. Tests

Per `CLAUDE.md`, every security test must be **confirmed failing against the unfixed code** before
it is kept.

### Portal isolation

1. The portal's SQL user is refused `SELECT` on `People`, `Notes`, `Users`, and `AuditEvents`.
2. No portal route accepts a person, artifact, or agency identifier in any position.
3. A token for agency A cannot reach a document belonging to agency B.

### Token

4. An expired, revoked, completed, or never-issued token all return the identical neutral page.
5. Tokens are stored only as hashes; the plaintext appears in no table.
6. Reissue revokes the prior token, and the prior token then fails.
7. A signing session cookie from one request cannot act on another.

### PIN

8. Five failures lock the request and the sixth correct PIN is still refused.
9. Every attempt writes an evidence row, success and failure alike.
10. The PIN never appears in any response, log, audit metadata, or desktop payload.
11. Unlock requires staff on the caseload and a recorded reason.
12. A stored PIN hash cannot be verified without the Key Vault pepper.

### Document integrity

13. A request cannot be created against a non-frozen artifact.
14. Superseding the artifact revokes the open request automatically.
15. Signing is refused when the blob's hash no longer matches `FrozenSha256`.
16. A request is refused when `BlankFieldsJson` is non-empty without a supervisor override.
17. The signed artifact contains the frozen bytes unmodified, plus exactly one appended page.
18. The appended page's printed hash equals the frozen artifact's recorded hash.

### Policy gate

19. `SafetyPlan` and `ReleaseDhhs` are refused while `GatedPendingConfirmation`.
20. `MedicalRecordsRequest` is refused as not signable, in every code path.
21. `PrivacyPractices` produces an acknowledgment record and prints acknowledgment wording, not
    authorization wording.

### Consent

22. A request is refused with no consent, and with withdrawn consent.
23. Withdrawal revokes open requests and leaves completed ones untouched.
24. The disclosure text is frozen on the row and a later revision does not alter existing rows.

### Evidence

25. `SignatureEvent` rejects updates and deletes at the `SaveChanges` boundary.
26. A completed request's timeline contains issued, delivered, viewed, displayed, and signed.
27. No evidence `DetailJson` contains a PIN, a token, or document content.

### Signing session

28. Signing is refused before the document has been displayed.
29. Declining ends the request and no signed artifact is produced.
30. The consent wording recorded is the wording that was displayed, not the current one.

### Tenancy and staff routes

31. Every `Sati.Api` route above refuses a consumer outside the actor's caseload, 404 not 403.
32. A PIN cannot be established for a consumer on another case manager's caseload.
33. `ApiSurfaceTests` passes with the nine new routes declared.

---

## 15. Landing order

1. Byte storage: blob container, `BlobPath`, `IsFrozen`, the freeze route, hash verification.
   Tests 13, 15, 18.
2. `SignatureMeaningCatalog` in Contracts with the policy gate, plus its tests. Tests 19–21.
3. `SignatureConsent`, the disclosure, the staff route. Tests 22–24.
4. PIN establishment with the Key Vault pepper, and `SigningPinRules`. Tests 8–12.
5. `SignatureRequest` and `SignatureEvent`, the staff routes, no portal yet. Tests 25–27, 31–33.
6. Email through Azure Communication Services, with delivery events. Content-free by default.
7. `Sati.Portal`: the application, the SQL user and grants, the token routes, the five screens.
   Tests 1–7, 28–30.
8. The signed artifact: append the evidence page, produce the linked artifact. Tests 14, 16, 17.
9. Desktop UI: request, track, resend, revoke, unlock, download.
10. Accessibility pass on the portal, against real assistive technology rather than a linter.

Steps 1 through 5 are useful alone: they give frozen documents, recorded consent, and an evidence
model, with signatures still captured on paper. Do not start step 7 before step 5 is tested; the
portal is a thin surface over a model that has to be right first.

---

## 16. Documents to update as each step lands

| Document | What changes |
|---|---|
| `API_AUTHORIZATION.md` | The nine staff routes. A new section for the portal, which is outside the `/api/v1` table and needs its own. |
| `AUDIT_EVENTS.md` | Eight new actions, plus why consumer actions are evidence rather than audit. |
| `ARCHITECTURE.md` | `Sati.Portal` as a new trust boundary; `SignatureMeaningCatalog` as rule owner; blob storage ownership. |
| `DECISIONS.md` | The separate portal and its SQL grants; evidence separate from audit; the PIN pepper; the records request not being signable. |
| `REGULATORY_CONCERNS.md` | The consent disclosure, what each kind's signature means and does not mean, and the two open policy gates. |
| `OPERATIONS.md` | Blob retention, the portal's deployment and identity, bounce handling, PIN lockout runbook. |
| `AGENDA.md` | Tick the vetted-direction items this closes; leave the policy gates open. |
| `CLAUDE.md` | Add `SignatureMeaningCatalog` and `SigningPinRules` to the rule-owner list. |
| `DATABASE_ENVIRONMENTS.md` | The portal connects to Demo and Production separately, with separate identities. |

---

## 17. Open questions — do not decide these in code

**O-1. The two policy gates.** `AGENDA.md`'s vetted direction already owns these: written
OADS/OMS confirmation for state-form and plan signatures. `SafetyPlan` and `ReleaseDhhs` stay
`GatedPendingConfirmation` until an answer is recorded in `REGULATORY_CONCERNS.md`. Do not flip
them because the plumbing works.

**O-2. IP address and user agent as evidence.** DocuSign records both. They strengthen
attribution and they are also a consumer's location and device. The vetted direction requires a
privacy and security review before collecting them. This design collects **neither** by default.
If review says collect, they go in `SignatureEvent.DetailJson` with their own retention rule.

**O-3. Does a generated signed PDF plus evidence page count as the retained original?** Open in
`AGENDA.md`. It determines whether paper originals must still be kept, which changes the
operational story more than the code.

**O-4. Guardian authority proof.** This design records the case manager's assertion and the
document that establishes it. Should Sati require an uploaded guardianship order? That means
document upload, malware scanning, and its own access control.

**O-5. Consent disclosure wording.** Section 9 lists the required elements. The actual text needs
counsel review before production, exactly as the privacy notice does.

**O-6. Withdrawal of consent after signing.** Signatures already given stand. Should the consumer
be able to revoke an authorization they signed, and is that a new document or a status on the old
one? Revocation of a release is a real workflow this design does not build.

**O-7. PIN reuse across documents.** One PIN per consumer per agency, reused for every request, is
what this design assumes. Per-request PINs are more secure and considerably worse to use. Confirm.

**O-8. Email address of record.** `Person.Email` and `PersonContact.Email` exist and neither is
verified. Should sending require a verified address, and what verifies it?

---

## 18. Risks

**R-1. The portal is the first internet-facing surface in this product.** It changes Sati's threat
model permanently. It needs its own entry in risk analysis, incident response, and breach response,
and the email vendor needs a business associate agreement in place before a single real message is
sent.

**R-2. "Very simple" is true of the signer's experience and false of the system.** The five screens
are simple. Byte storage, an email vendor, a second deployed application, database-level least
privilege, immutable evidence, ESIGN consent, and per-kind signature meaning are not. The vetted
direction in `AGENDA.md` says the same thing in its closing paragraph. Plan against that sentence.

**R-3. A six-digit PIN is weak on its own.** It is defensible only in combination with the token,
the pepper, and the lockout. If any of the three is dropped for convenience, the identity claim
fails and the evidence record becomes an assertion rather than proof.

**R-4. Signing a superseded document.** Handled by automatic revocation on supersession, and it is
worth restating because it is the failure that would most damage trust in the feature.

**R-5. Overclaiming.** `AtRequestPublication` is careful to say what it is not. Every surface here
must be equally careful. Sati captures a signature and its evidence; it does not thereby make a
document legally sufficient, and nothing in the UI, PDF, or release notes may suggest otherwise.
