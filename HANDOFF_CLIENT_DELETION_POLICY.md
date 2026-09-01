# Handoff — Client deletion and archival protocol

**Status:** reviewed and corrected with Josh's authorization, 2026-08-31. The additive
creation/status migration and archival slice are ready to build. Ordinary-client physical
deletion is intentionally blocked until a real legal-hold registry can return an affirmative
clear result. No code changed.
**Investigated against:** `master` @ `51b2341`.

Two decisions here deliberately narrow what Codex may do on its own, because both are the
kind of thing a later change would "helpfully" undo:

- Pre-existing clients are permanently archive-only (A2). No backfill.
- Rule-2 deletion currently blocks on *any* claim line; aligning it with the new
  transmitted-only test (A1) is recommended but is a change to a shipped safeguard —
  confirm with Josh before making it.

---

## Verdict

The protocol is sound. Privileged actor, a bright-line window, an attestation, and a
terminal state that archives rather than destroys — that is the right shape for a
MaineCare-facing record system, and rule 4 (no deletion ever after the window) is more
conservative than most systems in this space. It is consistent with `OPERATIONS.md`,
which already classifies clinical and billing source records as "No automated deletion."

Three things to know before building:

- **Rules 1 and 2 are already implemented.** See "What already exists" below. Do not
  rebuild them. The real work is rules 3 and 4.
- **The window must tolerate messy records.** A person created to try something out will
  normally have notes, maybe an assessment, maybe synthetic billing artifacts. All of
  that is deletable inside the window. The only thing that blocks is billing that
  actually reached a payer — see A1. A design that forced every such record into `Ghost`
  would defeat the purpose of the window.
- **`DECISIONS.md` constrains how the attestation is used.** The existing test-consumer
  decision rejects "attestation alone" because it "asks one click to establish a
  historical fact the application could have recorded at creation." So the attestation
  must not be the *only* control: it is paired with Admin authorization, agency scope, a
  revision check, the creation-time window (A2), the transmitted-billing gate (A1), and a
  legal-hold check (A3). It carries only the one claim the system cannot verify — *this
  is not a real person.*

---

## What already exists — do not rebuild

| Josh's rule | State |
|---|---|
| 1. Admin-only deletion | **Built.** `AdminService.DeleteTestConsumerAsync` requires `UserPermissions.Administration`, re-verified against the database inside a `Serializable` transaction, agency-scoped, with the Person `Revision` required. Mirrored on the API. |
| 2. Test clients deletable at any time | **Built, and deliberately narrower than stated.** `Person.IsTestData` is settable only by an Admin at creation (`PersonService.AddPersonAsync:38`) and immutable thereafter (`EditPersonAsync:87` throws). A versioned attestation lives in `Sati.Contracts.V1.TestDataDeletionRules`. |
| 3. 2-week window for non-test clients | **Not built.** No creation timestamp exists on `Person`. This is the bulk of the work. |
| 4. Archive-only beyond the window | **Not built.** No status or archive concept exists on `Person` at all. |

Read before writing anything:

1. `DECISIONS.md` ~line 1710, "Test-consumer deletion requires a durable creation marker
   and an Admin attestation" — including its **Rejected** paragraph, which constrains the
   design of rule 3.
2. `Data/AdminService.cs:247` — `DeleteTestConsumerAsync`, the pattern to extend.
3. `Sati.Contracts/V1/TestDataDeletionRules.cs` — where the new rules belong.
4. `OPERATIONS.md` lines 10–37 — retention classes and the legal-hold gate.
5. `AUDIT_EVENTS.md` line 131 — the append-only exception already carved for deletion.

Note rule 2 is already narrower than "at any time": a test consumer with billing claim
lines is refused (`TestDataDeletionRules.ConsumerHasClaimsMessage` — "Billing records are
retained even when they were created for testing"). **That existing behavior is correct
and should stay.** Josh's rule 2 as stated is slightly broader than what is built; the
built behavior wins.

---

## Amendments to the protocol as originally stated

All three are settled. They are kept as named sections because each changes or extends
Josh's original four rules, and the reasoning is what should be carried into
`DECISIONS.md`.

### A1. The gate is not "does data exist" — it is "did any of it leave the building"

*Revised 2026-08-31 after Josh's correction. An earlier draft proposed blocking deletion
whenever notes, claim lines, or billing artifacts existed. That was wrong: a record
created to try something out will normally have exactly those, and blocking on them would
force every such record into `Ghost` and accumulate junk that everyone knows is junk.*

**A person created inside the window may be deleted with any number of notes,
assessments, contacts, AT requests, claim lines, and synthetic billing artifacts.** That
is the intended use of the window and the design must support it.

The one category that must still refuse deletion is billing that **actually reached a
payer**. If an 837P was transmitted to MaineCare, deleting the local record does not
unsend it. Sati's books then disagree with the payer's, with no local record of what was
claimed. That is a claims-integrity problem, not a data-hygiene one, and no attestation
can repair it — an Admin's belief that the record was not a real person does not retract
a transmitted claim.

Sati already draws this line in the data, so the gate is machine-checkable rather than a
judgment call. Refuse rule-3 deletion **only** when the person's claim lines belong to a
`BillingPeriod` with any of:

- `BillingSubmissionEvent` where `IsSynthetic == false` and `Stage >= Transmitted`
  (`Generated` is local and does **not** block)
- `RemittanceClaimOutcome` where `IsSynthetic == false`
- `BillingPeriod.SubmittedAt != null` or `Status != BillingStatus.Draft`

Everything else is deletable inside the window, explicitly including `EdiGeneration` rows
with `IsTest == true`, `BillingSubmissionEvent`/`RemittanceClaimOutcome` rows with
`IsSynthetic == true`, and draft claim lines that were never submitted.

Note this **relaxes** the existing rule-2 behavior, which currently refuses to delete a
test consumer whenever *any* claim line exists
(`TestDataDeletionRules.ConsumerHasClaimsMessage`). That message predates the synthetic
flags. Recommend both paths adopt the same transmitted-only test so the two commands do
not disagree about what a claim line means — but that is a behavior change to a shipped
safeguard, so confirm with Josh rather than folding it in silently.

The attestation stays, and now covers only what the system genuinely cannot know: *this
is not a real person.*

### A2. What the window is measured from, and what happens to existing rows

`Person` has **no creation timestamp**. The earliest `PersonVersion` row with
`ChangeKind == "Created"` is close, but `PersonLifecycleLedger.EnsureBaselineAsync`
implies rows can be backfilled, so deriving a destructive gate from it is unsafe.

**Decided (Josh, 2026-08-31):** add an explicit immutable `CreatedAtUtc` to `Person`, set
once at creation, never writable through `EditPersonAsync` — same guard shape as
`IsTestData`, and it needs the same test.

**Decided: every row predating the column is outside the window — archive-only.** The
migration cannot know their real creation date, and guessing one would hand out deletion
rights the data does not support. This matches how the `IsTestData` migration refused to
infer from names or dates. The rejected alternative was backfilling from the earliest
`PersonVersion`, which trades that safety for the ability to delete a handful of recent
mistakes.

Practical consequence to state in the release notes: on the day this ships, **no existing
client is deletable.** The window applies only to clients created after the migration.
That is intended, not a defect — do not add a backfill later to "fix" it without going
back to Josh.

Define the window precisely, server-side only, never from a client-supplied clock:

```
deletable window = CreatedAtUtc.AddDays(14) > DateTime.UtcNow
```

UTC, exclusive at the far end. This decides permission, so per `CLAUDE.md` it belongs in
`Sati.Contracts.V1` with one owner, referenced by both `Sati.Api` and the transitional
desktop-local service.

### A3. Legal hold — fail closed until a real registry exists

`OPERATIONS.md` line 30 requires that "every purge query excludes records covered by an
active hold before selecting deletion targets," and the hold registry does not exist yet.
`DECISIONS.md` explicitly says the test-consumer command "does not create an ordinary-client
deletion policy and does not supersede the unfinished retention/legal-hold work." Rule 3
*is* an ordinary-client deletion policy, so it inherits that obligation.

**Correction approved by Josh, 2026-08-31:** an implementation that always returns "no
hold" is fail-open, not fail-safe. It would make the required gate decorative and permit
irreversible deletion precisely while Sati cannot establish whether a hold exists. Do not ship
that implementation.

Use a result that can represent uncertainty rather than a Boolean that collapses "not checked"
into "clear":

```csharp
public enum LegalHoldStatus
{
    Clear = 0,
    Active = 1,
    Unavailable = 2
}

public interface ILegalHoldRegistry
{
    Task<LegalHoldStatus> GetStatusAsync(int agencyId, int personId);
}
```

The rule-3 transaction may proceed **only** on an explicit `Clear`. `Active`, `Unavailable`,
timeouts, and exceptions all refuse deletion before any child row is changed. An interim
`UnconfiguredLegalHoldRegistry` may return `Unavailable` unconditionally so the seam and refusal
path can be built and tested, but it must never return `Clear` and must never make the deletion
button actionable.

Archive/status work is non-destructive and may ship before the registry. The ordinary-client
physical-deletion command may be implemented behind the fail-closed gate, but it remains
operationally unavailable until a registry-backed implementation can establish `Clear`.
`OPERATIONS.md` and `AGENDA.md` must state that limitation plainly.

---

## Data model

One migration. Additive only.

```csharp
// Person
public DateTime CreatedAtUtc { get; private set; }   // immutable after creation
public PersonStatus Status { get; set; }             // default Active
public string? StatusNote { get; set; }              // optional free text
public DateTime? StatusChangedAtUtc { get; set; }
public int? StatusChangedByUserId { get; set; }
```

```csharp
public enum PersonStatus
{
    Active = 0,
    NoLongerServed = 1,
    Deceased = 2,
    Ghost = 3
}
```

Persisted as `int`, so **do not reorder** — same constraint as `ReviewCategory`.

`Ghost` means a record that should have been deleted inside the window and was not. It is
a data-quality state about a record, not a service fact about a person; the other two are
facts about a real person. Keep them in one enum for simplicity, but every "clients
served" count, report, and clinical or billing surface must exclude `Ghost` explicitly.

Backfill: every existing row → `Active`, and `CreatedAtUtc` per A2.

---

## Archive semantics — define what archived actually does

A status that changes nothing is decoration. Each of these needs an explicit answer and a
test. Recommended behavior for any non-`Active` status:

| Surface | Behavior |
|---|---|
| `PersonService.GetAllPeopleAsync` (`:190`) | Excluded. This is the caseload load path. |
| `EnsureCurrentCycleForms` | **Not generated.** Without this you will produce forms and overdue flags forever for a deceased client. |
| `UpcomingEventService.GenerateEvents` | Excluded. |
| Reviews grid / `ReviewItemGenerator` | Excluded; no new `ReviewItem` generation. |
| Billing eligibility, claim building | Excluded — no new billable work. |
| Supervisor queues and dashboards | Excluded from active counts. |
| Compliance gate / overdue reporting | Excluded. |
| Admin views, audit, existing records | **Fully retained and readable.** Nothing is deleted. |

Existing notes, forms, assessments, claims, and `PersonVersion` rows are untouched by
archival. Archive is a visibility and work-generation change, not a data change.

**Un-archiving:** `NoLongerServed` → `Active` is routine in case management and should be
an ordinary Admin action, audited. `Deceased` and `Ghost` → `Active` should use the same
command but is worth a distinct audit action, since reversing either implies the original
classification was wrong.

**Who may archive — decided (Josh, 2026-08-31):** rule 1's Admin-only requirement covers
deletion, not archival. Archival is non-destructive and routine, so a case manager may set
`NoLongerServed` or `Deceased` on a client in **their own caseload**. Only an Admin may
set `Ghost`, because that status asserts the record is not a real person — the same claim
the deletion attestation makes, and it should not be reachable at a lower privilege than
deletion itself.

Gate the caseload restriction server-side on the actor's own `UserId` and agency, the way
`TenantAccess` handles every other caller-supplied scope value. Test that a case manager
cannot archive outside their caseload and cannot set `Ghost` at all.

---

## Where the gates live

Per `CLAUDE.md`, anything deciding permission or record status has one owner in
`Sati.Contracts.V1`, referenced by both the desktop client and `Sati.Api`. Extend
`TestDataDeletionRules` (or add a sibling `ConsumerDeletionRules`) with:

- the window calculation from A2
- the content-gate predicate from A1, taking a counts record rather than a `DbContext` so
  it stays pure and testable
- a new versioned attestation constant for the rule-3 path, distinct from
  `ConsumerAttestation` — an older client must not be able to invoke the newer, broader
  command
- the user-facing refusal messages

Both the API route and the transitional desktop-local `AdminService` repeat every check.
Do not rely on the API being the only caller.

The rule-3 delete itself should extend the existing `DeleteTestConsumerAsync` transaction
shape: `Serializable`, revision-checked, same child-record sweep, same
`peopleDeleted != 1` guard.

---

## Audit

The existing `test-data.consumer-deleted` action stays as-is for rule 2. Add:

| Action | When |
|---|---|
| `consumer.deleted-in-window` | Rule-3 deletion |
| `consumer.archived` | Any `Active` → non-`Active` transition |
| `consumer.unarchived` | Any non-`Active` → `Active` transition |

`AuditEvent` has no foreign key to `Person` (`ResourceId` is a string), so these survive
the deletion — that property is load-bearing and must not regress.

The rule-3 event must be a **tombstone**: it is the only remaining evidence the record
existed. Josh's requirement is that nothing is eliminated entirely from the record, and
because A1 now permits deleting a person who had notes and billing artifacts, the
tombstone carries more weight than the rule-2 one. It must be an **itemized inventory**,
not just counts.

Record, in `MetadataJson`:

- person id, `CreatedAtUtc`, deletion timestamp, deleting Admin, attestation version
- the free-text reason the Admin gave
- per **note**: id, event date, status, units, note type — **not** the narrative
- per **claim line**: id, date of service, procedure code and modifier, units, charge
  amount, billing period id — **not** `ClientMaineCareId`
- per **form / review item / assessment / AT request / contact / `PersonVersion`**: id and
  type, plus due or created date where one exists
- the billing-integrity check result from A1, and what it found

That is enough to prove exactly what existed, reconstruct the shape of the record, and
reconcile against any downstream system.

#### What the tombstone must not contain, and why

**Do not copy note narratives, journal text, the consumer's name, `MaineCareId`,
`EvergreenId`, birth date, or address into the audit event.** "Nothing is eliminated from
the record" means the record *of what happened* is complete and immutable — not that the
content survives deletion. The distinction is load-bearing:

- `AUDIT_EVENTS.md` and the `AuditEvent` retention class in `OPERATIONS.md` state that the
  ledger "intentionally excludes narrative PHI." Copying content in reverses a documented
  design decision for every consumer, not just deleted ones.
- It would defeat the deletion. If an Admin ever deletes a record that *was* a real
  person, the PHI would survive in a ledger with 7-year retention, different access
  controls, and no `PersonId` foreign key — unfindable by any future retention, legal-hold,
  or subject-access process.
- The content has value only in the case where the attestation was false. That case needs
  incident response, not a quiet PHI copy.

**Confirmed with Josh, 2026-08-31: note content is not retained.** The itemized inventory
above is the whole requirement — this is a settled decision, not an open question. If
retaining content is ever revisited, it is a separate archival store with its own access
control, retention class, and legal-hold coverage; it does not belong in the audit
ledger.

Two UI requirements, because confirmation is evidence rather than security
(`CLAUDE.md`: do not use UI visibility as security):

- Show the actual record counts before confirming, and record the same numbers in the
  audit metadata — so the attestation is falsifiable after the fact.
- Require typing the client's name to confirm, not clicking OK. Standard destructive-action
  practice, and it defeats muscle memory built up on the rule-2 path.

---

## Environments

The rules apply identically in Demo and Production. Do not loosen the destructive path by
environment: Demo is where Admins build the habits they will use in Production, and an
environment-conditional destructive gate is how a Production accident happens. Demo bulk
cleanup is the nightly reset's job (still unconfigured — see `AGENDA.md`), not per-client
deletion.

---

## Tests

Per `CLAUDE.md`, **confirm each security test fails against the unfixed code before
keeping it.** Add alongside the existing `AdminTestDataDeletionTests`.

Authorization and tenancy:
- A non-Admin cannot invoke rule-3 deletion or set `Ghost`.
- An Admin cannot delete or archive a person in another agency.
- A stale `Revision` is rejected.

Window (A2):
- Deletion succeeds at day 13, is refused at day 15, from `CreatedAtUtc` in UTC.
- `CreatedAtUtc` cannot be changed through `EditPersonAsync` — mirrors the existing
  `IsTestData` immutability test.
- A row with a backfilled/absent creation date is archive-only.

Legal hold (A3):

- `Active` and `Unavailable` each refuse deletion before any child record changes.
- A registry timeout or exception refuses deletion before any child record changes.
- Only an explicit registry-backed `Clear` permits the remaining deletion gates to run.

Billing-integrity gate (A1):
- **The permissive cases, which are the point of the window.** A person with notes in
  every status, a Comprehensive Assessment, AT requests, draft claim lines, an
  `EdiGeneration` with `IsTest == true`, a `BillingSubmissionEvent` with
  `IsSynthetic == true`, and a `RemittanceClaimOutcome` with `IsSynthetic == true` is
  **deletable** inside the window. Assert each of these does not block, individually and
  together.
- `BillingSubmissionStage.Generated` with `IsSynthetic == false` does **not** block —
  generation is local.
- Each of the three transmitted-billing conditions independently **refuses** deletion,
  even inside the window and even with a valid attestation.
- The refusal message names the billing period and stage, so an Admin can tell why.

Archive semantics:
- An archived person is absent from `GetAllPeopleAsync`, generates no forms via
  `EnsureCurrentCycleForms`, produces no `UpcomingEvent`, and produces no `ReviewItem`.
- Archiving destroys nothing: notes, forms, assessments, claim lines, and `PersonVersion`
  counts are unchanged before and after.
- `Ghost` is excluded from clients-served counts.

Audit:
- A rule-3 deletion leaves a `consumer.deleted-in-window` event that survives the person
  row and itemizes every destroyed record by id and type.
- **The exclusion test, which must fail against a naive implementation:** delete a person
  whose notes contain a known sentinel string in `Narrative` and whose profile carries a
  known `MaineCareId` and name, then assert neither string appears anywhere in the
  resulting `AuditEvent.MetadataJson`. This is the test that stops a well-meaning "record
  everything" change from turning the audit ledger into a PHI store.
- The inventory round-trips: the counts implied by the itemized lists match the counts
  returned by the deletion command.

---

## Docs to update

- `DECISIONS.md` — a new entry for the ordinary-client policy, explicitly extending the
  existing test-consumer entry and superseding its "does not create an ordinary-client
  deletion policy" sentence.
- `OPERATIONS.md` — add the deletion window and archive statuses to the retention classes
  table, and state plainly that rule-3 deletion remains unavailable until the registry returns
  an affirmative `Clear` (A3).
- `API_AUTHORIZATION.md` — the new routes, per the standing rule.
- `AUDIT_EVENTS.md` — the three new actions and the tombstone contract.
- `AGENDA.md` — the legal-hold registry, now with a second caller depending on it.

---

## Suggested sequencing

1. Migration + `CreatedAtUtc` immutability + `PersonStatus` (additive; nothing uses it yet).
2. Archive: status transitions, audit, and the exclusion behavior above. Non-destructive,
   independently useful, and it gives rule 4 a landing place.
3. Rule-3 deletion last, once the window, content gate, fail-closed hold seam, and a real
   registry-backed `Clear` result are all in place. The unconfigured implementation must keep
   physical deletion unavailable.

Landing 2 before 3 matters: until archive exists, an Admin facing a bad record past the
window has no correct action available, and pressure to loosen the deletion path is
exactly the failure mode this protocol is meant to prevent.
