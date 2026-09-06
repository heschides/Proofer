# Handoff — Electronic signature portal

**For:** whoever implements this next (written for a fresh agent with no prior context).
**Source of truth:** [`SIGNATURE_PORTAL_DESIGN.md`](SIGNATURE_PORTAL_DESIGN.md) on `master`. Read
it in full before writing code. Read `CLAUDE.md` at the repo root first for the project's
non-negotiable rules, and read the **"Electronic signature portal — vetted direction"** section of
`AGENDA.md`, which was reviewed 2026-08-07 with policy citations and is binding. Where that
section and the design document disagree, AGENDA wins.

**Status:** design only. Nothing implemented. No migration, no route, no project.

## What this is

A consumer or guardian gets an email, opens a link to a Sati-hosted page, enters a PIN established
in advance with their case manager, reads the exact document, and signs. Sati produces a signed
PDF and an append-only evidence record.

Scope decided with Josh on 2026-09-05: all six `AnnualDocumentKind` values, a separate
`Sati.Portal` application, one signer per request.

## Read this before estimating

The signer's experience is five screens. The system underneath is not small, and the vetted
direction in `AGENDA.md` says so in its own closing paragraph. Four things this repository does
not have today must exist first:

1. **Document bytes.** `DocumentArtifact` stores a SHA-256 and a byte count, and its own comment
   says PDF bytes are never stored. You cannot show someone a document you did not keep.
2. **Email.** There is no SMTP, SendGrid, or sender of any kind anywhere in the solution.
3. **A public surface.** Every `/api/v1` route is authenticated and re-validated per request.
   Nothing in Sati is reachable without an agency credential.
4. **A signature block.** Only the agency release generator draws signature lines. Four of the six
   kinds draw none, which is why the design appends a standard evidence page instead of editing
   five generators.

## The three things most likely to be got wrong

**1. Six document kinds do not mean six of the same thing.** A privacy notice signature is an
*acknowledgment of receipt*, not authorization. A medical records request is not signable at all —
its authority comes from the already-signed medical release, and a second weaker authorization
beside the real one invites a provider to rely on the wrong document. Two kinds are gated on
written OADS/OMS confirmation that does not exist yet. `SignatureMeaningCatalog` in
`Sati.Contracts.V1` owns all of this, and the API refuses anything not `Cleared`. A single
"signed" boolean across six kinds is the defect this design exists to avoid.

**2. The portal's real security control is a database grant, not application code.**
`Sati.Portal` connects to Azure SQL as its own SQL user with explicit table-level grants: a
handful of signature tables plus a narrow view over `DocumentArtifact`, and nothing at all on
`People`, `Notes`, `Users`, or `AuditEvents`. That means a SQL injection or a plain logic bug in
the portal cannot read a caseload. Test it by connecting as that user and asserting `SELECT TOP 1 *
FROM People` is refused. A grant list nobody tests is a comment.

**3. A six-digit PIN cannot be saved by its hash.** One million possibilities falls to an offline
attack regardless of PBKDF2 iterations. Three things carry it instead: an HMAC pepper from Key
Vault applied before hashing, so a leaked database is useless without the vault key; online
lockout at five attempts; and the fact that the attacker also needs a 256-bit single-use link they
were never sent. Drop any one of the three and the identity claim fails.

## Rule ownership — do not violate this

`SignatureMeaningCatalog` and `SigningPinRules` go in `Sati.Contracts.V1` and are referenced by
`Sati.Api` and `Sati.Portal`. Three applications agreeing on which documents are signable, and by
whom, is exactly the case `CLAUDE.md`'s single-owner rule was written for. A second copy in the
portal is a defect, not a convenience.

Follow `AtRequestPublication` as the model for frozen wording. Consent text and intent language
are stored on the row, never looked up at render. A consent that floats to whatever the current
wording happens to be consents to nothing in particular.

## Evidence is not audit

Two records with different jobs. `SignatureEvent` is the append-only evidence record, granular by
design, and the thing produced if a signature is ever challenged. `AuditEvent` gets the staff
actions at the existing granularity. Consumer-side actions write no `AuditEvent` row, because a
consumer is not an agency actor and `ActorUserId` has no honest value for them. Enforce
append-only on `SignatureEvent` at the `SaveChanges` boundary, as published document templates
already do.

## Landing order (design section 15)

Ten steps. Steps 1 through 5 are useful on their own: frozen documents, recorded consent, an
evidence model, with signatures still on paper. Do not start step 7, the portal, before step 5 is
tested. The portal is a thin surface over a model that has to be right first.

1. Byte storage and the freeze route.
2. `SignatureMeaningCatalog` and the policy gate.
3. `SignatureConsent` and the disclosure.
4. PIN establishment with the Key Vault pepper.
5. `SignatureRequest` and `SignatureEvent`, staff routes only.
6. Email through Azure Communication Services.
7. `Sati.Portal`: the application, the SQL grants, the token routes, the five screens.
8. The signed artifact and its appended evidence page.
9. Desktop UI.
10. Accessibility pass against real assistive technology, not a linter.

## Tests — read `CLAUDE.md`'s rule before writing any

Every security test must be **confirmed failing against the unfixed code** before you keep it.
Design section 14 lists 33. The ones that would be easiest to write as no-ops and hardest to
notice: test 12, that a stored PIN hash cannot be verified without the vault pepper; test 14, that
superseding an artifact revokes its open request; and test 1, the portal's refused `SELECT`.

## Things that must stay true

- **Frozen bytes are never modified.** The signed PDF is the frozen bytes plus one appended page.
  That is what makes "the signer saw exactly this" provable against the recorded hash.
- **Signing a superseded document must be structurally impossible**, not merely discouraged.
  Supersession revokes open requests automatically.
- **The PIN never travels by email**, is never shown to staff after establishment, and is never
  logged. The consumer types it themselves on the case manager's screen at a meeting.
- **A guardian signs in their own name and capacity**, never as though they were the consumer, and
  always on their own request under their own PIN.
- **Electronic signing is voluntary.** Paper and in-person paths stay, and must not be presented
  as second-class. Electronic transactions may never be a condition of receiving services.
- **Consent to transact electronically is captured before the first signature.** A signature
  without a current consent record is not usable evidence. This is the requirement home-grown
  signing systems most often miss.
- **Decline is a first-class outcome.** A signature flow with no way to refuse is not consent.

## Do not overclaim

`AtRequestPublication` in `Sati.Contracts.V1` is worth reading for its tone before you write any
user-facing wording. It states plainly that it is an attestation and not an electronic signature
under any standard. Every surface here needs the same discipline. Sati captures a signature and its
evidence; that does not make a document legally sufficient, and nothing in the UI, the PDF, or the
release notes may suggest it does.

## Explicitly unresolved — do not guess, ask or flag instead

Design section 17 lists O-1 through O-8. The ones most likely to block:

- **O-1** — the two policy gates. `SafetyPlan` and `ReleaseDhhs` stay gated until written
  OADS/OMS confirmation is recorded in `REGULATORY_CONCERNS.md`. Do not flip them because the
  plumbing works.
- **O-2** — whether IP address and user agent are collected as evidence. The vetted direction
  requires a privacy and security review first. This design collects neither.
- **O-5** — the consent disclosure wording needs counsel review before production, exactly as the
  privacy notice did.
- **O-7** — one PIN per consumer reused across documents, or one per request. The design assumes
  reuse; confirm.
- **O-8** — neither `Person.Email` nor `PersonContact.Email` is verified today. Should sending
  require a verified address, and what verifies it?

## Operational and environment notes

- **A business associate agreement must be in place with the email vendor before a single real
  message is sent.** Azure Communication Services Email is eligible under Microsoft's HIPAA BAA;
  eligibility is not the same as an executed agreement. As of 2026-09-05 none is signed and Sati
  holds only synthetic data, which blocks nothing: synthetic records are not PHI, so build and test
  the whole feature first. See design R-1 for what to get right meanwhile.
- **Email will fail closed if you skip the DNS work.** `satilogica.com` publishes `-all` with a
  DMARC policy of `quarantine`, so an Azure Communication Services sender that is not added to SPF
  produces notifications filed silently as spam while the desktop reports "sent." Microsoft 365
  DKIM is also not enabled today. Design section 7 has the measured records and what each one
  means. Treat this as part of step 6, and finish it by reading an `Authentication-Results` header
  from an external mailbox rather than by observing that a message arrived.
- **The portal is the first internet-facing surface in this product.** It needs its own entry in
  risk analysis, incident response, and breach response.
- Demo's SQL firewall is closed to workstations. Steps 1, 3, 4, and 5 all add tables or columns,
  so each needs a temporary firewall rule that only Josh can add. Flag it before applying a
  migration to Demo.
- The portal needs its own App Service, its own managed identity, and its own pipeline, in both
  Demo and Production, kept as separate as the databases already are.
