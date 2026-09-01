# Handoff — "90-day review marked compliant, flag didn't clear"

**Status:** implemented and tested on 2026-08-31.
Implementation commits: `a4dac74` (refresh cascade), `6400b22` (Reviews attestation),
`bb6a91a` (Local/API date validation). Documentation followed in a separate commit.
**Reported by:** Josh, 2026-08-31. **Investigated against:** `master` @ `51b2341`.

---

## Summary

There are **two independent defects** that both produce the reported symptom, and they
live in different layers. Which one Josh hit depends on *which screen he edited*.
Root cause A is a real mechanism gap and survives a restart. Root cause B is a
notification gap and clears on restart.

**Both are real and both should be fixed.** The approach for each is decided below — no
open product questions remain.

### Implementation result

- All form-compliance paths now converge on one awaited dashboard cascade that refreshes
  checkbox properties, the caseload matrix, and `UpcomingEvents`.
- The Reviews tab presents evidence status and `QnR` attestation status separately. Completion
  requires a blank-by-default actual date and can be reset deliberately.
- `FormCompletionRules` rejects future dates in WPF, Local persistence, and the API. The API
  applies accepted dates through a named server transition rather than assigning the paired
  fields independently.
- No Production query, migration, deployment, or data repair was performed.

**Suggested order:** B first, as its own commit. It is small, unambiguous, and touches
none of A's surface. Landing it separately also means the next time a flag sticks, the
restart test below gives a clean answer instead of an ambiguous one.

### Note for Josh — not an implementation step

**Whoever implements this does not need it, and should not query Production.** Both
defects are fully reproducible and testable in unit tests; see "Tests to add". Nothing
below is a prerequisite for writing the fix.

It is worth Josh confirming the diagnosis on his own machine, because it answers a
question the code fix does not: reproduce, then **restart Sati without changing anything
else**.

- Flag is **gone** after restart → root cause **B**. Nothing is wrong in the stored data;
  it was purely display.
- Flag is **still there** after restart → root cause **A**. Nothing was ever written.

**If it is A, expect an operational backlog.** Every client whose quarterly reviews were
tracked only on the Reviews tab has a `QnR` form that was never attested, and will show
as outstanding until someone closes it. That is not data corruption — the attestation
genuinely never happened — but it does mean the new completion control will land on a
pile of open quarters rather than a clean slate. Worth knowing before release, and worth
a line in the release notes.

---

## Root cause A — the "90-Day Reviews" tab never touches the compliance record

**This is the more likely one, and it is not visual.**

Sati tracks quarterly reviews in two disjoint record systems:

| | Written by | Shape |
|---|---|---|
| `ReviewItem` | The **90-Day Reviews** tab | Per category, per quarter; three stages (Requested → Received → Logged) |
| `Form` (`Q1R`–`Q4R`) | Dashboard checkbox grid, task board, client Overview toggle | One record; done / not-done against a computed due date |

The split is deliberate — `Sati.Persistence/Models/ReviewItem.cs:29` says so explicitly
("Deliberately NOT a Form"). What is missing is any bridge between them.

**Every compliance flag in the app reads `Form`, never `ReviewItem`:**

- Caseload matrix "OVERDUE" cell — `Helpers/FormCellStatusCalculator.cs` via `ViewModels/FormCellViewModel.cs:35`
- Dashboard Q1R–Q4R checkboxes — `ViewModels/CaseManagerDashboardViewModel.cs:499`
- "BILLING COMPLIANCE ATTENTION" banner — `Views/ClientsView.xaml:996` → `BillingComplianceGate.Evaluate`
- Overdue `LateReview` events — `Data/UpcomingEventsService.cs:48` (`if (form is null || form.IsCompliant) continue;`)
- Task board row — `ViewModels/FormTaskRow.cs:47`

**Nothing writes `Form` from the review pipeline.** Verified end to end:

- `ViewModels/ReviewsViewModel.cs:131` → `IReviewItemService.SetStageDateAsync`
- `Data/ReviewItemServices.cs` — **zero** references to `Form`, `FormType`, `MarkComplete`, or `CompletedDate`
- API `PUT /api/v1/reviews/{id}/stage` — `Sati.Api/Endpoints/ApiEndpoints.cs:2022` — writes only the three `ReviewItem` date columns

So a case manager can log every review item for the quarter and the `Q3R` form stays
`IsCompliant = false` forever.

**The UI actively promises otherwise.** The tab is titled "90-Day Reviews"
(`Views/ReviewsView.xaml:577`) and its legend says, of the Logged stage:
*"The date is entered; this quarter's 90-day review is complete."*
(`Views/ReviewsView.xaml:658`). That sentence describes a completion the data model
does not record.

### The decision — agreed with Josh, 2026-08-31

**Keep the two systems separate. Correct the Reviews-tab copy, and surface the quarter's
`QnR` compliance state on that tab with a control to complete it there, capturing a real
date. Do NOT derive `QnR` completion from logged review items.**

The review items are *evidence* — an appointment date, a provider's note, the revised
Goals section. The `QnR` form is the case manager's *attestation that the review
happened*. Those are not the same event, and the gap between them is exactly where a
client is billable-on-paper but not actually reviewed.

Auto-deriving was considered and rejected for three reasons; do not reintroduce it
without going back to Josh:

- **It flips a billing gate on someone else's action.** The last `LoggedDate` is often a
  provider's paperwork arriving. Auto-completing `QnR` means a client becomes billable
  because a fax landed, not because the case manager did the review.
- **The completion date would be synthesized.** `Form.MarkComplete` states that the date
  is "whatever the caller captured ... never synthesized here, because the
  late-vs-on-time distinction (`CompletedDate` vs `DueDate`) is a billing fact the entity
  has no business guessing." `max(LoggedDate)` is precisely that guess.
- **It rewrites history.** `BillingComplianceGate.IsBillingWindowBlocked` is date-keyed
  against `CompletedDate`, so a derived date retroactively changes whether *past* service
  dates were billable.

### What to build

Three pieces. None of them changes how compliance is *decided* — `BillingComplianceGate`
and `Form.MarkComplete`/`Reset` stay the only owners. This is about making the existing
rule visible and actionable from the screen where the work happens.

**1. Fix the copy.** `Views/ReviewsView.xaml:658` — the Logged stage legend currently
reads *"The date is entered; this quarter's 90-day review is complete."* It is not; the
`QnR` attestation is separate. Reword to describe what Logged actually means (the
documentation is in hand and dated) without claiming the review is complete. Check the
surrounding legend text at lines 577–700 for the same implication elsewhere.

**2. Show the `QnR` state per quarter on the Reviews tab.** `ReviewClientRowViewModel`
already resolves the right record — `CurrentQuarterForm` (line 63) via
`Person.GetCurrentCycleForm(QuarterFormType(q), _today)`, which is the same lookup the
matrix and dashboard use. Surface its compliance state on the row and in the detail pane
header, alongside the existing window display. Reuse `FormCellStatusCalculator.Compute`
rather than writing a second status calculation — it is already the owner of
timing → status for forms, and a hand-rolled variant here is the "second copy" defect
`CLAUDE.md` warns about.

Note `ReviewClientRowViewModel.IsOverdue` (line 68) already exists and is bound nowhere.
Either use it or delete it; do not leave a third unused compliance-ish property behind.

**3. Let the case manager complete `QnR` from the Reviews tab.** A command on the row or
detail pane that captures a completion date and routes through `IFormService` →
`Form.MarkComplete(date)` — the same door the dashboard checkbox and task board use. It
must:

- **Capture a real date — blank and required. Do not pre-fill it.** (Revised
  2026-08-31; the earlier draft said to default to `DueDate`, matching
  `NewClientViewModel.ToggleForm:1001`. That was wrong here — see below.) Do not stamp
  `DateTime.Today` silently either.
- **Reject a future date.** `BillingComplianceGate.IsIncompleteAndOverdue` treats
  `completedDate > today` as *still incomplete*, while legacy display code that reads
  `IsCompliant` could show it complete. That would make screens disagree. Validate at
  capture for immediate feedback and at both Local/API persistence boundaries as the
  authority.
- **Go through `Form.MarkComplete`/`Reset`.** No direct writes to `IsCompliant` or
  `CompletedDate`.
- **Refresh through the same single owner as fix B below**, so completing here updates
  the matrix, the dashboard checkboxes, and `UpcomingEvents` — not just the Reviews tab.
- **Take a `LatestRequestTracker` identity** if the completion triggers a reload that
  writes shared UI state, per the engineering rules.

`ReviewsViewModel` currently takes `ISessionService`, `IPersonService`,
`IReviewItemService`, `ISettingsService`. It will need `IFormService` injected via the
constructor — not resolved from a service locator.

#### Why the completion date must not be pre-filled

`CompletedDate` is date-keyed into `BillingComplianceGate.IsBillingWindowBlocked`:

```csharp
serviceDate.Date > dueDate.Date &&
(completedDate is null || serviceDate.Date < completedDate.Value.Date)
```

Setting `CompletedDate = DueDate` requires `serviceDate > dueDate` **and**
`serviceDate < dueDate` simultaneously. The blocking window collapses to empty — nothing
is ever blocked. So the `DueDate` default is not a neutral convenience; it is the
maximally permissive billing answer. If a review was actually completed 20 days late,
those 20 days of service should be blocked, and the default silently makes them billable.

That risk is tolerable on the dashboard checkbox, where no date context is on screen and
the affordance is a quick on-time attestation. It is not tolerable here: the Reviews tab
is the one screen where the real evidence dates (Requested / Received / Logged) are
visible, so there is an accurate answer available and no reason to guess at it. Guessing
would also be a smaller version of the very defect this change fixes — the app asserting
a completion it does not actually know about.

**Do not "fix" the dashboard and client-page defaults as part of this change.** They are
deliberate (`NewClientViewModel.ToggleForm:1001` documents the reasoning) and changing
them alters billing behavior on paths this bug does not touch. The resulting divergence —
Reviews tab requires an explicit date, the checkboxes default to `DueDate` — is
intentional and should be recorded in `DECISIONS.md`, with a note that the checkbox
default is the weaker of the two and a candidate for a later sweep.

The desired end state: the case manager sees "everything is gathered — the Q3
attestation is still open," and closes it deliberately, on the screen where they are
already working, instead of doing the same job twice in two places with no signal
between them.

---

## Root cause B — mark-complete paths under-notify the UI

Real, independent, and much smaller. The write persists correctly; the screen does not
re-read.

### B1. Dashboard checkbox toggle skips the matrix

`ViewModels/CaseManagerDashboardViewModel.cs:615`:

```csharp
if (form.IsCompliant) form.Reset(); else form.MarkComplete(form.DueDate);
await _formService.UpdateFormAsync(form);
RefreshComplianceFlags();          // <- only raises the 12 checkbox properties
```

It never calls `Matrix?.Rebuild(People, DateTime.Today)`. The caseload matrix cell keeps
rendering `OVERDUE` (`ViewModels/FormCellViewModel.cs:35`) because `FormCellViewModel` is
built once per rebuild and holds no change notification.

Compare the sibling paths, which *do* rebuild: `AfterRowStatusChange` (line 686) and
`MarkFormCompleteAsync` (line 870).

**Fix:** add `Matrix?.Rebuild(People, DateTime.Today);` after `RefreshComplianceFlags()`.

### B2. Nothing outside startup reloads `UpcomingEvents`

`UpcomingEvents` is the sole source of the `LateReview` overdue signal
(`OverdueCount` at line 304, `AllEvents` at line 291). `LoadUpcomingEventsAsync` is
called only from `LoadAsync` (startup), the note-delete path, and `OnNoteSavedAsync` —
**never from any form-completion path**:

| Path | Persists | Compliance flags | Matrix | UpcomingEvents |
|---|---|---|---|---|
| Dashboard `ToggleForm` (:615) | yes | yes | **no** | **no** |
| Board `MarkFormCompleted` (:657) | yes | yes | yes | **no** |
| `MarkFormCompleteAsync` (:858) | yes | yes | yes | **no** |
| Client Overview `ToggleForm` (`NewClientViewModel.cs:1001`) | yes | yes | **no** | **no** |

The `FormComplianceChanged` handler at `CaseManagerDashboardViewModel.cs:126` reloads
people and the notes log but rebuilds neither the matrix nor the events, and
`LoadPeopleAsync` (line 790) does not either.

**Fix:** give the dashboard one private `AfterFormComplianceChangedAsync()` that does
`RefreshComplianceFlags()` + `Matrix?.Rebuild(...)` + `await LoadUpcomingEventsAsync()`,
and route all four paths plus the `FormComplianceChanged` handler through it. One owner,
so the next path added cannot forget a step.

Note this is lower-visibility on Josh's own dashboard than it looks — `OverdueCount` is
currently bound only in the supervisor views (`Views/SupervisorDashboardWindow.xaml:548`,
`Views/TeamOverviewView.xaml:182`). The matrix cell (B1) is the flag Josh would actually
see on his own screen.

---

## Tests to add

Per `CLAUDE.md`: **confirm each test fails against the unfixed code before keeping it.**
There is currently no test anywhere in `Sati.Tests` referencing `ToggleForm`,
`RefreshComplianceFlags`, or `UpcomingEventKind` — this whole area is uncovered.

- **A1 (copy):** a `ReleaseUiStructureTests`-style assertion that the Reviews tab's Logged
  legend does not claim the 90-day review is complete. Cheap, and it stops the wording
  regressing.
- **A2 (state shown):** the quarter's displayed compliance state matches
  `FormCellStatusCalculator.Compute` for the same person, type, and date — i.e. the
  Reviews tab and the caseload matrix cannot disagree.
- **A3 (completion):** completing `QnR` from the Reviews tab uses the same sanctioned
  `Form.MarkComplete` transition, with its explicitly captured date, and the tab reflects
  it without a full reload. Include: an explicitly *late* date is stored as given and not
  clamped to `DueDate`; the command is unavailable until a date is chosen (no implicit
  `DueDate` or `DateTime.Today`); and a future date is rejected at capture and at
  both persistence boundaries.
- **A3 (billing consequence):** with a late `CompletedDate`, a service date falling
  between `DueDate` and `CompletedDate` is still blocked by
  `BillingComplianceGate.IsBillingWindowBlocked`. This is the test that would have caught
  the pre-fill mistake — it fails if the date is defaulted to `DueDate`.
- **A (guard):** an assertion that logging review items does **not** change
  `Form.IsCompliant` — the rejected auto-derive behavior, pinned so nobody adds it back
  by accident. This one passes today; keep it as a regression pin and say so in the test
  name, since per `CLAUDE.md` a test that passes either way otherwise reports safety it
  never checked.
- **B1:** after `ToggleFormCommand` on an overdue `Q3R`, the matrix cell for that person
  and type no longer reports `FormCellStatus.Overdue`.
- **B2:** after each of the four completion paths, `UpcomingEvents` contains no
  `LateReview` entry for the completed form.

---

## Adjacent findings — out of scope, flagging only

Not part of this bug; do not bundle them into the same change without asking Josh.

- `Data/Cloud/CloudCoreServices.cs:222`: `CloudFormService.OpenFormAsync` is
  `=> SaveAsync(form)` and never sets `OpenedDate`, while the local
  `Data/FormService.cs:25` does (`form.OpenedDate = DateTime.Today`). "Open form" is
  therefore a silent no-op in Demo when called via
  `CaseManagerDashboardViewModel.OpenFormAsync(FormType)` (line 874), which relies on the
  service to stamp the date. A behavior divergence between the EF and HTTP
  implementations of the same interface.
`ViewModels/Children/ReviewClientRowViewModel.cs:68` (`IsOverdue`, computed but bound
nowhere) is **not** an adjacent finding any more — it is in scope for piece 2 above.
Use it or delete it.

---

## Files to read first

1. `Sati.Persistence/Models/ReviewItem.cs` — the deliberate split, and why it is deliberate
2. `Sati.Persistence/Models/Form.cs` — `MarkComplete`/`Reset` are the only sanctioned writers
3. `ViewModels/Children/ReviewClientRowViewModel.cs` — `CurrentQuarterForm` already resolves the right record
4. `Views/ReviewsView.xaml` (577–700) — the legend copy to correct
5. `ViewModels/CaseManagerDashboardViewModel.cs` — the four completion paths that need one owner
6. `Sati.Contracts/V1/BillingComplianceGate.cs` — the rule owner; read it to confirm nothing here duplicates it
