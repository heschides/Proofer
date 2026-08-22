# Regulatory and Conflict-of-Interest Considerations

Last substantive review: August 7, 2026. Engineering addenda added August 14–15, 2026 (see
"Cross-consumer isolation" below). The review date deliberately does not advance with those
addenda: it marks when the regulatory analysis itself was last worked through, and no counsel,
agency stakeholder, or Maine authority has reviewed this document at any date.

> This document is an engineering and product-design note, not legal advice. Before
> Sati is used for OADS review, waiver authorization, or production MaineCare claims,
> Maine DHHS/OADS program-integrity and privacy staff—and counsel familiar with
> MaineCare Sections 13, 21, and 29—should review the final organizational roles,
> permissions, workflows, and data-sharing arrangements.

## Executive summary

Using one platform for case management, OADS document review, waiver authorization,
and MIHMS billing is not inherently a conflict of interest. The principal regulatory
concern is not whether the functions share software. It is whether one financially
interested person or organization can exercise incompatible authority over service
planning, provider selection, authorization, documentation, approval, and payment.

A shared platform may reduce communication delays and strengthen oversight if it
creates a complete, immutable chain from assessment through payment while enforcing
real separation of duties.

## Cloud product and SaaS obligations

Moving Sati to Azure does not make it HIPAA compliant, and using HIPAA-eligible Azure services does
not transfer Sati's responsibilities to Microsoft. A production platform must combine technical
controls with documented administrative processes, risk analysis, workforce access procedures,
incident response, contingency planning, vendor agreements, and continuing review.

The target architecture places an authenticated API between every distributed client and protected
cloud data. Installed applications must not contain shared Azure SQL credentials or execute schema
migrations. Azure-hosted services should use managed identities and least-privilege permissions.
Public database access should be disabled or narrowly constrained according to the approved network
design.

Before any real agency is provisioned, Sati needs evidence—not merely assertions—for:

- tenant isolation and attempted cross-tenant access rejection;
- unique user identification, authentication strength, session revocation, and emergency access;
- authorization at record, workflow, export, and administrative-operation levels;
- audit coverage for reads, changes, approvals, signatures, exports, impersonation, and overrides;
- integrity controls, version history, concurrency conflict handling, and amendment procedures;
- encryption in transit and at rest, key/secret management, and credential rotation;
- backup restoration, disaster recovery, downtime procedures, and availability monitoring;
- vulnerability management, dependency patching, secure deployment, and incident response;
- retention, legal hold, deletion, member access, accounting of disclosures, and breach response;
- BAAs and responsibility boundaries for every cloud, messaging, AI, support, and integration vendor.

Demo deployment is not production authorization. The Azure Demo must contain only synthetic data,
use a separate database and service identity, and reset from a canonical seed. No Demo pipeline,
credential, managed identity, backup, log, or administrator role may grant access to Production.

## Governing conflict-of-interest principle

Federal HCBS regulations generally prohibit an individual’s HCBS provider—or a party
with an interest in, or employed by, that provider—from also providing the individual’s
case management or developing the person-centered service plan. A limited exception
exists when the state demonstrates that the otherwise-conflicted entity is the only
willing and qualified entity in the geographic area. That exception requires
CMS-approved conflict protections, separation of provider and case-management
functions, and an accessible alternative dispute-resolution process.

Maine describes its OADS case-management program as **conflict free** and expressly
references the same federal requirement.

The practical distinction is:

- A case-management agency documenting its legitimate case-management work and
  submitting its own Section 13 claims through the same application is not, by that
  fact alone, the prohibited conflict.
- OADS reviewing assessments, PCPs, classifications, and waiver requests in the same
  platform is not inherently a conflict.
- A case-management entity developing a person’s plan while also providing the direct
  HCBS selected in that plan raises the core conflict-free-case-management concern.
- Allowing a financially interested entity to recommend, authorize, approve, document,
  and bill its own services without independent controls presents a serious conflict
  and program-integrity risk, regardless of how many software systems are involved.

## Required architectural boundaries

Sati should treat permissions as enforceable capabilities, not merely different menus.
Authorization must be enforced below the visual layer so that hiding a button is never
the only control.

### Case management

- Case managers may draft and submit assessments and person-centered plans.
- A case manager must not approve the case manager’s own submission when independent
  state or supervisory approval is required.
- The system should record the member’s choices, participating parties, alternatives
  considered, provider choices, and conflict-resolution steps.
- Potential organizational conflicts and recusals should be recorded explicitly.

### State and OADS review

- Authorized reviewers may approve, return, deny, or request amendments.
- Reviewers should not silently rewrite the submitted clinical record.
- Reviewer identity, organization, decision, timestamp, reason, and applicable policy
  basis should be retained.
- Reassignment, recusal, escalation, appeal, and dispute workflows should be explicit.

### Billing

- Billing personnel should generate claims only from eligible, authorized, and
  adequately documented services.
- Document approval and claim billability must remain separate states. An approved PCP
  does not by itself establish that a particular claim is payable.
- Claims should retain links to the applicable authorization, service documentation,
  rendering/billing provider, member, service code, units, and date of service.
- The system should prevent or flag duplicate claims, billing outside authorization,
  incompatible services, and retrospective changes affecting submitted claims.

### Direct-service providers

- Direct-service providers must not be able to control case-management recommendations,
  provider choice, or state authorization decisions merely because they share Sati.
- An organization’s role as a provider, case-management entity, reviewer, or billing
  entity should be represented explicitly rather than inferred from a user’s job title.
- Where one organization legitimately holds multiple roles, Sati should enforce the
  required separation between those functions and support independent review.

## Records, versions, and auditability

- Submitted and approved documents should be immutable versions.
- Corrections should create amendments or new versions, never invisible overwrites.
- Audit records should identify actor, role, organization, action, timestamp, prior
  value, new value, and stated reason.
- Audit history should be append-only and unavailable for ordinary users to alter.
- Electronic signatures and attestations should identify exactly which document
  version and representations were signed.
- Authorization changes should preserve the original decision and the complete chain
  of later modifications.
- Reports should detect self-approval, billing without authorization, edits after
  approval or claim submission, suspicious role combinations, and unusual override
  patterns.

## Privacy and data access

- Cross-agency access should be limited to the minimum information needed for the
  user’s assigned function.
- Access should consider member, agency, caseload, program, document type, workflow
  stage, and specific capability—not role name alone.
- Sensitive narrative, diagnostic, financial, and protected-health information may
  require different access boundaries.
- Export, print, download, and bulk-report operations require the same authorization
  scrutiny as on-screen access.
- Authentication, session handling, audit review, retention, incident response, and
  business-associate/data-sharing responsibilities must be established before external
  organizations receive access.

## Recommended domain concepts

The document workflow should be shared by case managers and OADS reviewers rather than
implemented as separate role-specific copies. Likely concepts include:

- `Document` and immutable `DocumentVersion`
- `Draft`, `Submitted`, `Returned`, `Approved`, `Denied`, and `Superseded` states
- `Submission`, `ReviewDecision`, `Amendment`, and `Attestation`
- `Authorization` and authorization revisions
- Organization-scoped `Role` plus narrowly defined `Capability`
- `Assignment`, `Delegation`, `Recusal`, and `ConflictDisclosure`
- Append-only `AuditEvent`

Exact models should be designed only after OADS and agency stakeholders agree on
ownership, signatures, approval stages, amendment rules, and authoritative systems of
record.

## Comprehensive Assessment and PCP design implications

Sati's new Comprehensive Assessment and planned PCP workflow should follow a
"live information, immutable approved record" model. Federal HCBS rules describe
person-centered planning as an ongoing process and require the service plan to reflect
assessed clinical and support needs, strengths, preferences, goals, risks, services,
supports, providers, and responsible parties. The person must be able to request updates.
The plan must also be finalized with informed consent and signatures from the people and
providers responsible for implementation.

The federal review rule requires review and revision at least every 12 months, when the
person's circumstances or needs change significantly, or at the person's request. It does
not require every administrative profile edit to silently rewrite the approved plan.
Accordingly:

- Current profile information may feed assessment and PCP drafts and may appear as live
  context where policy allows.
- An approved PCP must remain an identifiable, reproducible version.
- Changes to goals, assessed needs, services, providers, amount/frequency/duration,
  safeguards, backup plans, authorization facts, or rights restrictions should create a
  controlled review or amendment rather than mutate the approved version.
- Less consequential administrative changes may avoid a formal PCP amendment only after
  OADS adopts and documents a change-classification policy.
- The person or representative must have an accessible way to request an update and must
  remain involved in decisions about material revisions.

The Comprehensive Assessment may be waiver-agnostic, but it must still gather enough
functional, contextual, strengths-based, risk, preference, and support information for
the PCP and for OADS to apply the relevant Classification criteria independently. The
assessment should not become a scoring instrument that substitutes for person-centered
planning; CMS guidance specifically cautions that functional-assessment results should
inform, but not solely drive, the plan.

### Rights restrictions and safeguards

If an assessment or PCP documents modification of HCBS setting rights, the record must
support the heightened federal documentation requirements. These include a specific
assessed need, positive interventions and less intrusive methods tried, a proportionate
condition, data collection and periodic review, informed consent, time limits, and an
assurance against harm. A generic provider rule, diagnosis, or risk label is not enough.
Sati should treat any rights-restriction answer as structured, review-sensitive content
and should never allow it to become boilerplate through automatic profile propagation.

### Signatures and uploaded scans

The planned print/sign/upload workflow must bind signatures to the exact document version
the signer reviewed. Sati should retain both the system-generated PDF and signed scan,
record signer identity/role and relevant dates, and prevent replacement of an executed
artifact. Before production, counsel and OADS should confirm when a scanned physical
signature is acceptable, whether any electronic-signature standard applies, who must
sign each document, and how refusal or inability to sign is documented.

**This paragraph is about SCANNED signatures, and the distinction matters.** A scan is
evidence of a human act that no amount of stored data can reproduce, so when that workflow
exists, both artifacts have to be kept. It does not follow that every generated document
must be retained. The AT request attestation is the counter-example: nothing is printed or
scanned, the record is closed to edits once published, and the PDF is a pure function of
that frozen record — so it is regenerated rather than stored. See "The PDF is regenerated,
never retained" in `DECISIONS.md`. Applying the retention requirement above to a
system-generated document that cannot drift is overcaution, and it costs real storage.

### Billing consequences are an explicit program rule

The adopted product rule—no grace period for overdue PCPs or 90-day reviews, permanent
unbillability of notes during the compliance gap, and no retroactive cure—is high impact.
It should be enforced consistently in note entry, supervisory review, claim creation,
EDI export, reports, and audit history. Before production use, OADS/MaineCare policy owners
or counsel should confirm the controlling authority, exact midnight boundary, time zone,
which service codes and note types are affected, and when billability resumes. A supervisor
override allowing PCP submission despite an overdue assessment must not automatically
become a billing exception.

### Product policy versus externally mandated policy

The following current decisions should be labeled as Sati/OADS workflow policy until a
specific legal or contractual source is recorded:

- Comprehensive Assessment at intake and annually, due 60 days before PCP.
- Supervisor-only approval of the Comprehensive Assessment.
- OADS Resource Coordinator wholesale approval at the PCP level.
- The precise role of Classification in Section 21, Section 29, and future Lifespan
  Waiver determinations.
- Which assessment fields or changes require supervisor review or PCP amendment.
- The no-grace, permanently unbillable treatment of documentation created during an
  overdue PCP or 90-day-review period.

These may be sound operational controls, but the repository should not describe them as
federal requirements unless a controlling source is added.

## Questions requiring formal review

1. Which organization is the authoritative record holder for each document and
   authorization?
2. Which decisions require OADS approval, supervisory approval, member/guardian
   signature, or another attestation?
3. May an organization using Sati provide both case management and any direct HCBS to
   the same member? If an exception applies, what state/CMS-approved protections govern
   it?
4. Which users may view, draft, submit, return, approve, deny, amend, void, or export
   each record?
5. What separation is required between clinical documentation, authorization, billing,
   payment posting, and reconciliation?
6. What constitutes the authoritative completion date, submission date, approval date,
   and effective authorization period?
7. What retention, legal-hold, audit-access, and breach-notification rules apply?
8. Which data may be shared between agencies, OADS, MaineCare, and other providers, and
   under which agreements?
9. What appeal or dispute process must Sati expose to members and representatives?
10. What reports will program-integrity staff require to monitor conflicts and improper
    billing?
11. Which PCP changes are administrative, material, authorization-affecting, or a rights
    restriction, and what review/signature path applies to each category?
12. Are scanned physical signatures acceptable for the Comprehensive Assessment and PCP,
    and which participants must sign or acknowledge each document?
13. What authority establishes the 60-day assessment lead time and the billing effect of
    overdue PCPs and 90-day reviews?
14. Which note types, services, and billing codes become unbillable, at what exact instant,
    and when may billing resume after late completion?
15. May a supervisor-or-higher override permit PCP submission with an overdue assessment,
    and must OADS see or separately approve that override?
16. What constitutes a significant change requiring PCP revision under Maine's process,
    and may any current profile fields be displayed live without republishing the plan?

## Primary references

- [42 CFR § 441.301—person-centered planning and conflict-of-interest requirements](https://www.govinfo.gov/content/pkg/CFR-2022-title42-vol4/pdf/CFR-2022-title42-vol4-sec441-301.pdf)
- [Maine OADS—conflict-free case management and certification](https://www.maine.gov/dhhs/oads/providers/adults-with-intellectual-disability-and-autism/case-management)
- [Maine OADS—person-centered-planning requirements](https://www.maine.gov/dhhs/oads/providers/adults-with-intellectual-disability-and-autism/person-centered-planning)
- [MaineCare Benefits Manual index, including Chapter I § 6 and Chapter II § 13](https://www.maine.gov/sos/rulemaking/agency-rules/mainecare-benefits-manual)
- [CMS—fundamentals of the person-centered service-planning process](https://www.medicaid.gov/medicaid/home-community-based-services/downloads/fundmntl-cndct-pscp-prcess.pdf)
- [CMS—HCBS training series, including conflict-of-interest materials](https://www.medicaid.gov/medicaid/home-community-based-services/home-community-based-services-training-series)

These sources and requirements may change. The team should repeat a primary-source
review before production deployment and record the reviewed rule versions and dates.

## Local AI-Assisted Documentation

The development case-note formatter currently performs inference on the workstation. This reduces
disclosure risk compared with sending narrative text to a hosted model, but local execution alone
does not establish HIPAA, MaineCare, records-management, or billing compliance. Device encryption,
access controls, cache/log contents, backup behavior, incident response, retention, and model/runtime
telemetry still require review.

The model is an assistive drafting tool only. It must not independently establish that a covered
service occurred, decide billability, select units, create missing documentation, determine
medical necessity, or submit a note. Human comparison and explicit acceptance are required. Before
production, retain enough audit information to reconstruct the source, generated draft, final text,
model/rule-set version, and accepting user without creating an uncontrolled secondary PHI store.

The first model acquisition contacts an external model catalog but must not transmit note content.
Sati must not implement silent cloud inference fallback. Catalog/model licensing, update control,
telemetry behavior, vulnerability management, and the security of `%LOCALAPPDATA%\Sati\LocalAi`
must be approved before deployment to agency devices.

### Cross-consumer isolation (added 2026-08-14)

The AI data-access path now returns only the currently selected own-caseload person's ID and first
name; it does not retrieve prior notes, assessments, Bio, deadlines, or other historical records.
The model request contains only a snapshot of current note-entry facts. Tenant and own-caseload
checks are enforced at both the local service and API route, and source fingerprints prevent a
result from being displayed or accepted after a client or input change.

One in-process model instance still serves drafts on the workstation. Sati does not rely on the
native inference runtime to discard conversational state between calls. `ConsumerSessionBoundary`
records the consumer whose facts most recently reached the model and forces unload/reload before a
different consumer can be processed. An unload failure stops generation rather than allowing the
boundary to degrade. Automated tests cover authorization scope, context exclusion, reset decisions,
and stale-result suppression.

This addresses in-process carryover only. It does not address disk cache contents, swap, crash
dumps, or runtime telemetry, all of which remain open items in the paragraph above.
