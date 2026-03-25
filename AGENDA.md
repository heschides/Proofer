# Sati — Refactor Agenda

A WPF MVVM case-management desktop app built with EF Core, CommunityToolkit MVVM, and SQL LocalDB.

---

## Phase 1 — Fix the Foundation
*Goal: App starts, login works, no crashes*

- [ ] Fix double `mainWindow.Show()` in `App.xaml.cs`
- [ ] Implement `MainWindowViewModel.Initialize(user)` — store logged-in user, trigger initial load
- [ ] Fix `NewUserViewModel` missing null-conditional on `CloseWindowRequested` event
- [ ] Make `IUserService` public
- [ ] Remove hardcoded seed user from `LoginWindowViewModel`; replace with proper first-run or seeded user via hash path
- [ ] Fix `MainPage_Activated` firing `LoadPeopleAsync()` on every focus — load once only
- [ ] Add `OnModelCreating` to `SatiContext` with explicit keys and relationships

---

## Phase 2 — Remove Service Locator, Tighten DI
*Goal: No more `((App)Application.Current).Services`*

- [ ] Remove service locator from `LoginWindow.xaml.cs`
- [ ] Use `Func<T>` factory injection or similar pattern for window creation
- [ ] Audit all remaining `GetRequiredService` calls in view code-behind

---

## Phase 3 — Complete Person/Client Management
*Goal: Add, view, edit, delete clients with validation*

- [ ] Add edit support to `NewClientViewModel`
- [ ] Add input validation using `[NotifyDataErrorInfo]`
- [ ] Fix `RemoveSelectedPerson` — currently not awaiting `DeletePersonAsync`
- [ ] Eager-load `Forms` and `Notes` in `PersonService.GetAllPeopleAsync`
- [ ] Add client detail view

---

## Phase 4 — Complete Notes Workflow
*Goal: Notes are fully usable — create, edit, delete, filter*

- [ ] Add delete note command to `MainWindowViewModel`
- [ ] Confirm edit flow works end to end
- [ ] Add status filtering (not just text search)
- [ ] Unit count / duration display

---

## Phase 5 — Forms, Deadlines, and Events Dashboard
*Goal: Core business logic — deadline tracking per client*

- [ ] Flesh out `Event.cs` domain model
- [ ] Build `FormService` and display upcoming deadlines per client
- [ ] Build upcoming events dashboard — sorted, filtered view of what's due
- [ ] Wire `UpcomingEvents` collection in `MainWindowViewModel`

---

## Phase 6 — Polish and Portfolio Packaging
*Goal: Looks good, handles errors gracefully, README exists*

- [ ] Global error handling and user-friendly error dialogs
- [ ] Loading states / busy indicators
- [ ] UI polish — consistent spacing, styles, color scheme
- [ ] README with purpose, setup instructions, screenshots
- [ ] Seed data script for reviewers

---

## Session Log
*Update this after each working session*

| Date | Phase | What was done |
|------|-------|---------------|
| 3/19 | Ph5 | Settings model, migration, ISettingsService/SettingsService, SettingsViewModel, wired into MainWindowViewModel — settings now load on startup, AbandonedAfterDays no longer hardcoded |
| 3/20 | Ph5 | Scratchpad model/service/migration, auto-save timer, shutdown save, NoteType enum, template insertion on type selection |
| 3/21 | Ph5 | NoteType radio buttons, EnumToBoolConverter, Incentive model/service/migration, productivity dashboard with progress bar, Settings weekday/holiday exclusion flags |
| 3/23 | Ph5 | Day after Thanksgiving flag, tuple return from GetOrCreateAsync, custom new month prompt dialog with personalized greeting |
| 3/22 | Ph5 | SchedulerViewModel, WorkdayTile, Incentive ExcludedDates, ISessionService singleton, scheduler popup XAML — popup not yet displaying, debug tomorrow |
| 3/23 | Ph5 | Scheduler popup fully working — tile toggling, month navigation, DaysScheduled persistence, popup open/close behavior fixed |
| 3/23 | Ph5 | SettingsWindow fully wired — billing, templates, weekday/holiday flags, auto-save on close |
