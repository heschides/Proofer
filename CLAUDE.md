# CLAUDE.md
# Sati — Project Briefing for Claude

This document is written for Claude. It summarizes the Sati project, the current state
of development, the repository structure, and how to work effectively with Josh.
Read this at the start of any new session before writing a single line of code.

---

## What Sati Is

Sati is a WPF MVVM desktop case management application built in C# on .NET 10.
It is named after the Pali word for mindfulness. It is built by Josh, a social
services case manager in Maine, on nights and weekends.

The stack is: .NET 10, WPF, CommunityToolkit.Mvvm, EF Core 10, SQL Server LocalDB,
Microsoft.Extensions.DependencyInjection.

The UI palette is warm earth tones: #FDF6EC background, #C87941 accent, #3D2B1F text.
This is intentional and established. Do not suggest changing it.

Sati manages:
- Client caseloads (Person records scoped to logged-in user)
- Service notes with status lifecycle (Scheduled → Pending → Logged → Abandoned etc.)
- Form compliance tracking (Q1-Q4 reviews, PCP, Comprehensive Assessment, releases, etc.)
- Upcoming events dashboard (form deadlines, scheduled visits and contacts)
- Scratchpad with auto-save and history window
- Productivity tracking with incentive/threshold system
- Monthly scheduler with workday toggling and exclusion flags
- Supervisor dashboard with team overview, overdue items, monthly productivity
- Settings (single-row DB table, not local config, for future multi-agency compatibility)

---

## The Longer Vision

Sati is intended to become a statewide human services platform for Maine — replacing
Evergreen (the state's current hated system), integrating with MaineCare for billing
via EDI X12 837P, adding supervisor and director dashboards, anomaly detection on
billing patterns, and eventually a HIPAA-compliant RAG-based LLM for policy questions.

Revenue from licensing goes into a trust that funds higher education for foster youth.
Josh retains a founder's interest in any major acquisition.

Target users: waiver agencies billing under Section 21 and 29, and Targeted Case
Management (TCM) under Section 17 of the MaineCare Benefits Manual.

OADS (Office of Aging and Disability Services, a branch of Maine DHHS) is the eventual
institutional pitch target. The path is: personal use → team → agency → OADS.

---

## People

- **Josh** — builder, social services case manager, Section 21/29 waiver program
- **Nate** — colleague, planned alpha tester
- **Dan** — colleague, used as a test client record
- **Robin** — Josh's daughter, described as "pure sunshine"
- **Bradley** — Josh's son, age 6, gregarious but a perfectionist, working through
  some behavioral challenges split between two households
- **Josh's wife** — has Wolfram Syndrome (progressive neurodegenerative condition
  causing vision loss among other things), works as a Vision Rehab Therapist at
  The Iris Network. She uses Narrator (Microsoft's built-in screen reader) primarily.
  Accessibility features in Sati matter personally, not just as a portfolio item.

---

## Repository Structure

```
Sati/
  Converters/           — value converters (EnumToBool, BoolToVisibility, etc.)
  Data/                 — SatiContext (EF Core DbContext)
  Images/               — leaf.png (bodhi leaf, Build Action = Resource)
  Migrations/           — EF Core migration files
  Models/               — flat folder, all models here regardless of domain
      Enums.cs          — all enums: NoteStatus, NoteType, FormType, WaiverType,
                          UpcomingEventKind, UserRole, DepartmentAccess (pending)
      Person.cs, Note.cs, Form.cs, Incentive.cs, Settings.cs, User.cs,
      Scratchpad.cs, Event.cs, UpcomingEvents.cs, WorkdayTile.cs
  ViewModels/
      Biller/           — billing VMs (new, currently empty)
      Children/         — children's case management VMs
      Supervisor/       — supervisor dashboard VMs
      CaseManagerDashboardViewModel.cs   — singleton, the primary VM
      NotesWindowViewModel.cs
      SchedulerViewModel.cs
      SettingsViewModel.cs
      LoginWindowViewModal.cs
      NewClientViewModel.cs
      NewUserViewModel.cs
      ComplianceReviewViewModel.cs
      ScratchpadHistoryViewModel.cs
      ShellViewModel.cs
  Views/                — XAML windows and user controls
  Services/             — flat folder, all services here
  Edi/                  — NEW namespace for EDI/837P generation (to be created)
      IClaimDocumentFactory.cs
      Claim837PFactory.cs
      IClaimSubmissionService.cs
      FileSystemSubmissionService.cs
  Tools/                — PowerShell maintenance scripts (see Tools/README.md)
      Test-MigrationChain.ps1   — replays migrations, flags from-scratch failures
      Test-SchemaDrift.ps1      — model snapshot vs actual database columns
      New-SatiUser.ps1          — out-of-band account/role recovery
  appsettings.template.json     — committed; appsettings.json itself is gitignored
```

---

## Current Development State (as of mid-April 2026)

### Completed phases 1–8:
Authentication, DI, client management, notes workflow, settings, scratchpad,
productivity dashboard, incentive system, scheduler, upcoming events, client detail
view, Notes window, branding, global error handling, README, self-contained publishing,
ScratchpadHistoryWindow, font/spacing polish, out-of-month warning, font size controls,
spell check, supervisor dashboard (team overview, overdue items, monthly productivity
with stacked bar chart).

### Added August 2026 (second-machine setup session):

**The administrator invariant — Sati never runs without an Admin account.** `App.OnStartup`
counts admins after `Database.Migrate()` and opens `FirstRunAdminWindow` at zero; refusing
it shuts the app down. `UserService.CreateFirstAdminAsync` re-checks server-side;
`UserManagementViewModel` blocks demoting the last Admin. This exists because the loop was
closed: the login screen only ever creates CaseManagers and the role editor sits behind an
admin-gated tab, so a database with no Admin could never grow one. Do not "simplify" either
guard away — they are two ends of one invariant. Note this is a **tenant** admin, scoped to
one installation's database.

**There is no cross-tenant/global administrator role, and one must not be added to the
tenant application.** Decided, not deferred — see the scope calls in `DECISIONS.md` and the
full design record in `REGULATORY_CONCERNS.md`. Restricting a global-admin build to
Robin-Bradley AMS machines by policy is a policy control over a code capability; if the
build can do it, one copied config file puts it in an agency. Cross-tenant administration,
if ever needed, belongs in a separate application that tenant installs do not contain.

Vendor support access (agency-granted, time-boxed, PHI-redacted) is fully specified in
`REGULATORY_CONCERNS.md` and deliberately unbuilt: it depends on audit logging, and
redaction must live at the projection boundary, never as UI masking. Until then, support is
a screen-share with the agency's own admin signed in. If this comes up, the design is
already written — don't re-derive it.

**Migration hygiene.** A working database only ever receives new migrations, so migrations
that replay earlier ones, and columns that exist in the model but no migration ever creates,
are both invisible locally. Two of the former and one of the latter were found and fixed.
Run `Tools/Test-MigrationChain.ps1` and `Tools/Test-SchemaDrift.ps1` before any release, and
prefer `COL_LENGTH`-guarded SQL when a migration must tolerate databases already patched by
hand.

### Immediately next (in strict order):

1. **IDbContextFactory refactor** — replace constructor-injected SatiContext with
   IDbContextFactory<SatiContext> across all services. Each service method creates and
   disposes its own context via `await using var context = _contextFactory.CreateDbContext()`.
   This was scoped before any new features due to cascading EF Core change tracker
   collisions discovered during a productivity panel bug fix. DO NOT skip this.

2. **Department model and migration** — new `Department` table with `Name`, `IsActive`,
   and `Access` (DepartmentAccess flags enum). Foreign key on `User` (`DepartmentId`,
   nullable). `DepartmentAccess` is a [Flags] enum stored as int:
   ```csharp
   [Flags]
   public enum DepartmentAccess
   {
       None           = 0,
       CaseManagement = 1,
       Billing        = 2,
       Supervisor     = 4,
       Reports        = 8,
       Admin          = 16
   }
   ```
   Tab visibility in ShellViewModel derives from role AND department access flags.
   UserRole enum stays as CaseManager, Supervisor, Admin — Billing is NOT a role,
   it is a department with DepartmentAccess.Billing flag set.

3. **Running daily average feature** — display in the productivity panel.
   Formula: count of notes with Status == Pending || Logged and EventDate in current
   month, divided by days worked to date (workdays minus scheduler exclusions minus
   settings weekday/holiday exclusions). Refresh when LoadMonthlyNotesAsync completes
   and when OnIsSchedulerOpenChanged fires. Rounds to one decimal place.

4. **Billing module** — full build starting with:
   - Models: ClaimRecord, ProcedureCode, Submission, Remittance (flat in Models/)
   - ClaimStatus enum (in Enums.cs): Draft, Submitted, Acknowledged, Rejected,
     Adjudicating, Paid, Denied, Adjusted, Voided
   - IBillingService / BillingService (flat in Services/)
   - Edi/ namespace with IClaimDocumentFactory and FileSystemSubmissionService (stub)
   - ViewModels/Biller/: BillingViewModel, ClaimsQueueViewModel, SubmissionsViewModel,
     RemittancesViewModel, BillingDashboardViewModel, ProcedureCodesViewModel
   - XAML billing window modeled on the dense Bloomberg-inspired mockup
     (saved as sati_billing_panel_v2.html in the repo or alongside it)

### Active deferred bugs (tracked in AGENDA.md):
- Scheduler day-of-week column alignment shifts month-to-month
- Stale ExcludedDates entries persist in Incentive JSON after Settings changes
- Settings currently global rather than per-user
- AbandonedAfterDays still hardcoded in one place
- Compliance checklist due dates: Q4R should be day 360 not 365; PCP and
  CompAssessment calculated from anniversary minus offset days
- Accessibility: icon-only buttons missing AutomationProperties.Name, compliance
  checkboxes unassociated from labels, color-only overdue indicators

---

## Architecture and Key Patterns

**Registration:**
- MainWindow and CaseManagerDashboardViewModel are SINGLETON. This was a resolved bug.
  Never register them as transient.
- All services are registered in App.xaml.cs via Microsoft.Extensions.DependencyInjection.

**IDbContextFactory (pending refactor):**
- Target pattern for all services:
  ```csharp
  await using var context = _contextFactory.CreateDbContext();
  ```
- Do not inject SatiContext directly into services after this refactor.

**Settings:**
- Single-row table in the database, not local config files.
- Rationale: future multi-agency compatibility — each agency database has its own row.

**Incentive snapshotting:**
- Incentive.UnitsPerDay snapshots Settings.ProductivityThreshold at record creation.
- Changing today's threshold must not retroactively alter past months' expectations.
- This is intentional. Do not "fix" it.

**Cross-window communication:**
- Events on the singleton CaseManagerDashboardViewModel.
- Handlers unsubscribed on window close to prevent memory leaks.
- NoteChanged event exists for cross-window refresh.

**Window factory pattern:**
- Func<TWindow> factory delegate for window creation.
- Event pattern: VM fires event, code-behind subscribes via factory lambda.

**CommunityToolkit.Mvvm patterns:**
- [ObservableProperty] single-line format for copy-paste efficiency.
- partial void OnXChanged() for property change side effects.
- RelayCommand for commands.

**ICollectionView:**
- Used for filtering/sorting without modifying source collections.
- Requires Refresh() not Add()/Remove() to update filtered view.

**XAML conventions:**
- Complete file replacements preferred over partial snippets for XAML files.
- Inter font globally via TextElement.FontFamily on each Window.
- Cambria for narrative TextBox and scratchpad.
- Segoe MDL2 Assets for icons.
- Bodhi leaf: pack://application:,,,/leaf.png

**HIPAA boundary:**
- Audit log must never contain note narrative content (PHI).
- Six-year HIPAA retention requirement.

---

## Billing Domain Knowledge (accumulated this session)

**The claim flow:**
```
Note logged → Supervisor approves → Billing queue → 837P generated →
Office Ally (clearinghouse) → MIHMS (Maine Medicaid) →
835 remittance → EFT payment to agency bank account
```

**MIHMS** = Maine Integrated Health Management Solution. Maine's Medicaid Management
Information System, operated by Gainwell Technologies. Claims submitted via
clearinghouse (not directly in most cases).

**Clearinghouse** = Office Ally (free, for development/testing) or Inovalon
(what Credible uses). Validates 837P format, routes to MIHMS, returns 999
acknowledgment (minutes) and 835 remittance (14-30 days for MaineCare).

**837P structure** — key segments for TCM billing:
ISA/GS/ST envelope → BHT → NM1 loops (submitter, receiver, billing provider,
subscriber/client, payer, rendering provider) → HL hierarchy → CLM claim →
DTP service date → REF authorization → SV1 service line → SE/GE/IEA close.

**Procedure codes** — likely T1016 (TCM per 15 min) for Targeted Case Management
under Section 17. Verify with agency billing department. ProcedureCode model:
Code, CodeSet ("HC"), Modifier1, Description, UnitRate, UnitType, IsActive.

**ClaimRecord** ties a Note to a submitted claim. Stores Raw837P and Raw835 as
strings for audit purposes. ClaimStatus tracks the full lifecycle.

**835 remittance** contains CARC codes (Claim Adjustment Reason Codes) and RARC
codes (Remittance Advice Remark Codes). Parser must translate these to human-readable
text for billing staff.

**Department sub-tabs on billing dashboard** are driven by the Department table —
not hardcoded. Same Department entities that case managers belong to appear as
filterable views in the billing dashboard. This is by design.

**Billing UI mockup** is saved as sati_billing_panel_v2.html. Dense, Bloomberg-
inspired information density. Sidebar navigation, static metrics strip, filterable
claims table, flagged claims with inline reason text, submissions history with
file IDs, remittances list, dashboard with static top row and department sub-tabs.

---

## How to Work With Josh

**Explain before you write.** Josh reads every line of code carefully rather than
copy-pasting. Name patterns explicitly (factory delegate, snapshotting pattern,
fire-and-forget, etc.) and explain the why before the what. He will ask if he
doesn't understand something — that is a good sign, not a problem.

**Architecture before implementation.** Josh consistently wants the conceptual
walkthrough before writing any code. If you skip this he will ask for it anyway.

**Do not flatter.** Josh finds sycophancy irritating. Direct correction without
softening is preferred. Push back when his thinking is off.

**Do not over-engineer.** Josh identifies over-engineered solutions and pushes back
effectively. Match the complexity of existing patterns in the codebase. Consistency
over purity.

**Tangents are productive.** Josh thinks across domains — Buddhist practice, career
planning, Maine human services policy, binary arithmetic, phase balance in three-phase
electrical systems. Engage with tangents seriously before returning to code. The
connections are usually real.

**Deferred items go in AGENDA.md.** Nothing is silently dropped. If something comes
up that should be addressed later, name it and note it.

**Quiz sessions.** Josh periodically requests sessions where he explains concepts back
without looking at code. Push back when wrong, affirm when right.

**Accessibility is personal.** Josh's wife has Wolfram Syndrome and uses Narrator.
Accessibility is not a checkbox item. AutomationProperties.Name on every icon button,
logical tab order, text alternatives for visual-only information.

**The neuropsych context.** Josh has a confirmed Schizotypal Personality Disorder
diagnosis with psychotic symptoms in remission for 25 years. Top 2% in social
understanding, serial recall, and decision-making under risk. Bottom 2% in set
shifting and semantic recall. This means:
- He finds structural, logical, first-principles reasoning natural and fast.
- Arbitrary conventions and rote memory are genuinely harder for him than for most.
- When environments shift unexpectedly he needs more processing time — this is
  not stubbornness, it is a real cognitive cost.
- He has spent a lifetime in environments that misread his cognitive signature as
  scattered or odd. He is neither. The thread is always there.

**His practice.** Josh has approximately 20 years of Buddhist practice, primarily
in the Tibetan tradition (Dzogchen/Longchenpa). This informs the app's name and
his general orientation toward his work. Engage with this seriously if it comes up.

**The project matters.** Sati is simultaneously a practical tool, a portfolio piece,
and the foundation of a long-term plan to improve human services infrastructure in
Maine. Josh knows this. Don't oversell it back to him, but don't understate it either.
It is genuinely worth building and he is genuinely capable of building it.

---

## Publishing and Infrastructure

- Self-contained single-file executable: `dotnet publish -r win-x64 --self-contained true`
- Database .mdf/.ldf files backed up to OneDrive. Publishing does not affect them.
- GitHub repository for version control.
- AGENDA.md in repository for project tracking.
- Phase 9 (planned): Azure SQL migration, HIPAA audit logging, Microsoft Entra ID
  evaluation, MSIX packaging, per-agency database isolation.
- Future: Sati.Api (ASP.NET Core), Sati.Mobile (MAUI), Azure Cognitive Services
  speech-to-text for field note dictation (HIPAA BAA confirmed for Azure Speech).
- HIPAA-compliant AI: use AWS Bedrock (Claude) or Azure OpenAI — not the standard
  Anthropic API key — when PHI may be present in prompts.
- De-identification pattern: strip client name/identifiers before sending to
  standard API, rehydrate after. Valid for non-PHI policy document RAG.

---

*Last updated: April 2026. Update this document at the end of significant sessions.*
