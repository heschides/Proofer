# Handoff — "Suggested follow-up" on the note entry panel

**Status:** design agreed with Josh, 2026-08-31. No code changed.
**Investigated against:** `master` @ `51b2341`.

---

## What Josh asked for

Below the note narrative box on the Overview and Notes pages, show
`Suggested follow-up:` with the client's next upcoming due item and an **Accept
suggestion** button that appends it to the end of the note content.

## Verdict

Reasonable and small. One governing constraint, and it turns out the codebase already has
the right seam for it.

**`AI_CASE_NOTE_RULES.md` rule 3 forbids inferring a follow-up.** It requires every note
to end with a `Follow-up:` section drawn *only* from "a follow-up-enabled current-note
fact" supplied by the case manager, and says plainly: **"Never infer a next action."**

A client's next *due compliance item* is not the same thing as a follow-up the case
manager committed to during this contact. Auto-filling it would assert something that did
not happen.

**Josh's design already resolves this** — the Accept button is the explicit human act that
turns a suggestion into a case-manager-supplied fact, exactly matching the "explicit human
acceptance" standard `CLAUDE.md` sets for AI-assisted drafting. The feature is compatible
*because of* the button. Nothing may ever be appended without it.

---

## Why appending to the raw narrative is the correct integration point

`Services/LocalAi/CaseNoteFactCompiler.cs:44-52` splits the **raw narrative** into
fragments and marks each one's usage:

```csharp
var usage = FollowUpSignalRegex().IsMatch(fragment)
    ? CaseNoteFactUsage.Narrative | CaseNoteFactUsage.FollowUp
    : CaseNoteFactUsage.Narrative;
facts.Add(new($"RAW-{index + 1:000}", fragment, "Rough note", usage));
```

`FollowUpSignalRegex` (line 299) matches `follow up`, `follow-up`, `f/u`, `next step(s)`,
`plan to`, `will`, `confirm`, `send`, `check back`, `prepare for`.

So appending a line that begins `Follow-up:` to `NoteEntryViewModel.Narrative` becomes a
legitimate follow-up-enabled fact automatically. **No new pipeline, no new fact type, no
change to `CaseNoteDrafting`.** The AI formatter can then cite it, and
`CaseNoteDraftRules` validation passes because the follow-up traces to a real
case-manager-supplied fact rather than the `SYSTEM-NO-FOLLOW-UP` fallback.

Requirement that falls out: **the appended text must begin with `Follow-up:`** so it
matches the regex. If it does not, the suggestion silently becomes an ordinary narrative
fact and the note still renders "No follow-up was documented." Cover this with a test —
it is the kind of thing that looks fine in the UI and fails only in the formatted output.

---

## Where it goes

Both target surfaces host the same shared control:

- `Views/NoteEntryView.xaml` — the shared `UserControl`
- Rendered on the dashboard via the `NoteEntryViewModel` `DataTemplate`
  (`Views/CaseManagerDashboardContentView.xaml:50`)
- Rendered in the notes log via the `NoteEntryPanel` `ContentControl`
  (`Views/NotesLogView.xaml:42`)

**Put the row in `NoteEntryView.xaml` once.** Both pages pick it up, and it cannot drift
between them. Do not add it to the two host views separately.

The state belongs on `ViewModels/Children/NoteEntryViewModel.cs`, which already owns
`Narrative`, `SelectedPerson`, and the AI command surface.

---

## Where the suggestion comes from — do not write a third one

There are already **two** implementations of "upcoming items for a person" and they
disagree:

| Owner | Scope |
|---|---|
| `Data/UpcomingEventsService.cs` `GenerateEvents` | Windowed by settings (`openDaysBefore` / `daysAfterDue`); reads `GetCurrentCycleForm` |
| `ViewModels/NewClientViewModel.cs:658` `RefreshUpcomingItems` | Every non-compliant form, no window, deliberately different |

Adding a third calculation is the duplicate-rule defect `CLAUDE.md` warns about.

**Use `IUpcomingEventService`,** injected into `NoteEntryViewModel` by constructor. It is
the settings-driven owner and is what the dashboard and the new login agenda window (see
the companion handoff) both use, so all three surfaces agree about what "upcoming" means.

Take the **soonest** event for the selected person. If the person has none in the window,
render nothing — no empty `Suggested follow-up:` label.

The existing duplication between those two owners is out of scope here; note it in
`AGENDA.md` as a cleanup rather than fixing it in this change.

---

## Behavior

**Display.** A single line under the narrative box: the label, the item, and the button.
Collapsed entirely when there is no suggestion.

**Accept.** Appends to the end of `Narrative`, on its own line, formatted so it matches
the follow-up regex:

```
Follow-up: <item label> due <date:M/d/yy>.
```

Then the text is ordinary editable narrative — the case manager can reword or delete it.

**Rules the command must honor:**

- **Never append without the click.** No auto-fill, no fill-on-blank. This is the whole
  basis for rule-3 compliance.
- **Idempotent.** Clicking twice must not append twice. Disable the button once accepted
  for the current note, and re-enable when the note is reset or a different note is
  opened.
- **Do not create a second follow-up.** If `Narrative` already contains a line matching
  `FollowUpSignalRegex`, either disable the button with a tooltip saying a follow-up is
  already documented, or append without the `Follow-up:` prefix. Two `Follow-up:` sections
  in a clinical note is a defect. **Ask Josh which he prefers**; recommendation is to
  disable, because it keeps one follow-up per note and makes the state visible.
- **Appends, never replaces.** Note the existing template-fill behavior at
  `NoteEntryViewModel.cs:566` and `:671` only fills when `Narrative` is blank. Appending
  is a different and safer operation — do not route it through that path.
- **Reminder notes.** `NarrativeLabel` returns `"REMINDER"` for reminder notes
  (`NoteEntryViewModel.cs:322`). A suggested clinical follow-up does not belong on a
  reminder. Hide the row when `IsReminderNote` is true.
- **Selection races.** The suggestion loads on client selection, so take a
  `LatestRequestTracker` identity and check it before writing the suggestion to shared UI
  state, per the engineering rules.
- **Failures are silent.** If the upcoming-events lookup throws, log and render nothing.
  A suggestion is a convenience and must never block note entry.

**Accessibility** (required by `CLAUDE.md`): `AutomationProperties.Name` on the button
that names the item it will add, not just "Accept"; the suggestion text reachable by
screen reader; keyboard-focusable in a sensible tab order after the narrative box; and no
status conveyed by color alone — if an overdue item is styled differently, it also needs a
text or glyph cue.

---

## Tests

- Accept appends exactly one line, and the appended text matches `FollowUpSignalRegex`.
  **This is the load-bearing test** — without the regex match the follow-up silently
  degrades to "No follow-up was documented" in the formatted note.
- `CaseNoteFactCompiler.Build` over a narrative ending in the accepted line produces a
  fact with `CaseNoteFactUsage.FollowUp` set.
- Accept is idempotent: two clicks produce one line.
- Nothing is appended on client selection, note load, or note type change — only on the
  explicit click.
- The row is hidden when the person has no upcoming item, and when `IsReminderNote`.
- A narrative that already contains a follow-up does not gain a second one.
- An upcoming-events failure leaves the panel usable and the narrative untouched.

---

## Files to read first

1. `AI_CASE_NOTE_RULES.md` rule 3 — the constraint this feature has to satisfy
2. `Services/LocalAi/CaseNoteFactCompiler.cs:38-70` and `:296-300` — the seam and the regex
3. `Sati.Contracts/V1/CaseNoteDrafting.cs` — `CaseNoteFactUsage`, `NoFollowUpFactId`
4. `Views/NoteEntryView.xaml` — the one place the row belongs
5. `Data/UpcomingEventsService.cs` — the suggestion source
