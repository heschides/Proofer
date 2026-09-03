# Handoff — Compliance attestation, evidence, and annual documents

**For:** whoever implements this next (written for a fresh agent with no prior context).
**Source of truth:** [`NOTE_FORM_ATTESTATION_DESIGN.md`](NOTE_FORM_ATTESTATION_DESIGN.md) at
commit `ac37fbb` on `master`. Read the whole design doc before writing code — this brief is a
map to it, not a replacement for it. Also read `CLAUDE.md` at the repo root first; it states the
project's non-negotiable rules (rule ownership in `Sati.Contracts.V1`, no direct EF in
ViewModels, tenant isolation on every route, no PHI in logs, fail-first tests) and this design
was written to comply with it throughout.

## What this is

Sati's current note-to-form completion bridge is a message box in
`Views/ShellWindow.xaml.cs:152`/`:166` that stamps `DateTime.Today` onto a form's completion
date, discarding the actual event date and silently shifting `BillingComplianceGate`'s billing
window. Section 1 of the design doc lists eleven confirmed defects. This design replaces it with:

- An **evidence vs. attestation** model — a form note is evidence; only an explicit,
  human-entered date via a new `Form.Attest` call closes a form.
- A **prerequisite registry** — document-backed forms (releases, safety plan, privacy notice)
  require a server-checked artifact to exist before attestation is accepted.
- **Draft documents** — releases and the safety plan can be started early with identity-only or
  partial content, and completed later, without that partial state satisfying compliance.
- **Structured safety-plan authoring** in Sati, not a blank template.
- A **tracked acknowledgment** step for the Privacy Practices notice, separate from generating it.
- The **annual document packet** — a T-30 window, a one-click zip, a manifest.

## Landing order (design doc section 12)

Each step is independently shippable and testable. Work through them in order; don't skip ahead
to the packet before the attestation mechanism underneath it exists.

1. `FormAttestation` table, `Form.Attest`/`RevokeAttestation`, backfill, audit actions.
2. Delete the note bridge; add the derived pending-attestation list (section 2.1).
3. Generalize `ReviewsViewModel`'s attestation control to all twelve form types.
4. `DocumentArtifact` plus the prerequisite registry, wired to the two release generators that
   already exist (`AgencyReleasePdfGenerator`, `DhhsFormFiller`), including `Origin.Draft`.
   Narrow `PUT /api/v1/forms/{id}` (currently a full bypass of the registry) and fix
   `CloudFormService.OpenFormAsync` (currently a no-op).
5. `DocumentTemplate` and the MigraDoc token-merge composer, for the Privacy Practices notice.
6. `SafetyPlan` entity, draft/document/submit routes, and its own content-to-PDF renderer —
   this is the largest single step; see section 5.3 and 4.4.
7. `DocumentAcknowledgment` and the privacy-notice acknowledgment gate (section 5.4, 4.5).
8. The packet, the profile control, the release/safety-plan reminder, the manifest (section 6).
9. Hash verification (closes O-1) and the records-request recipient work (O-4).

Steps 1–3 fix the reported production defect and are safe to ship alone. Steps 4–8 are new
feature surface. Do not skip step 9's hash-verification half — section O-1 explains why a stored
SHA-256 nobody ever checks is decoration, not evidence.

## Rule ownership — do not violate this

Per `CLAUDE.md`, every new rule (`FormAttestationRules`, `AnnualDocumentCatalog`,
`AnnualPacketWindow`, `DocumentTemplateResolution`) goes in `Sati.Contracts.V1` and is referenced
by both `Sati.Api` and the desktop client — never implemented twice. The prerequisite check in
particular MUST be enforced server-side; a client asserting a prerequisite is met is not a check.

## Tests — read `CLAUDE.md`'s rule before writing any

Every security/tenancy/concurrency test must be confirmed **failing against the unfixed code**
before you keep it. Design doc section 11 lists 28 specific tests, split into "fails today"
(prove the defect first), "regression pins" (already pass — pin them so they can't regress), and
"new behavior." Test 9 in particular is the one that catches a regression of the `DueDate`
default that made billing windows collapse to empty — don't lose it.

## Explicitly unresolved — do not guess, ask or flag instead

Section 14 of the design doc lists open questions (O-1 through O-7) and risks (R-1 through R-4)
that were deliberately left open rather than decided unilaterally. The ones most likely to block
implementation:

- **O-2** — `Release_Medical` has no PDF generator. Nothing to build until this is resolved one
  of three ways (see O-2). Don't invent a generator without checking which resolution was chosen.
- **O-6** — is the safety plan's section schema (`SafetyPlanDocument`) one Sati-owned structure,
  or per-agency? The design assumes Sati-owned; confirm before building step 6.
- **O-7** — does the safety plan need a supervisor-review gate like `ComprehensiveAssessment`
  (which itself doesn't have that workflow fully wired up)? The design assumes no — case manager
  authors and finalizes it themselves, `Draft -> Final`, no approval step.
- **O-3, O-4, O-5** — reclassification's prerequisite, the records-request delivery channel and
  its ordering against the medical release, and whether a supervisor override on a missing
  document should exist. None of these are decided; don't decide them by default in code.

If any of these haven't been resolved with Josh by the time the relevant step is reached, stop
and ask rather than picking an answer.

## Regulatory note

`REGULATORY_CONCERNS.md` already carries open questions about signatures and acknowledgments for
the comprehensive assessment and PCP. This design adds one more — whether the Privacy Practices
acknowledgment (section 5.4) needs to be a real signature — and section 15 of the design doc
lists which docs (`DECISIONS.md`, `AGENDA.md`, `ARCHITECTURE.md`, `API_AUTHORIZATION.md`,
`AUDIT_EVENTS.md`, `REGULATORY_CONCERNS.md`) need updating as each step lands. Update them as you
go, not in one pass at the end — that's how they've stayed accurate through this project's history.

## Environment note

Demo's SQL firewall is closed to workstations; every schema release (steps 1, 4, 6, 7 all add
tables/columns) needs a temporary firewall rule that only Josh can add. Flag this before trying
to apply a migration to Demo.
