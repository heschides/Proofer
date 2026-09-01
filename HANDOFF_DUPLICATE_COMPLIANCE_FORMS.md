# Handoff — "Q1 review is checked and the billing gate still blocks the client"

**Status:** diagnosed, not fixed. No repair has been written and no data has been changed.
**Reported by:** Josh, 2026-09-01. **Investigated against:** `master` @ `95b3b59`.
**Evidence:** `scripts/Diagnose-BillingGateDisagreement.sql`, run against `SatiProduction`
on the other Windows login, 2026-09-01.

---

## Summary

**There are three `Q1R` rows for that client, all due 2026-08-28. One is complete. Two
are not. The checkbox reads one row; the billing gate reads all three.**

Neither reader is wrong, and neither is showing stale state. This is a data defect that
`master` no longer produces but never cleaned up, and it is table-wide: **492 duplicated
forms across 25 of 26 clients, every one of them triplicated.**

The reported symptom — checkbox checked, "BILLING COMPLIANCE ATTENTION" banner, red
caseload card, all three surviving a restart and immune to toggling the checkbox — is
fully explained by this and requires no other defect.

### What this is NOT

Do not spend time on either of these; both were investigated and excluded:

- **Not a refresh/notification gap.** It survives a restart and the values on screen are
  a faithful rendering of what is stored. The refresh cascade from `a4dac74` is working.
- **Not the `IsCompliant` / `CompletedDate` split.** That divergence is real and is
  written up in "Adjacent defect" below, but it is not what produced this client's
  banner: all three of their `Q1R` rows are internally consistent.

---

## The evidence

Person 1056, `EffectiveDate` 2026-05-30, so the current cycle is 2026-05-30 → 2027-05-30
and `Q1R` is due `cycleStart + 90` = 2026-08-28.

| FormId | Type | DueDate | IsCompliant | CompletedDate |
|---|---|---|---|---|
| 9371 | Q1R | 2026-08-28 | 0 | NULL |
| 9413 | Q1R | 2026-08-28 | 0 | NULL |
| **9455** | **Q1R** | **2026-08-28** | **1** | **2026-08-28** |

`Person.GetCurrentCycleForm` (`Sati.Persistence/Models/Person.cs:308`) filters to the
current cycle, then `OrderByDescending(f => f.DueDate).FirstOrDefault()`. All three due
dates are identical, so the ordering is a three-way tie and it returns whichever row EF
materialized first — 9455. Every `IsCompliant`-based reader therefore reports complete:
the Overview checkbox (`ViewModels/NewClientViewModel.cs:543`), the caseload matrix
(`Helpers/FormCellStatusCalculator.cs:26`), the task board (`ViewModels/FormTaskRow.cs:47`)
and the overdue events (`Data/UpcomingEventsService.cs:50`).

`Person.EvaluateComplianceGate` (`Sati.Persistence/Models/Person.cs:447`) projects **every
row in `Person.Forms`** into the snapshot — no cycle filter, no de-duplication. It sees
9371 and 9413, both overdue and incomplete, and `BillingComplianceGate.Evaluate` emits a
reason for each. The two reasons are byte-identical, so the `Distinct` at
`Sati.Contracts/V1/BillingComplianceGate.cs:73` collapses them into the single bullet on
screen.

**That is also why toggling the checkbox does nothing.** `ToggleFormForAsync`
(`ViewModels/NewClientViewModel.cs:1013`) resolves the form through
`GetCurrentCycleForm`, so every toggle writes 9455 — the copy that was already complete.
9371 and 9413 are unreachable from any screen in the application.

### Scale

From result sets 1–3 of the diagnostic:

| Measure | Value |
|---|---|
| Clients | 26 |
| Rows in `dbo.Forms` | 1,788 |
| Duplicated `(PersonId, Type, DueDate)` groups | 492 |
| Copies per duplicated group | **3, in every single group** |
| Surplus rows | 984 |
| Clients affected | 25 of 26 |
| Groups blocking billing *today* | 2 (person 1044 `ComprehensiveAssessment`, person 1056 `Q1R`) |
| Rows where `IsCompliant` and `CompletedDate` disagree | 147 (separate defect — see below) |

Person 1056's 2025-cycle forms (`Id` 8276–8290) are **single** copies. Every
current-cycle and next-cycle form is tripled. That pattern — prior cycle clean, current
and next cycle ×3 — is the signature of `EnsureCurrentCycleForms`, which creates exactly
those two cycles and nothing else.

**The copies have diverged.** Same person, `ComprehensiveAssessment` due 2027-01-30:

| FormId | IsCompliant | CompletedDate |
|---|---|---|
| 9376 | 0 | NULL |
| 9418 | 1 | 2026-06-22 |
| 9460 | 1 | NULL |

Edits landed on whichever copy the UI surfaced at the time. This is why the repair cannot
be mechanical — see "Piece 2".

---

## Root cause

`Data/PersonService.cs:190` `GetAllPeopleAsync` creates its own `SatiContext` from
`IDbContextFactory`. Before `57af6fa` (2026-07-24) it ran, unconditionally, on every
caseload load:

```csharp
foreach (var person in people)
    if (person.EnsureCurrentCycleForms(today, settings))
        anyChanges = true;

if (anyChanges)
    await context.SaveChangesAsync();
```

`Person.AddMissingFormsForCycle` (`Sati.Persistence/Models/Person.cs:404`) is idempotent
against **its own in-memory `Forms` collection** — `Forms.Any(f => f.Type == type && ...)`
— and idempotent against the database only if no other writer is in flight. Nothing
serialized those writers and there is **no unique constraint on
`(PersonId, Type, DueDate)`**, so it is a plain read-modify-write race:

1. Three startup loads run concurrently, each on its own `DbContext`.
2. All three read `Forms` before any of them writes; all three see the cycle forms missing.
3. All three add a full 12-type set for the current cycle and another for the next.
4. All three call `SaveChangesAsync`. The database accepts all three.

Exactly three concurrent loaders, exactly three copies, in all 492 groups.

`57af6fa` is titled *"fix: serialize startup loads to avoid LocalDB RESOURCE_SEMAPHORE
stalls"* — the same commit both serialized the startup loads and introduced
`EnableEnsureCycleFormsOnLoad = false` (`Data/PersonService.cs:10`), gating the write off
entirely. So the mechanism was closed on 2026-07-24. **The rows it had already written
were never cleaned up, and that is the whole of the current bug.**

### Why it only surfaced now, and why it will keep surfacing

A duplicate is invisible until one copy ages past its due date. Person 1056's `Q1R` came
due 2026-08-28 — four days before the report. Their `Q2R` triplet is due 2026-11-26,
`Q3R` 2027-02-24, `Q4R` 2027-05-25, and the same is true for the other 24 clients.

**Untouched, this produces a fresh false billing block every quarter, per client, for
years.** It will also block real billing: `BillingComplianceGate.IsBillingWindowBlocked`
is date-keyed, so service dates in the gap between an unreachable copy's due date and a
completion that will never arrive are treated as non-billable.

---

## What to build

Four pieces. Piece 1 is the only one that makes recurrence impossible; piece 2 is the only
one that fixes the client in front of Josh. Neither substitutes for the other.

**Suggested order: 3, then 2, then 1.** Piece 3 is small, independent, and stops a second
source of duplicates before the cleanup runs. Piece 1 must come after piece 2 because the
index cannot be created while duplicate rows exist.

### Piece 1 — a unique constraint, so this is structurally impossible

Add a migration creating a unique index on `dbo.Forms (PersonId, Type, DueDate)`.

This is the durable fix. `AddMissingFormsForCycle`'s existence check is a
check-then-insert with no protection between the check and the insert; only the database
can close that. `Form.Type` is `nvarchar(max)` in the current model
(`SatiContextModelSnapshot.cs:720`) and **cannot be indexed** — the migration must narrow
it to a bounded `nvarchar` first. The longest value is `ComprehensiveAssessment` at 23
characters; size it with headroom and pin it in the model configuration.

Then make the insert path survive losing the race: catch the unique-violation
`DbUpdateException` in `GetAllPeopleAsync`'s save, discard the losing insert, and re-read.
A crash on a benign concurrent write is not an acceptable trade for the constraint.

Note `EnableEnsureCycleFormsOnLoad` is currently `false`, so nothing calls this path today
— but `ViewModels/SettingsViewModel.cs:240` documents an intent to lift that guard once
the due-date backfill is done. **Do not lift it before the unique index exists.**

### Piece 2 — repair the existing rows

`scripts/Report-DuplicateComplianceForms.sql` (read-only, committed with this handoff)
produces the merge plan. Its result set 3 classifies every duplicated group as `AGREE`
(all copies hold identical state — any survivor is equivalent) or `CONFLICT` (the copies
hold different completion dates or disagree on `IsCompliant`).

**Do not collapse on lowest `Id`.** For person 1056's `ComprehensiveAssessment` the lowest
`Id` is 9376, which is blank, while 9418 carries a real attestation of 2026-06-22.
Collapsing mechanically would destroy it.

Build the repair on `Data/FormBulkCompletion.cs`'s discipline, which is the house pattern
for one-time maintenance and already correct: two-phase, dry-run latch pinning both the
count and the parameters, commit refuses unless the dry run ran this session and the
numbers match, full report written to a file.

Rules the repair must follow:

- **`AGREE` groups collapse automatically.** Keep one row, delete the rest.
- **`CONFLICT` groups must not be collapsed by the tool.** Report them and stop. Which
  `CompletedDate` survives decides whether past service dates were billable
  (`BillingComplianceGate.IsBillingWindowBlocked`), which makes it a billing decision, not
  a mechanical one. Josh chooses; the tool records the choice.
- **Write compliance state only through `Form.MarkComplete`/`Reset`** on the survivor.
  Never assign `IsCompliant` or `CompletedDate` directly.
- **Preserve `OpenedDate`** — take the earliest non-null across the group.
- **Do not touch the 2025-cycle rows.** They are single copies and correct.
- **Emit an `AuditEvent` per deleted row.** This is a bulk delete of billing-relevant
  records in a system whose whole posture is evidentiary; `AUDIT_EVENTS.md` governs.

Deleting is FK-safe: the model snapshot shows `Form` has exactly one relationship
(`Form → Person`) and nothing references `Form.Id`.

### Piece 3 — close the second duplication path

`ViewModels/NewClientViewModel.cs:917` is still live and creates duplicates the same way:

```csharp
var forms = Person.GenerateFormList(effectiveDate.Value, settings);
existing.Forms = forms;
```

`GenerateFormList` returns 12 brand-new `Form` objects with `Id == 0`. `EditPersonAsync`
(`Data/PersonService.cs:97`) then calls `context.People.Update(person)` on a detached
graph, so EF marks those children `Added` and inserts all 12. The rows already in the
database are not in the graph, so nothing removes them.

The guard `wasNoWaiver && isAddingWaiver` makes this rare — it fires only when a client
moves from no waiver to a waiver — which is likely why it has not compounded the damage.
It is still wrong. Reconcile against the stored forms instead of replacing the collection,
or delete the superseded rows explicitly in the same transaction.

### Piece 4 — decide what the gate should read

Independent of the repair, `EvaluateComplianceGate` iterating **every** row in
`Person.Forms` with no cycle filter is questionable on its own terms. It is why an
unreachable duplicate can block billing, and it also means a genuinely stale prior-cycle
form blocks forever with no screen that can clear it.

Once the unique index exists the duplicate case is gone, so this is not urgent — but it
should be a deliberate decision recorded in `DECISIONS.md` rather than an accident of
which collection was in scope. Ask Josh before changing it: narrowing the gate to the
current cycle would stop old outstanding documents from blocking, and that may be exactly
the behaviour he wants to keep.

---

## Adjacent defect — `IsCompliant` vs `CompletedDate` (real, separate, latent)

**147 rows already hold a state where the two readers disagree**, and this defect is
independent of the duplication. It did not cause the reported bug, but it will cause an
identical-looking one later.

The compliance checkbox, matrix, task board and overdue events read `Form.IsCompliant`.
`BillingComplianceGate` never sees that field — `EvaluateComplianceGate` projects
`(Type, DueDate, CompletedDate)` and decides on the date alone. So `IsCompliant = 1` with
a NULL or future `CompletedDate` renders checked and blocks billing, exactly like the
duplicate did.

The invariant is enforced by convention in `Form.MarkComplete`/`Reset`, and by
`PersonSaveRules.cs:140` — but **only for `Id == 0` forms at person creation**. Nothing
checks it on update, and there is no CHECK constraint.

The 147 rows are not corruption; they are the documented generation exception at
`Sati.Persistence/Models/Form.cs:26`. `AddMissingFormsForCycle` creates annual non-review
documents with `isCompliant: true` and no date, on the reasoning that a cycle started
because those documents were signed. Those rows are harmless *until their due date
passes* — at which point the gate starts reporting them incomplete while every other
screen shows them done. For person 1056 that is 2027-05-30.

The fix that makes it unrepresentable is to derive it: `IsCompliant => CompletedDate.HasValue`,
`[NotMapped]`, column dropped, and the generation constructor's `isCompliant` parameter
removed. Then there is one field and the readers cannot disagree. That changes what a
"born compliant" annual document means, so it needs Josh's sign-off before the schema
moves.

---

## Tests to add

Per `CLAUDE.md`: **confirm each fails against the unfixed code before keeping it.**

- **Duplicate blocks a completed form (the reported bug).** A person with three `Q1R`
  rows at the same due date, one complete and two not: `GetCurrentCycleForm(Q1R).IsCompliant`
  is `true` **and** `EvaluateComplianceGate` returns a blocking reason. Fails against the
  unfixed data shape; the assertion to keep afterwards is that the shape is unreachable.
- **Concurrency (piece 1).** Two `EnsureCurrentCycleForms` + `SaveChangesAsync` sequences
  against the same person on separate contexts, interleaved so both read before either
  writes. Before the index: two full sets. After: one set, and the loser's
  `DbUpdateException` is swallowed and re-read rather than surfacing.
- **Repair merge rule (piece 2).** A `CONFLICT` group is left untouched and reported. An
  `AGREE` group collapses to one row whose `CompletedDate` and `OpenedDate` match the
  input. A deleted row produces an `AuditEvent`.
- **`GenerateFormList` on an existing person (piece 3).** Adding a waiver to a client who
  already has forms does not increase the row count for any `(Type, DueDate)`.
- **Adjacent defect.** A `Q1R` with `IsCompliant = true, CompletedDate = null` makes
  `Q1RCompliant` return `true` while `EvaluateComplianceGate` returns a reason for the
  same record. Fails today. Nothing in `Sati.Tests` pins this.

---

## Operational note for Josh — not an implementation step

**Whoever implements this does not need Production access.** Every piece is reproducible
in unit tests against synthetic data. The diagnostic has already been run and its findings
are transcribed above.

Two things worth knowing before the repair runs:

- **Some past service dates were wrongly blocked.** Any note whose service date fell in
  the gap between an unreachable copy's due date and a completion that never arrived was
  treated as non-billable by `IsBillingWindowBlocked`. After the repair those dates become
  billable again. That is a re-billing question, not a code question.
- **Expect `CONFLICT` groups to need real decisions.** The copies diverged over roughly
  two months of editing. Each conflict is "which of these two completion dates is the one
  that actually happened," and only Josh can answer it.

No Production query, migration, deployment, or data repair was performed as part of this
investigation. The diagnostic script is read-only and returns no names, narratives, birth
dates or identifiers.

---

## Files to read first

1. `Sati.Persistence/Models/Person.cs:308` — `FindCurrentCycleForm`, the tie-break that picks one copy
2. `Sati.Persistence/Models/Person.cs:404` — `AddMissingFormsForCycle`, the unguarded check-then-insert
3. `Sati.Persistence/Models/Person.cs:447` — `EvaluateComplianceGate`, which iterates every form
4. `Data/PersonService.cs:190` — `GetAllPeopleAsync` and the disabled flag at line 10
5. `git show 57af6fa -- Data/PersonService.cs` — the commit that closed the mechanism
6. `Data/FormBulkCompletion.cs` — the house pattern for a two-phase one-time repair
7. `scripts/Report-DuplicateComplianceForms.sql` — the merge plan the repair consumes
