# Electronic signatures: implementation and legal review guide

Source review: September 2026. This guide accompanies the implemented synthetic-testing feature.
The completed checks and their limits are recorded in
[SIGNATURE_PORTAL_VALIDATION.md](SIGNATURE_PORTAL_VALIDATION.md).

Sati now has a feature that lets a person review one fixed document, enter their signing code, and make an
explicit signing decision. The initial feature is disabled by default and restricted to made-up
people and information. Every document remains blocked from real use until the applicable legal,
agency, security, accessibility, and operating requirements have been reviewed.

This guide explains the implemented behavior and the decisions Josh and an agency need to make. It
does not establish that the software or any document is legally sufficient. The original proposal
is retained in [SIGNATURE_PORTAL_DESIGN.md](SIGNATURE_PORTAL_DESIGN.md); the changes to that proposal
are explained in [SIGNATURE_PORTAL_REVIEW.md](SIGNATURE_PORTAL_REVIEW.md).

## 1. What a person should experience

For the initial test, an authorized staff member uses **Annual Documents** for a consumer that was
explicitly marked as fictional test data when created. Generate and save the complete PDF first,
then select that exact file when retaining it for signing. If the saved original is missing,
generate and review a new version. Sati will not reconstruct an older original from today's profile.

Select the consumer, guardian, or authorized representative from the current record. Confirm the
displayed name and email, record the representative's authority where needed, and establish a
new signing code of 8 to 12 digits. Re-enter the code to confirm it. Keep it separate from the
email invitation. The link lasts 72 hours by default; permitted test settings range from one to
seven days. Five incorrect codes lock the request. For an unfinished request, recovery means
verifying identity and issuing a new link with a different code. A completed signature is not
reopened or replaced to recover copy access; staff verify identity and arrange an approved copy.
The old code is never revealed or unlocked.

The feature, automatic copy preparation, and email delivery are separate settings and begin
disabled. Enabling the test screen alone does not configure hosting or send invitations. The
person performing setup should follow [the setup instructions](Sati.Portal/README.md). Email tests
are limited to exact approved test addresses. No emails were sent while building this feature.

An authorized staff member first prepares a complete document and checks who may sign it. Staff
confirm the intended person's contact details and agree on a safe way to communicate. An emailed
invitation contains a private link, without the person's name, diagnosis, document contents, or
other unnecessary information. A new PIN belongs to this request alone and is established through
a separate, verified conversation or meeting. It is never included in the invitation email.

After entering the PIN, the signer sees the electronic-signing explanation for this particular
request and opens the exact document that staff prepared. Before accepting electronic records,
they confirm that they can read and keep the file. They must also be able to print it and can ask
for help or choose paper instead. Agreeing to electronic records does not authorize a medical
disclosure or mean that they agree with the document. A new sign-in requires opening the file and
making this choice again for that visit.

The signer can decline or request changes without agreeing to electronic records. These choices
should be easy to find. Signing requires the person's own name and an explicit statement of
intent; nothing is preselected. A name that does not match the expected
signer stops the process for staff review and a corrected invitation when needed. The signer
should never be encouraged to type somebody else's name to get past a check.

After completion, the signer can obtain the signed copy. The agency must also offer a dependable
way to get another copy later. A link expiring after a few days is not the entire copy-delivery
process. Closing a signing request must prevent further signing while preserving a controlled way
to retrieve the completed record.

The signed copy may briefly show as being prepared. Its certificate includes the exact accepted
explanation and signing statement, the original file's identifying fingerprint, and the relevant
signing-session evidence. A separate protected copy link works only while the original invitation
is unexpired and access has not been stopped. Staff can obtain retained copies afterward. Staff
see separate invitation and signed-copy notification statuses; “sent” means the sending service
processed the message, not that it arrived in the person's inbox.

Each open browser page is tied to the document/session it actually displayed. Opening a different
invitation in another tab cannot silently change what the first tab signs or downloads. A mismatch
stops the action and asks the person to reopen the intended invitation.

The agency must make paper and in-person signing equally available. Choosing them should not
make services harder to receive. The agency must have a practical way to help people who lack a
device, cannot operate the portal independently, use supported decision making, or need an interpreter.

## 2. What each document's signature means

The six document types have different purposes. Supporting a document in the software does not
approve its wording or establish that a particular agency will accept it.

| Document | Meaning and intended handling | Real-use status |
|---|---|---|
| Agency release | Permission to disclose the information described in that release. | Blocked pending review of wording, scope, authority, and operation. |
| Medical release | Permission to disclose the medical information described in that release. | Blocked pending the same review and any special-record requirements. |
| Privacy practices notice | Acknowledgment that the notice was received. It is not agreement with the notice or permission to share information. | Blocked pending review of the actual agency notice and receipt process. |
| Safety plan | Any requested agreement must have a clearly defined clinical and agency purpose. | Blocked pending agency review and the required program confirmation. |
| DHHS release | Permission expressed on a state-owned form. | Blocked pending confirmation of the accepted form and signing method. |
| Medical-records request letter | An agency request relying on a separate, valid release when required. | Not offered for a consumer signature in this feature. |

A consumer's signature does not replace a case manager's signature, supervisor approval, a
provider's agreement, OADS approval, or a decision about payment. Each has its own purpose and
must stay separate.

For a covered provider with a direct treatment relationship, HIPAA generally calls for a
good-faith effort to obtain written receipt of the privacy notice. If receipt is not acknowledged,
the provider documents the effort and why it failed. Refusal must not be changed into agreement.
The notice also has its own delivery and paper-copy rules, including a paper response when the
provider knows emailed notice failed.
[HIPAA notice and acknowledgment requirements](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-E/section-164.520).

## 3. Keep the document and the signing history together

The person must sign the same document that staff prepared. The implemented process takes the
previously generated PDF and checks that the uploaded file exactly matches it. A changed, unknown,
incomplete, or superseded file is refused. Staff cannot override the completeness requirement.
An intentionally unused ink-signature line is different from missing information; document
review must distinguish these cases before the invitation is created.

Sati retains that original file without changing it. It also records a mathematical fingerprint
that detects whether any part of the file changed. Producing a signed package creates an
additional copy with signature evidence. The original remains separately available, and the
signed package has its own fingerprint. Creating the package must not falsely mark its own source
as replaced or erase the link to the annual document.

The agency must keep its identity, authority, and copy-handling records alongside Sati's retained
evidence. The software does not independently verify the enrollment conversation or prove inbox
arrival. Together, these records should explain:

- Which document and version were presented, and which original file was retained.
- The expected name, the name entered for signing, and the recorded capacity.
- How staff established identity and, where needed, authority to act for someone else.
- What electronic-record explanation and signing statement the person received.
- When the invitation, identity check, document supply, decision, and known copy-delivery actions occurred.
- Whether the person signed, declined, requested changes, or had an invitation revoked.
- Any failed identity checks, replacement invitations, or administrative corrections.

The certificate contains selected evidence for the completed signing session. The full event
history remains retained separately; repeated sign-ins do not create an endlessly expanding PDF.
The PDF is an evidence copy, not a certificate-based digital seal or an independent identity check.

The record should identify what the system actually observed. Supplying a file does not prove
that someone read it, understood it, or made an informed decision. An email provider accepting a
message does not prove a particular person received it. Those distinctions should remain visible
in both staff screens and evidence supplied during a review.

Maine law recognizes retained electronic records when they accurately preserve the information
and remain accessible. It also allows state agencies to impose additional requirements. The
agency must confirm what it will accept as the retained original before retiring an existing
paper-original process.
[Maine electronic-record retention](https://legislature.maine.gov/statutes/10/title10sec9412.html).

## 4. Establish who may sign before sending anything

Staff need to verify the actual person's identity, their intended confidential contact method,
and whether the request is going to a shared address. A shared household mailbox does not identify
which household member acted. Each request, PIN, and electronic-record consent belongs to one
specific signer.

When the relevant name, email, representative category or active status changes, unfinished
invitations are cancelled as part of the same record change. For an already signed document,
Sati preserves the signature and staff copies but stops the old online copy access and pending
copy notifications. An email already submitted cannot be recalled by this change; assess any
misdirected disclosure using the agency's incident procedure. Updating a contact is not a legal
determination about who is entitled to information, so staff must still follow the approved process.

A guardian or representative signs in their own name and explains their capacity. Staff must
review the document establishing authority, its scope, any limitations, and whether it is still
effective. Being listed as a contact, relative, emergency contact, or representative in Sati does
not by itself establish legal authority. A helper who assists the consumer does not automatically
become the signer.

Under HIPAA, a representative's rights follow the authority granted by applicable law and can be
limited to particular health decisions. There are also exceptions involving minors and possible
abuse or endangerment. The agency's procedure must address those situations rather than assuming
that every parent or guardian can receive every document.
[HHS explanation of personal representatives](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/personal-representatives/index.html).

Maine minors who can consent to particular health services generally receive adult confidentiality
protections for those services. Special substance-use rules can require the minor alone to
authorize disclosure or require both the minor and a parent, depending on the governing treatment
law. A one-signer feature cannot silently satisfy a two-signature requirement. Keep these cases
out of real use until a reviewed process supports them.
[Maine minor confidentiality](https://legislature.maine.gov/statutes/22/title22sec1505.html),
[Part 2 rules for minors](https://www.ecfr.gov/current/title-42/chapter-I/subchapter-A/part-2/subpart-B/section-2.14).

The email link and short PIN are safeguards, not a certification of identity or a guarantee of
strong authentication. The agency must approve the enrollment and recovery process. Current NIST
guidance does not accept email as an independent out-of-band authentication method, so this design
must not be advertised as meeting a NIST assurance level merely because it uses two steps.
[NIST authentication guidance](https://pages.nist.gov/800-63-4/sp800-63b.html).

## 5. Obtain separate agreement to electronic records

The safer product default is a fresh electronic-record choice for each request, after the PIN
check. The explanation is fixed when the invitation is created so later wording changes cannot
change what that person accepted. Sati records acceptance for the current signed-in visit; a
later sign-in requires the person to open the file and make the choice again before signing.

Before agreement, explain which record the choice covers, how to obtain paper, any copy costs,
how to withdraw, how to correct contact details, and what is needed to open and retain the file.
Require the signer to demonstrate that the file format is usable and to confirm that they can
keep a copy. A staff assertion alone should not stand in for the person's electronic acceptance.

Federal ESIGN consumer-disclosure rules apply in specified situations where law requires
transaction information to be provided in writing. They are not a universal rule that every
imperfect electronic signature has no legal effect. Maine's law also requires agreement to use
electronic means and protects the recipient's ability to retain the record. Per-request consent
is Sati's safer default, rather than a claim that the law always requires a new checkbox for every
document.
[Federal electronic-record consent](https://uscode.house.gov/view.xhtml?req=%28title%3A15+section%3A7001%28c%29+edition%3Aprelim%29),
[Maine agreement to electronic transactions](https://legislature.maine.gov/statutes/10/title10sec9405.html),
[Maine right to retain the electronic record](https://legislature.maine.gov/statutes/10/title10sec9408.html).

## 6. Review release wording and revocation before real use

HIPAA permits electronic authorization when the signature is valid under applicable law. It does
not approve Sati's release language.
[HHS electronic-authorization explanation](https://www.hhs.gov/hipaa/for-professionals/faq/554/how-do-hipaa-authorizations-apply-to-electronic-health-information/index.html).

Counsel and agency privacy leadership should review the information covered, who may disclose it,
who may receive it, why, when permission ends, who signs, and the representative's authority. They
must also review required statements about revocation, refusal, and further disclosure, and the
process for giving the signer a copy. Ordinary release permission generally cannot be required
as a condition of treatment or benefits, subject to the rule's specific exceptions.
[HIPAA authorization requirements](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-E/section-164.508).

For applicable Maine health-information authorizations, the law specifically addresses an
individual's unique identifier and electronic-authentication date. It generally limits an
authorization to 30 months, with specified insurance exceptions. It also addresses retained
revocations, including recorded oral revocation. Use a nonsecret signer reference in the evidence;
never treat the PIN or invitation link as a printable identifier. Review state and federal wording
together rather than copying one law's refusal language into every form.
[Maine health-information authorization law](https://legislature.maine.gov/statutes/22/title22sec1711-C.html).

Three different actions need different records:

| Action | Practical effect |
|---|---|
| Withdraw the choice to use electronic records | Stop the open electronic process and arrange another method. Preserve earlier completed evidence. |
| Cancel or replace an invitation | Stop that invitation and its open signing access. A replacement gets a new private link and PIN. |
| Revoke an authorization already signed | Preserve the original signature, record the revocation, and stop relying on the permission as required. Notify the appropriate staff and recipients through the agency's process. |

The first two actions do not implement the third. Before a real release is used, staff must have
a working way to receive, record, review, and act on revocation. The agency must also handle
actions already taken in reliance on an authorization under the applicable rules.

## 7. Resolve MaineCare and OADS acceptance separately

MaineCare's September 16, 2024 notice permits member electronic signatures through enforcement
discretion when the stated safeguards are met. It requires a complete document, authentication,
privacy and security protections, and retention of the signed document and evidence. It does not
certify this product or automatically answer every provider or team-signature question.
[MaineCare electronic-signature notice](https://www1.maine.gov/dhhs/oms/providers/provider-bulletins/notice-regarding-electronic-signatures-2024-09-16).

The published OADS PCP manual still describes physical signatures, implementing team signatures,
and keeping originals. Obtain written OADS/OMS direction on the relevant forms, signers, accepted
electronic method, and evidence to supply to Resource Coordinators. Keep the safety-plan and DHHS
release gates closed until their specific questions have been answered. Do not treat a consumer
signature in this portal as completion of a multi-person plan.
[OADS PCP manual, including Appendix D](https://www.maine.gov/dafs/bablo/sites/maine.gov.dhhs/files/documents/PCPManualpdf.pdf),
[Maine agency authority over electronic acceptance](https://legislature.maine.gov/statutes/10/title10sec9418.html).

Use the current Section 13 rule, effective April 28, 2026, when reviewing targeted case management.
Its documentation requirements include signed releases when needed, staff signatures and
credentials, and applicable plan and meeting signatures. It also requires supervisor signatures
on individual service plans. These obligations are not replaced by the six annual-document
choices described above.
[Current MaineCare Section 13](https://www.maine.gov/dhhs/sites/maine.gov.dhhs/files/rule-2026-04/MaineCare%20Benefits%20Manual,%20Chapter%20II,%20Section%2013.pdf).

## 8. Review specially protected information

Some substance-use records are subject to 42 CFR Part 2. The revised general compliance deadline
was February 16, 2026. An ordinary medical-release choice does not establish that a release meets
Part 2. The current rule addresses required consent language, withdrawal, special counseling
notes, and separate consent for proceedings against the patient. Keep these uses blocked until
the relevant forms and handling rules have been approved.
[HHS Part 2 overview](https://www.hhs.gov/hipaa/part-2/index.html),
[Current Part 2 consent requirements](https://www.ecfr.gov/current/title-42/chapter-I/subchapter-A/part-2/subpart-C/section-2.31).

The agency should also identify Maine requirements for HIV-test information. For agencies or
records covered by Maine's behavioral and developmental-services law, separately review its
consent and disclosure rules and confirm which programs and records fall within them. Do not
assume that one broad release overrides every confidentiality restriction.
[Maine HIV-test confidentiality law](https://legislature.maine.gov/statutes/5/title5sec19203.html),
[Maine behavioral and developmental-services confidentiality](https://legislature.maine.gov/statutes/34-B/title34-Bsec1207.html).

## 9. Make the actual experience accessible

An accessible web page is only part of the requirement. The actual document, consent explanation,
identity step, error recovery, and copy-download process must also be usable.

The generated evidence PDF currently has no accessibility tags. Successful page rendering does
not establish screen-reader usability. A hands-on browser review was not completed because the
browser screenshot launch was blocked and the available browser-control fallback was unavailable.
The automated page-behavior and website-request checks are useful evidence, but they do not
replace testing with people and assistive technology.

Test with keyboard-only navigation, screen readers, enlarged text, high contrast, small phones,
and people who need extra time. Allow people to paste their PIN or use an appropriate password
manager. Explain mistakes in text and move attention to an understandable error summary. Do not
require dragging, drawing a signature, remembering an unexplained code, or scrolling to the bottom
as supposed proof of reading.

Warn before signing access times out and give a usable way to continue or sign in again. Losing a
connection or needing extra reading time must not cause an accidental signature or leave the
person guessing whether it happened. An accessible assisted or paper process remains necessary
when the electronic document itself cannot be used effectively.

For recipients of HHS financial assistance covered by the rule, the current web/mobile standard
is WCAG 2.1 AA. In May 2026, HHS extended the specific compliance dates to May 11, 2027 for
recipients with at least 15 employees and May 10, 2028 for smaller recipients. Existing duties to
communicate effectively and avoid discrimination remain. Determine the actual agency's duties
and any other applicable requirements. WCAG 2.2 AA is a prudent design target, not a claim that
every current regulation specifies that newer version.
[Current HHS web/mobile rule](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-A/part-84/subpart-I/section-84.84),
[HHS deadline extension](https://www.hhs.gov/press-room/hhs-extends-mobile-and-web-accessibility-deadline.html),
[W3C accessible authentication](https://www.w3.org/WAI/WCAG22/Understanding/accessible-authentication-minimum.html),
[W3C adjustable timing](https://www.w3.org/WAI/WCAG22/Understanding/timing-adjustable.html).

## 10. Prepare privacy, security, and records operations

Determine whether each participating organization is a HIPAA covered entity or business associate
and document the responsibilities. Put the necessary agreements in place between the agency,
Sati, and relevant service providers before real protected information is handled. A vendor's
eligibility for healthcare use does not establish that the right agreement and operating controls
are in place.
[HHS business-associate guidance](https://www.hhs.gov/hipaa/for-professionals/privacy/guidance/business-associates/index.html).

The public signing service needs its own security review. Assess access to documents, staff
mistakes, shared devices, stolen invitations, PIN guessing, recovery, outages, backups, and
incident reporting. Prove that its access cannot be used to browse unrelated client records.
Include hosting, email, file storage, and their diagnostic records in the review.
[HIPAA security risk-management requirements](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-C/section-164.308).

The intended default does not retain raw internet addresses or browser/device descriptions as
signature evidence. Temporary abuse controls may use the connection source without storing those
identifiers permanently. Verify that the hosting and email services do not quietly defeat that
policy. Evidence must never contain PINs, working private links, or unnecessary medical text.

Approve a retention schedule covering the original document, signed package, signing evidence,
consent, revocation, delivery history, and backups. Keep related records together and preserve
them when a complaint, investigation, audit, or legal hold requires it. A rule against automatic
deletion is a temporary safeguard, not a complete retention program.

HIPAA's six-year documentation rule covers required privacy documentation, including signed
authorizations, and runs from creation or the last effective date, whichever is later. It is not
a universal instruction to delete all medical records after six years. Other program, state,
contract, and preservation requirements must be considered.
[HIPAA documentation retention](https://www.ecfr.gov/current/title-45/subtitle-A/subchapter-C/part-164/subpart-E/section-164.530),
[Current MaineCare rules index](https://www.maine.gov/sos/rulemaking/agency-rules/mainecare-benefits-manual).

## 11. Decide who handles problems

Assign a responsible person and an escalation path for each of these situations before real use:

| Situation | Required practical response |
|---|---|
| Wrong email or wrong signer | Stop the old invitation, assess any disclosure, verify the correct person, and issue a replacement when appropriate. |
| Forgotten or locked PIN | Verify identity by the approved separate process. For an unfinished request, record the reason and issue a replacement with a different code. For a completed signature, preserve it and arrange an approved staff-provided copy. Never reveal or unlock the old PIN. |
| Document needs correction | Preserve the earlier record, prepare a new complete version, and begin a new signing request. |
| Signer declines | Preserve the decision, contact the person appropriately, and follow the document-specific refusal procedure. |
| Signer asks for changes | Give staff the request without recording a signature. |
| Email bounces or is suppressed | Tell staff what failed and arrange an approved alternative. Do not label a queued message as delivered. |
| Connection fails during signing | Check the existing request's outcome before trying again; never create a second signature simply because the first reply was lost. |
| Person cannot open or keep the file | Offer accessible assistance or paper and document how the copy was provided. |
| Authorization is revoked | Record and act on the revocation while retaining the original signing history. |

Email setup needs an approved sending address, authenticated sending arrangements, a monitored
failure process, and testing of actual delivery results. Do not assume that creating an email
account or seeing one message arrive proves readiness.

If a send has an uncertain outcome, Sati checks that same send rather than automatically sending
another message. After at most five processing or status-check attempts, unresolved mail stops
for review. “Needs review” does not prove either delivery or failure. Staff must check the sending
service and arrange an approved alternative. Turning email off cannot recall a message already
submitted; its last known status remains part of the record. Actual inbox delivery and bounce
monitoring still require the separately verified operating process.

## 12. Release decisions and remaining confirmation

The document, signer, request, consent and evidence foundations were checked before the public
signing experience was added. The completed local feature and validation record remain separate
from deployment or real-use approval. Continue using only synthetic information during acceptance.

| Decision or evidence | Status in this guide |
|---|---|
| Local software and automated checks | Implemented; see the validation record for results and limits. |
| Actual browser, keyboard, screen-reader, and document acceptance | Still requires hands-on user acceptance; automated checks and visual PDF review do not establish it. |
| Hosted setup, actual permissions, authenticated email and delivery/failure monitoring | Requires separately reviewed setup and external-service verification. No deployment or real email occurred. |
| Legally sufficient agency and medical release wording | Requires qualified agency and legal review. |
| State-form, safety-plan, PCP/team, and other program acceptance | Requires the relevant written program decisions. |
| Identity, authority, shared-address, and recovery procedures | Requires agency approval and staff training. |
| Consent explanation, copy delivery, withdrawal, and authorization revocation | Requires reviewed wording and an operating procedure. |
| Special records, minors, and multiple required signers | Requires separately supported and reviewed handling. |
| Privacy contracts, security review, restoration, incidents, and retention | Requires completed evidence and assigned owners. |
| Real-use activation | Blocked; synthetic testing alone does not clear it. |

Josh should request a written sign-off identifying the approved document versions, permitted
signers, applicable programs, evidence to retain, accepted delivery method, and the person
responsible for each continuing obligation. Any unresolved item needs an explicit restriction in
the rollout plan. A successful demonstration is evidence that the feature works under those test
conditions; it is not approval to use every document with every person.
