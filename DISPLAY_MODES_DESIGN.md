# Adaptive display modes — design history and current amendment

## Current amendment — September 5, 2026

The original design below drove release 1.2.47. A same-day usability follow-up now gives Overview
fixed roles at desktop widths: Current note on the left, Work Agenda in the center, Upcoming Due
Dates on the right, and Productivity below the Agenda when height permits. Below 1080 effective
units, the three primary panels stack vertically. The Workspace selector, Focus note, duplicate
Notes panel, Forms summary tab, and center-layout preference have been removed. Do not reintroduce
those superseded controls from the historical sections below. Easy Eyes remains the single larger-
text-and-controls switch, and resizing still preserves each live editor instance.

The compact note header uses the reclaimed client-name line for the nearest open, ready-to-open,
upcoming, or overdue form. Detailed Notes, forms, reviews, and productivity continue through the
main Case Management tabs. `ARCHITECTURE.md` and the later 2026-09-05 decision in `DECISIONS.md`
record the current implementation contract.

## Historical 1.2.47 design

Designed and implemented September 5, 2026. The core responsive tiers, central Work Agenda,
single live agenda host, short note sections, Focus note, center-default migration, and scoped
empty states are implemented and render-checked. The final AGENDA section records optional later
refinements; release work remains separate.

## 1. Intended experience

Keep Sati's warm palette, restrained panels, familiar workflows, and all existing capabilities.
Make **Work Agenda the default center workspace**. Keep note entry readily available beside it
when space permits. Give notes, deadlines, forms, and productivity deliberate places instead of
squeezing every panel into every window.

The user makes two simple choices:

- **Easy Eyes:** one switch for larger text and controls, with the layout making room automatically.
- **Focus note / Return to overview:** an explicit, reversible way to expand the current note.

The application chooses Wide, Balanced, or Compact from usable window space. These are internal
layout names, not three more settings users must learn. A smaller window never means smaller type.
There is no resolution chooser, automatic switch to another client, or new docking framework.

Josh explicitly prefers Work Agenda in the center by default. The existing option to put Notes in
the center remains available for someone who deliberately prefers it. Refer to the user-facing
panel as **Work Agenda**; `Scratchpad` can remain its internal name.

## 2. Current code and constraints

Read this against the current worktree, not just the screenshots. It already contains unrelated
uncommitted navigation, note-review, and other changes. Preserve them.

| Current owner | What matters to this change |
|---|---|
| `Views/ShellWindow.xaml` and `.xaml.cs` | Whole-root Easy Eyes transform; fixed shell side column; splitter/chevron; startup display detection; shutdown, account switch, privacy screen and activity feedback. |
| `Services/DisplayLayoutService.cs` | Currently chooses compact state once from physical monitor dimensions; not window size. |
| `ViewModels/ShellViewModel.cs` | `EasyEyesScale` is 1.0/1.3; passes the same Scratchpad VM to the Overview; routes the side panel according to active page and center preference. |
| `Views/CaseManagerDashboardContentView.xaml` | Three proportional columns (`.55*`, `1*`, `.65*`), center forms/productivity block, embedded deadlines board. |
| `Views/CaseManagerDashboardView.xaml` | Current navigation is already Overview, Clients, Notes, Caseload Matrix, Calendar, Statistics, Reviews, Providers, Help, Documents. Help and Documents have subordinate destinations. Do not restore the older extra Dashboard row visible in the screenshots. |
| `Views/NoteEntryView.xaml` and `.xaml.cs` | Shared editor with pinned bottom action, scrolling form, narrative minimum 160, conditional meeting/form/reminder/AI controls, compliance overlay. Also used outside Overview. |
| `Views/NotesPanelView.xaml` | Dashboard filter/grid, selection, double-click and delete context command. This is distinct from the full Notes workspace. |
| `Views/ScratchpadView.xaml` | Today's Work and Tomorrow's Agenda, separate drafts, history, text-size controls, loading/conflict/reload states. |
| `Services/EasyEyesPreferenceService.cs` | Per-user, per-environment setting in the current Windows profile. |
| `Services/ScratchpadLayoutPreferenceService.cs` | Existing explicit center preference; currently missing profiles default to false. |
| `Sati.Tests/WpfUiHarness.cs` | Existing STA render harness; use its bounded width/height overload for layout tests. |

This work owns presentation only. Keep existing commands, validation, scope checks, authoritative
rules, persistence and asynchronous request guards. No API contract, database migration, agency
setting, release/version bump, installer, or deployment is required for this design.

## 3. Space measurement and mode selection

### Coordinate contract

`W` and `H` are the **finite usable Overview viewport** in layout units inside the Easy Eyes scale,
after top-level and feature navigation, but **before** allocating any Overview columns, outer
margins, optional panels or splitters. Measure one stable parent spanning the whole available row.
Do not measure the leftover main-content column after the shell side panel has consumed width:
that would let opening a panel change the breakpoint that decides whether it can open.

WPF already handles Windows DPI scaling. If measuring inside the scaled subtree, use that finite
viewport without dividing again. An equivalent outer-viewport measurement divides by the Easy
Eyes factor exactly once. Never divide layout units by Windows DPI a second time. A constrained
parent allocation is the input; a child's overflowing desired/actual size is not the viewport.
WPF's measure/arrange behavior and device-independent units are documented in
[Microsoft's layout reference](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/layout).

### Starting thresholds

These are engineered starting values, to be tuned against populated WPF renders during
implementation. They are not claims of verified fit or rules about monitor specifications.

| Effective width W | Automatic arrangement | Simultaneously visible main panes |
|---|---|---|
| 2100 or more | Wide | Note editor; central Work Agenda; Deadlines; Notes |
| 1440–2099 | Balanced | Note editor; central Work Agenda; tabbed supporting pane |
| 1080–1439 | Compact, two panes | Note editor; central work pane, initially Work Agenda |
| Below 1080 | Compact, one pane | One full-width workspace, initially Work Agenda; every other workspace has a labeled selector |

Compact's one-pane arrangement is its fallback, not another user setting. Work Agenda remains the
home workspace at the narrowest size; do not automatically turn the Overview into a note editor.

Use 12-unit outer margins and 12-unit inter-pane gaps, inclusive of any splitter. Start with:

| Arrangement | Default allocation, after the above margins/gaps |
|---|---|
| Wide | Editor 520; Deadlines 380; Notes 380; Agenda takes the remainder (760 at W=2100). |
| Balanced | Editor 440; supporting pane 380; Agenda takes the remainder (572 at W=1440). |
| Compact, two panes | Editor 440; central pane takes the remainder (604 at W=1080). |
| Compact, one pane | Selected pane takes the available width. |

On wider viewports allow the editor to grow toward 600, giving remaining surplus primarily to the
Agenda. For example, at W=2880 use editor 600, Agenda 1460, Deadlines 380, Notes 380, plus 60 of
margins/gaps. Limit the agenda's editable text column to approximately 960 units, centered within
its pane, so a large display does not create excessively long lines. This limit is on text layout,
not clipping of content. User text-size choices still apply.

User-adjusted splitters must respect editor >=440, central pane >=560, and support panes >=340
in arrangements where those panes coexist. The Compact central pane may use >=600. Clamp saved
widths to what currently fits; never use a saved width to force overflow or pick a denser tier.
Provide keyboard-operable splitters and a **Reset panel sizes** action.

### Changes while working

- Re-evaluate on finite viewport size changes, Easy Eyes changes and monitor/DPI transitions.
- At startup choose directly from the table. On later growth require 48 additional units beyond
  a threshold before expanding; collapse below the base threshold. This avoids oscillation.
- Apply a change once per settled size, approximately 150 ms after resize input. Do not run
  repeated domain loads or preference writes from size/layout events.
- Width/height changes while minimized or before a finite nonzero measure do nothing.
- Do not auto-expand into more panes while a text editor has keyboard focus. Keep the current
  arrangement until focus leaves that editor; contraction must still happen when needed to fit.
- On contraction reveal the pane containing keyboard focus in its new tab group. If two formerly
  visible panes become one, the focused pane wins over a default. Otherwise retain the last
  deliberate selection where applicable; first use defaults to Agenda.

Approximate examples: a 1920-wide display at 100% scaling will usually reach Balanced; turning on
Easy Eyes reduces the available layout width by 1.3 and will usually reach Compact. A 2880-wide
display at 150% scaling has about the same layout width as 1920 at 100%, before window chrome.
Actual mode is always determined by the measured viewport, not these examples.

## 4. Pane placement and access

### Wide

```text
Note editor | WORK AGENDA                    | Deadlines | Notes
            | Today's Work / Tomorrow       |           | filters + grid
            |                               |           |
            | Forms / Productivity band *   |           |
```

All four major panes remain available at a glance. At H>=840, a lower band under the Agenda hosts
**Forms** and **Productivity** tabs. The band has a starting height of 280; Agenda gets remaining
height, with a minimum 320 for its whole pane. Each band tab shows its entire existing feature in
a bounded scrollable area. Forms and productivity are separate scopes, not one unexplained card.

At H<840, remove the lower band and put Forms and Productivity alongside Notes in the far-right
pane. This gives the Agenda the full height. Do not truncate the lower band or its actions.

### Balanced

```text
Note editor | WORK AGENDA                    | Notes / Deadlines
            | Today's Work / Tomorrow       | (selected supporting pane)
            | Forms / Productivity band *   |
```

The supporting pane defaults to Notes. Both Notes and Deadlines have always-visible, labeled
selectors. The same H>=840 lower-band rule applies. At shorter heights, its selectors become
Notes, Deadlines, Forms, Productivity and the Agenda uses the full height.

### Compact, two panes

```text
Note editor | Work Agenda / Notes / Deadlines / Forms / Productivity
            | WORK AGENDA selected initially
```

The central work pane gets the majority of the width. Selecting another tab uses this space until
the user selects Work Agenda again; it does not close, flush, reload or reset the agenda draft.
There is no permanently empty far-right rail and no lower band competing for height.

### Compact, one pane

```text
Work Agenda / Note / Notes / Deadlines / Forms / Productivity
SELECTED WORKSPACE AT FULL WIDTH
```

Agenda is the initial selection. **Note** means the current entry/editor; **Notes** means the
existing notes list. Give them distinct automation names: "Current note" and "Notes list".
Selecting a note in the list reveals Current note after the existing selection/draft guard allows
it. New Note also reveals that editor without changing the selected client unnecessarily.

If selectors cannot fit in one row, replace that selector group with a labeled **Workspace**
ComboBox containing every entry; retain the selected workspace name. Do not wrap tabs across three
rows, hide entries off-screen, or make users hunt for an unlabeled chevron. Measure actual label
widths rather than assuming English abbreviations will fit. This rule also applies to supporting
pane and lower-band selectors at their own local widths.

### Preferences and context

- Default Work Agenda to center when there is no stored center preference. Preserve an explicitly
  stored false value. Update setting wording to **Keep Work Agenda in the center**. No destructive
  rewrite of existing preference files; distinguish missing from false.
- If someone selects Notes as the center preference, swap Agenda and Notes' principal slots in
  Wide/Balanced; the forms/productivity band remains in the center. In Compact, that preference
  chooses the initial central/full-width tab. Both still remain directly selectable.
- A temporary tab selection does not change that preference. First entry after sign-in follows
  it; navigation away/back within a session restores the current pane selection.
- Outside Overview, a labeled **Work Agenda** shell action remains available. It never opens the
  Overview-specific Notes panel on another page. If the page has >=720 units left after a 360-unit
  agenda pane, margins and gap (W>=1120), the action can show a side pane. Otherwise it switches to
  a full-width agenda surface with **Return to [page]**, retaining the page instance and state.
- A user can close a supporting pane in Wide/Balanced. A labeled **Show notes** or **Show supporting
  pane** action remains in the Overview toolbar and reopens its last tab. Compact always retains
  its central work pane and selector; there is no automatic disappearance of Work Agenda.
- Temporary fitting decisions must not overwrite saved panel choices. Remember deliberate panel
  sizes/open choices per user/environment/Windows profile using local presentation storage.
  Store no client IDs, narratives, note IDs or agency data in a layout-preference file.
- Reset sizes affects layout preferences only. It does not reset Easy Eyes, the agenda date,
  active client, entered text, filters or permission state.

### Navigation above the panes

Keep the current two-level structure: shell area navigation, then the current area's page
navigation. Keep Case Management's new Help/Documents grouping. When a navigation strip cannot
fit, replace that strip with a labeled **Area** or **Page** selector using the same available
destinations and commands. Help/Documents children use a labeled section selector when their
sidebar would compromise the active page. Keep the active destination visible. Do not restore a
redundant Dashboard row or rely on sideways scrolling to discover destinations.

Demo identity, Settings, My Account and the Work Agenda action remain reachable in every shell
state. At very narrow widths, secondary shell actions may use a visibly labeled menu; Demo
identity and the current area/page remain visible. Role filtering and authorization do not change.

## 5. The note editor must work vertically as well as horizontally

Keep **one shared NoteEntryView**. Focus mode and the ordinary note list use that same component
and established VM commands. Do not build a second editor containing a second copy of the rules.

Always show its current New/View/Edit heading, lock state where applicable, New Note action,
Focus note action and selected client context. Keep the existing bottom action visible in its
existing states, using the existing label and CanExecute behavior; do not make a locked note
savable merely to display a button.

### Tall editor: H>=840

Preserve the familiar field order. Use compact, readable field grouping, with minutes/date side
by side only where their labels and values fit. Give the narrative a flexible row with a target
minimum visible height of 240. Spare height goes to writing space, not a blank gap above Save.
Optional meeting details, service-day timeline and AI review can require scrolling; they must not
be clipped by a fixed-height parent. Opening a long detail section never resets another field.

If measured metadata, validation messages or optional content would leave less than 240 for the
narrative, use the short-editor structure below even at this height. Check metadata's intrinsic
size independently of the chosen structure to avoid feedback loops. Contraction honors focus.

### Short editor: H<840, or the tall editor cannot fit

Use two stable sections, **Write** and **Details**, under the pinned note heading/client context:

- **Write:** narrative and its text-size/copy controls; a wrapping summary of the actual type,
  status, date and minutes (blank values explicitly remain unset); a labeled **Edit details**
  action; suggested follow-up, template and AI controls retain their existing conditions.
- **Details:** the existing client selector, status and guidance, type, form subtype, minutes,
  date, service start/time-conflict feedback, meeting observations and other conditional inputs.
  All field bindings and dependencies remain the same. Show **Return to writing**.

Client context remains visible in both sections; the selector itself has one owner in Details.
An explicit New Note begins in Details so the worker can choose a client and required inputs.
Opening a saved note begins in Write. Automatic reflow chooses the section holding focus,
otherwise restores the user's last section. Do not pretend the metadata is complete by assigning
new automatic defaults. Required input and status rules remain exactly as they are today.

At least 200 visible units of narrative is the short-editor target when H>=540. Below that,
allow bounded scrolling; no control minimum may force the footer outside the available viewport.
Errors relating to Details select that section, reveal the relevant control and preserve all text.
Compliance dialogs remain on top of the whole note module with their choices reachable by keyboard.

Use bounded Grid rows; putting the whole editor in a vertical StackPanel/ScrollViewer and assigning
a star row inside it does not establish a finite writing viewport. Each long section has its own
bounded scroll region, with scrolling contained in the active feature. Keep list virtualization.
See [Microsoft's panel reference](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/panel).

### Focus note

An explicit **Focus note** action expands the same current note across the Overview. It preserves
New/View/Edit/lock state, never implicitly unlocks, selects, saves, submits, or clears a note.
It provides **Return to overview** and a labeled **Work Agenda** action at the top.

At editor width >=960 and H>=620, show Details on the left (320 starting width) and writing on the
right (remaining width, >=560). At smaller sizes use Write/Details sections. The agenda action
temporarily shows the existing agenda full-width with **Return to note**, retaining focus-mode
state and all drafts. Supporting work remains accessible through a labeled Workspace selector.
Only explicit Return to overview exits focus mode. Do not use double-clicking a note to enter
focus mode automatically; preserve its current guarded editing behavior.

Do not give Escape a new focus-mode meaning: it already invokes guarded StartNewNoteCommand.
Popups/menus/dialogs consume Escape before it reaches that command. Use the labeled return
actions, avoiding a new ambiguous shortcut or accidental draft discard.

## 6. Feature preservation checklist

This is an acceptance inventory, not authorization to weaken, hide or reimplement a rule.

| Feature | Must remain accessible and retain behavior |
|---|---|
| Work Agenda | Today/Tomorrow (next workday), both drafts, history/search, both text-size controls, Ctrl+Enter timestamp, existing autosave/rollover, loading/retry/conflict explanations and reload choices. A blank agenda is an editable document, not "no tasks due". |
| Note lifecycle | New/View/Edit, lock/unlock verification, save/action label, selection/draft-discard guards, returned-note explanation, client reassignment confirmation, status guidance and existing role restrictions. |
| Note data | All five note types and conditional fields, form type, minutes/units, dates/scheduled reminders, time reservation and overlap warning, meeting facts/attendees, narrative/copy/text size, suggested follow-up and explicit acceptance. |
| Templates/local AI | Existing availability, fact controls, original narrative, build-template action, progress/download, provenance/warnings, proposed draft and accept/discard. Resizing must never accept a suggestion, restart inference or lose a proposed draft. |
| Notes list | Search, status filter, all existing columns, selection, double-click editing and delete command through existing confirmation. Add a visible selected-row Actions menu for the context-menu command and keyboard access. |
| Forms | All twelve existing form types, quarterly/annual/releases grouping, pending-attestation links, existing attestation/revocation UI. Label the selected client and cycle where already available. No client: show "Select a client to view forms", not twelve misleading unchecked facts. |
| Deadlines | All eight existing categories: Comp Assessments, Reclasses, PCPs, Releases, Appointments, 90-Day Reviews, Effective Dates, All; date-window cycle/filter; existing item details and Opened/Completed/Not Started commands. |
| Productivity | Pending, logged, abandoned, daily average, units/target, needed/day, days left, incentive estimate, explanatory tooltips and fallback messages. Label it as the worker's monthly productivity, distinct from selected-client forms. |
| Navigation | Every current role-gated main area and all current feature destinations, Help children and Documents children, Settings/My Account, Demo identity, activity feedback, privacy screen and existing permissions. |

For Deadlines use a labeled **Category** ComboBox and visible date-window control at narrow pane
widths. Retain the existing category selection owner; do not create a parallel filter. Full-width
tabs are optional only where they fit in one row. Use native, keyboard-operable menus for item
actions; preserve context menus as a convenience.

Empty/loading/failed/unselected states must be distinct. Derive zero-item messaging only from a
successful completed load of the current scope. An in-flight or failed request is never "All caught
up". Suggested wording: "No notes match these filters"; "No deadlines in this date range".
Reuse existing load/error state where present; add presentation state only where needed. Do not
invent a numeric overdue or task badge from incomplete data, free-form agenda text or a new rule.
If an existing save failure, conflict or unsaved-session warning occurs in a hidden pane, expose a
concise visible status action that opens that pane. Hiding a pane must not hide the fact that its
work failed to save. Use existing state, with no narrative text in the status or layout logs.

## 7. Easy Eyes and accessibility

Keep **Use Easy Eyes mode** as a single ungated personal setting, default off. Suggested help:
"Larger text and controls. Sati rearranges panels to keep your work comfortable."

Retain the existing 1.3 LayoutTransform for this implementation. With the viewport contract above,
the layout naturally selects fewer panes. Replacing all explicit fonts throughout the app is not
necessary to fix the current problem and would greatly widen this change. Never substitute a
RenderTransform that enlarges drawing without allocating layout space. A later typography-token
refactor can preserve the same one-switch experience, but is outside this implementation.

Preserve existing Easy Eyes behavior for note-list narrative-column visibility and the Clients
horizontal selector, along with the ordinary choices restored when it is turned off. Preserve
per-editor text-size controls. Changing narrative font alone should alter wrapping/scrolling,
not unexpectedly rearrange the entire dashboard.

Keep theme resources, font families and accent-button tokens. Strengthen pane headings and scope
labels; do not replace the palette with extra decoration. Do not reduce current text or click
targets to satisfy a width threshold. Avoid icon-only replacement of primary actions.

Keyboard focus must follow visual reading order. Native tab/selector semantics, meaningful
automation names, labeled inputs, visible focus indicators and non-color status cues apply to every
placement. On automatic rearrangement preserve the actual focused editor, caret, selection, undo
history and scroll position. Explicit workspace selection focuses its heading/first appropriate
control; returning restores the previous control. Do not trap focus in an ordinary supporting pane.
Treat keyboard focus and saved logical focus separately; see
[Microsoft's focus reference](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/advanced/focus-overview).

The whole shell must fit the monitor's available work area even when Windows scaling makes the
current 900x600 minimum too large. Clamp effective minimum dimensions to that work area and exercise
the single-pane/short-height fallback. Popups, compliance prompts and Settings must also fit; do not
claim that shrinking the main window alone verifies those surfaces. The privacy overlay remains
above and outside the Easy Eyes transform, covering all page/agenda/focus states as it does today.

## 8. Implementation architecture

### A small, pure presentation policy

Introduce `OverviewLayoutPolicy` (suggested name) in desktop `Services`, with immutable inputs and
outputs. Inputs: finite W/H, previous effective state, current editor focus/section, deliberate
pane preferences and whether Focus note is active. Output: tier, single/two-pane flag, lower-band
placement, note presentation variant and validated pane widths. No monitor APIs, EF, commands,
network operations, UIElement instances or clinical-rule logic belong in that policy.

Keep measurement, focus/caret handling, constrained reparenting and row/column assignments in the
view layer. Use constructor injection for collaborators. ViewModels remain unaware of Views.
A small presentation-state object can own selected panes and notify the view without adding more
business responsibilities to CaseManagerDashboardViewModel.

The shell supplies one stable full-width viewport and coordinates its optional side host with the
Overview. Do not add a second independent breakpoint engine that still reserves a fixed 300-unit
shell side column. Retire physical-resolution tier selection and the startup recommendation dialog
once automatic fitting replaces their function. Update their tests and architectural documentation;
do not keep two conflicting policies active. The monitor detector may remain only if needed to
obtain available work area for window bounds.

Separate transient responsive spacing flags from deliberate Clients selector choice. Repeatedly
calling current `ApplyCompactDisplayMode()` would repeatedly force `IsClientListCompact=true`;
replace that coupling with derived presentation state so resizing cannot erase the user's choice.
This is a narrow compatibility change, not a redesign of every other feature page.

### One stateful view instance; stable command owners

Extract the Forms, Productivity and Deadlines blocks into small reusable views with the current
dashboard DataContext and commands. Then change placement, not business behavior. Keep one live
NoteEntryView for each current editor VM and one live agenda control for the shell's Scratchpad VM.
Eliminate the competing shell/center agenda renderings as part of placement integration. State
must survive moving between Overview, another page and the agenda surface.

Prefer one stable Grid per workspace whose existing children change coordinates/visibility.
Where crossing shell/Overview hosts requires reparenting, a view-only coordinator detaches and
reattaches the same control once; it does not reconstruct it from a new DataTemplate. WPF controls
have one parent. Never attach one control twice or introduce service-locator resolution to obtain
another copy. Review Loaded/Unloaded handlers so relocation only changes subscriptions and does
not initialize, reset, flush or reload business data.

Within NoteEntryView, move the same detail/narrative blocks between their presentation slots.
Changing template to a fresh TextBox can preserve the binding text while losing undo history;
retaining only the VM is therefore insufficient. Keep live editors, including the two dated agenda
textboxes. Tab selection can use persistent child hosts whose visibility changes; do not rely on
default template recreation to preserve editing state.

Resize/focus/selector operations issue **zero domain writes and zero new data reloads**. Ordinary
timers and an existing in-flight request may still finish normally. Any genuinely new load triggered
by navigation follows existing `LatestRequestTracker` rules and invalidates stale results. Keep
all shutdown flush, account-switch, session-expiration and note discard paths intact.

### Preference migration

Keep the existing Easy Eyes and center-preference files and their error reporting. Missing center
profile => true; explicit false => false; explicit true => true. A malformed file gives a safe
centered in-memory default and a warning without overwriting it. Cover this behavior with tests.
Any new size/open-state preference store uses bounded validated values and the same local profile
scope. Focus mode, active tabs, editor sections and caret state are session state, not durable
document data. Clear session presentation state through the established account-switch boundary.

## 9. Build order for the implementation model

1. Re-read AGENTS.md and the relevant architecture/decision/agenda sections. Inspect dirty changes
   and current navigation. Inventory conditional editor fields and existing command ownership.
2. Add the pure layout policy, finite viewport measurement, migration of the center default and
   meaningful boundary tests. Preserve the current rendered layout until the next slices connect it.
3. Extract Forms/Productivity/Deadlines views without behavior changes. Implement Wide/Balanced/
   Compact placement and the single live agenda host, including off-Overview access and navigation
   overflow. Remove the old competing startup-resolution behavior.
4. Implement short/tall note presentation and explicit Focus note using the same live controls.
   Verify all note types and transient AI/compliance states before polishing spacing.
5. Add discoverable supporting-pane actions, preference persistence, empty states and scoped labels.
   Preserve existing theme and accessibility behaviors. Tune starting sizes from populated renders.
6. Run the acceptance checks below. Document actual results and any remaining limits. Update
   ARCHITECTURE.md to describe implemented ownership and mark this plan's AGENDA items complete
   only after verification. Do not deploy or run the DATT release process as part of implementation.

## 10. Acceptance checks

### Automated, using synthetic data and existing fixtures

- Policy boundaries at 1079/1080, 1439/1440 and 2099/2100; 48-unit growth margin; 839/840 height;
  initial, minimized and invalid geometry; saved-width clamping; focused-pane precedence.
- Equivalent effective viewports yield the same mode regardless of physical monitor resolution.
  A full outer 1920-unit test container with a 1.3 transform yields the expected reduced viewport
  once, and no double DPI/scale division. Assert measured constraints, not just enum values.
- Missing/true/false/corrupt center preferences, Easy Eyes restoration, user/environment isolation,
  reset scope, save failure and rapid toggle ordering follow existing preference-service patterns.
- Use the real WpfUiHarness and real theme resources at finite W/H. Assert pane bounds, useful
  narrative bounds, no shell horizontal overflow, reachable footer and selectors, visible selected
  workspace, and actual command bindings. XML-presence tests alone cannot prove fit or reachability.
- Populate a long synthetic client name, >=30 notes with all statuses, long agenda text for both
  days, all deadline categories, all form states and meaningful productivity values. Also test
  unselected, empty, loading and failed states distinctly.
- Type in note and both agenda editors; select text; resize across every boundary; toggle Easy Eyes;
  open/close focus mode; navigate away/back. Assert text, caret/selection, undo, draft flags, selected
  note/client, filters, scroll positions and pending AI draft survive. Check control identity.
- Count service calls around transitions with timers controlled by the fixture: reflow adds no
  save/load calls, does not auto-submit and does not trigger an extra unsaved-work prompt.
- Exercise New/View/Edit, allowed/forbidden edits, reminders including future dates, form subtype,
  meeting details, time conflicts, compliance dialog, canceled selection/reassignment and stale
  unlock. Existing authoritative tests remain; any new security/concurrency regression test must
  fail against its unguarded implementation, per AGENTS.md.
- Preserve/update relevant tests including DisplayLayoutServiceTests, ScratchpadSwapRenderTests,
  ScratchpadLayoutPreferenceTests, EasyEyesPreferenceTests, NotePanelRenderTests, and navigation
  render tests. Some current navigation assertions may predate the dirty navigation changes:
  compare with that established work rather than restoring obsolete screen structure.
- Build the WPF project; run targeted UI/preference tests first, then the desktop suite once after
  the integration stabilizes. Run API tests only if actual implementation unexpectedly touches
  shared/domain/API behavior; such expansion needs a documented reason.

### Manual visual and interaction matrix

Use a Demo/synthetic fixture; do not query or copy Production records to fill test screens.

| Display/window situation | Windows scaling | Easy Eyes |
|---|---|---|
| 2880x1800 maximized | 100%, 150% | Off and on |
| 1920x1080 maximized | 100%, 125%, 150% | Off and on |
| 1366x768 and 1280x720 maximized | 100% | Off and on |
| 1920x1080 maximized | 200% | Off and on; verify work-area fitting |
| Windowed, narrow and short | Across the measured thresholds | Off and on |
| Move between monitors with different scaling | At least 100% to 150% | Off and on |

For representative large, 1080p and compact cases, verify keyboard-only operation, a Windows
screen reader, visible focus, light/dark/high-contrast behavior and long labels. Test the actual
rendered app, including overlays/popups and settings, with more than empty panels. Record physical
size, Windows scale, Easy Eyes and measured W/H beside each screenshot. Thresholds may move after
this QA, but the feature inventory, central-agenda default and state-preservation contract may not.

## 11. Historical implementation status

The original implementation prompt is complete and its selector/focus details are superseded by the
current amendment at the top of this document. Future display work should begin with that amendment,
`ARCHITECTURE.md`, and the latest 2026-09-05 display decision in `DECISIONS.md`.
