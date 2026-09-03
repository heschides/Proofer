# Design — Evidence, prerequisites, and attestation for compliance forms

**Status:** implementation steps 1–9 landed in source on 2026-09-03; not released or deployed.
**Written:** 2026-09-03, against `master` @ `1e64cf7` (release 1.2.41).
**Decided with Josh:** 2026-09-03. Answers recorded inline; deviations flagged in
"Where I did not do what was asked".

**Implementation addendum (supersedes the original Draft/Final and open-question assumptions
below):** Josh selected one Sati-owned safety-plan schema and supervisor review. The implemented
lifecycle is Draft -> ReadyForReview -> Approved/Returned, with non-author review scoped to the
supervisor's actual caseload. Submitted/reviewed versions are locked; editing starts a new version.
Only an approved plan renders a non-Draft artifact. O-1 is implemented by byte-length/SHA-256
verification, including an operator-selected original-file check. Josh resolved O-4 as download
only, staff send, after medical-release attestation, addressed to the linked primary-care provider.
O-2/O-3/O-5 retain the recorded generator/assessment/technical-override decisions. O-6/O-7 are thus
resolved; the clinical/legal review questions are not.

The packet cannot reconstruct a completed or externally recorded release because Sati retains
metadata, not its signed/saved bytes. Those originals are explicitly listed for retrieval in the
manifest instead of replaced with blank drafts. When no completed release is recorded, the packet
includes an identity-only draft; partial input still in a release editor is not a persisted packet
source. Safety-plan drafts are persisted and render their saved content. Each new privacy PDF
requires its own receipt/effort record. The profile and dashboard reminder are read-time, not a job.
See `ANNUAL_DOCUMENT_RELEASE_READINESS.md` for verified scope and the remaining release gates.

This replaces the note-to-form bridge, adds the prerequisite checks that must pass before
compliance can be asserted, and adds the annual document packet. It does not change how
compliance is *decided* — `BillingComplianceGate` stays the only owner of that — it changes
who is allowed to say a document is done, and on what evidence.

---

## 1. What this replaces

The current bridge is a message box in view code-behind. `NoteEntryViewModel` fires
`FormNoteSavedAsync` when a saved note carries a form type and its status is `Pending` or
`Logged`; `CaseManagerDashboardViewModel` forwards to one of two callbacks; both land at
`Views/ShellWindow.xaml.cs:152` and `:166` as a modal asking whether the form was completed
today. "Yes" calls `MarkFormCompleteAsync`, which stamps `DateTime.Today`.

Confirmed defects, all of which this design dissolves rather than patches:

| # | Defect | Where |
|---|---|---|
| 1 | Completion date synthesized as today, discarding the note's event date | `CaseManagerDashboardViewModel.cs:891` |
| 2 | Cycle resolved against today, not the note's event date | same, via `GetCurrentCycleForm(formType)` with no `asOf` |
| 3 | Person resolved from the dashboard's selection, not the saved note | same, `SelectedPerson` |
| 4 | One-way: deleting the note, abandoning it, or changing its form type never resets the form | no reverse path exists |
| 5 | Fires only at save; a draft later moved to `Logged` from the grid never triggers it | `NoteEntryViewModel.cs:1956` |
| 6 | Notes log deliberately excluded, so the same note saved elsewhere does nothing | `NotesWindowViewModel.cs:146` |
| 7 | The new-vs-edit split is dead; both handlers are byte-identical | `ShellWindow.xaml.cs:152`, `:166` |
| 8 | Silent no-op when the person is unset or no current-cycle form exists | `CaseManagerDashboardViewModel.cs:884` |
| 9 | No server-side equivalent; the rule lives in one WPF client | `ApiEndpoints.cs:2919`, `:2996` store the form type and touch no form |
| 10 | `CloudFormService.OpenFormAsync` never sets `OpenedDate`, so the "No" branch is a Demo no-op | `Data/Cloud/CloudCoreServices.cs:238` |
| 11 | No audit event on any form-completion change, local or API | `Data/FormService.cs:18`, `ApiEndpoints.cs:4298` |

Defect 1 is the serious one. `BillingComplianceGate.IsBillingWindowBlocked` is date-keyed
against `CompletedDate`, so a synthesized date silently changes which past service dates were
billable. `Form.MarkComplete`'s own comment forbids exactly this. The 2026-08-31 quarterly
review decision in `HANDOFF_90DAY_REVIEW_FLAG.md` rejected auto-derivation for the Reviews tab
for the same reason; the note bridge has been doing it all along, from code-behind.

---

## 2. The model

Three distinct things, currently conflated into one message box.

**Evidence** is a record that work happened. A case note tagged with a form type is evidence.
So is a generated release PDF. Evidence never closes a compliance record.

**A prerequisite** is a fact that must be true before an assertion is allowed. For a release,
the prerequisite is that the release document was prepared. Prerequisites are checked at the
moment of attestation, server-side, and cannot be satisfied by the client asserting they were.

**An attestation** is a human saying, on a named date, that this document is done. It carries
an actor, an explicit completion date, the prerequisite state at the time, and optionally a
citation to the evidence that prompted it. It is the only thing that writes
`Form.CompletedDate`.

Flow:

```text
note saved with FormType            -> evidence exists, nothing written to Form
document generated / recorded       -> DocumentArtifact row, nothing written to Form
pending-attestation list (derived)  -> "Q3R for J. Doe is documented but not attested"
case manager attests with a date    -> prerequisites checked -> Form.Attest(...) -> CompletedDate
```

### 2.1 The pending list is derived, never stored

There is no attestation-prompt entity and no event that must fire at the right moment. The
list of waiting attestations is a projection over data that already exists:

> notes with a form type, in status `Pending`, `Logged`, or `Approved`, whose matching form
> (person, type, cycle containing the note's event date) has no live attestation.

This is why defects 4, 5, 6, and 7 disappear rather than getting fixed. Deleting the note
removes it from the list. Moving a draft to `Logged` adds it. Saving from the notes log
behaves identically to saving from the dashboard, because neither one fires anything. There is
no new-versus-edit distinction left to preserve.

Derivation lives in `Sati.Contracts.V1` so the desktop and the API produce the same list.

### 2.2 Cycle resolution

Defect 2 dissolves because nothing infers a form any more. The case manager attests against a
form row they selected, and that row already knows its cycle. The pending list *suggests* a
form by resolving (person, type, cycle containing the note's event date), and shows which
cycle it chose. A suggestion is not a write, so a wrong suggestion is visibly correctable.

Attestation date validation:

- Not in the future. Delegates to the existing `FormCompletionRules.Validate`.
- Not before the form's own `cycleStart`. A document cannot have closed this cycle before the
  cycle began.
- No upper bound short of today. A late completion after cycle end is real, and clamping it
  would be the maximally permissive billing answer.

---

## 3. Rule owners in `Sati.Contracts.V1`

All new rules go in contracts, referenced by both `Sati.Api` and the desktop, per `CLAUDE.md`.

**`FormAttestationRules`** — the single owner of attestation legality.

```text
AttestationDecision Evaluate(
    string formType,
    DateTime completedOn,
    DateTime cycleStart,
    DateTime today,
    AttestationActorKind actor,
    IReadOnlyCollection<ArtifactFact> artifactsForCycle)

IReadOnlyList<PendingAttestation> PendingAttestations(
    IReadOnlyCollection<NoteFact> notes,
    IReadOnlyCollection<FormFact> forms,
    DateTime? effectiveDate,
    DateTime today)

PrerequisiteKind PrerequisiteFor(string formType)
```

`AttestationDecision` returns `Accepted`, a `DateError`, and a list of `UnmetPrerequisite`
values naming what is missing. `ArtifactFact`, `NoteFact`, and `FormFact` are narrow readonly
records, not entities and not DTOs. That is the convention `AtRequestLine` and
`BillingComplianceGate` already use, so neither side has to hand the other its own shape.

**`AnnualDocumentCatalog`** — the mapping owner. Which document kinds exist, which form type
each satisfies, which are in the annual packet, which render without consumer input, and the
display name for each. One table, so the packet, the prerequisite check, and the UI cannot
disagree about what a document is called or what it proves.

**`AnnualPacketWindow`** — window math. `IsOpen(effectiveDate, today, openDaysBefore)` and
`OpensOn(...)`, computed from the same cycle boundaries `Person.GetCurrentCycleBoundaries`
uses. No second copy of anniversary arithmetic.

**`DocumentTemplateResolution`** — which template version wins for an agency and kind.

---

## 4. Persistence additions

Five new tables in `Sati.Persistence`: three from the original design (`DocumentArtifact`,
`FormAttestation`, `DocumentTemplate`), plus `SafetyPlan` and `DocumentAcknowledgment` from 5.3
and 5.4. No existing column changes except `Form` gaining a navigation collection.

### 4.1 `DocumentArtifact`

The record that a document exists. **No bytes are stored.**

| Column | Notes |
|---|---|
| `Id` | |
| `PersonId`, `AgencyId` | tenant scoping; `AgencyId` denormalized for the isolation filter |
| `Kind` | `AnnualDocumentKind`, stored as a string, per the `Form.Type` precedent |
| `CycleStart` | date; ties the artifact to one compliance cycle |
| `Origin` | `GeneratedInSati`, `Draft`, or `RecordedAsExternal` — see 5.2 |
| `GeneratedAtUtc`, `GeneratedByUserId` | |
| `ContentSha256` | `char(64)`, null when `Origin` is `RecordedAsExternal` |
| `ByteCount` | null for external |
| `SuggestedFileName` | what the save dialog offered |
| `TemplateOwner`, `TemplateKey`, `TemplateVersion` | null for the two PDF-filler documents |
| `BlankFieldsJson` | field *names* left blank, reusing the existing `DhhsFormResult.BlankFields` warning |
| `ExternalNote` | required and non-blank when `Origin` is `RecordedAsExternal` |
| `SupersededByArtifactId` | regeneration chain; the latest live row satisfies the prerequisite |

`BlankFieldsJson` holds names, never values. No narrative, no SSN, no merged content. The row
records that a document was produced, and enough to identify it. It is not a copy of the
document.

Unique index on `(PersonId, Kind, CycleStart)` filtered to `SupersededByArtifactId IS NULL`,
mirroring how `Forms (PersonId, Type, DueDate)` was fixed in `20260901150802`. Regeneration
supersedes rather than inserting a second live row, so the duplicate-row failure mode that
cost 984 surplus form rows cannot repeat here.

### 4.2 `FormAttestation`

Append-only. Every attestation and every revocation is a row. Nothing is updated or deleted.

| Column | Notes |
|---|---|
| `Id`, `FormId` | |
| `Kind` | `Attested` or `Revoked` |
| `CompletedOn` | the date attested; null on a revocation |
| `ActorKind` | `CaseManager`, `Supervisor`, or `System` |
| `ActorUserId` | null only for `System` |
| `RecordedAtUtc` | |
| `EvidenceNoteId` | nullable citation; not a cascading foreign key |
| `PrerequisiteStateJson` | which prerequisites were satisfied, by which artifact ids |
| `Reason` | required on a revocation and on any `System` row |

`Form.CompletedDate` stays the authoritative scalar that `BillingComplianceGate`, the caseload
matrix, the twelve dashboard checkboxes, and `UpcomingEventsService` all read. Nothing about
those readers changes. `CompletedDate` becomes the projection of the latest live attestation,
written by the entity in the same call that appends the row:

```text
Form.Attest(FormAttestation attestation)   // appends, sets CompletedDate
Form.RevokeAttestation(...)                // appends, clears CompletedDate
```

`MarkComplete` and `Reset` become private to the entity. The existing comment on `Form` warns
that "two writers kept in step by convention is a rule with no owner"; keeping both writes
inside one entity method is what keeps that from becoming true again.

`EvidenceNoteId` is deliberately not a cascading foreign key. If the evidence note is later
deleted, the attestation stands, because a human attested and that is not derived from the
note. The citation becomes a dangling id, recorded honestly, and the note deletion is already
audited. A compliance record must not be silently revoked by a note delete.

### 4.3 `DocumentTemplate`

Append-only versions. `AgencyId` null means the Sati default.

| Column | Notes |
|---|---|
| `Id`, `AgencyId` (nullable), `Kind` | |
| `Version` | monotonic per `(AgencyId, Kind)` |
| `Body` | template source, see 6.2 |
| `PublishedAtUtc`, `PublishedByUserId` | |
| `RetiredAtUtc` | nullable |

Never edited in place. A document generated under version 1 keeps citing version 1, for the
same reason `AtRequestPublication` freezes its attestation wording onto the request rather
than looking it up at render.

### 4.4 `SafetyPlan`

**Added 2026-09-03**, replacing the earlier plan to render the safety plan from a blank template
alongside the privacy notice. See 5.3 for why.

Same shape as `ComprehensiveAssessment` (`Sati.Persistence/Models/Assessments/`), which is the
existing pattern for a structured, per-consumer, versioned clinical document — reusing it rather
than inventing a second one is the same reasoning `CLAUDE.md` gives for not writing a second copy
of a rule.

| Column | Notes |
|---|---|
| `Id`, `PersonId`, `Person` | |
| `AuthorUserId`, `AuthorUser` | |
| `Status` | `SafetyPlanStatus`: `Draft`, `Final`, `Superseded` — no supervisor review gate; see 14 |
| `Version`, `Revision` | same convention as `ComprehensiveAssessment` |
| `CreatedAt`, `UpdatedAt`, `FinalizedAt` | |
| `DocumentJson` | structured content, see below |

`DocumentJson` deserializes to a `SafetyPlanDocument`, the same convention `AssessmentDocument`
and `Note.VisitDocumentation` already use: a typed shape backing a JSON column, so a field can be
added without a migration touching every existing row. **The actual sections — warning signs,
coping strategies, support contacts, means-safety steps, whatever your template specifies — are
not designed here.** That is clinical content, not a persistence question, and it is exactly the
template you said you'd provide. What this entity fixes is the container: `SafetyPlanDocument`
is a versioned bag of named sections, and your template becomes the section list the first time
someone builds against this schema — the same relationship `AssessmentDocument.Answers` has to
the comprehensive assessment's actual questions, which also aren't hardcoded here.

### 4.5 `DocumentAcknowledgment`

For the privacy-notice receipt requirement in 5.4. Small and separate from `DocumentArtifact`
rather than columns bolted onto it, the same reasoning that keeps `FormAttestation` its own
table instead of extra columns on `Form`.

| Column | Notes |
|---|---|
| `Id`, `DocumentArtifactId` | the live artifact this acknowledges |
| `ReceivedOn` | nullable date; null means not received |
| `GoodFaithEffortReason` | required when `ReceivedOn` is null — the consumer declined or could not be reached, and this is Sati's record of trying |
| `RecordedByUserId`, `RecordedAtUtc` | |

This is not an electronic signature. Same scope note `AtRequestPublication` already carries for
its own attestation: it records the authenticated Sati user and the time, and does not itself
satisfy any state or federal e-signature standard. Whether a signature is required here at all is
exactly the kind of question `REGULATORY_CONCERNS.md` exists to hold open rather than assume; see
5.4.

---

## 5. The prerequisite registry

| Form type | Prerequisite | Satisfied by |
|---|---|---|
| `Q1R`–`Q4R` | None | The attestation *is* the review record. Per the 2026-08-31 decision, review items are evidence and are deliberately not derived. |
| `PCP` | None | Evergreen holds the real plan. See 5.1. |
| `ComprehensiveAssessment` | None | Same. See 5.1. |
| `Reclassification` | Completed `ComprehensiveAssessment` form in the same cycle | Decided by Josh 2026-09-03. |
| `SafetyPlan` | A `SafetyPlan` in `Final` status for the cycle, or external | Authored in Sati; see 5.3 |
| `PrivacyPractices` | Live artifact for the cycle **and** a `DocumentAcknowledgment` (receipt or documented good-faith effort), or external | Rendered from template; see 5.4 |
| `Release_Agency` | Live **non-Draft** artifact for the cycle, or external | `AgencyReleasePdfGenerator` |
| `Release_DHHS` | Live **non-Draft** artifact for the cycle, or external | `DhhsFormFiller`, `FormKey.AuthorizationToRelease` |
| `Release_Medical` | Live **non-Draft** artifact for the cycle, or external | `MedicalReleasePdfGenerator`; decided by Josh 2026-09-03. |

Rules:

- The prerequisite is checked server-side against the API's own artifact rows. A client cannot
  satisfy it by claiming it is satisfied. This is the same reason `FormCompletionRules` is
  enforced at both persistence boundaries today.
- `RecordedAsExternal` satisfies the prerequisite and requires a non-blank note. It is
  recorded per cycle, not permanently, so next year asks again.
- `ActorKind.System` bypasses prerequisites and requires a reason. This is for
  `FormDuplicateRepair` and the backfill, not for anything a user can reach.
- An unmet prerequisite **blocks** for a case manager. A Supervisor may override only for a
  technical problem, must enter a reason, and produces a separate audit event. The override does
  not bypass date validation and is not a billing or signature override; decided by Josh 2026-09-03.

### 5.1 Sati's comprehensive assessments and PCPs are development-only

Evergreen holds the production records. Sati's `ComprehensiveAssessment` entity and PCP
authoring are for development, with a future direction of feeding Evergreen through their
APIs.

So both map to `PrerequisiteKind.None`, with the registry entry carrying the reason and a
named future owner. When the Evergreen integration exists, these two entries change from
`None` to `ExternalSystem` and nothing else in the design moves.

No Evergreen API contract, credentials, or business-associate posture exists today. That is a
`REGULATORY_CONCERNS.md` item before any integration work, not a coding task.

### 5.2 Draft releases, and the PCP / packet reminder

**Decided with Josh, 2026-09-03.** A release may be rendered early, with only the identity Sati
already holds filled in, and Sati reminds the case manager to finish it when either the PCP is
attested or the annual packet opens — whichever comes first.

**What a Draft is, and is not.** `Origin.Draft` is a third state alongside `GeneratedInSati` and
`RecordedAsExternal`, produced by the same generators (`AgencyReleasePdfGenerator`,
`DhhsFormFiller`) called with the recipient, information categories, scope, date range, and the
three sensitive-category consents (drug/alcohol, mental health, HIV/AIDS) left unset. This is
safe precisely because those fields are the only thing that makes a release an authorization:
identity alone consents to nothing. A Draft is not the "prerequisite satisfied" state — the row
in 5 above reads **non-Draft**. Its own field names are recorded in `BlankFieldsJson`, the same
mechanism `DhhsFormFiller` already uses for a field left blank because the case manager doesn't
have it yet; here every consumer-choice field is blank by design rather than by omission.

Completing a Draft — the case manager returns to the workspace, enters the consumer's actual
choices, and generates for real — calls the same `POST /people/{id}/documents/{kind}` route with
the full request. It supersedes the Draft row rather than creating a second live artifact, the
same chain `SupersededByArtifactId` already carries for regeneration, so the filtered unique
index on `(PersonId, Kind, CycleStart)` never sees two live rows.

**The reminder is derived, not stored**, the same way the pending-attestation list in 2.1 is. It
is not a notification record and not a scheduled job — Sati has none. It is a read-time
computation: for this person's current cycle, does `Release_Agency` and `Release_DHHS` have a
live artifact of any origin (Draft counts as "started", non-Draft as "done")? The result is
shown wherever the case manager is already looking at this person — the profile page, the
pending-attestation list — the moment either of two things becomes true:

- **PCP is attested.** The `Form.Attest` call for `FormType.PCP` is already a discrete, observed
  event in this design (section 2). No new write happens because of it; the reminder is just
  computed and shown the next time this person's page renders.
- **The annual packet opens** — `AnnualPacketWindow.IsOpen` becomes true (section 6.5). Reusing
  this rather than waiting only on PCP matters because the three release due dates are set
  independently per `Settings.Release*DaysBeforeAnniversary` and are not guaranteed to fall after
  PCP's own due date. An agency that configures releases due well before PCP would otherwise get
  no reminder until after the release was already overdue.

Whichever fires first is what the case manager sees; nothing double-fires, because the condition
being displayed ("these releases aren't done yet") is the same fact read from the same place
regardless of which event caused the read.

**This is a nudge, not a gate.** It does not block PCP attestation, does not change any release
due date, and adds no new prerequisite. It is layered on top of the mechanism in section 5, not
a second copy of it — the reminder and the hard block at attestation time both read the same
live-artifact fact, so they cannot disagree about whether a release is done.

**Scope.** `Release_Agency`, `Release_DHHS`, and `Release_Medical` support Draft. Agency and DHHS
use their existing renderers; the medical form uses the Sati-owned `MedicalReleasePdfGenerator`
chosen by Josh on 2026-09-03. Section 5.3 puts the safety plan through the identical Draft/reminder
mechanism, so the same PCP-or-packet-open reminder also names it when unfinished.

### 5.3 The safety plan holds real content

**Decided with Josh, 2026-09-03** — supersedes the earlier plan (still visible in 6.1's original
framing) to treat the safety plan the same as the privacy notice: a template merge with only
identity filled in. That would have produced a blank shell and let a prerequisite check read
"prepared" as "exists," which is precisely the failure mode section 5's "compliant, date
unknown" framing exists to prevent. `SafetyPlan` (4.4) is the fix: a real, versioned, per-consumer
document, structured the same way `ComprehensiveAssessment` is.

**Authoring.** A case manager opens the current-cycle `SafetyPlan` — created on first open,
`Status = Draft` — and works through it with the consumer, the same shape as
`ComprehensiveAssessment`'s draft/document/submit flow (`POST /people/{id}/safety-plans/draft`,
`PUT /safety-plans/{id}/document`, `POST /safety-plans/{id}/submit`). Submitting sets
`Status = Final` and `FinalizedAt`. Unlike the assessment, there is no `ReadyForReview`/`Approved`
supervisor gate here by default — see the open question below.

**Generation is now two different things, by document kind.** The privacy notice stays a
token-merge over your template (6.2's shared MigraDoc composer) — it has no per-consumer content,
so a template is the whole story. The safety plan needs a second, separate renderer that walks
`SafetyPlanDocument`'s actual sections into a PDF, the same relationship
`AgencyReleasePdfGenerator` has to `AgencyReleaseRequest` — structured data in, formatted document
out, not a token substitution. `DocumentArtifact` for `SafetyPlan` records which `SafetyPlan.Id`
and `Version` it was rendered from (`SourceContentId`/`SourceContentVersion`, alongside the
existing `TemplateOwner`/`Key`/`Version` columns, which stay null for this kind since there is no
prose template involved).

**Draft still means Draft.** Rendering a `SafetyPlan` while it is `Status = Draft` produces
`Origin.Draft` on the artifact — same rule as the two releases, same reason: an unfinished plan
is not evidence the plan exists. Only a render of a `Final` plan produces `Origin.GeneratedInSati`
and satisfies the prerequisite. This means `SafetyPlan` moves out of 6.1's "renders complete, no
input needed" tier and into the same "renders as Draft, completion deferred" tier as the two
releases — see the updated table below. It is no longer true that the packet can hand the case
manager a finished safety plan at T-30 with zero effort; it can hand them a *started* one, the
same as it can start a release, and actually finishing either one still requires the case
manager's own work with the consumer.

### 5.4 The privacy notice needs proof of receipt, not just existence

**Decided with Josh, 2026-09-03.** Generating the document is not the prerequisite by itself —
`DocumentAcknowledgment` (4.5) also has to exist for the live artifact, recording either a
receipt date or a documented good-faith-effort reason the consumer didn't sign. The reason field
matters as much as the date: a consumer who declines or can't be reached does not mean the case
manager failed at anything, and the record has to be able to say that honestly rather than force
a signature that may never come — the same posture `AgencyReleaseRequest.ReleaseWithoutReview`
already takes for the agency release.

This makes `PrivacyPractices` the one form type whose prerequisite is genuinely two-part rather
than one artifact check, and it is worth naming that difference plainly in the attestation
control's UI rather than letting it look like every other form's single "document exists" gate.

Whether Sati is even the right place to capture this, versus a physical signed copy kept in a
paper file being sufficient under whatever notice-of-privacy-practices rule actually governs
Maine case-management agencies, is a real open question — not one I'm resolving here. It goes on
`REGULATORY_CONCERNS.md`'s list alongside the existing question about which documents require a
signature (question 12).

---

## 6. The annual document packet

At `AnnualPacketOpenDaysBefore` days before the anniversary, the packet opens for that
consumer's next cycle.

### 6.1 What renders when

The hybrid answer is correct, because a *complete, authorizing* release cannot be rendered
before the consumer has chosen a recipient and a scope, and — as of 5.3 — a *complete* safety
plan cannot be rendered before the consumer and case manager have actually built one.
`AgencyReleaseRequest` requires recipient identity, information categories, dates, and scope,
none of which Sati can invent; a `Final` `SafetyPlan` requires the same kind of work Sati cannot
do on its own. Section 5.2's Draft tier applies to both: identity-only or in-progress content can
render at packet-open even though completion still waits on the people, not the software.

**Renders complete, no consumer input needed:**

- Privacy Practices notice. Template merge with agency and consumer identity. Note this
  satisfies *generation* only — 5.4's acknowledgment is still separate and cannot be produced by
  Sati alone.
- Medical records request. Template merge; recipient resolved from the consumer's primary care
  provider.

**Renders as a Draft; completion deferred until real content or the consumer's choices exist:**

- Consumer Safety Plan, from whatever `SafetyPlan` currently exists for the cycle — blank if the
  case manager hasn't started it, in-progress content if they have. See 5.3.
- `Release_Agency`, through the existing agency release workspace.
- `Release_DHHS`, through the existing DHHS forms workspace.
- `Release_Medical`, through the Sati-owned medical release generator.

### 6.2 Templates

Per agency with a Sati default.

The PDF stack is PDFsharp and MigraDoc, per `Sati.Forms.csproj`. It is not HTML to PDF. So a
template is not markup. It is a constrained document source that one shared MigraDoc composer
walks: headings, paragraphs, lists, simple tables, page breaks, and `{{token}}` substitution.
That keeps a template editable by a non-programmer and replaceable per agency, without giving
agencies arbitrary layout control and without adding an HTML rendering dependency.

The token set is closed and validated at publish time. An unknown token fails the publish
rather than rendering as literal braces in a document handed to a consumer. Available tokens
are agency identity, consumer identity, cycle dates, case manager identity, and for the records
request the resolved provider block.

The rendered artifact records `TemplateOwner` (`SatiDefault` or `Agency`), `TemplateKey`, and
`TemplateVersion`, so the evidence trail is never ambiguous about which wording was used.

### 6.3 The medical records request recipient

Resolved from `PersonProvider` where `IsPrimaryCare` is true and the relationship is current,
then up the provider chain via `Provider.ParentProviderId`. That is the same read-time
derivation `PersonProvider` already documents, so correcting a directory entry corrects the
letter.

`Provider` has `Street`, `City`, `State`, `Zip`, `Phone`, and `PrimaryContact`. It has **no fax
field**, and records requests commonly go by fax. Open question O-4.

Following the `DhhsFormFiller` precedent, a missing value never fails the render. The document
is produced with the box blank and the field name recorded in `BlankFieldsJson`, so the case
manager can see what needs completing by hand. If no primary care provider is linked at all,
the records request is omitted from the packet and the manifest says why.

### 6.4 "Save Annual Documents Locally"

One control on the consumer's profile page. Visible always, enabled from the packet open date
through cycle end, and when disabled it states the date it opens. Not hidden. A control that
appears and disappears is undiscoverable, and the existing status vocabulary already has
`NotYetDue` and `InWindow` for exactly this.

Clicking it produces **one zip**, not a sequence of save dialogs. `System.IO.Compression` is
already referenced in the desktop project, so this adds no dependency.

Since 5.2 and 5.3 let `Release_Agency`, `Release_DHHS`, and the safety plan render as Drafts with
whatever the case manager has so far — including nothing at all — the zip includes them
alongside the two documents that render complete on their own. The point of "save locally" is
handing the case manager everything there is to work with in one action, and a Draft is
something to work with. Each Draft is named plainly in both the file name and the manifest
(`Agency-Release-DRAFT-...`, `Medical-Release-DRAFT-...`, `Safety-Plan-DRAFT-...`), so it cannot
be mistaken for the finished, signed version sitting next to it in the same folder.

The zip contains each rendered PDF plus `MANIFEST.txt` listing, per document: display name,
file name, SHA-256, template owner and version (or `SafetyPlan` id and version, for the plan),
generation timestamp, the case manager who generated it, and any blank fields. For the Privacy
Practices notice it also states whether 5.4's acknowledgment has been recorded yet — the PDF
existing in this folder is not the same fact as the consumer having received it. It lists the
deferred releases as outstanding work, with a line saying what each still needs. The manifest is
what makes the consumer's folder self-describing a year later, and it is what makes the stored
hash usable.

Each rendered document writes a `DocumentArtifact` row. Saving the zip writes one
`annual-packet.saved` audit event. Re-saving supersedes the previous artifacts rather than
duplicating them.

### 6.5 The packet window setting

One new `Settings.AnnualPacketOpenDaysBefore`, default 30. `Settings` is already agency-scoped.

This is deliberately *not* derived from the existing per-form `*OpenDaysBefore` values. Those
drive the per-form upcoming-events window and several currently default to zero. The packet is
one event with one window; deriving it from nine per-form settings would mean the packet opens
nine times. The relationship between the two goes in `DECISIONS.md`, so nobody later unifies
them and reintroduces a second source of truth for when prep starts.

### 6.6 There is no scheduler

The window is evaluated at read time, when the profile or the dashboard computes it. Sati has
no background job today, and the nightly Demo reset is still not configured. So nothing
happens *to* a consumer whose record is never opened.

That is adequate here, because the packet is a case manager's task and it surfaces the moment
they look. It is stated explicitly so nobody assumes a job exists. If the packet later needs
to reach a work queue without anyone opening the record, that queue is the existing
caseload-scoped `UpcomingEvents` mechanism, which is also read-time.

---

## 7. API surface

New routes. Every one gates on `TenantAccess.CanAccessUserAsync` before a caller-supplied id
reaches a query, and `ValidatedActorFilter` re-confirms identity. `API_AUTHORIZATION.md` gets a
row per route.

| Route | Purpose |
|---|---|
| `POST /api/v1/people/{id}/forms/{type}/attestation` | Attest. Server re-evaluates prerequisites and date rules. Typed 422 naming unmet prerequisites; typed 409 on a `Revision` conflict. |
| `POST /api/v1/people/{id}/forms/{type}/attestation/revoke` | Revoke, reason required. |
| `GET /api/v1/people/{id}/attestations/pending` | The derived pending list. |
| `POST /api/v1/people/{id}/documents/{kind}` | Render one document, record the artifact, return bytes. For `Release_Agency`/`Release_DHHS`, an `AgencyReleaseRequest` with the consumer-choice fields unset records `Origin.Draft`; the full request supersedes it and records `Origin.GeneratedInSati`. See 5.2. |
| `POST /api/v1/people/{id}/documents/{kind}/external` | Record external origin, note required. |
| `GET /api/v1/people/{id}/documents?cycleStart=` | Live artifacts for a cycle. |
| `POST /api/v1/people/{id}/annual-packet` | Render the input-free set, record artifacts, return the zip. |
| `GET`/`POST /api/v1/agencies/{id}/templates/{kind}` | Read and publish template versions. Agency administrator only. |
| `GET /api/v1/people/{id}/safety-plans/latest` | Current-cycle plan, mirrors the comprehensive-assessment route. |
| `POST /api/v1/people/{id}/safety-plans/draft` | Create-or-get the current-cycle draft. |
| `PUT /api/v1/safety-plans/{planId}/document` | Save `DocumentJson`. |
| `POST /api/v1/safety-plans/{planId}/submit` | `Status -> Final`, sets `FinalizedAt`. This is what lets a subsequent `documents/{kind}` render produce `Origin.GeneratedInSati` instead of `Origin.Draft`. |
| `POST /api/v1/people/{id}/documents/privacy-practices/acknowledgment` | Record a `DocumentAcknowledgment` — a receipt date, or a good-faith-effort reason. Required before the `PrivacyPractices` prerequisite is met; see 5.4. |

**`PUT /api/v1/forms/{id}` must be narrowed.** It currently accepts an arbitrary
`CompletedDate` with a future-date check and nothing else. Left as it is, it is a complete
bypass of the prerequisite registry, which is exactly what `CLAUDE.md` means by a client
bypassing the rule by sending the update directly. It keeps `OpenedDate` and stops accepting
`CompletedDate`.

`CloudFormService.OpenFormAsync` gets fixed to set `OpenedDate` while this route is being
touched, closing defect 10.

---

## 8. Desktop changes

**`NoteEntryViewModel`.** Delete `FormNoteSavedAsync` and the block at `:1956`. Saving a note
no longer has a form side effect. `SelectedFormType` stays, because it is evidence tagging, and
the narrative-seeding behavior in `OnSelectedFormTypeChanged` is unaffected.

**`CaseManagerDashboardViewModel`.** Delete `MarkFormCompleteRequested`, `FormStatusRequested`,
`MarkFormCompleteAsync`, and the wiring at `:108`. Add the derived pending-attestation list,
refreshed through the existing `AfterFormComplianceChangedAsync` owner.

**`ShellWindow.xaml.cs`.** Delete both message-box handlers at `:152` and `:166`. No business
decision returns to code-behind.

**One shared attestation control.** `ReviewsViewModel`'s quarterly attestation is already the
correct implementation: blank-by-default date, validated at capture, routed through
`IFormService`, with a deliberate reset. Generalize it to all twelve form types, add the
prerequisite display, and reuse it everywhere. It shows unmet prerequisites by name with a jump
to the workspace that satisfies each, plus the "recorded externally" path with its required
note.

**The existing completion paths that synthesize dates.** These all become attestations:

| Path | Today | Becomes |
|---|---|---|
| `CaseManagerDashboardViewModel:625` dashboard checkbox | stamps `DueDate` | opens the attestation control |
| `CaseManagerDashboardViewModel:661` task board | stamps `Today` | opens the attestation control |
| `CaseManagerDashboardViewModel:891` note bridge | stamps `Today` | deleted |
| `NewClientViewModel:1188` client page toggle | stamps `DueDate` | opens the attestation control |
| `ComplianceFormRow:57` per-row picker | explicit date | unchanged; already correct |
| `ReviewsViewModel:204` | explicit date | unchanged; becomes the shared control |
| `FormBulkCompletion:88` | stamps `DueDate` | requires one captured date for the batch |
| `FormDuplicateRepair:154` | preserves existing | `ActorKind.System` with a reason |

`HANDOFF_90DAY_REVIEW_FLAG.md` recorded the `DueDate` default as the weaker of the two
defaults and a candidate for a later sweep, and explained why it is not neutral: setting
`CompletedDate = DueDate` collapses the `IsBillingWindowBlocked` window to empty, so nothing is
ever blocked. This is that sweep. Landing it changes billing behavior on the dashboard
checkbox and client page paths, which is the point, and it needs a release note.

**The release reminder (5.2).** Reads as a small banner on the profile page and in the
pending-attestation list, computed the same way both are already computed — no new event
subscription, no new stored state. It becomes visible the next time the person's page renders
after either `Form.Attest(FormType.PCP, ...)` or `AnnualPacketWindow.IsOpen` turns true, listing
whichever of `Release_Agency` / `Release_DHHS` / `Release_Medical` / `SafetyPlan` has no live
non-Draft artifact for the cycle, with a jump to the relevant workspace — the agency release
workspace opens directly into the existing Draft, if one exists, rather than a blank form, and
the safety plan link opens the existing `SafetyPlan` draft the same way.

---

## 9. Audit

New actions, mirrored between `LocalAuditActions` and the API's `AuditActions`:

```text
form.attested
form.attestation-revoked
document.generated
document.recorded-external
annual-packet.saved
document-template.published
safety-plan.created
safety-plan.updated
safety-plan.finalized
document.acknowledgment-recorded
```

`safety-plan.*` is deliberately separate from the existing `assessment.created`/`updated`/
`submitted` actions rather than reused. Those three are already scoped by their call sites to
`ComprehensiveAssessment`; giving `SafetyPlan` its own names keeps both greppable as what they
actually are, the same reasoning that keeps `Form.Attest` and `SafetyPlan.Submit` as separate
code paths rather than one generic "complete a thing" method.

There is currently no audit event for any form-completion change in either implementation.
`AUDIT_EVENTS.md` gets a row per action with its payload shape. Payloads carry form type, cycle
start, the attested date, actor, and satisfied-prerequisite artifact ids. Never narrative — this
matters more than usual for `safety-plan.*`, since `DocumentJson` is exactly the kind of content
CLAUDE.md's "do not log unrestricted note narratives" rule is written for.

---

## 10. Migration and backfill

Five tables (`DocumentArtifact`, `FormAttestation`, `DocumentTemplate`, `SafetyPlan`,
`DocumentAcknowledgment`), one settings column, one `Form` navigation, two filtered unique
indexes. The same index and column length must be declared on `ApiDbContext` so the server model
does not drift, which was the correction needed in `20260901150802`. `SafetyPlan` also needs the
same `Version`/`Revision` concurrency handling `ComprehensiveAssessment` already has — copy the
pattern, don't rederive it.

Backfill `FormAttestation` from every existing `Form` that has a `CompletedDate`, as
`ActorKind.System` rows with reason `pre-attestation record` and no actor. This does not invent
who attested or when they did. It records that the completion predates the attestation
mechanism. `PrerequisiteStateJson` is null for these rows, and prerequisites are never enforced
retroactively.

`SafetyPlan` and `DocumentAcknowledgment` need no backfill — they are new record classes with no
prior data of their own. A `Form` for `FormType.SafetyPlan` with an existing `CompletedDate`
still gets the same `System`-actor `FormAttestation` backfill row as every other form type; it
does not retroactively require or synthesize a `SafetyPlan` entity, for the same reason
prerequisites are never enforced retroactively.

No existing `CompletedDate` value changes. No production query, transformation, or repair is
part of this design.

Demo needs a temporary SQL firewall rule for the schema release, which only Josh can add.

---

## 11. Tests

Per `CLAUDE.md`, every security, tenancy, and concurrency test must be confirmed failing
against the unfixed code before it is kept. The ones that pass either way are marked as
regression pins in their names.

Fails today:

1. Saving a note with a form type does not change `Form.CompletedDate`.
2. An attestation records the entered date, not `Today` and not `DueDate`.
3. Attesting a release with no live artifact for the cycle is rejected at the view model, the
   local service, and the API.
4. `PUT /forms/{id}` no longer accepts a `CompletedDate`.
5. `CloudFormService.OpenFormAsync` sets `OpenedDate`.
6. A form completion emits an audit event.
7. The pending list resolves the form from the note's person and event-date cycle, not from the
   dashboard selection and today.
8. An attestation dated before the form's `cycleStart` is rejected.

Regression pins:

9. With a late `CompletedDate`, a service date between `DueDate` and `CompletedDate` is still
   blocked by `BillingComplianceGate.IsBillingWindowBlocked`. This is the test that catches a
   reintroduced `DueDate` default.
10. Logging review items does not change `Form.IsCompliant`. The auto-derive behavior rejected
    on 2026-08-31, pinned so it cannot come back.
11. Deleting an evidence note does not revoke the attestation.

New behavior:

12. The packet window opens exactly `AnnualPacketOpenDaysBefore` days before the anniversary
    and closes at cycle end.
13. An artifact cites the template version live at generation, and publishing a new version
    does not change what an existing artifact cites.
14. Regenerating a document supersedes the prior artifact rather than creating a second live
    row, and the filtered unique index rejects a second live row.
15. `RecordedAsExternal` without a note is rejected at both persistence boundaries.
16. A packet with no linked primary care provider omits the records request and says so in the
    manifest.
17. A template publish with an unknown token is rejected.
18. Tenant isolation on every new route, following the pattern in `TenantAuthorizationTests`.
19. A Draft artifact (identity only, recipient/categories/scope/consents unset) does not satisfy
    the release prerequisite — attesting the release is still rejected while only a Draft exists.
20. Completing a Draft supersedes it rather than creating a second live row, same as regeneration
    in test 14.
21. A Draft cannot include a sensitive-category consent (drug/alcohol, mental health, HIV/AIDS)
    set to true — the API rejects a Draft request that sets any of them, since a Draft by
    definition has no consumer authorization to record.
22. The release reminder appears once `Form.Attest(FormType.PCP, ...)` is called, and
    independently once `AnnualPacketWindow.IsOpen` becomes true — each trigger alone is
    sufficient, and neither depends on the other having fired.
23. A `Release_Medical` Draft is generated and recorded, but does not satisfy its prerequisite;
    the completed medical release supersedes it and does satisfy the prerequisite.
24. A `SafetyPlan` in `Draft` status does not satisfy the `SafetyPlan` prerequisite; rendering it
    produces `Origin.Draft`.
25. Submitting a `SafetyPlan` sets `Status = Final`, and rendering it after that produces
    `Origin.GeneratedInSati` and satisfies the prerequisite.
26. The rendered artifact records the exact `SafetyPlan` id and version it came from, and
    re-rendering after a further edit supersedes the prior artifact — same invariant as test 14,
    applied to content-authored documents instead of template-merged ones.
27. Attesting `PrivacyPractices` with a live artifact but no `DocumentAcknowledgment` is rejected;
    attesting with either a receipt date or a good-faith-effort reason on file succeeds.
28. Recording a `DocumentAcknowledgment` with `ReceivedOn` null and no `GoodFaithEffortReason`
    is rejected at both persistence boundaries — same shape as test 15 for external releases.

---

## 12. Suggested landing order

Each step is independently shippable and independently testable.

1. **`FormAttestation` table, `Form.Attest`/`RevokeAttestation`, backfill, audit actions.** No
   user-visible change. Every existing completion path routes through the new door.
2. **Delete the note bridge; add the derived pending list.** This is the fix for the reported
   problem and it is subtractive. Tests 1, 7, 11.
3. **Generalize the Reviews attestation control to all twelve form types.** Retires the four
   date-synthesizing paths. Tests 2, 8, 9.
4. **`DocumentArtifact` plus the prerequisite registry**, wired to Agency, DHHS, and Medical
   release generators, including `Origin.Draft`. Tests 3, 14, 15, 19, 20, 21, 23. Narrow
   `PUT /forms/{id}` and fix `OpenFormAsync`. Implemented locally 2026-09-03.
5. **`DocumentTemplate` and the MigraDoc token-merge composer.** Implemented locally 2026-09-03.
   Josh authorized a generic provisional Privacy Practices default while the actual template is
   unavailable. It is visibly marked for review and must be revisited before production use.
   Tests 13, 17. Source format and tokens are documented in `DOCUMENT_TEMPLATES.md`.
6. **`SafetyPlan`: entity, draft/document/submit routes, and the content-to-PDF renderer.**
   Structured content, not a template — its own step because it is the largest single addition
   here. Josh's safety-plan template supplies the section schema `SafetyPlanDocument` is built
   from. Tests 24, 25, 26.
7. **`DocumentAcknowledgment` and the privacy-notice acknowledgment gate.** Small, and only
   possible once step 5 exists to attach it to. Tests 27, 28.
8. **The packet, the profile control, the release/safety-plan reminder, and the manifest.** Tests
   12, 16, 22.
9. **Hash verification** (see O-1) and the records request recipient work (O-4).

Steps 1 through 3 fix the reported defect and remove the code-behind rule. Steps 4 through 8
are the new feature. Step 9 is what keeps the hash from being decoration.

---

## 13. Where I did not do what was asked

**Comprehensive assessment: no warning.** The answer given was "warn but do not block", before
the clarification that Sati's comp assessments are development-only and Evergreen holds the
real ones. Those two together produce a warning that fires on every production consumer,
forever, because Sati will never hold a production comp assessment. A warning with a 100%
false-positive rate trains people to click past warnings, including the real ones. So
`ComprehensiveAssessment` and `PCP` map to `PrerequisiteKind.None` with no warning, and the
registry entry names the future Evergreen check. Say so if you want the warning anyway.

---

## 14. Open questions and risks

**O-1. A hash nothing ever checks is decoration.** Metadata plus SHA-256 was chosen over
metadata-only. But Sati never sees the file again, so the hash proves nothing on its own. It
becomes real with one small addition: a "verify a document against its record" action that
takes a file, hashes it, and reports match or mismatch. That is roughly a day's work and it is
step 9 in the landing order. Without it, drop back to metadata-only rather than shipping a
column that implies verification nobody can perform.

**O-2. Resolved 2026-09-03 — build a `Release_Medical` generator.** Josh chose a distinct Sati-owned
medical release generator. It supports Draft and completed artifacts through the same release
workspace and choice contract as the Agency Release. Regulatory review remains required before
the generated wording is represented as legally or programmatically sufficient.

**O-3. Resolved 2026-09-03 — Comprehensive Assessment is the prerequisite.** Reclassification may
be attested only after the Comprehensive Assessment form in the same compliance cycle is complete.

**O-4. The records request has no delivery channel, and there is an ordering problem.**
`Provider` has no fax field, and records requests commonly go by fax. Adding one is trivial;
the question is whether the letter should be addressed for fax, mail, or a portal. More
importantly, a records request usually has to travel *with* the signed medical release, and the
packet renders the request at T-30 while the release is still waiting on the consumer's
signature. So the letter is produced before it can be sent. The design has the manifest state
that the request requires the signed release attached, but if you would rather the request not
render until the medical release is attested, that is a one-line change to the catalog and
worth deciding now.

**O-5. Resolved 2026-09-03 — Supervisor technical-problem override.** A Supervisor may accept an
attestation with an unmet prerequisite only after entering a reason describing the technical
problem. The reason stays on the attestation ledger row and the action emits
`form.prerequisite-overridden`. Case managers cannot override, and date rules still apply.

**R-1. Resolved 2026-09-03 — was "a blank safety plan template can satisfy a prerequisite."**
The original design put the safety plan in the same template-merge tier as the privacy notice, so
its existence would have proven preparation, not that a safety plan exists for this consumer.
Section 5.3 replaces that with structured authoring: `SafetyPlan` holds real content, and only a
render of a `Final`-status plan satisfies the prerequisite. Kept here, marked resolved, so the
history of why this design has a `SafetyPlan` entity at all isn't lost. Two new questions fall
out of that decision — O-6 and O-7 below.

**O-6. Who owns the safety plan's section schema?** `SafetyPlanDocument` needs a defined set of
sections before anyone can build against it, and this design deliberately doesn't invent one —
that's clinical content, and it's what you said you'd supply as a template. What it doesn't
settle is whether that schema is one Sati-owned structure everyone gets, the same way
`ComprehensiveAssessment`'s question set isn't per-agency, or whether it should be versioned per
agency the way `DocumentTemplate` (4.3) already is for the privacy notice. The assumption in 5.3
is the former — one schema, because a safety plan's structure is more clinical-practice than
agency-legal wording, and because your ask described providing "a template," singular, not a
per-agency-customizable one. Correct this if agencies actually need their own sections.

**O-7. Does the safety plan need supervisor review?** `ComprehensiveAssessment` has
`ReadyForReview`/`Returned`/`Approved` states and an `ApprovedByUserId` column, though the
grep of current call sites shows only the `Draft -> ReadyForReview` transition actually wired up
today — the rest of that lifecycle isn't fully built even for the assessment it was modeled on.
5.3 gives `SafetyPlan` a deliberately smaller lifecycle, `Draft -> Final`, with no supervisor
gate: the case manager finalizes it themselves, the same way they attest every other form type in
this design. If a safety plan should require a second set of eyes before it counts as done, that
is a real scope addition on top of 5.3, not a small one — it's the review workflow
`ComprehensiveAssessment` itself doesn't fully have yet.

**R-2. The packet is smaller than "the annual documents."** At T-30 it renders complete only the
privacy notice and the records request. The safety plan and the two releases render at most as a
Draft — something started, not something finished — and all three still need real work with the
consumer to close out. That follows from what these documents actually are, and is not a
limitation worth engineering around. But the profile control's label and the manifest both need
to say it plainly, or the packet will read as "the annual paperwork is handled" when for three of
the five documents it is closer to "the annual paperwork is started."

**R-3. Retention and legal hold are still outstanding.** `OPERATIONS.md` lists legal-hold and
retention enforcement as unfinished foundation work. `DocumentArtifact`, `FormAttestation`, and
now `SafetyPlan` are new record classes describing PHI, and `SafetyPlan` is the one that holds
actual clinical content rather than metadata about a document — a bigger addition to that
surface than anything else in this design. Not a blocker, but it belongs in `AGENDA.md` against
the retention item rather than being discovered later.

**R-4. Billing behavior changes at step 3.** Retiring the `DueDate` default on the dashboard
checkbox and client page means service dates that were silently billable become blocked when a
completion was genuinely late. That is the correct behavior, and it was flagged as a needed
sweep on 2026-08-31. But it will change real numbers on a real caseload, and it needs a release
note rather than arriving as a surprise.

---

## 15. Documents to update when this lands

- `DECISIONS.md` — evidence versus attestation; the packet window as its own setting;
  append-only attestations with `CompletedDate` as their projection; templates versioned rather
  than edited; the safety plan as structured content reusing `ComprehensiveAssessment`'s shape
  rather than a template merge.
- `AGENDA.md` — remaining items from section 14; close the "no in-app control captures an
  arbitrary completion date for a non-review form" item at step 3.
- `ARCHITECTURE.md` — the new rule owners and the artifact/attestation boundary.
- `API_AUTHORIZATION.md` — one row per new route, and the narrowed `PUT /forms/{id}`.
- `AUDIT_EVENTS.md` — the ten new actions.
- `REGULATORY_CONCERNS.md` — Evergreen integration posture; template wording as an agency legal
  document; whether the Privacy Practices notice needs a tracked signature or acknowledgment
  beyond what `DocumentAcknowledgment` (4.5) already assumes, added to the existing question 12
  list rather than as a separate question.
