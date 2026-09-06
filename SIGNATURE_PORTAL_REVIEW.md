# Electronic signature handoff: findings and safer defaults

Source review: September 2026. Implemented safeguards and test limits are recorded in
[SIGNATURE_PORTAL_VALIDATION.md](SIGNATURE_PORTAL_VALIDATION.md).

This review preserves the original [design](SIGNATURE_PORTAL_DESIGN.md) and
[handoff](HANDOFF_SIGNATURE_PORTAL.md). The electronic-signature section of [AGENDA.md](AGENDA.md)
remains the governing prior direction where the original design conflicts with it. The choices
below are the revised implementation direction. None establishes legal or production approval.
The practical guide is [SIGNATURE_PORTAL_GUIDE.md](SIGNATURE_PORTAL_GUIDE.md).

## 1. Completeness cannot have an administrative escape hatch

**Original flaw:** The freeze and request rules permit a supervisor to override missing fields.
That directly weakens the condition used to justify electronic member signatures.

**Safer default:** Refuse incomplete documents without an override. Treat intentional unused ink
signature lines separately from missing substantive information. A list showing no blank fields
does not establish that the wording or clinical content is legally sufficient.

**Basis:** MaineCare's September 2024 notice requires completed, legally sufficient documents in
addition to authentication, privacy/security safeguards, and retained proof.
[MaineCare notice](https://www1.maine.gov/dhhs/oms/providers/provider-bulletins/notice-regarding-electronic-signatures-2024-09-16).

## 2. Software availability was confused with legal clearance

**Original flaw:** Agency and medical releases are labeled cleared because the wording belongs to
Sati, although the repository explicitly says those templates have not received the needed review.

**Safer default:** Disable the feature by default, allow only isolated synthetic testing, and keep
every live document type blocked. Treat the medical-records request as not signable. Preserve
separate program gates for the safety plan and DHHS release. Record the actual approval and
reviewed wording before enabling any real use.

**Basis:** HIPAA permits an electronic authorization only when it is otherwise valid. Maine
agencies can specify what electronic records and methods they accept.
[HHS electronic authorizations](https://www.hhs.gov/hipaa/for-professionals/faq/554/how-do-hipaa-authorizations-apply-to-electronic-health-information/index.html),
[Maine agency acceptance](https://legislature.maine.gov/statutes/10/title10sec9418.html).

## 3. One consumer record is not one signer

**Original flaw:** Reused consumer PINs and broadly indexed consent can blur consumer, guardian,
and different representative identities. A contact label can be mistaken for authority.

**Safer default:** One expected signer and one PIN per request. Record electronic-record consent
for that request and the current signed-in visit; do not carry it into a new request or sign-in.
Preserve the signer's identity and capacity, contact reference where applicable, and the reviewed
basis of authority. A guardian signs their own name. A typed-name mismatch stops for staff correction or
replacement; it is not merely a warning that allows a different person to proceed.

**Basis:** Attribution depends on the actual person's act. Representative authority depends on
applicable law and the relevant health decision.
[Maine attribution](https://legislature.maine.gov/statutes/10/title10sec9409.html),
[HHS representatives](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/personal-representatives/index.html).

## 4. Consent requirements contradicted the proposed screens

**Original flaw:** Requests require existing consent, but a later portal screen offers to capture
missing consent. The claim that every signature without this record is unusable evidence also
overstates ESIGN.

**Safer default:** Freeze the disclosure when the request is created. After the PIN succeeds,
capture the signer's agreement for this request before signing. Explain retention, paper,
withdrawal, contact correction, requirements, and scope. Confirm access to the actual file format.
Keep this agreement separate from authorization to disclose information.

**Basis:** ESIGN's consumer-disclosure provisions have a defined scope and do not universally
invalidate a signature for every consent defect. Maine requires agreement to electronic
transactions and protects the recipient's ability to retain records. Per-request acceptance is
the selected product safeguard.
[ESIGN](https://uscode.house.gov/view.xhtml?req=%28title%3A15+section%3A7001%28c%29+edition%3Aprelim%29),
[Maine consent](https://legislature.maine.gov/statutes/10/title10sec9405.html),
[Maine retainable records](https://legislature.maine.gov/statutes/10/title10sec9408.html).

## 5. Scrolling and downloading do not prove understanding

**Original flaw:** Reaching the bottom or downloading a file is treated as evidence that it has
been reviewed. These conditions can also exclude people using assistive technology.

**Safer default:** Record that the exact file was supplied, then obtain an explicit review and
intent acknowledgment. Do not describe a network response as proof of reading. Keep the file
available for review and download without forced scrolling. Provide effective assistance and
an equal paper path.

**Basis:** This is an evidence-accuracy and accessibility safeguard. The relevant standards
address understandable, operable access rather than a scroll-distance test.
[W3C accessibility standard](https://www.w3.org/TR/WCAG22/).

## 6. Appending pages does not preserve a PDF file's original bytes

**Original flaw:** The proposal promises an unchanged byte-identical original inside a newly
written PDF, with exactly one evidence page. Ordinary PDF rewriting does not justify that promise,
and a full timeline can be longer than one page.

**Safer default:** Retain the exact uploaded, previously generated source PDF separately. Check
the actual file's fingerprint when retaining it, supplying it for review, and preparing the
signed package. Before signing, confirm that the retained document still matches the current,
unreplaced source record. Create a separately identified signed package and certificate linked
to that source. Do not use the package to supersede the active
source document. Store the full evidence record separately from the readable certificate summary.
Report the original and package fingerprints honestly.

**Basis:** The legal requirement is accurate, accessible retention, not a particular PDF-writing
technique. Implementation tests must verify the original bytes directly.
[Maine electronic originals and retention](https://legislature.maine.gov/statutes/10/title10sec9412.html).

## 7. Completion must not destroy the person's access to a copy

**Original flaw:** Completed links must become indistinguishable from invalid ones, yet the design
uses those links to download the completed file and deliver later copies.

**Safer default:** Close the authority to sign when a terminal decision occurs. Provide separately
controlled, authenticated access to the completed package and a dependable agency copy process.
Do not restore signing authority merely to make a download work.

**Basis:** HIPAA requires a signed authorization copy for the individual, and Maine protects the
ability to retain an electronic record. A short-lived invitation alone does not settle this duty.
[HIPAA authorization copies](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-E/section-164.508).

## 8. Revoking permission is different from canceling electronic signing

**Original flaw:** Withdrawal of electronic-record consent is modeled, but revocation of an
executed release is deferred while releases are described as cleared.

**Safer default:** Preserve the signed record and separately record whether the authorization may
still be relied upon. Keep real use blocked until the agency can receive and act on revocation,
including communicating it to the right people. Canceling an invitation does not revoke an
already executed release, and withdrawing electronic delivery does not erase its history.

**Basis:** HIPAA and Maine have revocation rules and reliance qualifications. Maine also addresses
recorded oral revocation and electronic identification.
[Maine authorization and revocation](https://legislature.maine.gov/statutes/22/title22sec1711-C.html).

## 9. A privacy acknowledgment needs honest signer provenance

**Original flaw:** Projecting a consumer's portal action into an existing staff-recorded receipt
can imply that a staff member performed an action they never took.

**Safer default:** Retain the signer-origin evidence separately and let the receipt workflow refer
to it through an explicit relationship. Do not invent a staff recorder. The statement and
certificate describe receipt only; refusal and good-faith efforts remain distinct outcomes.

**Basis:** HIPAA distinguishes acknowledgment from the underlying notice and requires records
of the relevant receipt or unsuccessful effort.
[HIPAA notice documentation](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-E/section-164.520).

## 10. A short PIN is not a recognized assurance claim

**Original flaw:** The proposed combination of link, peppered PIN, and lockout is described too
close to proof of identity. Reuse across requests also makes recovery and guessing controls harder
to reason about.

**Safer default:** Use a request-specific PIN, established through a verified separate process.
Keep PIN protection separate from the main stored records, refuse access if that protection is
unavailable, limit attempts, and revoke earlier access on replacement. Do not send PINs by email
or reveal them after establishment. Do not advertise NIST compliance or strong multi-factor
authentication. The risk assessment must approve the actual enrollment and recovery process.

**Basis:** NIST's current rules distinguish recognized authenticators and reject email as an
out-of-band authentication method. Those standards are a benchmark here, not a declaration that
every private provider must implement a particular assurance level.
[NIST authentication guidance](https://pages.nist.gov/800-63-4/sp800-63b.html).

## 11. A session must remain bound to the current request state

**Original flaw:** The design lists expirations, lockout, replacement, supersession, and withdrawal
without fully specifying how a previously authenticated browser loses authority when they happen.

**Safer default:** Recheck current request state for every protected action. Replacement with a
different code, lockout, withdrawal, expiry, and supersession invalidate earlier sessions. There
is no PIN-reveal or unlock action. Complete the signature and its evidence as one protected
decision; retrying an uncertain response must not create a second act.
Defend against requests submitted by another website, and do not let browser-visible values decide
which person or document is authorized.

**Basis:** These are integrity, access-control, and recovery safeguards that need meaningful tests.
[HIPAA technical safeguards](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-C/section-164.312).

## 12. A narrow table list is still a sensitive-data boundary

**Original flaw:** Refusing access to the main People table is described as if it prevents all
caseload disclosure. Signature rows and the document store still contain identities and sensitive
documents across requests. A broad read-only document-store permission is not request isolation.

**Safer default:** Keep a separate public application with a separate identity and explicitly
limited access. Prove that it cannot read unrelated client tables, and also prove that each
request can reach only its own signer and document. Review the actual file-store permissions.
Do not describe an unexecuted grant test as evidence of isolation.

**Basis:** Access must be limited to authorized information, including secondary stores.
[HIPAA technical safeguards](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-C/section-164.312).

## 13. Evidence collection must match the privacy promise

**Original flaw:** The proposal omits internet-address and device evidence while its infrastructure
may still record those values and private URL paths. It also requires delivery events that may
not be available before a person signs.

**Safer default:** Do not retain raw internet addresses or browser descriptions by default.
Use temporary connection-source abuse controls without permanent retention. Review hosting,
diagnostic, proxy, and email records for unwanted collection. Preserve only delivery facts actually
reported; do not fabricate a delivered or read event to complete an expected timeline.

**Basis:** Data minimization is the selected privacy default. Hosting and delivery services remain
part of the documented security assessment.
[HIPAA administrative safeguards](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-C/section-164.308).

## 14. One signer cannot satisfy every plan or protected-record requirement

**Original flaw:** Six supported document types can suggest that all annual, clinical, or program
signature requirements are handled. Minors and some plans can require more than one signer.

**Safer default:** Preserve separate document-purpose and legal-acceptance gates. Do not use this
one-signer workflow to imply complete PCP/team execution or to authorize specially protected
records without a reviewed process. Current Section 13 replaced the older rule in April 2026;
review its current staff, release, and plan requirements.

**Basis:** OADS still publishes physical/team-signature instructions; Part 2 has specific minor
and consent requirements.
[OADS PCP manual](https://www.maine.gov/dafs/bablo/sites/maine.gov.dhhs/files/documents/PCPManualpdf.pdf),
[Current Section 13](https://www.maine.gov/dhhs/sites/maine.gov.dhhs/files/rule-2026-04/MaineCare%20Benefits%20Manual,%20Chapter%20II,%20Section%2013.pdf),
[Part 2 minor signatures](https://www.ecfr.gov/current/title-42/chapter-I/subchapter-A/part-2/subpart-B/section-2.14).

## 15. Accessibility includes the file and the recovery path

**Original flaw:** A fixed thirty-minute session conflicts with the promise of no expiry during
reading. A plain web page does not establish that its PDF is accessible.

**Safer default:** Test actual documents and decisions with assistive technology. Warn and offer
an accessible continuation or reauthentication path. Permit PIN paste and assistance without
impersonation. Keep paper equally available. Do not call an automated accessibility check a
completed acceptance review. The generated evidence PDF currently has no accessibility tags;
rendering it successfully does not establish screen-reader usability.

**Basis:** HHS's specific web/mobile compliance dates were extended to 2027/2028 in May 2026;
existing effective-communication duties continue. Design against the relevant standard and
document the actual agency's obligations.
[Current HHS accessibility rule](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-A/part-84/subpart-I/section-84.84),
[W3C timing guidance](https://www.w3.org/WAI/WCAG22/Understanding/timing-adjustable.html).

## 16. A browser tab must stay bound to its displayed document

**Implementation flaw caught in independent review:** Browser tabs share sign-in cookies. A second
invitation could replace the cookie while the first tab continued showing an earlier document.
Where the same person signs both documents, typing the correct name alone cannot detect the mix-up.

**Safer default:** Each authenticated page receives a non-secret session reference. Every decision
and document download must match that page reference to its protected sign-in cookie. A mismatch
stops with instructions to reopen the intended invitation. The reference cannot sign in on its
own, and it is never used to look up a different consumer or document.

## 17. Changing a signer record must invalidate outstanding access

**Implementation flaw caught in review:** Confirming an address at issuance does not make a later
address correction or representative removal safe. The earlier link could otherwise remain usable.

**Safer default:** Changes to the relevant name, email, representative category or active status
cancel unfinished requests in the same operation. For an already signed document, preserve its
signature and retained copies but separately stop old online receipt access and pending copy mail.
This is neither a fabricated signing refusal nor a withdrawal of medical authorization. Staff
must use the approved identity/copy process to assist the appropriate person afterward.

The affected staff and local record-editing paths also recheck the actual consumer's agency,
assigned case manager and current staff permissions. A screen selection is not proof of access.

## 18. Retry, deadline and clock details can change the evidence

**Implementation flaws caught in review:** A repeated send action could appear to accept a
different newly entered code, an expiry could be interpreted in the browser's local time, or
access could expire between the initial check and the final signing decision.

**Safer default:** Repeated submissions must match the previously established code and confirmed
identity; replacements require a different code. All retained signing times are restored and
returned as UTC. Recheck deadlines after identity checks, document loading, and consent checks,
and at the final signing decision. Extending reading time cannot revive expired access. Record
one exact time for the signing decision, request and evidence. The signed copy must be
reconstructable from those facts.

## Verification status

The validation record distinguishes automated application checks, deliberate safeguard-removal
checks, the disposable SQL role/migration rehearsal, and inspected PDF output from uncompleted
hosting and user acceptance. Automatic approval review blocked the browser screenshot launch;
the browser-control fallback was unavailable. No hands-on browser or screen-reader acceptance is
claimed. No live mail was sent, no hosted deployment or database migration occurred, and no
production activation or legal clearance occurred. Those remaining requirements are described
in the guide.
