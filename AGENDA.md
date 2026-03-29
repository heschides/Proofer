# Sati — Refactor Agenda

A WPF MVVM case-management desktop app built with EF Core, CommunityToolkit MVVM, and SQL LocalDB.

---

## Phase 1 — Fix the Foundation ✅
*Goal: App starts, login works, no crashes*

- [x] Fix double `mainWindow.Show()` in `App.xaml.cs`
- [x] Implement `MainWindowViewModel.Initialize(user)` — store logged-in user, trigger initial load
- [x] Fix `NewUserViewModel` missing null-conditional on `CloseWindowRequested` event
- [x] Make `IUserService` public
- [x] Remove hardcoded seed user from `LoginWindowViewModel`; replace with proper first-run or seeded user via hash path
- [x] Fix `MainPage_Activated` firing `LoadPeopleAsync()` on every focus — load once only
- [x] Add `OnModelCreating` to `SatiContext` with explicit keys and relationships

---

## Phase 2 — Remove Service Locator, Tighten DI ✅
*Goal: No more `((App)Application.Current).Services`*

- [x] Remove service locator from `LoginWindow.xaml.cs`
- [x] Use `Func<T>` factory injection or similar pattern for window creation
- [x] Audit all remaining `GetRequiredService` calls in view code-behind

---

## Phase 3 — Complete Person/Client Management ✅
*Goal: Add, view, edit, delete clients with validation*

- [x] Add edit support to `NewClientViewModel`
- [x] Add input validation using `[NotifyDataErrorInfo]`
- [x] Fix `RemoveSelectedPerson` — currently not awaiting `DeletePersonAsync`
- [x] Eager-load `Forms` and `Notes` in `PersonService.GetAllPeopleAsync`
- [x] Add compliance review dialog on client creation
- [x] EffectiveDate replaced with MM/DD TextBox with CustomValidation and waiver-gating

---

## Phase 4 — Complete Notes Workflow ✅
*Goal: Notes are fully usable — create, edit, delete, filter*

- [x] Add delete note command to `MainWindowViewModel`
- [x] Confirm edit flow works end to end
- [x] Add status filtering (not just text search)
- [x] Unit count / duration display
- [x] Add NoteType (Visit, Contact, Other, Form) with per-type narrative templates
- [x] Add FormType nullable property on Note with migration
- [x] Form note submission triggers MarkFormCompleteRequested popup

---

## Phase 5 — Productivity, Settings, and Scheduler ✅
*Goal: Daily work tracking, configurable settings, monthly scheduler*

- [x] Settings model, migration, ISettingsService/SettingsService
- [x] SettingsWindow fully wired — billing, templates, weekday/holiday exclusion flags
- [x] Settings confirmation dialog on close summarizing changed values
- [x] Scratchpad model/service/migration with auto-save timer
- [x] Incentive model/service/migration with productivity dashboard and progress bar
- [x] Scheduler popup with workday tile grid, month navigation, DaysScheduled persistence
- [x] New month prompt — fires PromptSchedulerRequested when wasCreated is true
- [x] NoteType radio buttons with EnumToBoolConverter

---

## Phase 6 — Forms, Deadlines, and Events Dashboard ✅
*Goal: Core business logic — deadline tracking per client*

- [x] UpcomingEvent record and UpcomingEventService with full computation logic
- [x] Upcoming Tasks panel split into two columns (forms left, visits right)
- [x] Sort radio buttons wired via SortByDate computed property
- [x] Forms checklist bound to real Form entities via ToggleFormCommand
- [x] Compliance flags computed per FormType using GetCurrentCycleForm
- [x] GetCurrentCycleForm on Person model replaces FirstOrDefault throughout
- [x] EnumDescriptionConverter, BoolToVisibilityConverter, InverseBoolConverter
- [x] Description attributes on FormType enum for human-readable display
- [x] Enums moved to top-level namespace — Enums wrapper class removed
- [x] UserId foreign key on Person, GetAllPeopleAsync filtered by userId

---

## Phase 7 — Client Detail View and User Management
*Goal: Per-client deep view, user administration, soft-delete*

- [ ] Per-client detail view — all notes, forms, compliance status, upcoming events scoped to one client
- [ ] User management / admin panel — add, edit, deactivate users
- [ ] Soft-delete recycle bin for notes
- [ ] DataGrid column cleanup — replace AutoGenerateColumns with explicit column definitions

---

## Phase 8 — Polish and Portfolio Packaging
*Goal: Looks good, handles errors gracefully, README exists*

- [ ] Global error handling and user-friendly error dialogs
- [ ] Loading states / busy indicators
- [ ] UI polish — consistent spacing, styles, color scheme
- [ ] README with purpose, setup instructions, screenshots
- [ ] Seed data script for reviewers

---

## Phase 9 — Azure / HIPAA Readiness
*Goal: Production-ready deployment with audit logging and cloud hosting*

- [ ] Migrate from LocalDB to Azure SQL (EF Core provider swap)
- [ ] Audit logging — AuditLog table with SaveChanges override
- [ ] Microsoft Entra ID evaluation for authentication
- [ ] MSIX packaging for deployment
- [ ] Per-agency database isolation strategy
- [ ] Connection string secret management (no plaintext in appsettings.json)

---

## Pre-Release Fixes
*Must address before shipping to team or OADS*

- [ ] Form compliance query — replace remaining FirstOrDefault usage with GetCurrentCycleForm everywhere
- [ ] Annual form regeneration — when anniversary rolls over, generate new Form records for the new cycle
- [ ] First login condition for scheduler prompt — currently `if (wasCreated)` which is correct; verify behavior across month boundaries

---

## Future Roadmap (Post-OADS)
*Ideas parked for v2.0 and beyond*

- [ ] Sati.Core extraction — move models, services, EF context into shared class library
- [ ] Sati.Api — ASP.NET Core Web API exposing note creation and client read endpoints
- [ ] Sati.Mobile — MAUI app for field note entry and upcoming task visibility (Android/iOS)
- [ ] Azure Cognitive Services Speech-to-Text integration for field note dictation
- [ ] reMarkable integration — push visit PDFs to device, pull annotated docs post-visit
- [ ] Scratchpad search window — read-only historical entry viewer

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
| 3/25 | Ph6 | Added SafetyPlan + PrivacyPractices to FormType enum and GenerateFormList. Added 18 form deadline offset properties to Settings model, service, and ViewModel. Added Form Deadline Windows section to SettingsWindow XAML. Created UpcomingEvent record and UpcomingEventService with full computation logic for annual forms, 90-day reviews, and scheduled notes. |
| 3/26 | Ph6 | Wired UpcomingEventService into MainWindowViewModel, added UserId foreign key to Person, filtered GetAllPeopleAsync by userId, added ISessionService to NewClientViewModel, fixed UpcomingEvents collection type |
