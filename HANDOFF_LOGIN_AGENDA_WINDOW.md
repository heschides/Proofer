# Handoff — Login agenda window

**Status:** implemented, tested, documented, committed, and pushed 2026-09-01.
**Investigated against:** `master` @ `51b2341`.

> **Structured successor implemented 2026-09-05.** The original implementation below appended
> selected items as scratchpad text. Today's Work now displays Scheduled notes by type, and sign-in
> selections create Scheduled Form notes through `WorkAgendaService`. The once-daily cadence and
> recommendation rules remain current; descriptions of text-line insertion are historical.

---

## What Josh asked for

An attractive, theme-matched window on login with a greeting — *"Hello [username]. I have
prepared a list of items for you to work on today. Please select from the following to add
them to your agenda for the day."* — listing 5–10 upcoming items. Selected items are added
to Today's Work. Twenty phrasings of the greeting, chosen at random per login, to keep it
fresh. Plus a pleasant empty-state message, also with permutations, for a user without
enough client data.

Extended 2026-08-31 with two additions:

- **A lookback.** Surface overdue items and invite the case manager to either do the work
  or record a completion whose status is out of date.
- **A quiet-period recommendation.** When nothing is coming due for a while, say so and
  point at the next due-but-incomplete Comprehensive Assessment — OADS expects CAs to be
  worked throughout the year, and the due date is a no-later-than, not a start date.

## Verdict

Reasonable. Two things to check before building, one of which changes what the feature is.

### 1. "Today's Work" is a free-text box, not a task list

`Today's Work` is `ScratchpadViewModel.ScratchpadContent` — a plain `string` bound to a
`TextBox` in the shell's rail panel (`Views/ScratchpadView.xaml:211`,
`Views/ShellWindow.xaml:255`). It is not a structured task collection.

So "add to Today's Work" means **appending lines of text**. The added items will not be
checkboxes, will not track completion, and will not link back to the form or review they
came from. Marking one done means deleting the line.

That is a perfectly good feature and it matches how the scratchpad is already used — but
it is worth being explicit, because the phrase "added to my agenda" can suggest something
more structured.

**Decided (Josh, 2026-08-31): text lines for now, something more structured later.**

Two consequences for how this gets built:

- **Do not build for the future version.** No task abstraction, no hidden markers, no
  machine-readable payload smuggled into the scratchpad text. Speculative generality is
  its own defect, and a text box that users freely edit cannot carry reliable structure
  anyway — lines get reworded, merged, and deleted, which is entirely correct behavior for
  a scratchpad. A future structured feature will start from its own entity, not from
  parsing this text.
- **Do keep the line formatting in one named method,** so that when the structured version
  arrives there is a single call site to redirect rather than formatting logic spread
  through the window. That costs nothing today and is the only concession worth making.

Record the structured version in `AGENDA.md` as deferred work, per the engineering rules,
so the intent survives this handoff.

### 2. Show once per day, not once per login

A modal on *every* login becomes friction fast — re-authentication, user switching
(`SwitchUserViewModel`), and restarts all trigger it. Recommend persisting a last-shown
date per user and showing the window at most once per calendar day, with an obvious
**Skip** that is never a trap. Josh to confirm the cadence.

---

## Placement in startup — this ordering is load-bearing

`App.xaml.cs:301-331` runs: splash → `LoginWindow.ShowDialog()` → `session.SetUser` →
`shellVm.InitializeAsync()` → `shellWindow.Show()`.

`ShellViewModel.InitializeAsync` calls `Scratchpad.InitializeAsync()` (`:219`), which
**loads and overwrites `ScratchpadContent`** from storage.

**Show the agenda window after `shellWindow.Show()`.** If it runs between login and shell
init, every appended line is destroyed when the scratchpad loads. This is the single most
likely way to build this feature wrong, and the symptom — selections silently vanishing —
looks like a save bug rather than an ordering bug.

Appends must go through `ScratchpadViewModel.ScratchpadContent`, never the `Scratchpad`
model directly, so the existing dirty-tracking (`_lastSavedScratchpadContent`), autosave
timer, and conflict handling all still work.

---

## Behavior — three sections, not four exclusive states

The lookback changes the shape of this window. Overdue work and upcoming work can both
exist, so the window renders up to three sections and the greeting varies by which are
present:

| Section | Shown when | Source |
|---|---|---|
| **Needing attention** (lookback) | Any incomplete overdue form exists | The `IsIncompleteAndOverdue` predicate — see below |
| **Coming up** | Any event falls in the forward window | `IUpcomingEventService.GenerateEvents` |
| **Suggested work** | Both sections above are empty | Next due-but-incomplete Comprehensive Assessment |

Greeting selection: no clients → **Set B**. Overdue present → **Set D**. No overdue but
upcoming present → **Set A**. Neither, with a CA available → **Set E**. Neither, with no CA
→ **Set C**.

### The lookback needs a different source than the upcoming list

`UpcomingEventService.GenerateFormEvents` cannot supply it. It skips anything past
`dueDate.AddDays(daysAfterDue)`:

```csharp
if (today < openDate || today > lateDate)
    continue;
```

That is exactly backwards for a lookback — the *most* overdue items are the ones it drops.
Use the predicate that already owns "incomplete and overdue" with no upper bound,
`BillingComplianceGate.IsIncompleteAndOverdue`:

```csharp
dueDate.Date < asOfDate.Date && (completedDate is null || completedDate.Value.Date > asOfDate.Date)
```

Expose it through a shared helper rather than re-typing the comparison — it is currently
`private` in `BillingComplianceGate`. Making it public (or adding a small
`OverdueForms(person, today)` alongside it) keeps one owner, per `CLAUDE.md`.

**Scope — decided (Josh, 2026-08-31): all incomplete overdue forms, regardless of type.**

Note this is deliberately *wider* than `BillingComplianceGate.Evaluate`, which filters to
types enabled in the agency's `BillingComplianceRequirements` and by default excludes
Privacy Practices and the three releases. Use the `IsIncompleteAndOverdue` predicate
directly across all form types — do **not** route through `Evaluate`, which would silently
drop them.

Because the list now mixes gate-blocking and non-blocking items, **mark the ones that
additionally block billing.** Without that cue, a Privacy Practices form and an overdue
`Q3R` look equally urgent, and they are not. Use a text or glyph marker, not color alone.

### The backlog problem — this is the important one

The 90-day review handoff records that clients whose reviews were tracked only on the
Reviews tab have `QnR` forms that were never attested. Shipping a lookback immediately
after that fix can open a case manager's first login with *"you have 147 overdue items."*
That is demoralizing and useless.

Requirements:

- **Show at most 5.** Same scale as the upcoming list.
- **State the true total separately** — "showing 5 of 147" — so the number is honest
  without the list being punishing.
- **Order oldest-due first** (decided, Josh 2026-08-31). Those carry the most risk, and
  with all form types in scope the list is longer, so the five shown need to be the five
  that matter most.
- **No bulk completion. Ever.** Each row links to where that form can be attested with a
  real date. `Data/FormBulkCompletion.cs` exists but is explicitly *"TEMPORARY one-time
  maintenance"* behind a dry-run latch and marked for deletion — do not reuse it,
  generalize it, or model anything on it.

That last point matters because Josh's phrasing — "mark them as complete if their current
status is outdated" — naturally invites a bulk action. Bulk-marking with a defaulted date
is the exact hazard settled in the 90-day handoff: `CompletedDate` is date-keyed into
`IsBillingWindowBlocked`, and a defaulted date silently collapses the blocking window.
The lookback **surfaces and navigates**; it never writes compliance state itself.

### The Comprehensive Assessment recommendation

Josh's rationale: OADS expects CAs to be worked throughout the year, and the due date is a
no-later-than rather than a start date. So a quiet stretch is the right time to make
progress on one.

**Selection — keep it cheap.** Do not deserialize every client's `DocumentJson` at login.
Pick by due date from the `Form` records (`FormType.ComprehensiveAssessment`, incomplete,
soonest due), then load only that one client's `ComprehensiveAssessment` to show progress.

**Show progress, because that is what makes it actionable.** The editor already computes
it: `ComprehensiveAssessmentViewModel.CompletionText` renders `"{AnsweredCount} of
{TotalCount} questions addressed"`, where an answer counts once its
`AssessmentAnswerStatus` is anything other than `NotYetAnswered`. Reuse that calculation
rather than writing a second one. A client with no assessment started yet is a perfectly
good recommendation — render it as "not started."

**Select on the `Form`, not the assessment's `Status`.** A `ComprehensiveAssessment` can be
`Approved` while its `Form` is unattested — the same evidence-versus-attestation split
settled for `QnR`. Use the `Form`'s `CompletedDate is null` for selection, show the
assessment's status and progress as context, and **do not** auto-complete the form from the
assessment status. Same reasoning, same answer.

### Turning it off — decided (Josh, 2026-08-31)

A setting that disables the window entirely.

#### It must NOT go in `Settings`

`Sati.Persistence/Models/Settings.cs` carries an `AgencyId`, and
`SettingsService.LoadAsync` resolves **one row per agency**:

```csharp
var settings = await context.Settings.SingleOrDefaultAsync(x => x.AgencyId == agencyId);
```

Adding the toggle there would mean one case manager switching it off turns the window off
for **every user in the agency**. That is not what "let me turn this off" means, and it
would be a quiet, hard-to-diagnose bug — the kind where a colleague reports the feature
"just stopped working."

#### Where it goes

`Views/SettingsWindow.xaml` already separates personal preferences from agency policy. Its
own footer at line 1026 states the model: *"Appearance changes are remembered immediately.
Personal text shortcuts are saved from their own tab. Agency settings remain Admin-only."*

Put the toggle on the **Appearance** tab (line 211), directly under the theme picker,
which is the existing personal, non-Admin-gated, saves-immediately preference. Do not add
it to Billing & Requests, Schedule, Templates & Forms, or Reference Data — those are the
Admin-only agency tabs and would gate a personal preference behind a permission.

It saves on toggle, like the theme. It is not part of the agency Save button's payload.

Suggested label: **"Show the daily agenda window at sign-in."** Checked by default. A short
explanatory line beneath, matching the tab's existing style, is worth adding — something
like *"Lists overdue and upcoming work so you can add items to Today's Work."*

#### Persistence — per Sati user, following the text-shortcut precedent

There is no per-Sati-user preference table today. Two local precedents exist and they are
not equivalent:

| Existing preference | Storage | Scope |
|---|---|---|
| Theme (`Services/ThemeService.cs:16`) | `%LOCALAPPDATA%\Sati\theme.txt` | Per **Windows** user — not keyed by Sati user |
| Text shortcuts (`Services/TextShortcutService.cs:24-30`) | `%LOCALAPPDATA%\Sati\…`, keyed by `_activeUserId` | Per **Sati** user |

**Follow the text-shortcut precedent: a local file keyed by the Sati user id.** The theme
approach is the looser one — on a shared workstation, two case managers switching users
(`SwitchUserViewModel`) would share a single theme file, and sharing a *this window is off*
flag the same way would surprise the second user.

Store the "show once per calendar day" state (open item 2) in the same per-user record, so
one file covers both and there is one place to look when the window does not appear.

**Known limitation, worth stating in the release notes:** local storage does not roam. A
case manager who uses two machines will set the preference on each, and the once-per-day
state is tracked per machine. That is acceptable for a display preference. If it should
roam, the alternative is columns on `User` plus a migration — note that `CLAUDE.md`
singles out `User` as the entity whose persistence model must never cross the network
whole, so any such column needs the narrow-DTO discipline applied deliberately. Do not
make that change without asking Josh.

#### When the setting is off

Skip the window entirely — do not build the lists. The overdue and upcoming queries are
the expensive part of this feature; a disabled window should cost nothing at sign-in.

### Selection and append

Multi-select list, nothing pre-selected. Confirm appends each selected item as its own
line to the end of Today's Work, under a dated header when the scratchpad is not empty.
Skip closes without touching anything.

**Never block sign-in.** If any query throws, log via `AppErrorLog` and skip that section —
or the whole window. A convenience must not stand between a case manager and their
caseload.

---

## Five copy sets

All variants live in one static presentation helper — suggest `Helpers/AgendaGreetings.cs`
in the desktop project. This is presentation, not a rule, so it stays client-side and does
**not** belong in `Sati.Contracts.V1`.

Pick the index **once** when the window is constructed and store it. Do not use a computed
property that calls `Random` on every read — WPF re-evaluates bindings and the text would
flicker. `Random.Shared.Next(count)` at construction is fine.

`{0}` is the signed-in user's `DisplayName` from `ISessionService.CurrentUser` — the same
trusted display name the AI note rules use. Never a raw username or email.

Deliberate constraints on all copy:

- **No time-of-day greetings.** "Good morning" is wrong for a 4pm login, and case managers
  work irregular hours.
- **No exclamation marks or forced cheer.** Someone may be signing in on the worst day of
  their year — a client death, a crisis call. Warm and calm, never bouncy.
- **No clinical judgment or prioritization advice.** The window presents due dates. It does
  not suggest what matters most.

### House style for this copy

Warm and plain, but grammatically exact. Contractions are welcome — they are correct and
they carry the warmth. Sloppiness is not.

- **Restrictive clauses take `that`; non-restrictive take `, which`.** "Items that need
  attention" narrows the set; "these items, which need attention" describes a set already
  identified. Where the choice is genuinely ambiguous — as in "overdue items ___ need
  attention," where every listed item needs attention — **prefer a participial phrase**:
  *items needing attention*. It is shorter and sidesteps the question. That is why Set D
  below is built around "needing attention" rather than either relative pronoun.
- **No implementation jargon.** Never "window," "lookahead," "lookback," or "event" in
  user-facing text. Say "the next {1} days."
- **Parallel structure in any list or pair.** "Some may need work; others may need only an
  update" — not "some need work, others just recording."
- **Join independent clauses properly.** Comma plus conjunction, semicolon, or a full stop.
  No comma splices.
- **Em dashes mark a genuine break in thought,** not as general-purpose glue. At most one
  per variant.

### Set A — items coming up (20 variants)

```
Hello, {0}. Here's what's coming up on your caseload. Select anything you'd like on today's agenda.
Welcome back, {0}. A few items are due soon. Choose the ones you'd like to carry into today.
Hi, {0}. These are the next items on your caseload. Pick whichever ones belong on today's list.
Good to see you, {0}. Here's what Sati is tracking for the days ahead. Select what you'd like to work on.
Hello again, {0}. Some deadlines are approaching. Choose any you'd like to add to today's work.
Welcome, {0}. Here's a short list of what's coming due. Take whatever makes sense for today.
Hi there, {0}. These items are waiting on your caseload. Select the ones you want for today.
Hello, {0}. Sati has gathered your upcoming items. Choose where you'd like to start.
Good to have you back, {0}. A few things need attention soon. Pick whatever fits today.
Welcome back, {0}. Here's what lies ahead. Add anything you'd like to today's work.
Hello, {0}. These are your nearest deadlines. Select what you want on today's agenda.
Hi, {0}. Here's what's coming due. Choose the items you'd like to take on.
Welcome, {0}. A handful of items are coming due. Pick whichever ones belong on today's list.
Hello, {0}. Here's the upcoming work on your caseload. Select any you'd like to add to today.
Good to see you, {0}. These items come due soon. Choose what you'd like to handle today.
Hi there, {0}. Sati has your next items ready. Take whichever ones fit today.
Hello again, {0}. Here's what's on the horizon. Select anything for today's work.
Welcome back, {0}. A few deadlines are close. Pick the ones you want on today's agenda.
Hi, {0}. Here's what's next on your caseload. Choose what you'd like to bring into today.
Hello, {0}. These items are coming due. Select any you'd like to work on today.
```

### Set D — overdue items, the lookback (12 variants)

Josh's requested wording, rebuilt around "needing attention" per the house style above.
Every variant acknowledges that the record may simply be stale rather than the work
undone — that framing is deliberate and should survive editing.

```
Hello, {0}. Your records show a few items needing attention. Take a moment to review them; some may simply need their status brought up to date.
Welcome back, {0}. A few items are showing as overdue. They're worth a look, since some may already be done and not yet recorded.
Hi, {0}. These items are past due in your records. Some may need work, and others may need only recording.
Hello, {0}. Sati still shows these as outstanding. Review them, then either finish the work or record what's already complete.
Good to see you, {0}. Some items have passed their due dates. A quick pass will show which need doing and which need only a status update.
Hi there, {0}. Your records list these as incomplete. Check whether each still needs work or has simply gone unmarked.
Hello again, {0}. A few items slipped past their due dates. Review their status when you have a moment.
Welcome, {0}. These items are overdue according to your records. Some may need work; others may need only an update.
Hello, {0}. Sati still has these open. It's worth confirming whether each one is genuinely outstanding.
Hi, {0}. These items are past due. Take a moment to bring their status current, whether or not the work is done.
Welcome back, {0}. A handful of items are showing overdue. Reviewing them will clear anything already finished.
Hello, {0}. These items remain open past their due dates. Check each one and record any that are already complete.
```

### Set B — no clients on the caseload (10 variants)

Josh's requested empty state. Shown when the signed-in user has **no people at all**.

```
Hello, {0}. Your caseload is empty right now. Add a client to begin tracking reviews, forms, and deadlines.
Welcome, {0}. Once you add your first client, Sati will begin tracking what's due and when.
Hi, {0}. There's nothing to show yet. Add a client, and your upcoming work will appear here.
Hello, {0}. This is where your upcoming items will live. Add a client to get started.
Welcome to Sati, {0}. Add your first client, and Sati will begin building your agenda.
Hi there, {0}. You have no clients on your caseload yet. Add one to begin tracking reviews and forms.
Hello, {0}. Your caseload is ready when you are. Add a client to see upcoming work here.
Welcome back, {0}. There's nothing to list yet — adding a client is the first step.
Hi, {0}. Sati will track deadlines for you once there's a caseload to follow. Add a client to begin.
Hello, {0}. Add a client, and their reviews, forms, and due dates will begin appearing here.
```

### Set C — nothing coming due, and no assessment to suggest (10 variants)

**Not requested, but required.** Without it, a case manager with 25 clients and a quiet
week gets told to "add a client to begin managing your caseload," which is nonsense. These
are two different empty states and they need different copy.

This is now the narrower fallback: it applies only when nothing is coming due **and** no
Comprehensive Assessment is available to recommend. When one is available, use Set E.

```
Hello, {0}. Nothing is coming due in the next {1} days.
Welcome back, {0}. No deadlines are approaching in the next {1} days.
Hi, {0}. Nothing on your caseload comes due in the next {1} days.
Hello, {0}. There's nothing due in the next {1} days.
Good to see you, {0}. No items come due in the next {1} days.
Hi there, {0}. Nothing is scheduled or due in the next {1} days.
Hello again, {0}. No dates are approaching in the next {1} days.
Welcome, {0}. Nothing comes due over the next {1} days.
Hi, {0}. Your next {1} days are clear.
Hello, {0}. No items are coming due in the next {1} days.
```

### Set E — nothing coming due, assessment suggested (10 variants)

`{1}` is the lookahead day count; `{2}` is the client's name.

```
Hello, {0}. Nothing comes due in the next {1} days. That makes it a good stretch for a Comprehensive Assessment, and {2}'s is next up.
Welcome back, {0}. Your calendar is clear for now. Consider putting time into {2}'s Comprehensive Assessment while there's room.
Hi, {0}. Nothing comes due in the next {1} days. Assessments are meant to be built gradually, and {2}'s is the next one due.
Hello, {0}. No dates are approaching right now. This is a good time for a Comprehensive Assessment; {2}'s comes due soonest.
Good to see you, {0}. Nothing is due in the next {1} days. {2}'s Comprehensive Assessment is next on the calendar if you'd like to make progress.
Hi there, {0}. The next {1} days are open. Comprehensive Assessments reward steady work, and {2}'s is up next.
Hello again, {0}. Nothing is pressing at the moment. {2}'s Comprehensive Assessment is next due, if you'd like to chip away at it.
Welcome, {0}. Nothing comes due in the next {1} days. A quiet stretch is a good time for {2}'s Comprehensive Assessment.
Hi, {0}. Nothing is coming due right now. {2}'s Comprehensive Assessment is the next one on the horizon.
Hello, {0}. Nothing is due in the next {1} days. Consider spending the time on {2}'s Comprehensive Assessment — its due date is a deadline, not a start date.
```

The last variant states the OADS rationale outright. Keep at least one that does; it is
the reason the recommendation exists, and a case manager new to Sati will not otherwise
know why the app is suggesting work that is not yet due.

#### Why Sets C and E must not say "you're all caught up"

This constraint predates the lookback and still holds, for a narrower reason.

`UpcomingEventsService.GenerateFormEvents` drops any form where
`today > dueDate.AddDays(daysAfterDue)`, so a badly overdue item never appears in the
forward list. **The lookback now catches those**, which is the main reason it is a good
addition — it closes the gap that made an empty forward list misleading.

But the two lists answer different questions, and Sets C and E are chosen on the forward
list alone. They must therefore stay strictly statements about the next `{1}` days, never
claims about compliance. The cap is the clearest reason: with the lookback showing 5 of
147, "you're all caught up" would be false by a wide margin.

Do not "improve" any Set C or Set E variant into a reassurance about the state of the
caseload. Saying "nothing comes due in the next 30 days" is true and useful; saying
"you're all caught up" is a compliance claim the window is not entitled to make.

---

## Visual and accessibility requirements

**Theme.** Thirteen themes ship in `Themes/`. Use `DynamicResource` brushes throughout —
no hard-coded colors, no assumptions about light or dark. Check the window against at
least `MidnightOpal` and `PearlescentCream` (opposite ends) before calling it done.

**Demo indicator.** `CLAUDE.md` requires a Demo session to display a permanent Demo
indicator. A large modal at startup must not be the one surface that omits it — carry the
`IDataEnvironment.DisplayName` marker on this window too.

**Accessibility** — required, not optional:

- `AutomationProperties.Name` on the list, every item, and every button. Item names must
  read usefully on their own ("Q3 Review for [client], due September 12" — not "item 3").
- Focus lands on the list when the window opens; `Esc` skips; `Enter` confirms; `Space`
  toggles an item.
- Overdue items need a text or glyph cue, not color alone.
- The greeting is a heading in the automation tree, so the structure stays stable even
  though the wording changes each login — a screen-reader user should not have to re-learn
  the window daily.

---

## Tests

- Each of the five sets returns a non-empty string for every index, and every variant
  contains its required placeholders (`{0}` everywhere, `{1}` for Sets C and E, `{2}` for
  Set E).
- Set selection covers every branch: no people → B; overdue present → D; no overdue with
  upcoming → A; neither, with a CA available → E; neither, with no CA → C.
- **Lookback source:** a form overdue by more than `daysAfterDue` appears in the lookback.
  This is the test that fails if someone reuses `UpcomingEventService` for it, which is the
  obvious wrong turn.
- **Lookback scope:** an overdue form of a type *not* in `BillingComplianceRequirements`
  (Privacy Practices, or any of the three releases, under the default set) still appears.
  This fails if someone routes the lookback through `BillingComplianceGate.Evaluate`, which
  is the second obvious wrong turn.
- **Lookback ordering:** given overdue forms with mixed due dates, the five shown are the
  five oldest.
- The lookback caps at 5 rows while reporting the true total, and the reported total
  matches the number of incomplete overdue forms.
- The lookback writes nothing: after opening and closing the window, every form's
  `IsCompliant` and `CompletedDate` are unchanged.
- **CA selection** picks the soonest-due incomplete `ComprehensiveAssessment` *form*, and
  still recommends a client whose assessment entity is `Approved` but whose form is
  unattested — mirroring the `QnR` evidence-versus-attestation rule.
- CA progress matches `AnsweredCount`/`TotalCount` for the same assessment, and a client
  with no assessment row renders as "not started" rather than throwing.
- **Ordering:** appended lines survive `Scratchpad.InitializeAsync()` — i.e. the window
  runs after shell init. Write this one so it fails if the call is moved earlier.
- Confirm appends exactly one line per selected item; Skip appends nothing.
- Nothing is pre-selected on open.
- An `IUpcomingEventService` failure leaves sign-in working and shows no window.
- The greeting index is stable across repeated reads of the bound property.
- A Demo session shows the Demo indicator on this window.

Settings toggle:
- **Per-user isolation:** user A disabling the window leaves it enabled for user B on the
  same machine. This is the test that fails if someone puts the flag in `Settings` or
  copies the theme file approach — both are the obvious wrong turns, and both fail
  silently in normal single-user use.
- With the setting off, sign-in completes and **no** overdue or upcoming query runs.
- The toggle persists across a restart and defaults to on for a user who has never set it.
- The setting appears on the Appearance tab and is reachable by a case manager with no
  Admin permission.

---

## Open items for Josh

One left, and it does not block the build.

1. **Cadence** — once per calendar day per user (recommended), or every sign-in? The state
   lives in the same per-user record as the on/off toggle either way, so this can be
   decided once the window exists and Josh has used it for a week.

Settled since the first draft:

- Lookback scope and ordering — **all incomplete overdue forms, oldest due first.**
- Suppression — **an Appearance-tab toggle, stored per Sati user.** See "Turning it off."
- Today's Work integration — **text lines now, structured later.** The structured version
  goes in `AGENDA.md` as deferred work; do not build toward it now.

---

## Files to read first

1. `App.xaml.cs:295-335` — the startup sequence and where this window slots in
2. `ViewModels/Children/ScratchpadViewModel.cs` — what "Today's Work" actually is
3. `Data/UpcomingEventsService.cs` — the forward list, and the `lateDate` skip that makes
   a separate lookback source necessary
4. `Sati.Contracts/V1/BillingComplianceGate.cs` — `IsIncompleteAndOverdue`, the lookback's
   real source
5. `ViewModels/ClientDocuments/ComprehensiveAssessmentViewModel.cs:287-291` —
   `AnsweredCount` / `TotalCount` / `CompletionText`, the progress calculation to reuse
6. `Views/ScratchpadView.xaml`, `Views/ShellWindow.xaml:255` — the target surface
7. `Themes/` — the thirteen palettes the window must survive
8. `HANDOFF_90DAY_REVIEW_FLAG.md` — the attested-`QnR` backlog that the lookback will
   surface on day one

---

## Implementation record — 2026-09-01

The handoff is complete. The open cadence question was resolved using the recommended **once per
local calendar day, per Sati user, per environment, per computer** behavior. The Appearance-tab
toggle is checked by default and saves immediately. Its local-storage limitation is documented in
`ARCHITECTURE.md` and `DECISIONS.md`.

Implementation checkpoints:

- `a5e0fb9` — per-user, per-environment local preference and last-shown date;
- `cce213b` — read-only builder, unbounded all-form lookback, five-row cap and true total;
- `0a2926e` — five stable greeting sets;
- `5181fc7` — narrow tenant-scoped latest-assessment read route;
- `91867cb` — shared assessment progress calculation;
- `cdf6540` — modal UI, accessibility, settings toggle, startup/account-switch integration, and
  coordinator failure isolation; and
- `bbd5633` — compatibility-manifest declaration for the new route.

Two corrections were necessary:

1. Four supplied Set E variants omitted `{1}`, while the handoff's test section required `{1}` in
   every Set E variant. Those four now name the guaranteed forward period.
2. `IUpcomingEventService` emits `LateReview` rows for forms also owned by the unbounded overdue
   lookback. The agenda filters those forward rows so one form is not presented twice.

No form, assessment, compliance, completion-date, or billing state is written by this feature.
Confirm appends only selected, human-editable lines to `ScratchpadViewModel.ScratchpadContent`;
Skip is a no-op. The structured successor is recorded as deferred work in `AGENDA.md`.
