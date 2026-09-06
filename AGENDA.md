# Sati — Refactor Agenda

## Release 1.2.49 — 2026-09-06 (DATT audit in progress)

"Clear queues and current demos." This release carries the supervisor approval filters and
newest-first paging, billing-period names and 837P readiness guards, clearer administrator
password-reset outcomes, the room-dock chat presentation, and the versioned daily Demo caseload
refresh worker. The refresh remains synthetic-only and is distinct from a full baseline reset.

- [x] Preflight: `master` equals `origin/master`; no migration file changed or was added;
      `SatiContext` reports no pending model change; the temporary SQL firewall rule is absent.
      No Demo migration or Production deployment is required or authorized.
- [x] The daily Demo refresh was deployed separately under its restricted managed identity and
      passed a live run plus an immediate repeat run before this DATT release began.
- [x] Version 1.2.49 is coordinated. The Release build completed with zero errors and nine known
      warnings; 2,067 automated tests passed, zero failed, and one optional local-AI case was
      skipped. The dependency advisory audit covered all 13 projects and found no known vulnerable
      packages. Focused security, paging, rendering, billing, and password-reset tests are included
      in that passing total, and the changed-source secret scan found no credentials or keys.
- [x] Verified source commit `34e06ff` is on `master` and `origin/master`. Demo API deployment
      `157642b8a4ad4dc6ac25b0240115e402` succeeded from the 8,780,461-byte 32-bit
      framework-dependent package with SHA-256
      `cafee80876708b3b8398d04713f0e4066d862f689567856e93f9fe92cfc5ff94`.
      Live and ready are healthy; `/health/version` reports Sati.Api 1.2.49 and contract revision
      `2E69F7DDF962`. The supervisor-filter route also returned 401 without authentication.
- [x] Both new installers passed acceptance and were published with matching checksum files without
      overwriting prior releases. The Demo installer passed five responsive launches, normal closes,
      exact version, and cleanup; it is 101,281,792 bytes with SHA-256
      `821358eb4202d1db27b99a714579212b7b683655940cde05cc2ca4f8445f4d40` in
      `SatiLogica Demo Files`. The Local installer passed exact-version, Microsoft-signed embedded
      LocalDB, integrated-security, and cleanup checks; it is 203,330,314 bytes with SHA-256
      `6b98a068bd77bc5401638beebe0d12f4d01a5a464826e15ae328ad7246896268` in
      `Sati Desktop`. Final copies and checksum contents were independently reverified.
- [x] Evidence commit `57e2572` is pushed; this checked ledger entry indexes that commit. No
      Production API deployment, Production migration, Demo migration, or firewall change belongs
      to or was performed by this release.

**Branches.** Fully merged `signature-portal-design` was safely deleted locally and remotely at
tips `9c19af4` and `359e8ef`. `team-chat-design`, `second-machine-setup`,
`origin/claude/local-vs-github-workflow-dlcqpb`, and the linked
`claude/cool-jang-f6b3c4` worktree are retained because they contain unique, uncertain, or active
context.

## Release 1.2.48 — 2026-09-06

This release carries the reviewed in-app team chat and synthetic electronic-signature portal work.
Both features remain disabled by default; the signature portal is restricted to synthetic testing.

- [x] Demo migration 96 applied to the identity-validated `SatiDemo` database after explicit
      authorization, using a guarded exact-history check. The two new migrations added the chat and
      signature schema; the temporary workstation firewall rule was removed and verified absent.
- [x] Release build and automated verification passed: 2,036 passed, 0 failed, 1 optional AI case
      skipped; nine simulated portal-page checks passed. Demo desktop build succeeded with two
      existing EF1002 warnings. Detailed evidence is in `SIGNATURE_PORTAL_VALIDATION.md`.
- [x] API route inventory and documentation reconcile to 168 protected routes; dependency advisory
      lookup covered 13 restored projects and found no known vulnerable packages.
- [x] Source commit `9c19af4` is on `master` and `origin/master` without history rewriting. The
      Demo API deployment `7e5d70b57e574cb5bfd27262123c2ff4` reports live, ready, and release
      `1.2.48`; its 32-bit framework-dependent package is 8,772,786 bytes with SHA-256
      `42b3dde9daf3d05a158d7f91446f2836f56dfce25ea31af2ad9063a732ab324f`.
- [x] Accepted installers are published without overwriting prior versions. Demo is 101,277,696
      bytes (`bdec62e1521d7bdabbe50e9106b90c4a122d80a0176fd0d4a138ca2c80061dbd`) in the Demo
      distribution folder; Local is 203,597,066 bytes (`7aac1f222b21a2e2f71f1c8764cd36f415158797104fb44bb2cd35bd4c78a8d9`) in the Desktop folder. Both checksum files match.
- [x] Final evidence commit `5e2a3f9` is on `master` and `origin/master`. No Production API
      deployment or Production migration was performed or authorized by this entry.

### Local Production machines

This release changes the local schema as well as the Demo schema. Demo migration 96 is applied;
the desktop applies the same pending migrations when each Local Production machine next starts.

- [ ] SatiLogica workstation: installed version is not rechecked after the release; treat it as
      behind until `1.2.48` is installed and the startup migration completes.
- [ ] Joshu workstation: version remains unverified and must be treated as behind until its
      operator installs `1.2.48` and confirms successful startup migration.


## Unreleased follow-up — fixed Overview roles, faster Statistics, and structured Today's Work

- [x] Remove the Overview Workspace selector, Focus note mode, duplicate Notes workspace, and the
      obsolete center-layout setting and local preference service.
- [x] Keep Current note left, Work Agenda center, and Upcoming Due Dates right at desktop widths;
      stack those same live panels below 1080 effective units. Keep only Productivity in the lower
      center band, shown when at least 700 effective height units are available.
- [x] Replace the compact note header's repeated client name with the nearest open, ready-to-open,
      upcoming, or overdue form state and its relevant dates.
- [x] Replace Statistics' full yearly-note reads with a narrative-free date/minutes projection and
      actor-scoped monthly API totals. Start independent report reads together, show progress and
      load failures, and prevent an older request from overwriting a newer date filter.
- [x] Add local range/scope tests, API tenant isolation and route-manifest coverage, form-status
      tests, and responsive WPF render checks. No database schema change or migration is required.
- [x] Keep the freeform Today's Work scratchpad while grouping today's Scheduled notes into
      Paperwork, Visits, Calls, Emails, and Freeform. Keep Work Agenda in the center at desktop
      widths and bound the structured list so the scratchpad remains usable under Easy Eyes.
- [x] Split new Contact entry into Phone and Email without renumbering legacy enum values. Preserve
      a future plan's specific type, optional form type, and estimated minutes while its actual start
      time remains unset.
- [x] Turn selected sign-in paperwork into retry-safe Scheduled Form notes and omit Scheduled-note
      duplicates from the prompt. Start a row in the existing note panel as an unsaved Pending draft,
      updating that same row only on Save.
- [x] Default started work to the earliest five-minute-grid opening that fits its estimated minutes;
      retain client, type, date, and bracketed replacement text. Support Start, double-click, Enter,
      keyboard focus, screen-reader names, inline load failure, and narrow rendered layouts.

Validation: the complete solution builds with zero errors; 1,340 desktop/domain tests pass with the
one documented optional local-AI competence test skipped, all 386 API tests pass, and all 4 Carika
tests pass. The real Work Agenda view was also rendered at a 360-by-700 effective Easy Eyes width;
all five groups scroll vertically, Start remains visible, and the scratchpad retains usable height.
EF reports no pending model changes.

## Release 1.2.47 — 2026-09-05

"Room to Work." The Overview now responds to the WPF space it actually receives, including window
size, Windows scaling, and Easy Eyes. It moves stable, live workspaces through Wide, Balanced, and
two Compact arrangements without discarding draft state. Work Agenda is the default center
workspace for a missing preference, explicit existing preferences remain respected, and Focus note
temporarily gives the current note the available workspace. Notes, Forms, and Deadlines now explain
their empty, loading, failed, or unselected scope where the existing load boundary can establish it.

Case Management now opens directly onto one feature-navigation row. Help contains Guidance and
Reference; Documents contains AT Requests, Authorized Rep, and Releases. The supervisor Pending
Approvals screen retrieves 10 notes at a time and adds an explicit maximum-units batch command. Each
approval still crosses the normal server or LocalDB boundary for reviewer scope, compliance,
revision, note validity, service-time conflict, and audit checks. Client-save errors now distinguish
a confirmed save followed by a failed screen refresh from a truly unknown save result.

`DISPLAY_MODES_DESIGN.md` records the implemented responsive contract. `LOGGING_DESIGN.md` records
a separate proposed diagnostic-logging and support-bundle design; that document adds no runtime
logging behavior in this release.

**No schema change.** No migration was added or touched, and `dotnet ef migrations
has-pending-model-changes` confirmed that the model still matches the migration snapshot. No Demo
migration or firewall rule is needed.

**Desktop-and-API release.** The API adds the bounded supervisor review-page route and the optional
automatic-approval threshold, so the matching API must be deployed before either desktop installer
is distributed.

- [x] Release-configuration build across the complete solution succeeded with 0 errors. Full test
      suite: 1,325 desktop/domain passed with 1 documented optional local-AI competence test skipped,
      382 API passed, and 4 Carika passed. The first API run correctly caught the new paging route
      missing from `ApiSurface.Routes`; adding it changed the contract revision to `79FB0BD6EAA2`,
      after which the complete affected build and desktop/API suites passed.
- [x] Version 1.2.47 coordinated across desktop/API assemblies, installer builders, readiness
      defaults, installer examples, release-note tracker, and release assertions. Source commit
      `ba129510321cafb10c29ad42001fbd3ab2227ed5` was pushed to `origin/master` before artifact
      generation.
- [x] Published the Demo API. The package was built from the pushed source commit with 70 entries,
      0 backslash entry names, no `appsettings*.json` or key files, and both expected triggered
      WebJob scripts. Packaged `Sati.Api.dll` reports file version `1.2.47.0`.
      `artifacts/Sati.Api-1.2.47.zip` is 9,871,401 bytes with SHA-256
      `FE331B880FA84276BA634FEC241398035702083A73168477C5CA02290747C5F8`. OneDeploy deployment
      `94fd732e970b463b813b4518f357b5df` to `sati-demo-api-satilogica` in `rg-sati-demo` completed
      with `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, and `/health/version` reported product `Sati.Api`, release
      `1.2.47`, contract revision `79FB0BD6EAA2`, equal to the local 146-route manifest.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.47.exe` is 101,117,952 bytes with SHA-256
      `CB3876AE402B4D7BF1189AB5C897811690C445E47EE60B3824D9868CEE2E38ED`. Five 15-second launches
      each reached the visible sign-in window, remained responsive, closed normally with exit code
      0, reported installed version `1.2.47.0`, and cleaned up. This ran on the build workstation,
      so it is not an external clean-machine attestation. Its 92-byte checksum file has SHA-256
      `BA75A7D440B18A665323820EF220A0B805CF9DB10B758F7F6B7F9889232B2462`.
      Local: `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.47.exe` is 203,182,346 bytes with
      SHA-256 `C466A389498C0328B08378EC1FD6469C280D0D09B0B76C7C7A5160C2DA98D135`. Acceptance verified
      installed version `1.2.47.0`, Windows integrated security, no SQL username or password, and
      cleanup. The embedded `SqlLocalDB.msi` carries a Valid Microsoft Authenticode signature. The
      Local checksum file is 93 bytes with SHA-256
      `CC183140CF575DB51A38305ADF24B643A151162EB24CBE2BED16CA897C630103`. Neither installer is
      code-signed.
- [x] Published the installers and checksum files without overwriting existing files:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.47.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.47.exe`
        and its `.sha256`

      Every published SHA-256 was re-compared with its accepted artifact and matched; no temporary
      distribution file remained.
- [x] Final evidence commit with deployment, health, test, acceptance, distribution, and hash
      evidence — this entry.

**Local Production machines.** No schema change, so no workstation migration record is required.

**Branches retained, not deleted.** `second-machine-setup` and
`origin/claude/local-vs-github-workflow-dlcqpb` retain unique work whose current status is uncertain.
`claude/cool-jang-f6b3c4` is fully merged but remains checked out by its linked worktree, so it fails
the safe-deletion rule. No branch is merged or deleted for this release.

**Validation warnings.** NuGet could not query the vulnerability-feed service index during the
build, though restore and compilation succeeded from the resolved package graph. Existing warnings
also remain for the deliberately escaped SQL `BACKUP` statement and test-only nullability/xUnit
style. None was introduced by this release scope, and all required build and test gates passed.

## Release 1.2.46 — 2026-09-04

"Middle Ground." Corrects 1.2.45. That release read "the Notes pane on the Overview" as the whole
role dashboard and traded it with the Scratchpad at the shell level, so turning the setting on only
moved the Scratchpad from the right edge of the screen to the left — reported by Josh with a
screenshot the same afternoon. The intended swap was between the Overview's *middle* notes column
and the Scratchpad, and that is what it now does.

The filter bar and notes grid are extracted into `Views/NotesPanelView.xaml` so one control renders
in either position; it trades places with `ScratchpadView` between the Overview's middle column and
the shell's collapsible side panel. `ShellWindow.xaml`'s main content area returns to a fixed
column, reverting 1.2.45's `Grid.Column` swap.

Two decisions worth keeping:

- **The side panel only takes the notes panel while the Overview is on screen**
  (`ShellViewModel.ShowNotesInSidePanel`). The other Case Management tabs have no notes panel to
  host, so the side slot keeps the Scratchpad rather than leaving Today's Work unreachable.
- **The centered Scratchpad is the shell's own `ScratchpadViewModel`, handed down through
  `CaseManagerDashboardViewModel.AttachScratchpad`, not injected.** That type is registered
  `AddTransient`, so resolving a second one would have compiled and rendered correctly while
  silently discarding whatever was typed into it — the shell saves only its own instance on close
  and on user switch. DI lifetime is a correctness property for a view model holding unsaved text.

**No schema change.** No migration was added or touched; `dotnet ef migrations
has-pending-model-changes` confirmed clean. No firewall rule needed for this release.

**Desktop-and-API release.** Both assembly versions move together per `StabilizationTests`; only
the desktop client changed in substance, so publishing the API is version-parity, not a functional
deploy.

- [x] Release-configuration build across `Sati.csproj` and `Sati.Api/Sati.Api.csproj`, 0 errors.
      Full test suite: 1,293 desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika
      passed — confirmed both before and after the version bump. New coverage:
      `Sati.Tests/ScratchpadSwapRenderTests.cs` (3 tests) loads the real Overview and reads back
      which panel is actually visible in each state, plus asserts the centered Scratchpad is bound
      to the handed-down instance. Added deliberately: 1.2.45's mistake compiled, passed every
      test, and was only detectable by looking at the screen.
- [x] Version bump to 1.2.46 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs` (title "Middle Ground"), with
      matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`. Fix commit
      `62fa6afc1a55cd1c968170a117699611857e3e59` and release commit
      `2c372bdbb2ca429d32349720a023cf4883a86f42` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10, 70 entries, 0 backslash entry names,
      `artifacts/Sati.Api-1.2.46.zip` (9,867,277 bytes; SHA-256
      `E2A0156618106DE681474226407E940D07C9A6653A6C6257C6FD2D81E11C2C04`). No `appsettings*.json`
      present; both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files confirmed
      present. Packaged `Sati.Api.dll` reports file version `1.2.46.0`.
      OneDeploy deployment `453b2ba3f9da46fd8fd483c8ab45851f` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.46`, contract revision `78B5A2F71629` — unchanged, expected since nothing in this
      release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision` computed
      locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.46.exe` (101,122,048 bytes; SHA-256
      `59f37e20624bd74e3c02ac36841874335fdac8bc15f4de45e71f9cff10d7fcf4`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.46.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.46.exe` (203,165,450 bytes; SHA-256
      `0720f35e3f39b007dc480e252f80eff93a861b0865b16333193acdd8577d7eb0`). Acceptance passed:
      installed version `1.2.46.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.46.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.46.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

**Local Production machines.** No schema change, so nothing is pending on any workstation beyond
installing the new build. Known machines and the release each is on are not tracked here yet; this
release adds no migration, so a machine still on an older build is behind only in features.

**Branches retained, not deleted.** `second-machine-setup` (5 unmerged commits, including "Require
an administrator account before Sati will run") and `origin/claude/local-vs-github-workflow-dlcqpb`
(1 unmerged commit) both hold unique work of uncertain status. `claude/cool-jang-f6b3c4` is fully
merged but still checked out by the linked worktree at `.claude/worktrees/cool-jang-f6b3c4`, which
fails the safe-deletion rule.

**Known flake, not caused by this change:** one full desktop run aborted with a test host crash
("Fatal error") after 918 tests. It did not reproduce across five subsequent clean runs in both
Debug and Release, and was not root-caused. The shared WPF `Application` on a single STA thread is
the likely area if it recurs.

## Release 1.2.45 — 2026-09-04

"Trading Places." A new Settings option, "Display Scratchpad in the center of the display," swaps
which content occupies the Overview's main panel versus its collapsible side panel: on, the
Scratchpad (Today's Work / Tomorrow's Agenda) fills the main area and the role dashboard collapses
to the side; off (the default), it's the arrangement Sati has always had. The same chevron still
toggles whichever content ends up on the side — the code-behind collapses by grid column index, not
by which control is in it, so the swap needed no change to that collapse/restore logic. Invoked via
`invoke DATT!` the same evening as 1.2.42/1.2.43/1.2.44.

**No schema change.** No migration was added; `dotnet ef migrations has-pending-model-changes`
confirmed clean (`--project Sati.Persistence --startup-project Sati.Persistence`). No firewall rule
needed for this release.

**Desktop-and-API release.** Both assembly versions move together per `StabilizationTests`, though
only the desktop client changed in substance — the API's own behavior is unaffected, so publishing
it is routine version-parity, not a functional deploy.

- [x] Release-configuration build across `Sati.csproj` and `Sati.Api/Sati.Api.csproj`, 0 errors.
      Full test suite: 1,290 desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika
      passed — confirmed both before and after the version bump. New coverage:
      `Sati.Tests/ScratchpadLayoutPreferenceTests.cs` (4 tests: default, persistence and
      notification, per-user/environment isolation, corrupt-file fallback), mirroring the existing
      `ConsumerPickerSortPreferenceTests.cs` pattern.
- [x] Version bump to 1.2.45 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title
      "Trading Places"), with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`.
- [x] Source commit `b80e552495e39b6bbccf59fd698ca78e2e89ea56` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10, 70 entries, 0 backslash entry names,
      `artifacts/Sati.Api-1.2.45.zip` (9,867,299 bytes; SHA-256
      `0E3D2FAC3D103BF900D4547E931FB220E41FC98655A85A93FD4AE17837B8E5FB`). No `appsettings*.json`
      present; both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files confirmed
      present. Packaged `Sati.Api.dll` reports file version `1.2.45.0`.
      OneDeploy deployment `4571aa5a42404a2bab3eb37f2cd033c8` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.45`, contract revision `78B5A2F71629` — unchanged, expected since nothing in this
      release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision` computed
      locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.45.exe` (101,122,048 bytes; SHA-256
      `e323d67aa8fbdad290d7293f51658a35274d8d8f3bc79811cdb416dc63c5e8ea`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.45.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.45.exe` (203,165,450 bytes; SHA-256
      `3a5bd5a695e53f499732d80bc89695b9f9c015662e4a7e3e9d531eb3d139e0d5`). Acceptance passed:
      installed version `1.2.45.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.45.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.45.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

**Known open question, not a defect in this release:** `CaseManagerDashboardContentView` (the
Overview's dashboard content) is a five-column layout designed for the wide main panel. When this
setting is on, that view renders in the narrow ~300px collapsible side column instead. Whether it
degrades gracefully there has not been checked against a running Overview screen — flagged to Josh
at implementation time and repeated here rather than silently shipped as untested. Follow-up, if
needed, would be reflowing that view for a narrow column, not part of this release.

## Release 1.2.44 — 2026-09-04

"Last, First." Three follow-ups from testing 1.2.42/1.2.43: the client-list sort preference now
also changes the printed name format, not only list order; the dashboard's sub-navigation pill
uses the lightened button colors in Blue-Gray Pearl and Cedar Grove instead of the darker,
un-lightened accent; and — landing in release notes for the first time, having shipped between
DATT invocations — the Admin dashboard's new Status control, which is the actual answer to "how do
I get a consumer off my active list when it predates creation-date tracking and can never qualify
for the 20-day delete window." Invoked via `invoke DATT!` the same evening as 1.2.42/1.2.43.

**No schema change.** No migration was added; `dotnet ef migrations has-pending-model-changes`
confirmed clean. No firewall rule needed for this release.

- [x] Release-configuration build across the full solution, 0 errors. Full test suite: 1,286
      desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika passed — confirmed both
      before and after the version bump. Includes a new WPF render test
      (`TheSelectedClientNameFormatFollowsTheSortPreference`) that loads the real client-picker
      ComboBox and reads back its rendered text in both sort states, rather than trusting the
      RelativeSource binding crossing the item template boundary to resolve correctly untested.
- [x] Version bump to 1.2.44 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title "Last, First"),
      with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`. Release notes also cover the Status control
      shipped in the prior commit, which had not yet appeared in a DATT-invoked release.
- [x] Source commit `095d6f855378c8b7826dbff55b61ed9b31b95b48` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10, 70 entries, 0 backslash entry names,
      `artifacts/Sati.Api-1.2.44.zip` (9,867,291 bytes; SHA-256
      `DD9EBB28896BF5E19F36ED424FD17259F9008529961946F24480AF6E31E624F9`). No `appsettings*.json`
      present; both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files confirmed
      present. Packaged `Sati.Api.dll` reports file version `1.2.44.0`.
      OneDeploy deployment `63854beb55d148b29be3195c6e5f60fe` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.44`, contract revision `78B5A2F71629` — unchanged, expected since nothing in this
      release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision` computed
      locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.44.exe` (101,101,568 bytes; SHA-256
      `c5831483d9bbf94a7c7b8fb0b8244f74e5e3bed7e56d693037e5e1070fc518a1`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.44.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.44.exe` (203,165,962 bytes; SHA-256
      `9752cbbfbfc320202bb75deabcc30769350fe139f02a9c7e7f7f8c360b13e395`). Acceptance passed:
      installed version `1.2.44.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.44.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.44.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

## Release 1.2.43 — 2026-09-04

"A button that explains itself." Fixes the Admin rule-3 delete button, which silently disabled
itself with no explanation whenever the Reason field was empty — reported directly by Josh after
using 1.2.42 ("the delete button wasn't working"). Also adds a personal Settings preference to sort
the Notes client picker by last name. Invoked via `invoke DATT!` the morning after 1.2.42 shipped.

**No schema change.** No migration was added; `dotnet ef migrations has-pending-model-changes`
confirmed clean. No firewall rule needed for this release.

**Desktop-and-API release.** Both assembly versions move together per `StabilizationTests`, though
only the desktop client changed in substance — the API's own behavior is unaffected by either fix,
so publishing it is routine version-parity, not a functional deploy.

- [x] Release-configuration build across the full solution, 0 errors. Full test suite: 1,274
      desktop/domain passed (1 legitimate skip), 374 API passed, 4 Carika passed — confirmed both
      before and after the version bump.
- [x] Version bump to 1.2.43 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title "A button that
      explains itself"), with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`.
- [x] Source commit `61cd5f5eb074b93ee636d28c70e22aa36f70f8e2` pushed to `origin/master`.
- [x] Published the Demo API. Package built under .NET 10 (same throwaway console app as 1.2.42),
      70 entries, 0 backslash entry names, `artifacts/Sati.Api-1.2.43.zip` (9,866,141 bytes;
      SHA-256 `2AA98E131745CB593604AFFCD23EFBA2C001B31CD3472E1E3DF7634E0E6C1D6E`). No
      `appsettings*.json` present; both `App_Data/jobs/triggered/demo-history-reconciliation`
      WebJob files confirmed present. Packaged `Sati.Api.dll` reports file version `1.2.43.0`.
      OneDeploy deployment `4e47dc1573c74b7f9d357d0c29e8a1a9` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy`, `/health/version` reported product `Sati.Api`, release
      `1.2.43`, contract revision `78B5A2F71629` — unchanged from 1.2.42, expected since neither
      fix in this release touches `ApiSurface.Routes` — and confirmed equal to `ApiSurface.Revision`
      computed locally from the same build.
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.43.exe` (101,113,856 bytes; SHA-256
      `73cc596a0ccd211f134a677936d7ed4627f3e1d4efd2cfd7bcc18631b2cae5e1`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.43.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` still carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.43.exe` (203,160,842 bytes; SHA-256
      `75519d8a5845bf183383c1e0a59d922df1c4a8ac082ad357c22bb9d69d24aed9`). Acceptance passed:
      installed version `1.2.43.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.43.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.43.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

## Release 1.2.42 — 2026-09-03

"Room to undo a duplicate." Consumer-deletion, archive, and legal-hold foundation from the
[[Ordinary-consumer deletion, archive status, and bulk-import dedupe (2026-09-03)]] work, plus the
safety-plan/annual-document/privacy-notice workflow from
`ANNUAL_DOCUMENT_RELEASE_READINESS.md` and `NOTE_FORM_ATTESTATION_DESIGN.md`. Invoked via `invoke
DATT!`; preflight paused once for a concurrent-edit check (the working tree was still moving under
a second, independently running agent) and once for explicit authorization of the schema-changing
Demo migration below, both confirmed by Josh before continuing.

**Schema-changing release.** Seven pending migrations: `AddFormAttestations`,
`AddDocumentArtifacts`, `AddPersonCreatedAtAndStatus`, `AddLegalHolds`,
`AddDocumentTemplatesAndSafetyPlans`, `AddSafetyPlans`, `CompleteAnnualDocumentWorkflow` — 94 total
in the chain. `dotnet ef migrations has-pending-model-changes` confirmed clean against the model
before proceeding.

**Release validation, independently re-run rather than only trusted from source preparation:**
Release-configuration build across the full solution, 0 errors. Full test suite in Release
configuration after the version bump: **1,266 desktop/domain passed** (1 legitimate skip, the
opt-in local-AI-model evaluation), **374 API passed**, **4 Carika passed**. `git diff --check`
clean.

- [x] Version bump to 1.2.42 across `Sati.csproj`, `Sati.Api/Sati.Api.csproj`, the three installer
      builder script defaults, `scripts/Test-DemoReadiness.ps1` and
      `scripts/Test-DemoGlobalAdmin.ps1`'s expected-release defaults, `installer/README.md`'s
      example commands, and `Services/ProductReleaseNotes.cs`'s release notes (title "Room to undo a
      duplicate"), with matching assertions updated in `Sati.Tests/StabilizationTests.cs` and
      `Sati.Api.Tests/TenantAuthorizationTests.cs`. Verified by the Release-configuration test run
      above.
- [ ] Source commit and push to `origin/master`.
- [x] Applied the authorized Demo migration via `scripts/Apply-CompliancePlatformMigrations.ps1`
      (new script; extracted EF's own generated idempotent DDL for the seven target migrations
      verbatim via `sed`, rather than hand-transcribing it, then added fail-closed identity/
      chain-position checks and dry-run/commit control around it). Rollback-only dry run reported
      0/7 target migrations recorded and rolled back cleanly; the real run recorded 7/7 and
      committed; a second real run reported the same 7/7 with no errors, proving idempotency.
      Connected via an Azure AD access token from Josh's own `az` session (not integrated
      security — Azure SQL does not support Windows logins) through the temporary exact-IP
      `SatiDemo` firewall rule Josh added and will remove. Public IP: `72.95.106.10`.
      One bug found and fixed before the first live attempt reached the database: the guard
      script's header here-string had no trailing newline before its closing `'@`, so string
      concatenation ran the last header comment directly into the extracted DDL's first
      `IF NOT EXISTS (`, commenting it out and orphaning its closing paren (`Incorrect syntax
      near ')'`). SQL Server does not partially execute a batch that fails to parse, so nothing
      was touched before the fix; verified by reconstructing the assembled command text locally
      and confirming the fix before reconnecting.
- [x] Published the Demo API. Package built under .NET 10 (a throwaway console app calling
      `System.IO.Compression.ZipFile`, not Windows PowerShell 5.1's `System.IO.Compression.FileSystem`
      assembly, per the `.NET 10, not Windows PowerShell` lesson in `DECISIONS.md`), 70 entries, 0
      backslash entry names, `artifacts/Sati.Api-1.2.42.zip` (9,866,162 bytes; SHA-256
      `3566CA0CB4F91EC6D455B3CE3FE541343E7E55D5F504DAB7857EE4D81EC165AF`). No `appsettings*.json`
      present in the publish output (config comes from App Service settings/Key Vault, never a
      packaged file); both `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files
      confirmed present. Packaged `Sati.Api.dll` reports file version `1.2.42.0`.
      OneDeploy deployment `621076ef4057476a8a60f99548e6230a` to `sati-demo-api-satilogica` in
      `rg-sati-demo`, `provisioningState: Succeeded`. `/health/live` returned `{"status":"live"}`,
      `/health/ready` returned `Healthy` (confirming `SchemaDriftHealthCheck` accepted the schema the
      migration above produced), `/health/version` reported product `Sati.Api`, release `1.2.42`,
      contract revision `78B5A2F71629` — matching `ApiSurface.Revision` computed locally from the
      same build, confirmed via a throwaway .NET 10 console app referencing `Sati.Contracts.dll`
      (Windows PowerShell 5.1's `Add-Type` cannot load a .NET 10 assembly directly).
- [x] Built and accepted both installers.
      Demo: `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.42.exe` (101,093,376 bytes; SHA-256
      `022583f901df2baeaf949e023b9d361dfdd795c21dd027c797b49b807686d2fd`). Five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.42.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Local: built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` carries a Valid
      Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.42.exe` (203,158,282 bytes; SHA-256
      `122bef469144c1d3fb46bd2c53c2bc3a2a31afabdcd50cb51369ed60b80487ce`). Acceptance passed:
      installed version `1.2.42.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Neither installer is code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.42.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.42.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Evidence commit with final hashes, deployment identifiers, and test totals — this entry.

## Compliance attestation and annual documents — designed 2026-09-03

**Steps 1–9 implemented in source on 2026-09-03; not released or deployed.** Release preparation
and remaining operational gates are recorded in `ANNUAL_DOCUMENT_RELEASE_READINESS.md`. Full design in
`NOTE_FORM_ATTESTATION_DESIGN.md`.

The removed note-to-form bridge used a message box in `Views/ShellWindow.xaml.cs`. Saving a note
tagged with a form type could stamp `DateTime.Today`, discarding the note's event date and using
the dashboard selection. Eleven confirmed defects are inventoried in section 1 of the design. Defect 1 is the
serious one: a synthesized `CompletedDate` silently changes which past service dates
`BillingComplianceGate.IsBillingWindowBlocked` treats as billable.

Decided with Josh on 2026-09-03: a form note is **evidence that prompts an attestation, never the
attestation itself**. Prerequisites that live outside Sati must be checked before an attestation is
accepted, with a per-cycle "recorded externally" escape hatch. Document existence is recorded as
metadata plus a SHA-256, never stored bytes. Templates are per agency with a Sati default,
append-only versioned. The annual packet opens 30 days before the anniversary and renders only the
notice and permitted records request plus draft releases and the saved safety-plan content.
Consumer choices, authorization, final safety approval and form attestation remain separate work.

- [x] Steps 1 through 3 of the landing order: `FormAttestation` table and backfill, delete the
      bridge, generalize the Reviews attestation control to all twelve form types. This closes the
      "no in-app control captures an arbitrary completion date for a non-review form" item below.
- [x] Step 4: `DocumentArtifact` and the shared prerequisite registry, including metadata/hash
      recording for generated Agency, DHHS, and Medical releases, Draft handling, external-record
      capture, artifact supersession, Comprehensive Assessment before Reclassification, and a
      reasoned Supervisor technical override. `PUT /api/v1/forms/{id}` is narrowed and
      `CloudFormService.OpenFormAsync` records its opened date.
- [x] Step 5: immutable published template versions, shared resolution/token validation, a
      constrained MigraDoc composer, agency-Admin read/publish routes, and local/cloud privacy
      generation with exact template-version provenance. Josh authorized a generic provisional
      Privacy Practices default; it is visibly marked for agency/privacy/legal review.
- [x] Step 6: seven-section safety-plan authoring in WPF/local/API, locked submitted/reviewed
      versions, scoped supervisor approval/return, optimistic concurrency, PDF source provenance.
- [x] Step 7: immutable privacy-notice receipt/effort rows bound to the current generated artifact;
      generation alone no longer satisfies the prerequisite. Regeneration requires another receipt.
- [x] Step 8: agency-configurable packet window (default 30 days, 0–180 supported), one ZIP and
      manifest, profile/Annual Documents controls and read-time dashboard preparation reminder.
      Existing completed/external releases are explicitly omitted rather than reconstructed.
- [x] Step 9: SHA-256 plus byte-length verification, both during ZIP construction and through a
      staff-selected-file verifier. Primary-care recipient inherits missing organization address/
      phone; request included only after medical release attestation. Download only; staff send it.
- [ ] Replace/review the provisional Privacy Practices wording when Josh has the actual template.
      Confirm agency-specific legal effective date, privacy contact, complaint channels, uses,
      rights, and additional applicable restrictions before production use.
- [x] O-1 through O-7 engineering choices resolved: user-approved generator, Comprehensive
      Assessment prerequisite, reasoned technical override, shared safety structure with supervisor
      review, downloadable/staff-sent records request after medical release, and working verifier.
      Agency/legal review and the retention questions in R-3/R-4 remain operational work, not a
      claim of compliance. See `REGULATORY_CONCERNS.md` and the implementation addendum in the design.
- [x] Form completion and revocation now emit `form.attested` and
      `form.attestation-revoked`; document, safety-plan, receipt and packet events are documented in
      `AUDIT_EVENTS.md` without copying narratives or receipt explanations into metadata.
- [ ] Apply `20260903152847_AddFormAttestations` only through the controlled migration process.
      It has not been applied to Demo; workstation access requires the temporary exact-IP firewall
      rule that only Josh may add and remove.
- [ ] Apply `20260903173950_AddDocumentArtifacts` through the same controlled migration process.
      It has not been applied to Demo or Production.
- [ ] Apply `20260903185920_AddDocumentTemplatesAndSafetyPlans` through the controlled migration process.
      It creates the versioned table and provisional default; it has not been applied to any runtime database.
- [ ] Apply `20260903190302_AddSafetyPlans` and `20260903200511_CompleteAnnualDocumentWorkflow`
      through the same controlled process. No runtime database was changed during preparation.
- [ ] Sati's comprehensive assessments and PCPs are development-only; Evergreen holds the
      production records. Both map to no prerequisite until an Evergreen API integration exists,
      which is a `REGULATORY_CONCERNS.md` item first.

## Release 1.2.41 — 2026-09-03

"A quieter screen." Case note template from the checked meeting facts, a suggested follow-up that
actually appears, an inactivity privacy screen, and lighter buttons in the two orange palettes.

**No schema change.** No migration was added and no persistence contract moved. `UpcomingEventKind`
gained a display-only `UpcomingForm` value; the enum is not persisted and is not part of any DTO.

**Desktop-only release.** `ApiSurface.Revision` is unchanged at `E807EDE42231`, so a 1.2.41 desktop
runs against the deployed 1.2.40 Demo API. The API assembly version moves with the desktop because
`StabilizationTests` requires the two to match, but the hosted Demo API was **not** republished:
that was not part of the requested work. Readiness expectations now name 1.2.41 and will therefore
report a version mismatch until the API is published.

**Requested work.** Josh asked for four changes and then commit, push, merge, release notes in
Settings, a version bump, and both installers. The work landed directly on `master`, so there was
no feature branch to merge.

### Lighter buttons in the orange palettes

- [x] Split button fill from accent type: every theme now supplies `AccentButtonBrush`,
      `AccentButtonHoverBrush`, `AccentButtonPressedBrush`, and `OnAccentButtonBrush`.
- [x] Thirteen themes copy their existing accent values, so nothing about them changed.
- [x] Blue-Gray Pearl and Cedar Grove fill buttons with `#FBA76B` over `#3B1D06` text, roughly
      7.9:1 contrast. Their `AccentBrush` stays `#E25507`, so orange type is untouched.
- [x] `PrimaryButton` is the only style bound to the button set. Selection highlights and accent
      text still bind `AccentBrush`.
- [x] Structure test asserts all fifteen themes supply all four keys, and that the two orange
      palettes' fill is measurably lighter than their accent while keeping dark text.

### Case note template

- [x] `CaseNoteTemplateComposer` writes Meeting Details, Observations, and Discussion and Activity
      from the ticked meeting controls.
- [x] It renders `CaseNoteFactCompiler.VisitFacts` rather than restating the checkboxes, so the
      template and the local-AI draft cannot phrase the same tick two different ways.
- [x] Existing narrative is preserved verbatim below a `MEETING NARRATIVE` header. Nothing is
      removed or rewritten; a second press stacks rather than replacing. See `DECISIONS.md`.
- [x] The Format with Local AI trigger is withdrawn. The drafting pipeline and its review panel are
      untouched and unreachable, so the button can return.
- [x] The button is gated on `IsVisitNote`, not `IsLocalAiEnabled`: it is not an AI feature and
      needs no model.

### Suggested follow-up now appears

- [x] Root cause found and reproduced: the row was built on `GenerateEvents`, which reports only
      forms inside their open/late window. With the default zero-day review window a quarterly
      review is open on exactly its due date, so for an ordinary client the row was blank for
      months. The existing tests drove the panel with a stub event service and never exercised the
      real generator.
- [x] `UpcomingEventService.NextFormSuggestion` reports the client's next outstanding form
      regardless of the window, sharing the one form table, `GetCurrentCycleForm`, and
      `IsSatisfiedAsOf`, so it cannot name a form the compliance gate treats as satisfied.
- [x] `NoteEntryViewModel` prefers an actionable event and falls back to that suggestion.
- [x] Regression tests exercise the real service. They fail against the unfixed code: the probe
      that found this asserted `GenerateEvents` returns nothing for a client effective today, which
      it does.

### Inactivity privacy screen

- [x] After a configurable idle period Sati blurs its whole window behind a Paused card. Any key or
      click clears it, and that first input is consumed rather than delivered.
- [x] `IdleSessionState` owns the rule behind an injectable clock; `ShellWindow` supplies the
      `InputManager` hook and a one-second tick. The view model references no WPF input type.
- [x] `IdleLockPreferenceService` stores the delay per Sati user, Windows profile, and environment,
      mirroring `EasyEyesPreferenceService`. No migration and no agency Settings row.
- [x] Settings offers Never and one minute through one hour, saved immediately for that account.
- [x] A bare mouse move counts only when the pointer actually travelled. Without that the overlay
      woke itself the moment it appeared, because showing it changes what is under the cursor.
- [x] Presented honestly as a privacy screen: the overlay, the Settings help text, and the release
      notes all state that it does not lock Windows. `TryDismiss` and `RequiresUnlockChallenge` are
      the seam a PIN would use.

### Validation

- [x] Release build of the full solution: 0 errors.
- [x] Sati desktop/domain: 1,184 passed, 1 skipped (the documented `SATI_RUN_LOCAL_AI_MODEL_EVAL`
      opt-in). API integration: 324 passed. Carika: 4 passed.
- [x] 26 tests added across the four changes.

### Release evidence

- [x] Source commit `675fb11ef7bda4692343c5875721e047ded1f35c` pushed to `origin/master`. The work
      was done directly on `master`, so there was no branch to merge.
- [x] Demo installer acceptance passed on
      `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.41.exe` (100,925,440 bytes; SHA-256
      `5ea7e68d023fa73bba53e2d93a075e60f1f7702652a20d67bdb8aa4a2380c515`): five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.41.0`, cleanup
      passed. Run on the build workstation, so it is not a clean external-machine attestation.
      Evidence: `artifacts/release-1.2.41-demo-installer-acceptance.json`.
- [x] Local installer built after confirming `artifacts\Prerequisites\SqlLocalDB.msi` carries a
      Valid Authenticode signature from `CN=Microsoft Corporation`.
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.41.exe` (202,985,226 bytes; SHA-256
      `0875b2389ab7cf2234ccad29c949e0e5b62c8a5f68d58e717ad2379daf2a9419`). Acceptance passed:
      installed version `1.2.41.0`, `integratedSecurity=True` with no SQL credentials in the Local
      configuration, cleanup passed. Generated installers are not code-signed.
- [x] Published both installers and their `.sha256` files. Each was copied to a uniquely named
      temporary sibling, hash-verified, renamed to the final versioned name, and verified again.
      No destination file was overwritten and no temporary file remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.41.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.41.exe`
        and its `.sha256`

      Both published hashes were re-compared against the accepted build artifacts and match.
- [x] Published the Demo API at 1.2.41. **No firewall rule was requested, added, or removed.** The
      temporary exact-IP rule exists only to reach `SatiDemo` from a workstation for a controlled
      migration; this release adds no migration, and `az webapp deploy` never touches SQL. That
      distinction is the 2026-08-30 decision in `DECISIONS.md`. The allow-list was checked
      afterwards and still holds exactly `sati-demo-api-outbound-01` through `-03`.
- [x] `artifacts/Sati.Api-1.2.41.zip` (9,673,370 bytes; SHA-256
      `E88087B5DBFA1A5F5B053217CABD9D4FFE5EF5F44543907239AB62B5B0234D23`; 70 entries). The packaged
      `Sati.Api.dll` reports `1.2.41+d1802ed4fea1ebc8728540c768faa248acff00f0`. Inspection found 0
      backslash paths, 0 forbidden configuration or key files, no `appsettings*.json`, and both
      `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files.
- [x] The first packaging attempt was discarded unpublished. `ZipFile.CreateFromDirectory` under
      Windows PowerShell 5.1 wrote 20 backslash entry names, which App Service would extract as
      files literally called `App_Data\jobs\...`. The package is now built under .NET 10 with entry
      names normalized to forward slashes, and the count is asserted before use.
- [x] OneDeploy deployment `5b1c29fdd16f4ccaa3823b6d2130844b` completed successfully to the
      existing Demo App Service `sati-demo-api-satilogica` in `rg-sati-demo`.
- [x] `/health/live` returned `{"status":"live"}`, `/health/ready` returned `Healthy`, and
      `/health/version` reported product `Sati.Api`, release `1.2.41`, contract revision
      `E807EDE42231` — unchanged, and equal to the client's `ApiSurface.Revision`. A healthy
      readiness result also means `SchemaDriftHealthCheck` accepted the deployed schema, which is
      the direct confirmation that no migration was owed. Evidence:
      `artifacts/release-1.2.41-demo-readiness.json`; authenticated Admin checks were skipped
      because this workstation has no designated `SATI_DEMO_USERNAME` and password.
- [ ] Consolidate `EasyEyesPreferenceService` and `IdleLockPreferenceService` onto one personal
      preference store. Two near-identical file-IO implementations is one too many.


## Release 1.2.40 — 2026-09-02

"Easy on the eyes." Close confirmation, full-height client editing, personal Easy Eyes mode,
Blue-Gray Pearl and Cedar Grove themes, and visible vector year-navigation arrows.

**No schema or API-contract change.** This release adds presentation behavior and local personal
preference storage only; it requires neither a database migration nor a firewall change.

### Preflight

- [x] Literal `invoke DATT!` received; `master` and `origin/master` began equal at `096e8ef`.
- [x] Release scope reviewed; no persistence migrations, API routes, or network contracts changed.
- [x] No 1.2.40 API ZIP, Local installer, Demo installer, checksum, or distribution artifact exists.
- [x] Retain `claude/cool-jang-f6b3c4` because it is checked out by a linked worktree; retain
      `second-machine-setup` because its seven unique commits are unrelated to this release.
      `feature/caseload-transfer` is already fully merged and is retained rather than deleted.
- [x] Known Local state recorded without assuming an upgrade: SatiLogica's installed executable is
      1.2.23. Joshu has the hash-verified 1.2.39 Local installer downloaded, but no executable in
      Joshu's per-user install folder; Joshu's existing shortcut targets SatiLogica's 1.2.23 copy.

### Release evidence

This release completed in a second `invoke DATT!` pass. The first pass pushed the source commit,
deployed the Demo API, and built the Demo installer, then stopped before the Local installer, the
acceptance gates, distribution, and this record. The second pass reused the existing 1.2.40 API ZIP
and Demo installer rather than rebuilding them, so no artifact was replaced under an existing
version.

- [x] Release build of the full solution: 0 errors, 10 warnings (existing NuGet vulnerability-feed
      reachability, EF raw-SQL, nullable, and xUnit analyzer warnings).
- [x] Sati desktop/domain: 1,158 passed, 1 skipped
      (`LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`,
      the documented `SATI_RUN_LOCAL_AI_MODEL_EVAL` opt-in whose on-device model prerequisite is
      absent). API integration: 324 passed. Carika: 4 passed. Totals: 1,486 passed, 1 skipped,
      0 failed.
- [x] `git diff --check` clean on the release commit; the working tree stayed clean throughout.
- [x] Source commit `ab7613776a093f13f6613ada2808b1c82cefd299` confirmed present on
      `origin/master`; the remote did not advance during the release.
- [x] `artifacts/Sati.Api-1.2.40.zip` (9,674,472 bytes; SHA-256
      `C28EBFECEA3ECDC8D18DF761944E05B33E7F587AD7F81B5E3E89CD64CEA04779`; 79 entries). The packaged
      `Sati.Api.dll` reports `1.2.40+ab7613776a093f13f6613ada2808b1c82cefd299`, proving the package
      was built from the pushed commit. Inspection found 0 backslash paths, 0 forbidden
      configuration or key files, no `appsettings*.json` of any kind, and both required
      `App_Data/jobs/triggered/demo-history-reconciliation` WebJob files. The nine entries it holds
      beyond the 1.2.39 package are directory records, not new files.
- [x] Published only to the existing Demo App Service `sati-demo-api-satilogica` in `rg-sati-demo`.
      OneDeploy deployment `74850bbe93d243c3ab8bff465a240b96` completed successfully and is the
      active deployment. No database migration was required or performed, and no firewall rule was
      requested, added, or altered.
- [x] Hosted `/health/live` returned `{"status":"live"}` and `/health/ready` returned `Healthy`.
      `/health/version` reported product `Sati.Api`, release `1.2.40`, and contract revision
      `E807EDE42231`. The client's `Sati.Contracts.V1.ApiSurface.Revision`, evaluated from the
      Release build under .NET 10, is the same `E807EDE42231`, so client/API contract parity holds.
      Health-only readiness evidence is `artifacts/release-1.2.40-demo-readiness.json`;
      authenticated Admin checks were skipped because this workstation has no designated
      `SATI_DEMO_USERNAME` and password.
- [x] Demo installer acceptance passed on
      `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.40.exe` (100,917,248 bytes; SHA-256
      `56baaa084f09dadda69065be56d4f015dd03b55ffe783b8028c254a7959c53b3`): five launches, each
      responsive with a graceful close and exit code 0, installed version `1.2.40.0`, cleanup
      passed. It ran on the build workstation, so it is not a clean external-machine attestation.
      Evidence: `artifacts/release-1.2.40-demo-installer-acceptance.json`.
- [x] Local installer built from the durable repository prerequisite after confirming that
      `artifacts\Prerequisites\SqlLocalDB.msi` carries a Valid Authenticode signature from
      `CN=Microsoft Corporation`. `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.40.exe`
      (202,972,938 bytes; SHA-256
      `14c06c3eedde67320ad8888e33c09ed1a50420ee498653482f66eb77e51087a1`). Acceptance passed:
      installed version `1.2.40.0`, `integratedSecurity=True` with no SQL username or password in
      the Local configuration, cleanup passed. The generated installers are not code-signed; only
      the embedded Microsoft LocalDB prerequisite is.
- [x] Published both installers and their `.sha256` files by copying each to a uniquely named
      temporary sibling, verifying that copy's hash, renaming it to the final versioned name, and
      verifying the final file again. No destination file was overwritten and no temporary file
      remained:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.40.exe`
        and its `.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.40.exe`
        and its `.sha256`

      Both published hashes are identical to the accepted build artifacts.
- [x] Evidence commit `4cef43e75c6ee64ba8cc67523f5e7b967f321313` pushed to `origin/master`. The
      working tree is clean and local `master` equals `origin/master`.

### Local Production machines

This release changes no schema, so no local migration is pending. Versions are still recorded so
that no machine is assumed to have caught up.

- [x] SatiLogica: `C:\Users\SatiLogica\AppData\Local\Programs\Satilogica\Sati\Sati.exe` is
      **1.2.23**, verified after this release. It is well behind and was not upgraded by this
      workflow.
- [ ] Joshu: **unverified.** `C:\Users\Joshu\AppData\Local\Programs\Satilogica\Sati` is not
      readable from the SatiLogica profile, so its installed version could not be confirmed and
      must be treated as behind. Record it once that machine installs 1.2.40.

## Orange-accent themes and calendar navigation — 2026-09-02

- [x] Add Blue-Gray Pearl and Cedar Grove as complete interchangeable theme palettes, using the
      supplied `#E25507` orange for their main accent and preserving distinct Local AI colors.
- [x] Replace font-dependent year-navigation characters with visible rounded vector-chevron
      buttons, retaining accessible names, tooltips, keyboard focus, and existing commands.
- [x] Verify both palettes supply every theme resource and cover the calendar arrows with a
      UI-structure regression test.

## Easy Eyes presentation mode — 2026-09-02

- [x] Add an off-by-default personal Easy Eyes option to the ungated Appearance settings.
- [x] Persist it per Sati user, Windows profile, and Demo/Production environment without placing
      presentation state in agency data.
- [x] Enlarge the working surface by roughly 30%, hide (without deleting) Narrative columns in
      both note grids, and force the Clients workspace to its horizontal selector while enabled.
- [x] Apply successful changes immediately to the open shell and reload them at sign-in/account
      switch; add persistence and UI-structure regression tests.

## Vocational Rehabilitation assignments — 2026-09-02

- [x] Add consumer-profile assignments for the Vocational Rehabilitation Counselor and the
      counselor's assistant, revealed only while `OpenWithVR` is selected.
- [x] Store the assistant's agency-wide display title in Settings, default it to `VSA`, and
      refresh the Consumers workspace after Settings closes.
- [x] Carry both assignments through shared validation, Local and API saves, optimistic
      concurrency, immutable person versions, and audit history.
- [x] Applied `AddVocationalRehabilitationAssignments` to hosted Demo through the controlled
      migration path in release 1.2.39.

## Existing-profile Credible updates — 2026-09-02

- [x] Add agency setting `AllowCredibleProfileUpdates`, default false, to persistence, API
      settings contracts, Admin Settings UI, and optimistic-concurrency save path.
- [x] When enabled, allow the field-level single-consumer review to fill the currently selected
      edit form without bypassing the ordinary person save/version/audit path.
- [x] Refuse differing nonblank Credible client ids before any form field changes; preserve absent
      or declined fields and every Sati-only field.
- [x] Keep bulk folder matches report-and-skip. Bulk replacement remains a separate, deferred
      workflow requiring recovery and batch-audit design.
- [x] Applied `AddCredibleProfileUpdateSetting` to hosted Demo through the controlled migration
      path in release 1.2.39, using the documented user-managed exact-IP firewall process.

## Duplicate compliance form rows — 2026-09-01

Implemented and tested; the repair has NOT yet run against a database holding real records.
Full write-up in `HANDOFF_DUPLICATE_COMPLIANCE_FORMS.md`.

Every `(PersonId, Type, DueDate)` in `SatiProduction` that was generated before `57af6fa`
exists three times — 492 duplicated forms across 25 of 26 clients, 984 surplus rows. Cause was
a read-modify-write race: `GetAllPeopleAsync` ran `EnsureCurrentCycleForms` + `SaveChangesAsync`
unconditionally on every caseload load, startup issued those loads concurrently, and there is no
unique constraint on `dbo.Forms`. `57af6fa` closed the mechanism on 2026-07-24 by serializing the
loads and gating the write behind `EnableEnsureCycleFormsOnLoad = false`. The rows it had already
written were never cleaned up.

It surfaces as a completed form that still blocks billing: `GetCurrentCycleForm` returns one copy
on a due-date tie, so the checkbox reads complete, while `EvaluateComplianceGate` iterates every
row in `Person.Forms` and sees the unreachable copies. Untouched it produces a fresh false block
each quarter, per client.

- [x] Add a unique index on `dbo.Forms (PersonId, Type, DueDate)` —
      `20260901150802_AddUniqueFormPersonTypeDueDateIndex`. `Form.Type` narrowed from
      `nvarchar(max)` to `nvarchar(40)` so it can be indexed. The migration refuses with a
      named message if duplicates remain rather than failing on the index itself.
- [x] Handle the losing writer's `DbUpdateException` in `GetAllPeopleAsync` — discard the
      losing inserts and re-read rather than crashing on a benign concurrent insert.
- [x] Repair the existing rows: `Data/FormDuplicateRepair.cs`, run by `LocalDatabaseUpdater`
      between the pre-migration backup and `MigrateAsync`. Merges only groups holding at most
      one completion fact; a group with two different completion dates is reported and left
      alone. One `AuditEvent` per removed row under `ActorUserId = 0`.
- [x] Close the second duplication path — `NewClientViewModel` now calls
      `Person.AddMissingForms` instead of assigning over `Forms`.
- [x] Declare the same index and length on `ApiDbContext.ServerForm` so the server model
      matches the column it writes to.
- [x] Ran `scripts/Report-DuplicateComplianceForms.sql` against `SatiProduction` on 2026-09-01:
      1,788 form rows, 804 distinct forms, 492 duplicated groups at exactly 3 copies each, 984
      surplus rows, 25 of 26 clients. **Zero conflicted groups** — every group holds at most one
      completion fact, so the repair merges all 492 unattended and the index binds on the same
      launch. One duplicated group blocks billing today: person 1056 `Q1R` due 2026-08-28, the
      reported record, classified `FALSE BLOCK -- work was attested on another copy`.
- [ ] Apply the migration to `SatiDemo` through the controlled path. The desktop repair does
      not run in Demo (`UsesCloudApi` skips the whole block), so if `SatiDemo` holds
      duplicates the migration will refuse there until they are cleared separately.
- [ ] Decide deliberately whether `EvaluateComplianceGate` should read every form or only the
      current cycle, and record it in `DECISIONS.md`. Ask Josh; narrowing it changes whether
      stale prior-cycle documents keep blocking.
- [x] Derived `IsCompliant` from `CompletedDate` and dropped the stored column
      (`AddDerivedFormCompliance`). The `isCompliant` constructor parameter is gone, so the
      state cannot be built. The migration backfills the 147 rows from the cycle start their
      own generator implied, writes a `form.compliance-date-backfilled` audit event per row,
      and never backfills a review, a not-yet-started cycle, or a person with no effective date.
      **Person 1044 is resolved by this**, not by the duplicate repair. (Person 1042's `Q2R` due
      2026-08-24 still blocks, correctly — it is genuinely incomplete.)
- [x] Added `Form.IsSatisfiedAsOf(date)` for the distinct question "is this in force as of
      today", sharing its predicate with `BillingComplianceGate.IsIncompleteAndOverdue`, and
      routed the caseload matrix, `UpcomingEvents`, task rows and `GetComplianceStatus` through
      it. A completion date that has not arrived is recorded but not in force; no screen can
      now call such a form complete while the gate blocks on it.
- [x] Re-enabled cycle form generation and removed `EnableEnsureCycleFormsOnLoad`. Nothing else
      generates forms for an ongoing caseload — clients only still had records because the
      racing pre-`57af6fa` runs pre-created the current *and* next cycle, which run out through
      2027–2028. Safe now because the unique index decides the race and `GetAllPeopleAsync`
      treats losing it as a re-read. `Person.InForceSince` owns the born-in-force rule, so the
      generator no longer mints dateless compliant rows.
- [x] Operational re-billing: **closed 2026-09-01 by Josh** — nothing currently in
      `SatiProduction` will ever be billed for real, since real billing is 6–8 months out. The
      one-time unblocking from the duplicate repair and the 147 backfilled rows therefore has no
      financial consequence. Neither recurs: both corrected rows that were blocking on a missing
      field rather than on a real compliance failure.
- [x] **Intervening cycles get no forms for a backdated admission** — fixed 2026-09-01.
      `EnsureCurrentCycleForms` now generates every cycle from the effective date through the
      one after the current, bounded at 25 cycles and dropping the oldest end so the workable
      cycles are always present. A form that was never created cannot be enforced, so those
      years previously carried no compliance requirements at all.
- [x] **The in-force assumption is scoped to the cycle containing today** — same change. It
      previously applied to any already-started cycle, which was harmless while only the current
      cycle was generated and would have asserted compliance nobody attested across every
      historical year at once. Closed cycles are generated outstanding: Sati has no record of
      whether a closed year's documents were renewed, and a later cycle beginning proves nothing
      because cycles turn over on the anniversary, not because anything was signed.
- [ ] Expect open historical documents on any client entered with a backdated effective date.
      That is the honest reading of an unknown, matching the quarterly-review precedent — do not
      bulk-close and do not invent dates. The creation dialog is where those years get recorded.
- [ ] No in-app control captures an arbitrary completion date for a non-review form. The
      quarterly attestation control (`ReviewsViewModel`) covers `Q1R`–`Q4R`; `ComplianceFormRow`'s
      per-row date picker is reachable only from client creation and the add-a-waiver dialog.
      Everywhere else stamps `DueDate` (Clients/dashboard checkbox) or `DateTime.Today` (task
      board). So a Comprehensive Assessment completed on a date that is neither cannot be
      recorded truthfully without a database edit.

## Release 1.2.39 — 2026-09-02

"A better fit." Responsive calendar and compact-display work, guaranteed Consumers overflow,
reviewed existing-profile Credible updates, and Vocational Rehabilitation assignments with an
agency-configurable assistant title.

**Schema-changing release.** Two additive migrations:

- `20260902140636_AddCredibleProfileUpdateSetting` adds the non-null
  `Settings.AllowCredibleProfileUpdates` bit, default false.
- `20260902142303_AddVocationalRehabilitationAssignments` adds nullable
  `People.VrCounselorName` and `People.VrAssistantName` (`nvarchar(150)`) plus non-null
  `Settings.VrAssistantTitle` (`nvarchar(100)`), default `VSA`.

**Authorization and firewall.** Invoked with literal `invoke DATT!`. Josh explicitly authorized
both controlled SatiDemo migrations and confirmed both Local Production machines were on 1.2.38.
Josh added `datt-workstation-temp` for the workstation's exact public IP `66.211.131.66`, then
removed it immediately after migration. The allow-list was verified afterward as exactly the three
`sati-demo-api-outbound-*` entries; the release workflow did not alter the firewall.

### Validation
- [x] Release build of the full solution: 0 errors, 9 warnings (existing NuGet vulnerability-feed
      reachability, EF raw-SQL, nullable, and xUnit analyzer warnings).
- [x] Sati desktop/domain: 1,151 passed, 1 skipped
      (`LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`,
      the documented `SATI_RUN_LOCAL_AI_MODEL_EVAL` opt-in whose prerequisite is absent).
      API integration: 324 passed. Carika: 4 passed.
- [x] `git diff --check` clean; release diff and staged scope reviewed.

### Demo migration and deployment
- [x] `scripts/Apply-CredibleProfileAndVrMigrations.ps1` dry run validated exact
      `SatiDemo` / `Demo`, 1 Settings row and 177 consumers; reported 4 columns and 2 history rows,
      then rolled back.
- [x] Controlled migration committed those 4 columns and 2 EF history rows. A third pass reported
      0 columns and 0 history rows, proving idempotency; no blank VR assistant titles were found.
- [x] `datt-workstation-temp` removed by Josh and verified absent. The SQL allow-list contains only
      `sati-demo-api-outbound-01` through `-03`.
- [x] Source commits `8a5cc185c1de495b016baad1595ca9cbdaaaf700` and
      `cb061a3b8c265b10bd7d9bd9b2e56d6e3805b07f` pushed to `origin/master`. The latter adds the
      compatibility fingerprints for the two persistence-relevant contract changes discovered
      during final package inspection.
- [x] Built
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\Sati.Api-1.2.39.zip`
      from `cb061a3b8c265b10bd7d9bd9b2e56d6e3805b07f` (9,673,491 bytes; SHA-256
      `4FC0373B5CC8E2DBD931ECED175B2FE0368F770A96FC2C7A589FEA7E729FD08A`; 70 entries). Package
      inspection found 0 backslash paths, 0 forbidden configuration/key files, and both required
      Demo history-reconciliation WebJob files. Published only to existing Demo App Service
      `sati-demo-api-satilogica` in `rg-sati-demo`; OneDeploy deployment
      `e45f97e926824f96ba3de82247b47f4a` completed successfully.
- [x] Hosted `/health/live` and `/health/ready` returned healthy. `/health/version` reported
      product `Sati.Api`, release `1.2.39`, and contract revision `E807EDE42231`, exactly matching
      the packaged client/API contract. Readiness therefore also confirmed that
      `SchemaDriftHealthCheck` accepted the migrated Demo schema. Health-only evidence is
      `artifacts/release-1.2.39-demo-readiness.json`; authenticated Admin checks were explicitly
      skipped because this workstation has no designated `SATI_DEMO_USERNAME` / password.

### Local Production machines
- [x] Both known Local Production machines are on 1.2.38 before release, confirmed by Josh.
- [ ] Record each machine after it launches 1.2.39 and applies the pending migrations locally.

### Artifacts
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.39.exe`
      (100,909,056 bytes; SHA-256
      `D0E9101FCBEBAFF70338F433453664A2EAC13D08438A726160C80AF04B1688D0`). All five installed
      launches reached a responsive sign-in window, closed normally with exit code 0, reported
      version 1.2.39.0, and isolated cleanup passed. Evidence is
      `artifacts/release-1.2.39-demo-installer-acceptance.json`; this is a same-machine acceptance,
      not a clean external-machine attestation.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.39.exe`
      (203,245,322 bytes; SHA-256
      `989C0D83B5E45135049ADBA3F676B3357F40BB3150961CAE513214D2194A0387`). Version 1.2.39.0,
      Windows integrated security, and isolated cleanup passed. Embedded `SqlLocalDB.msi`
      (SHA-256 `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) had a valid Microsoft
      Corporation Authenticode signature before use. The generated installers themselves are not
      represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
      Destination hashes and checksum contents match the accepted artifacts; no existing file was
      overwritten and no publication temporary file remains.
- [x] Release evidence commit `da66977` pushed. The checklist-closing commit was then pushed and
      final local `master` confirmed equal to `origin/master`.

### Branches
- [x] `master` and `origin/master` began at `cd22dda`; remote default confirmed as `master`.
- [x] Retain `claude/cool-jang-f6b3c4`: checked out by a linked worktree.
- [x] Retain `second-machine-setup` and remote `claude/local-vs-github-workflow-dlcqpb`: both have
      unique work unrelated to this release.
- [x] Retain merged `feature/caseload-transfer`; it has no unique commits and is not required to be
      deleted.

---

## Release 1.2.38 — 2026-09-01

"Bring a caseload with you." Credible export import — single consumer and bulk folder — plus the
caseload ownership transfer and supervisor distribution screen it depends on. Designed in
`CREDIBLE_IMPORT_DESIGN.md`; all six sequencing steps built and tested.

**A schema-changing release.** Migration `20260901232228_AddPersonCredibleClientId` adds
`People.CredibleClientId` (`nvarchar(32)`, nullable). Additive, no backfill, no data transformation.
It is the dedupe key for import: re-running a folder must report rather than duplicate. Bounded
rather than following the `nvarchar(max)` convention `EvergreenId` and `MaineCareId` use, so a
future filtered unique index on `(AgencyId, CredibleClientId)` does not need a narrowing migration
first — the mistake `Form.Type` had to be corrected for.

**Authorization.** Invoked with the literal `invoke DATT!`. The Demo migration was authorized
explicitly by Josh after the preflight report named it, and the temporary
`datt-workstation-temp` SQL firewall rule for `72.95.106.10` was added by Josh. A general "I defer
to your opinion" earlier in the same conversation was deliberately **not** treated as authorization
for either — see `AGENTS.md` section 5.

**New dependency.** AngleSharp 1.7.2, for reading saved Credible print views. Pure managed, no
native components; the Local installer acceptance gate is what proves it packages onto a clean
machine.

**New API routes.** `PUT /api/v1/people/{personId}/owner` and
`POST /api/v1/people/credible-matches`. Both recorded in `API_AUTHORIZATION.md`; the route
inventory moves from 114 to 116. `SavePersonRequest` gained `CredibleClientId`, recorded as
contract shape `person-credible-client-id-v1`, so a newer client cannot silently lose the dedupe
key against an older server.

### Validation
- [x] Release build of the full solution: 0 errors, 10 warnings.
- [x] Sati desktop/domain tests: 1,126 passed, 1 skipped
      (`LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`
      — the `SATI_RUN_LOCAL_AI_MODEL_EVAL` on-device model evaluation, a documented opt-in whose
      prerequisite is genuinely absent).
- [x] Sati API integration tests: 324 passed. Carika tests: 4 passed.
- [x] `git diff --check` clean.

### Demo deployment
- [x] Temporary `datt-workstation-temp` firewall rule added by Josh for `72.95.106.10`.
- [x] `SatiDemo` reachable and confirmed as `EnvironmentName = Demo`, with
      `People.CredibleClientId` absent and no `__EFMigrationsHistory` row - a clean starting state.
- [x] Controlled Demo migration applied through `scripts/Apply-CredibleClientIdMigration.ps1`,
      written for this release on the `Apply-DerivedFormComplianceMigrations.ps1` pattern: fails
      closed on database and environment identity, guards on the real schema rather than history,
      and verifies an already-present column is `nvarchar(32)` and nullable rather than merely
      correctly named. Three passes: dry run reported 1 column and 1 history row and rolled back;
      the real pass wrote both; the third reported 0 and 0, proving idempotency. Final state
      verified directly: `nvarchar(32)`, nullable, one `__EFMigrationsHistory` row, 0 of 177
      consumers populated.
- [x] `datt-workstation-temp` removed by Josh and verified absent: the allow-list now holds
      exactly the three `sati-demo-api-outbound-*` entries. It was still open at the first check
      after publication, which is longer than the migration needed it; closing it immediately
      after the migration rather than at the end of the release is the habit to keep. The release
      workflow never adds, alters, or deletes a firewall rule.
- [x] API ZIP `artifacts/Sati.Api-1.2.38.zip`, 9,519,208 bytes, SHA-256
      `0EDF6DCC2887B2199540CBB8CC7B53D1693D4C2A3DABAA9A39581E43B72222C9`; 70 files with
      forward-slash entry paths, both WebJob files present, no `appsettings*.json` or key material.
- [x] Deployed only to the existing App Service `sati-demo-api-satilogica`; OneDeploy
      `d48c57ab-2056-42df-bca2-e1dc2abf10b9` succeeded. `/health/live` returns `{"status":"live"}`,
      `/health/ready` returns `Healthy`, and `/health/version` reports `Sati.Api`, release
      `1.2.38`, contract `64831C77F89C`. The revision read from the locally built `Sati.Contracts`
      is also `64831C77F89C`; it moved from `729A9E9F9B2B` because two routes were added. A healthy
      readiness result is the real confirmation the migration satisfied the deployed model, because
      `SchemaDriftHealthCheck` compares the model against the database.

### Local Production machines
- [x] Both machines are on 1.2.38, per Josh. Neither is behind. The desktop applies pending
      migrations at launch, so this is the half of a schema release that the Demo application
      does not speak for.

### Artifacts
- [x] Generated and accepted
      `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.38.exe` (100,896,768 bytes; SHA-256
      `A238FC50AB173E1795D3EF415D6EED53A313B0E4C0A0545793FEC062AB6060EF`) without overwriting an
      artifact: all five installed launches responded, closed gracefully with exit code 0,
      reported version 1.2.38.0, and isolated cleanup passed. Evidence in
      `artifacts/release-1.2.38-demo-installer-acceptance.json`. Those five launches are also what
      proves the new AngleSharp 1.7.2 dependency packages and loads on a clean install.
- [x] Generated and accepted
      `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.38.exe` (202,957,066 bytes; SHA-256
      `B97803691CDC7C5731D9FBFEFC95103F79ECEFD2051F8EDE84A9794E00B5DA20`) without overwriting an
      artifact: version 1.2.38.0, Windows integrated security with no SQL username or password in
      configuration, and isolated cleanup passed. Its embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` (63,508,480 bytes) carried a valid Microsoft
      Corporation Authenticode signature, verified before use. The generated installers themselves
      are not code-signed.
- [x] Published without overwriting anything. Each file was copied to a uniquely named temporary
      sibling, hash-verified there, renamed to its final versioned name, and verified again:
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop\SatiLocalSetup-1.2.38.exe`
        and `SatiLocalSetup-1.2.38.exe.sha256`
      - `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files\SatiDemoSetup-1.2.38.exe`
        and `SatiDemoSetup-1.2.38.exe.sha256`

      Both destinations resolved inside the named documents root and neither previously held a
      1.2.38 artifact. No API ZIP, LocalDB prerequisite, or private configuration was published to
      either folder.

### Branches
- [x] Merged `feature/caseload-transfer` (7 commits) into `master`; the branch is retained
      locally rather than deleted, because it is the working branch this release came from and
      nothing requires its removal.
- [x] Retained `claude/cool-jang-f6b3c4`: checked out by a linked worktree, which the playbook
      forbids deleting regardless of its ancestry.
- [x] Retained `second-machine-setup` (7 unique commits) and remote
      `claude/local-vs-github-workflow-dlcqpb` (1 unique commit): both hold work unrelated to this
      release.

### Landed after publication - NOT in the 1.2.38 artifacts
- `7c7d92b` added a `ScrollViewer` to the supervisor dashboard toolbar and a
  `ReleaseUiStructureTests` assertion covering both dashboards. It was written while chasing a
  reported "the bulk import option is missing", which turned out to be nobody's bug: the option
  was on the Supervisor tab, where it is designed to be, and was simply not where it was being
  looked for.
- The change is still worth keeping. The toolbar was a bare horizontal `StackPanel`, which clips
  silently once its children exceed the window width, and it had gained two entries in this
  release with no guard.
- **It is not in the published installers.** Source at 1.2.38 now produces different bytes than
  `SatiDemoSetup-1.2.38.exe` and `SatiLocalSetup-1.2.38.exe`. Nothing was overwritten and the
  published artifacts remain the accepted ones; this note exists so that a later build from
  `master` calling itself 1.2.38 is not mistaken for them. It ships in the next release.
- The Clients workspace now exposes automatic overflow for its Overview, section rail, editor, and
  fixed-width document workspaces. A separate compact-display mode detects the physical monitor at
  shell startup. At the 1920 × 1080 boundary it silently starts the horizontal consumer selector and
  tighter layout; below the boundary it also explains the adjustment once and collapses Today's
  Work. It condenses navigation and spacing, enables pixel-rounded display-optimized text, keeps the
  ordinary reopen controls, and does not globally shrink fonts or hit targets. These changes are
  likewise post-publication source and ship in the next release, not the accepted 1.2.38 installers.
- The Clients Overview now reserves a real working height for Notes and Journal instead of leaving
  them in a star row that collapses under the outer overflow viewer. Forms comes immediately before
  them; Contacts and Support Team plus Medical Providers now follow at the bottom as reference
  panels. Both the full roster and horizontal selector expose the same theme-aware person-plus Add
  Client action, replacing the ambiguous circular-arrow glyph. The Overview now measures the inline
  consumer editor against its available width instead of creating a wide horizontal canvas, and the
  compact Forms matrix uses the Overview's one vertical scrollbar rather than adding a nested third
  scrollbar. This is also next-release source.

### Known gaps shipped deliberately
- An SSN in an export is shown and refused rather than saved. Nothing writes an imported SSN yet;
  the row says so instead of appearing to capture it. See `CREDIBLE_IMPORT_DESIGN.md`.
- No `person.imported` audit action. An imported consumer records `person.created`, which is
  accurate; the gap is granularity, and closing it needs a contract change to answer a question
  nobody has asked yet.
- Bulk import and the distribution screen had not been exercised by a person before this release
  pass; both are checked by hand against Demo before installers are published.

---

## Release 1.2.37 — 2026-09-01

"Sati starts again." A one-defect release fixing the 1.2.36 startup refusal described below, and
carrying the whole 1.2.36 change set to Local machines that could not install it.

**Authorization note.** This release ran on Josh's explicit, twice-repeated instruction rather than
the literal `invoke DATT!` phrase. The concern was raised once and reaffirmed; recording it here so
the audit trail says what actually happened.

**Not a schema-changing release.** No migration was added after 1.2.36. `SatiDemo` is already at
that schema and healthy, so no Demo migration, no temporary SQL firewall rule, and no cloud
database action of any kind belongs to this release. The API publish is a version bump only.

**The gate that was missing.** 1.2.36 proved its migration against `SatiDemo` through the guarded
script, which bypasses the desktop startup path entirely — so the code that actually refused was
never exercised before publication. This release adds that check: the real `LocalDatabaseUpdater`
path is run against a genuinely un-migrated `SatiProduction` before any artifact is published.

### Validation
- [x] Release build of the full solution: 0 errors.
- [x] Sati desktop/domain tests: 1,014 passed, 1 skipped (the `SATI_RUN_LOCAL_AI_MODEL_EVAL`
      on-device model evaluation — documented opt-in prerequisite, genuinely absent).
- [x] Sati API integration tests: 302 passed. Carika tests: 4 passed.
- [x] **The gate 1.2.36 lacked.** The dev `SatiProduction` was rewound to the exact pre-1.2.36
      shape — `Forms.Type` back to `nvarchar(max)`, `IX_Forms_PersonId` restored, `IsCompliant`
      re-added, both history rows deleted — and seeded with the reported defect: three `Q1R` rows
      at one due date with one completed, plus a `PCP` pair flagged compliant with no date. The
      real `LocalDatabaseUpdater.UpdateAsync` path then reported **`Applied`**, having taken a
      backup (the database held records), merged 2 duplicate groups removing 3 rows with 0
      conflicts, and applied both migrations.
      Result: one `Q1R` keeping its real completion date `2026-08-28`; one `PCP` backfilled to its
      cycle start `2026-05-30`; `IsCompliant` dropped; unique index present; 2 history rows;
      3 `form.duplicate-removed` and 1 `form.compliance-date-backfilled` audit events.
      This is the first time the repair-then-index sequence ran against real duplicates on SQL
      Server rather than SQLite. The synthetic "Rehearsal Client" remains in the dev database,
      marked `IsTestData`.

### Deployment and artifact evidence
- [x] Source release commit `c48fc12` pushed to `master` and confirmed on the remote before any
      artifact was produced.
- [x] **No cloud database action.** `SatiDemo` was already at the 1.2.36 schema; no migration, no
      temporary firewall rule, and no Azure SQL access of any kind belongs to this release.
- [x] Demo API ZIP `artifacts/SatiApi-1.2.37.zip` built from `c48fc12`. `Sati.Api.dll` reports file
      version `1.2.37.0` and product version
      `1.2.37+c48fc1285df7562d31b85c82ffd9a5f30d1273c0`. 9,502,275 bytes; SHA-256
      `5880468485814AD10B26FD03964ACAD29FF08DD606C82ADAFFE9BF56E6407243`; 70 files with
      forward-slash entry paths, both WebJob files present, no `appsettings*.json` or key material.
      The 1.2.36 package remains in `artifacts`.
- [x] Deployed only to the existing App Service `sati-demo-api-satilogica`; OneDeploy
      `941e7c0cd86e4d838872902e23144b67` succeeded. `/health/live` live, `/health/ready` Healthy,
      `/health/version` reports `Sati.Api`, release `1.2.37`, contract `729A9E9F9B2B` — matching
      the locally built `Sati.Contracts`, and unchanged from 1.2.36 because no contract moved.
- [x] Generated and accepted `artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.37.exe`
      (100,487,168 bytes; SHA-256
      `F3D19F4A5F46B77123A4437975E01EB0CAEF872BEE431AC5D0DF50E6CAF88D0E`): five installed launches
      responded, closed gracefully with exit code 0, reported version 1.2.37.0, cleanup passed.
      Evidence in `artifacts/release-1.2.37-demo-installer-acceptance.json`.
- [x] Generated and accepted `artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.37.exe`
      (202,546,954 bytes; SHA-256
      `E2CBE465E6AF383C06E3BE881F1010DB3BB56DE1CF23778E1F357F09AC7BCD18`): version 1.2.37.0,
      Windows integrated security, cleanup passed. Its embedded `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carried a valid Microsoft
      Corporation Authenticode signature, verified before use. The generated installers are not
      represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to `...\SatiLogica - Documents\Sati Desktop` and
      `...\SatiLogica - Documents\SatiLogica Demo Files`. No file overwritten, no temporary left.
      The 1.2.36 installers remain in place, superseded and deliberately not deleted — **they will
      still refuse to start until 1.2.37 is installed over them.**
- [ ] Local Production machines and the release each is on. Awaiting Josh. Both were on 1.2.35;
      one has 1.2.36 installed and cannot open until 1.2.37 is applied over it.

## 1.2.36 blocks Local startup — found 2026-09-01, fixed, needs 1.2.37

**Every Local Production machine refuses to start on 1.2.36.** Josh hit it installing the release.
The dialog is correct and nothing was written, but the premise it stopped on was false.

`MigrationEffectAnalyzer` judged an `AlterColumnOperation` satisfied on nullability alone.
`AddUniqueFormPersonTypeDueDateIndex` narrows `Forms.Type` from `nvarchar(max)` to `nvarchar(40)`
so it can be indexed, and that column is `NOT NULL` before and after. So on a database that had
had *none* of the migration, the alter read as already applied while the unique index beside it
read as missing — one present, one missing, `PartiallyPresent`, which is the one verdict
`LocalDatabaseUpdater` deliberately will not act on. It is the correct refusal for a genuinely
mixed schema; here the schema was simply un-migrated.

Verified directly against this machine's `SatiProduction`: `Forms.Type` still `nvarchar(max)`,
`IX_Forms_PersonId` still present, `IX_Forms_PersonId_Type_DueDate` absent, `IsCompliant` present,
zero 2026-09 history rows. Nothing of the release had been applied, on either migration.

**Fixed:** an alter is now satisfied only when a declared bound has actually been applied. An
unbounded live column where the migration declares a bound is proof the alter has not run. Only
unbounded-versus-bounded counts as evidence — a column merely wider than declared stays satisfied,
because stopping startup over benign drift is the same class of mistake. Re-run against the live
database, both migrations now report `NotApplied`, which is the ordinary path: backup, duplicate
repair, migrate.

The analyzer had no automated coverage because it reads SQL Server catalog views. It has five
tests now, against a hand-built schema; the headline one fails against the unfixed classifier.

- [ ] Cut 1.2.37 with this fix. 1.2.36 cannot be re-cut — its installers are published and the
      playbook forbids replacing bytes under an existing version.
- [ ] Demo needs no repeat migration; `SatiDemo` is already at the 1.2.36 schema and healthy. The
      1.2.37 API publish is a version bump only.
- [ ] Treat the published 1.2.36 installers as superseded. Do not delete them; publish 1.2.37
      beside them.

## Release 1.2.36 — 2026-09-01

"One record, one answer." A completed 90-day review that kept blocking billing, traced to three
causes that all produce the same symptom and are individually invisible from the screen. Eleven
commits since the 1.2.35 audit commit `95b3b59`.

**This IS a schema-changing release.** Two migrations:
`20260901150802_AddUniqueFormPersonTypeDueDateIndex` and `20260901154714_AddDerivedFormCompliance`.
Both halves must be recorded — the Demo application below, and the Local Production machines, which
receive it only at their own next launch.

**Ordering is forced, not preferred.** `AddDerivedFormCompliance` drops `dbo.Forms.IsCompliant`,
which `InitialCreate` created `bit NOT NULL` with **no default constraint** (verified on `SatiDemo`:
`IsCompliantDefault = 0`). The 1.2.36 API no longer writes that column, so publishing it against a
database that still has the column breaks every `INSERT` into `Forms` — client creation in Demo.
The migration therefore precedes the API publication rather than following it.

### Pre-migration survey of SatiDemo — 2026-09-01, read-only

| Measure | Value |
|---|---|
| People / Forms | 177 / 4,124 |
| Duplicate `(PersonId, Type, DueDate)` groups | **0** — the index applies cleanly |
| `IsCompliant = 1` with no `CompletedDate` | 1,147 |
| Of those: reviews left open / no effective date / future cycle | 0 / 0 / 0 |
| Of those: backfilled from their cycle start | **1,147** |
| `IX_Forms_PersonId_Type_DueDate` present beforehand | no |

Demo has no duplicates because its forms come from the API's `BuildInitialForms`, not the desktop
path whose concurrent-load race produced them locally.

### Validation
- [x] Release build of the full solution: 0 errors.
- [x] Sati desktop/domain tests: 1,009 passed, 1 skipped.
- [x] Sati API integration tests: 302 passed.
- [x] Carika tests: 4 passed.
- [x] The single skip is `LocalAiModelCompetenceTests.ConfiguredModelCompletesGroundedWorkflow`
      `AcrossRepresentativeCurrentNoteInputs`, gated on `SATI_RUN_LOCAL_AI_MODEL_EVAL=1` for the
      on-device Foundry Local model evaluation. Documented opt-in prerequisite, genuinely absent.

### Deployment and artifact evidence
- [x] Source release commit `18ef75e` pushed to `master` and confirmed equal to `origin/master`
      before any artifact was produced.
- [x] Temporary `datt-workstation-temp` firewall rule added by Josh for `72.95.106.10` using
      `scripts/Set-DemoWorkstationFirewallRule.ps1`. The release workflow never created, altered,
      or deleted a firewall rule; it only wrote the script and validated that it parses.
- [x] Controlled `SatiDemo` migration via `scripts/Apply-DerivedFormComplianceMigrations.ps1`,
      guarded on the real schema rather than on `__EFMigrationsHistory`, and fail-closed on
      database and environment identity. Three runs:
      **dry run (rolled back)** and **real run** each reported `TypeNarrowed 1, IndexesAdded 1,
      RowsBackfilled 1147, AuditEventsWritten 1147, IsCompliantDropped 1, HistoryRowsWritten 2`;
      the **third run** reported all zeros, proving idempotency. The script refuses outright if
      duplicate `(PersonId, Type, DueDate)` rows exist, and aborts if the backfill and its audit
      trail disagree on the row count.
- [x] Post-migration state verified independently: `IsCompliant` column absent, unique
      `IX_Forms_PersonId_Type_DueDate` present, `Type` at `nvarchar(40)`, 4,124 forms unchanged,
      1,848 carrying a completion date (701 pre-existing plus 1,147 backfilled), 1,147
      `form.compliance-date-backfilled` audit events, 2 history rows.
- [x] Demo API ZIP `artifacts/SatiApi-1.2.36.zip` built from pushed source commit `18ef75e`.
      `Sati.Api.dll` reports file version `1.2.36.0` and product version
      `1.2.36+18ef75e4fe68c5c1ac8714ab8f9ebe62987c562b`. The ZIP is 9,501,366 bytes with SHA-256
      `1D951038A809A8459BA6B7F2573E3DA1C140061C261482855F3720B404022109`, holds 70 files — an
      identical file set to the 1.2.35 package — including the two `demo-history-reconciliation`
      WebJob files, and contains no `appsettings*.json`, private desktop configuration, credential
      pattern, or key material. The prior known-healthy 1.2.35 package remains in `artifacts`.
      **Rebuilt once before deployment:** `Compress-Archive` on Windows PowerShell 5.1 wrote 29
      backslash-separated entry paths where the known-good 1.2.35 package had none, which would
      have deployed files to wrong paths. Repacked with normalised separators and diffed against
      1.2.35 to confirm no file was gained or lost.
- [x] Demo API deployed only to the existing App Service `sati-demo-api-satilogica`; OneDeploy
      deployment `62c49231d5724f9fb78e9c8d373c16b7` succeeded. `/health/live` returns
      `{"status":"live"}`, `/health/ready` returns `Healthy`, and `/health/version` reports product
      `Sati.Api`, release `1.2.36`, contract revision `729A9E9F9B2B`. The revision read from the
      locally built `Sati.Contracts` is also `729A9E9F9B2B`. A healthy readiness result is the
      real confirmation the migration satisfied the deployed model, because `SchemaDriftHealthCheck`
      compares the model's tables and columns against the database.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.36.exe`
      (100,487,168 bytes; SHA-256
      `0DB4225E5FDBCB76E1D7EC6B9F34A8BB8385B38DA04D358897C8D20BA7284985`) without overwriting an
      artifact: all five installed launches responded, closed gracefully with exit code 0, reported
      version 1.2.36.0, and isolated cleanup passed. Evidence in
      `artifacts/release-1.2.36-demo-installer-acceptance.json`.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.36.exe`
      (202,545,930 bytes; SHA-256
      `8EFF42D1E6D057237FCAF571D8833CA43E2FB28BF86E60AEE867B2D39EFD06D7`) without overwriting an
      artifact: version 1.2.36.0, Windows integrated security, and isolated cleanup passed. Its
      embedded `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carried a valid Microsoft
      Corporation Authenticode signature, verified before use. The generated installers themselves
      are not represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
      Destination hashes match the accepted artifacts; no existing file was overwritten and no
      temporary file remains.
- [x] `datt-workstation-temp` removed by Josh immediately after the migration and verified absent:
      the allow-list holds exactly the three `sati-demo-api-outbound-01/02/03` entries
      (`52.189.72.76`, `20.118.56.5`, `20.118.56.47`) and nothing else. The removal path re-lists
      after deleting and throws if the rule survives, so this is a verified absence rather than an
      assumed one. The release workflow never created, altered, or deleted a firewall rule.
- [ ] Local Production machines and the release each is on, including any known to be behind.
      Awaiting Josh. Both machines were on 1.2.35 before this release; **neither receives 1.2.36
      until the new Local installer is run there**, and the desktop applies the duplicate repair and
      both migrations at that first launch, not before.

### Branch audit
- [x] Deleted `fix/derived-form-compliance` (`6d98931`), `fix/duplicate-compliance-forms`
      (`3aa960c`) and `handoff/duplicate-compliance-forms` (`1184140`) locally and remotely after
      proving each fully merged, with no unique commits and no worktree holding it.
- [x] Retained `claude/cool-jang-f6b3c4`: fully merged but checked out by a linked worktree.
- [x] Retained `second-machine-setup` (7 unique commits) and remote
      `claude/local-vs-github-workflow-dlcqpb` (1): historical setup and documentation branches from
      2026-08-16 whose current intent is not safe to infer.

## Release 1.2.35 — 2026-09-01

Daily sign-in agenda, explicit quarterly-review attestation, human-accepted suggested follow-ups,
successful-save Notes filter clearing, and the fail-closed legal-hold boundary for ordinary-client
deletion. Twenty commits since the 1.2.34 evidence commit `51fd1aa`.

**This is not a schema-changing release.** No migration was added after 1.2.34. The API change adds
a narrow read over the existing Comprehensive Assessment table and does not depend on a new column
or table, so no Demo database migration or temporary SQL firewall rule applies.

### Validation
- [x] Source release commit `70dea6d` created and pushed to `origin/master` without rewriting
      history. The API and both installers were built from that pushed source.
- [x] Complete Release build passes: 0 errors, 6 warnings (offline NuGet vulnerability feed, the
      existing guarded raw-SQL analyzer warning, and three test-code analyzer/nullability warnings).
- [x] 1,284 tests pass — 978 desktop/domain, 302 API integration, and 4 Carika. One documented
      opt-in local-AI model competence test is skipped because `SATI_RUN_LOCAL_AI_MODEL_EVAL=1`
      is absent.
- [x] 54 focused release regressions pass, including opposite-theme WPF rendering, agenda behavior,
      quarterly attestation, form-date validation, suggested-follow-up acceptance, Notes filter
      clearing/retention, API surface parity, and tenant-scoped assessment reads.

### Deployment and artifact evidence
- [x] Demo API ZIP `artifacts/SatiApi-1.2.35.zip` built from pushed source commit `70dea6d`.
      `Sati.Api.dll` reports file version `1.2.35.0` and product version
      `1.2.35+70dea6dfa3fd2dcc9cb1864d69dfc86c54ca27ca`. The ZIP is 9,635,903 bytes with
      SHA-256 `B344E8EB60063A8451ED924BD393ED3CF8248F904B533CBEE3558DCDF345F2DD`.
      It contains the two `demo-history-reconciliation` WebJob files and no `appsettings*.json`,
      Development/private desktop configuration, credential pattern, or key material. The prior
      known-healthy 1.2.34 API package remains in `artifacts`.
- [x] Demo API deployed only to existing App Service `sati-demo-api-satilogica`; OneDeploy
      deployment `632393f093f9427ea4af6b3b2508fb77` succeeded. `/health/live` returns live,
      `/health/ready` returns Healthy, and `/health/version` reports product `Sati.Api`, release
      `1.2.35`, and contract revision `729A9E9F9B2B`. The revision read directly from the locally
      built `Sati.Contracts` is also `729A9E9F9B2B`. The health-only readiness gate passed;
      authenticated readiness was not required by the release gate and was skipped because the
      synthetic Demo credentials were not present in this process.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.35.exe`
      (100,470,784 bytes; SHA-256
      `42053C72EC06CA094E39A8B8FAF0C7CD489E3D9388C3B54FAF2A5F8FFFFB17C6`) without overwriting an
      artifact: all five installed launches responded, closed gracefully with exit code 0, reported
      version 1.2.35.0, and isolated cleanup passed.
- [x] Generated and accepted
      `C:\Users\SatiLogica\source\repos\heschides\Sati\artifacts\SatiLocalInstaller\SatiLocalSetup-1.2.35.exe`
      (202,810,634 bytes; SHA-256
      `DB339529AD8D7184312C538FB3DC912910A2F4144664F76692E218C48015CAA1`) without overwriting an
      artifact: version 1.2.35.0, Windows integrated security, and isolated cleanup passed. Its
      embedded `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) had a valid Microsoft
      Corporation Authenticode signature before use. The generated installers themselves are not
      represented as code-signed.
- [x] Published both accepted installers and only their `.sha256` files through uniquely named,
      hash-verified temporary siblings to
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop` and
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
      Destination hashes match the accepted artifacts and checksum contents; no existing file was
      overwritten and no temporary file remains.
- [x] Release evidence commit `5293aad` pushed. The checklist-closing commit was then pushed and
      final local `master` confirmed equal to `origin/master`.

### Branch audit
- [x] Deleted completed `docs/feature-handoffs` at `74b191f` locally and remotely after proving it
      fully merged, had no unique commits, and was not checked out by a worktree.
- [x] Retained `claude/cool-jang-f6b3c4`: fully merged but checked out by a linked worktree that
      also contains an untracked `AGENTS.md`.
- [x] Retained `second-machine-setup` and remote `claude/local-vs-github-workflow-dlcqpb`: both
      contain unique divergent setup/documentation work whose current intent is not safe to infer.

## Quarterly review attestation and refresh repair — 2026-08-31

- [x] Preserve the deliberate split between `ReviewItem` evidence and `Q1R`-`Q4R` form
      attestations; logging evidence does not auto-complete a billing gate.
- [x] Correct the Reviews legend, display the shared matrix-owned attestation status in current-
      quarter and all-quarter views, and expose the state and dates in the detail pane.
- [x] Add explicit completion/reset controls. Completion starts with a blank required date,
      preserves an entered late date, rejects a future date, and writes only through
      `Form.Attest`/`RevokeAttestation` and `IFormService`.
- [x] Centralize the post-form-change cascade so dashboard flags, the caseload matrix, and
      `UpcomingEvents` refresh after dashboard, task-board, form-note, Clients, and Reviews paths.
- [x] Enforce the non-future completion rule in shared contracts, Local persistence, and the API;
      the API returns a validation problem without changing stored state.
- [x] Add regressions for copy, shared status ownership, explicit late dates, historical billing
      windows, rejected future dates, the no-auto-derive boundary, and all completion cascades.
- [ ] Before release, tell case managers that quarters tracked only as Review items remain open
      attestations and may form an operational backlog. Do not bulk-close them or invent dates.
- [x] Replace the older dashboard and Clients quick-toggle convention that recorded `DueDate` with
      the shared blank attestation capture. Completed in the 2026-09-03 steps 1–3 slice above.

## Release 1.2.34 — 2026-08-31

Per-user permissions, the line-by-line audit that followed, and the claim-response half of the
billing exchange. Eleven commits since the 1.2.33 evidence commit `6704c17`.

**This is a schema-changing release.** Two migrations, and the first adds a column
`ValidatedActorFilter` reads on every authenticated request, so the Demo migration must land
BEFORE the dependent API is published. Both halves are recorded below, per the rule added in
`9cbcf0e`: Demo is one database migrated deliberately; Local `SatiProduction` is a separate
database on every machine, migrated by the desktop at its next launch.

| Migration | Effect |
|---|---|
| `20260830224423_AddUserPermissions` | `Users.Permissions` int NOT NULL default 0, plus the legacy-role backfill |
| `20260830231500_SeparateAgencyWideSupervision` | Director 7 → 19, Admin 15 → 31 |

### Validation
- [x] Source release commit `c2f19b6`, pushed to `origin/master` without rewriting history.
- [x] Release build clean: 0 errors, 7 warnings.
- [x] 1,221 tests pass across all three projects — 930 desktop, 287 API, 4 Carika. One skip:
      `LocalAiModelCompetenceTests`, whose documented prerequisite `SATI_RUN_LOCAL_AI_MODEL_EVAL=1`
      is genuinely absent.
- [x] `scripts/Apply-UserPermissionsMigrations.ps1` written and rehearsed against a throwaway
      `SatiDemo` in an isolated LocalDB instance, never against a real database. Dry run rolled
      back with nothing left behind; real run applied; third pass reported every count and proof
      at 0. Two further scenarios exercised: the column-present-without-history drift case that
      makes EF's generated script fail with SQL 2705 (skipped the backfill rather than clobbering
      it, applied only the correction, reconciled history), and both fail-closed guards
      (correction recorded without its base migration; environment identity mismatch).
- [x] Resulting values match `UserPermissionRules.FromLegacyRole`: CaseManager 1, Supervisor 3,
      Director 19, Admin 31, PlatformOperator 0.

### Deployment evidence
- [x] Demo migration applied to real `SatiDemo` with the three-pass sequence, no EF-generated
      script involved. Dry run: column added, 16 users backfilled, 1 Director and 1 Admin
      corrected, 2 history rows, rolled back — verified afterwards that the column was absent,
      zero history rows, 80 migrations, so the rollback genuinely held. Real run: identical
      counts, committed. Third pass: every count and every proof `0`.
- [x] Resulting distribution across 16 accounts, 82 migrations in history: Admin 31 (1),
      CaseManager 1 (12), **Director 19 (1)**, PlatformOperator 0 (1), Supervisor 3 (1).
      Demo held exactly one Director — the account the original backfill would have handed the
      audit export, settings writes, test-data deletion, and provider merge.
- [x] API ZIP built from the pushed source commit `c2f19b6`, confirmed by the stamped
      `ProductVersion 1.2.34+c2f19b6b19b6045cd3f7113ee9eaba2f03ea3395`. 9,494,910 bytes; SHA-256
      `D797B6F426FD9B65EFB5E4404461AA58041614390F1EBDFDFD01952C54257016`. No `appsettings*.json`,
      Development configuration, or key material in the payload; the triggered WebJob is present
      at `App_Data/jobs/triggered/demo-history-reconciliation/`.
- [x] Deployed to `sati-demo-api-satilogica`, deployment `3a36b20eab2148c3a09a1f0df886e718`,
      provisioning state Succeeded.
- [x] `/health/live` live. `/health/ready` **Healthy** — the real confirmation the migration
      satisfied the deployed model, because `SchemaDriftHealthCheck` compares the model's tables
      and columns against the database. `/health/version` reports release `1.2.34` and contract
      revision `88B12BEC015F`.
- [x] Client/API contract parity verified rather than assumed: `ApiSurface.Revision` read directly
      from the locally built `Sati.Contracts` is `88B12BEC015F`, identical to the deployed value.
- [x] Generated and accepted `SatiDemoSetup-1.2.34.exe` (100,442,112 bytes; SHA-256
      `A3798804E1C5671C7B131DCB4ACE06152AC040D6993F8F99F75A31F0037C0020`): five launches, all
      responding, graceful closes, zero exit codes, installed version 1.2.34.0, isolated cleanup
      passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.34.exe` (202,507,018 bytes; SHA-256
      `DD6E77AF60F667A4DD561954A9072D5CD7D63A5629F3768CB0DE3B086AED7534`): version 1.2.34.0,
      integrated security confirmed with no SQL username or password in configuration, isolated
      cleanup passed. The embedded `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carries a valid Microsoft
      Corporation Authenticode signature, verified before use.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `...\SatiLogica - Documents\SatiLogica Demo Files` and `...\SatiLogica - Documents\Sati
      Desktop`. Destination hashes re-verified after publication and identical to the accepted
      artifacts; no file overwritten; no temporary file left behind.
- [x] The temporary `datt-workstation-temp` firewall rule was added by the user for the migration
      and removed immediately after it. Verified absent afterwards: the allow-list holds exactly
      the three `sati-demo-api-outbound-*` entries. The release workflow never created, altered,
      or deleted a firewall rule.

### Local Production machines
Both machines were on 1.2.33 before this release, per Josh. `SatiDemo` is one database and was
migrated deliberately above; `SatiProduction` is a separate database on each of these machines,
migrated by the desktop at its next launch.

- [x] **This workstation (development machine).** Sati is run from source here, so the release it
      is "on" is the working tree, which was 1.2.33 before this change. Its `SatiProduction` is
      already at **82 migrations** with the permissions backfill applied — its single Admin account
      carries 31 — so the schema half is satisfied on this machine. It was migrated by a
      development build at startup rather than by an installed release: the
      `SeparateAgencyWideSupervision` migration was authored during the 1.2.34 release session, so
      whatever applied it ran from current source the same day.

      Recorded because it is confusing otherwise: the *installed packages* on this machine are
      stale and not the thing being used. The Windows uninstall registry and the on-disk file
      versions both report `Sati (LocalDB) 1.2.23` and `Sati Demo 1.2.27`. A future reader
      comparing installed versions against this record will find them ten releases apart and
      should not conclude the machine was missed.

- [ ] **Colleague's laptop — 1.2.33, has NOT received either migration.** It needs
      `SatiLocalSetup-1.2.34.exe` from `...\SatiLogica - Documents\Sati Desktop`; the desktop will
      apply `AddUserPermissions` and `SeparateAgencyWideSupervision` to its `SatiProduction` at
      first launch, taking a backup first as it does for any change.

      Two things make this the machine to watch. It is the one `AGENDA.md` records as possibly
      still carrying unreconciled migration-history drift, and `SatiProduction` there has not
      received `AddBillingExchangeHistory` or `AddRemittanceDeposits` either. So its first 1.2.34
      launch attempts more than the two migrations named here, against the database most likely to
      disagree with its own history. If it refuses to start, that refusal is correct and
      `scripts/remote-repair` or `scripts/Apply-UserPermissionsMigrations.ps1 -DatabaseName
      SatiProduction` is the path, not a retry.

- [x] Nothing was assumed to have caught up. The one machine reachable from here was inspected
      directly rather than taken on trust; the other is recorded as outstanding rather than
      presumed done.

## Release 1.2.33 — 2026-08-30

Sati repairs the provable half of a migration history disagreement at startup, instead of refusing
to start. No user-facing feature changes and no new migrations.

### Why this release exists
1.2.32 refused to start on three machines with SQL 2705, "Column name 'AgencyId' in table
'Settings' is specified more than once". The refusal was correct — the startup guard backed up,
stopped, and changed nothing — but the message was a provider error, and clearing it needed a
person who could read it and a PowerShell script per machine.

### Startup schema handling
- [x] `MigrationEffectAnalyzer` in `Sati.Persistence` compares what each pending migration declares
      against the live schema before anything is written. Columns, indexes, foreign keys, and
      primary keys are matched by what they map rather than by name.
- [x] Every effect present is recorded rather than applied — an insert into
      `__EFMigrationsHistory` touching no schema and no consumer data. No effect present migrates
      normally, unchanged.
- [x] Only partly present, or a verdict the analyzer cannot reach, still refuses. That is the
      judgement the startup path has always declined to make unattended, and it is unchanged.
- [x] The refusal now names the migration and states that nothing was changed, rather than
      surfacing the provider error.
- [x] Raw SQL and data steps are reported as unverifiable and left out of the verdict. An
      unrecognised operation type counts as unverifiable too, so an unfamiliar migration reports
      `Indeterminate` rather than a confident wrong answer.
- [x] The backup still happens first whenever the database holds records.

### Validation
- [x] Six new tests. The partial-drift guard was confirmed to fail against the ungated code before
      being kept.
- [x] Verified end to end against a real SQL Server database, not only with fakes: a scratch
      database built from the chain, drifted by removing one history row, run through the real
      updater, then dropped. Outcome `Applied`, drift recorded, nothing pending, second run
      `AlreadyCurrent`.
- [x] The analyzer was also run against a genuinely drifted `SatiProduction`, which was restored
      afterwards.

### Release evidence
- [x] Source release commit `d5c640a`, pushed to `origin/master` without rewriting history.
- [x] Full Release build of `Sati.slnx` succeeded. 853 desktop/domain tests passed with the one
      documented opt-in local-AI skip, 250 API integration tests passed, 4 Carika tests passed.
- [x] No schema change. The 80 migration ids are identical to those released in 1.2.32, and the
      contract surface is unchanged, so neither a controlled Demo migration nor a temporary
      firewall rule applied. Verified by comparing id sets, not counts.
- [x] Published the Demo API ZIP built from `d5c640a` (9,462,028 bytes; SHA-256
      `7F6FC2A0850977DE663BB01BF2BAA86C81CE18C03E962F01F6137F27C0F542F3`; no `appsettings*.json`,
      credential, or private desktop configuration in the payload; carries only the
      `demo-history-reconciliation` WebJob). Deployment `8db52990075c479db5870f51629ea1a6`
      succeeded.
- [x] `/health/live` live, `/health/ready` Healthy, `/health/version` reports release 1.2.33 and
      contract revision `7C6F00E77F6E`, matching the client's `ApiSurface.Revision`.
- [x] Full authenticated `Test-DemoReadiness.ps1` gate passed: Admin role, agency 2, 15 users, 177
      people, recent activity, `PolicyOnly` retention, 332 audit events.
- [x] Generated and accepted `SatiDemoSetup-1.2.33.exe` (100,417,536 bytes; SHA-256
      `B80CA22A82532F23B85B30DA583F3D6AB119F9A70B619DF635F5B9478A322DCB`): five responsive
      launches, graceful closes, zero exit codes, version 1.2.33.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.33.exe` (202,486,026 bytes; SHA-256
      `56C6940794408C194BD0585D9757BF8B772B1EB600250767CE9D3ADA34555B4C`): version 1.2.33.0,
      integrated security confirmed, isolated cleanup passed. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` carries a valid Microsoft Corporation Authenticode
      signature, verified before use.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [x] The SQL allow-list held exactly its three `sati-demo-api-outbound-*` rules throughout. No
      temporary firewall rule was created or removed by this release.
- [ ] **Not yet proven on a machine that still has the drift.** Every database reachable from here
      was repaired before this shipped, so the self-repair has been verified against recreated and
      scratch drift rather than against an untouched drifted machine. The one remaining is the
      colleague's. Installing 1.2.33 there rather than running `scripts/remote-repair` would be the
      first real-world exercise of it; note that if that is the chosen route.
- [ ] Once distributed, the remaining drifted machine can install this rather than run
      `scripts/remote-repair`. That kit stays available for anyone on an older build.

## Release 1.2.32 — 2026-08-30

Platform-neutral persistence, schema drift detection, and Demo schema changes without a firewall
rule. No user-facing feature changes and no new migrations.

### Controlled migration deployment
- [x] `Sati.Persistence` targets plain `net10.0` and owns the entity model, `SatiContext`, and all 80
      migrations, so nothing that needs the chain is forced onto Windows.
- [x] `SchemaComparison` in `Sati.Contracts.V1` owns the rule for how two descriptions of a schema
      differ, shared by the readiness check and the drift report. `GET /api/v1/admin/schema-drift`
      returns it, Admin only.
- [x] The `demo-history-reconciliation` triggered WebJob runs inside the App Service, so reconciling
      `__EFMigrationsHistory` on Demo no longer needs a temporary exact-IP SQL firewall rule.
      **Corrected 2026-08-31: this originally read "applying a Demo schema change", which overstated
      what shipped.** The job writes only to `dbo.__EFMigrationsHistory` and reads catalog views —
      no `CREATE`, `ALTER`, or `DROP` — which is why it needs only `db_datawriter`. Applying real
      DDL still needs `Sati.Migrator`, which does not exist yet, and until it does a schema-adding
      release still opens the rule. 1.2.34 did. The sentence was read later as a promise the
      release never made; see `DECISIONS.md`, 2026-08-30 and 2026-08-31.
- [x] `SatiDemo`'s migration history reconciled: two ids applied under superseded timestamps removed,
      two chain migrations with no history row written, verified idempotent.

### Defect fixed
- [x] `ServerPerson.FirstName`, `ServerPerson.LastName`, and `ServerClaimLine.Units` were declared
      nullable in the API model while `SatiDemo` has them `NOT NULL`. The database was the stricter
      side, so the model was tightened and no schema was touched.

### Validation and release evidence
- [x] Source release commit `afa59df`, pushed to `origin/master` without rewriting history.
- [x] Full Release build of `Sati.slnx` succeeded. 847 desktop/domain tests passed with the one
      documented opt-in local-AI skip, 250 API integration tests passed, 4 Carika tests passed. Two
      pre-existing `CS8604` nullable warnings remain in provider test files; they are warnings, not
      errors, and predate this change set.
- [x] No schema change. The 80 migration ids are identical to those released in 1.2.31, so neither
      the controlled Demo migration authorization nor a temporary firewall rule applied to this
      release. Verified by comparing id sets, not counts.
- [x] Published the Demo API ZIP built from `afa59df` (9,453,008 bytes; SHA-256
      `8FCE069B21EB30B663DF68DCE4F5081E0ECB035FF4708F61C900D4BD06A5DFF1`; no `appsettings*.json`,
      credential, or private desktop configuration in the payload; carries only the
      `demo-history-reconciliation` WebJob). Deployment `4e65fda9c6a24ef88db74b52bfcc046e` succeeded.
- [x] `/health/live` live, `/health/ready` Healthy, `/health/version` reports release 1.2.32 and
      contract revision `7C6F00E77F6E`, which exactly matches the client's `ApiSurface.Revision`.
      **This restores parity**: the published 1.2.31 installers expected `F929FEB01DEB` while the
      deployed API already served `7C6F00E77F6E`, so a fresh Demo install showed the compatibility
      banner until now.
- [x] Full authenticated `Test-DemoReadiness.ps1` gate passed, not only the health-only form: Admin
      role confirmed, agency 2, 15 users, 177 people, recent activity, `PolicyOnly` retention, 331
      audit events. The 1.2.30 and 1.2.31 releases could only record this as skipped.
- [x] Generated and accepted `SatiDemoSetup-1.2.32.exe` (100,421,632 bytes; SHA-256
      `D6AC3024E6A4D965065EECAF6CC667349B2800815CCE2EE8EBC7F3E2DF4E3A94`): five responsive launches,
      graceful closes, zero exit codes, version 1.2.32.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.32.exe` (202,474,762 bytes; SHA-256
      `CA4E3FA08C43B9316F79A714D6052ED3C7D1322B6F59E470E8917BD7F06F4534`): version 1.2.32.0,
      integrated security confirmed, isolated cleanup passed. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carries a valid Microsoft
      Corporation Authenticode signature, verified before use.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [x] The SQL allow-list held exactly its three `sati-demo-api-outbound-*` rules throughout. No
      temporary firewall rule was created or removed by this release.
- [ ] **Flaky test, identified by name at last:
      `DatabaseActivityTests.PatienceStateAppearsOnlyAfterTheConfiguredContinuousDelay`.** It failed
      once in ten runs across this release's gates and once during the 2026-08-30 persistence work,
      always inside `EventuallyAsync`, which polls against a one-second wall-clock deadline. Under
      concurrent build load that budget is too tight for the awaited state transition. It is a
      timing artifact, not a defect in the code under test, and nothing in 1.2.32 touches
      `DatabaseActivityViewModel`. Widen the deadline or make the transition awaited rather than
      polled — deliberately, not inside a release commit to turn a gate green.
- [ ] `SatiProduction` has not received `AddBillingExchangeHistory` or `AddRemittanceDeposits`. The
      desktop applies them on its next direct connection, now from a relocated assembly.
      `LocalDatabaseUpdate` takes a full backup first when the database holds records and names the
      backup path on failure, so the bad case is a legible error rather than a half-applied database.
      Watch that first launch.

## Release 1.2.31 — 2026-08-30

Billing submission home, denial worklist, humanized adjustment reasons, and deposit reconciliation.

### Billing exchange operations
- [x] Add inclusive billing-month range filters and keep each locked monthly period as its own
      retry-safe 837P generation request.
- [x] Add an API-authoritative submission home that groups append-only exchange events into current
      batch rows with claim count, charge value, send time, current status, search, and outstanding
      filters. Synthetic provenance remains a dedicated field.
- [x] Add a denial/unpaid worklist with status and 30/60/90/120+ aging filters, fast claim/payer/date
      search, and a shared CARC group-code explanation catalog for CO/PR/OA.
- [x] Add an explicit 835/EFT deposit model whose shared arithmetic exposes claim payments, PLB
      provider-level adjustments, EFT difference, pending EFT, mismatch, and penny-match states.
- [x] Extend the Demo-only synthetic seed and test exchange with accepted, rejected, partial, denied,
      reversed, unmatched, needs-review, PLB, pending-EFT, and EFT-mismatch contingencies.

### Validation and release evidence
- [x] Apply the identity-validated Demo billing schema runner twice; both real runs found the three
      tables/indexes and migration-history rows already present and changed nothing.
- [x] Source release commit `f3f56cd`, pushed to `origin/master` without rewriting history.
- [x] Full Release build of `Sati.slnx` succeeded. 834 desktop/domain tests passed with the one
      documented opt-in local-AI test skipped, 244 API integration tests passed, 4 Carika tests
      passed.
- [x] Published the Demo API ZIP built from `f3f56cd` (9,230,215 bytes; SHA-256
      `F5EECBE04C05CB3ACDB244817EADFCAA3485101FDC5F23FA6E949E0BAB095374`; no `appsettings*.json`,
      credential, or private desktop configuration in the payload). OneDeploy deployment
      `a140d7d5883f4a3b98b9b5401310b06a` succeeded 2026-08-30T01:40:34Z.
- [x] `/health/live` live, `/health/ready` Healthy, `/health/version` reports release 1.2.31 and
      contract revision `F929FEB01DEB`, which exactly matches the client's `ApiSurface.Revision`.
      `SchemaDriftHealthCheck` returning Healthy is the deployed confirmation that the three
      billing exchange tables are present in `SatiDemo`.
- [x] `Test-DemoReadiness.ps1 -HealthOnly` gate passed. The authenticated extension was skipped:
      no synthetic Demo credentials are configured in this Windows session.
- [x] Generated and accepted `SatiDemoSetup-1.2.31.exe` (100,388,864 bytes; SHA-256
      `707286DFACAEE6A35436EC808E365E3E2A60A6F85356DB23B0F097CAA6F717FE`): five responsive
      launches, graceful closes, zero exit codes, version 1.2.31.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.31.exe` (202,454,794 bytes; SHA-256
      `E383480A64E65C2F41E01F10CD995E0AD36BB54DDCA3FF4EE2D642D93BA49E34`): version 1.2.31.0,
      integrated security confirmed, isolated cleanup passed. The embedded
      `artifacts\Prerequisites\SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`) carries a valid
      Microsoft Corporation Authenticode signature.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [ ] The rollback-only dry run of `scripts/Apply-BillingExchangeMigrations.ps1` was not repeated in
      this release pass. It reaches `SatiDemo` from the workstation, which needs a temporary
      exact-IP SQL firewall rule that only the user may add and remove. The deployed readiness
      check above already confirms the schema the release depends on.
- [ ] Keep live Office Ally transport, real 999/TA1/277CA/835 ingestion, corrected claims, raw X12
      drill-through, payer certification, note-to-denial loop, auth/unit alerts, forecasting, and
      benchmarking as explicitly deferred work.

### Follow-up
- [x] The `datt-workstation-temp` SQL firewall rule from the 1.2.30 release is gone. Verified
      2026-08-30 against `sati-demo-satilogica-central`: the allow-list holds only the three
      `sati-demo-api-outbound-*` App Service addresses. The box had simply never been ticked;
      nothing in 1.2.31 needed or used the rule.
- [ ] `SatiProduction` has not received `AddBillingExchangeHistory` or `AddRemittanceDeposits`. The
      desktop applies them on its next direct connection; given that database's own history drift,
      watch that first launch.


## Permissions per user, not a user type — 2026-08-30

Completed 2026-08-30. Agency authorization is now a persisted per-user permission set rather than
the legacy `Role` label. All fourteen billing routes require billing permission, so billing access
no longer grants user management, test-data deletion, audit export, operations, or schema reports.

Replace the single role with a per-user permission set: billing, case manager, supervisor,
admin. Someone with the billing permission sees and uses the billing dashboard without
being an Admin.

- [x] One owner in `Sati.Contracts.V1`, beside `BillingComplianceGate` and `BillingRules`,
      so the desktop and the API cannot answer "can this person bill?" differently. A
      `[Flags]` set with `HasBillingPermission`-style predicates rather than four loose
      booleans: call sites stay readable and the set stays extensible.
- [x] Resolve permissions in `ValidatedActorFilter`, which already re-confirms identity,
      role, and agency against the database per request. Not from a token claim — revoking
      billing access should take effect immediately rather than at the next 30-minute token
      expiry.
- [x] Billing domain services take the actor as an explicit parameter rather than reading ambient
      login state. **The value stays server-derived.** A signature that accepts an actor is
      good design; a route that reads a user id from the request body is a tenant-isolation
      hole, and the rule that caller-supplied `userId`/`agencyId` is never trusted does not
      relax here.
- [x] `PlatformOperator` stays orthogonal. It is a separate cross-tenant identity for
      incident telemetry, not a bundle of agency permissions.
- [x] Deny by default. `!= "Admin" → Forbid` becomes `!HasBilling → Forbid`, never "no
      permission matched, so allow".
- [x] Migration backfills existing roles to permission sets.
- [x] **Land it in one change.** The permission gates, route inventory in
      `API_AUTHORIZATION.md`, and a test per route. A half-migrated model where some routes
      check roles and others check permissions is worse than either end state, because the
      gap is invisible until somebody finds it.
- [x] UI visibility follows the permission, but the API enforces independently. Showing the
      dashboard is not what grants access. **Was not true on local Production**, where no API sits
      behind `UserService` — closed 2026-08-31, finding 5 below.

### Line-by-line review — 2026-08-31

The conversion shipped with an explicit caveat: it had not verified that each gate got the right
permission, that no route lost a tenant check, or that the new tests fail against ungated code.
That pass is done and recorded in `API_SECURITY_AUDIT.md` (third pass plus resolution). Tenant
scoping survived intact and the route inventory matches the code exactly. Three findings blocked
release and are fixed; what remains:

- [x] **Denial tests for the supervision gates — done 2026-08-31.** `SupervisionGateTests` covers
      all nine plus the one in `TenantAccess.CanAccessUserAsync`. The actor is a demoted
      supervisor: case management only, still named as user 19's supervisor, which is what a real
      database holds the moment supervision is revoked while supervisees still point at the row.
      That shape is what makes the tests load-bearing — every query beneath these gates is scoped
      by `SupervisorId`, so an ordinary case manager sees an empty list either way and proves
      nothing. Verified: 11 of the 13 fail with every supervisor gate disabled. The two that
      survive are the positive controls, which is correct.
- [ ] **Denial tests for the case-management gates.** Still outstanding. All API tests pass with
      every `!actor.HasCaseManagerPermissions` gate disabled.
- [ ] **Denial tests for `GET /admin/incidents`, `PUT /admin/incidents/{id}/status`, and
      `ProviderDirectoryRules.CanCreateOrEdit`.** The only administration and provider gates with
      no covering test.
- [ ] **Finding 6: four caseload routes scope by owning `UserId` with no agency predicate.**
      `GET /people/{personId}/journal`, `PUT /people/{personId}`,
      `PUT /people/{personId}/contacts/{contactId}`, `DELETE /contacts/{contactId}`. Pre-existing,
      same class as the accepted `POST /at-requests` item. They also silently miss the
      case-management clause `OwnsPersonAsync` gained in the conversion.
- [ ] **Finding 7: the validated-permissions claim is shadowable by construction.** Safe today
      because `TokenIssuer` never emits it; prefer `HttpContext.Items` over mutating the principal.
- [ ] **No API route for self-service profile editing.** `UpdateOwnContactDetailsAsync` has to go
      through `PUT /api/v1/users/{id}`, which requires supervision or administration, so against a
      hosted database an ordinary case manager cannot change their own email or phone. Unchanged
      behaviour, newly visible now the operation has its own name.

## Controlled migration deployment — 2026-08-30

`CLAUDE.md` lists controlled migration deployment as outstanding cloud-platform foundation. Today a
schema release needs a hand-written `Apply-*.ps1` run from a workstation, which needs a temporary
exact-IP hole in the `SatiDemo` SQL firewall. That hole is the last link in a chain, not the first:
`__EFMigrationsHistory` disagrees with the real schema in both directions, so
`dotnet ef migrations script --idempotent` fails with SQL 2705, so every migration gets its own
bespoke script, so a human must run it, so the firewall must open. There are ten such scripts in
`scripts/`, each a fresh chance to get it wrong. Reconciling the history table dissolves the rest.

### Phase 0 — Build the instrument (done 2026-08-30)
- [x] `SchemaComparison` in `Sati.Contracts.V1` owns the rule for how two descriptions of a schema
      differ, shared by the readiness check, the drift report, and later the migrator verify step.
      It takes plain data, because `Sati.Contracts` carries no package references and must not
      acquire EF Core.
- [x] Report both directions. `SchemaDriftHealthCheck` was one-directional and name-only —
      model-expects-but-database-lacks, columns only — and so was blind to the drift that actually
      breaks releases: objects the database has that the chain never recorded.
- [x] `SchemaSnapshotReader` extracts a snapshot from an EF model and from a live database, provider
      aware so the SQLite-backed integration tests exercise it rather than leaving it to run for the
      first time against Azure SQL.
- [x] A partial model may report only what it needs. `ApiDbContext` maps just the tables the API
      serves, so declaring it authoritative would report every desktop-only table as drift and bury
      the real findings.
- [x] `GET /api/v1/admin/schema-drift`, Admin only, returns the report. `/health/ready` still emits
      only the status word, and its description still never reaches the anonymous response writer.
- [x] Readiness still gates on `PreventsQueries` alone, which is the same set of failures it
      reported before the rule was extracted. Widening the readiness gate is a release-blocking
      decision and belongs in its own change.
- [x] 12 comparison tests and 5 route tests. The Admin gate was confirmed to fail against ungated
      code before the test was kept.
- [ ] Store-type comparison is deliberately absent. EF reports `nvarchar(max)` where
      `INFORMATION_SCHEMA` reports `nvarchar` with length -1, and `decimal(18,2)` as three separate
      columns. Normalizing well enough to avoid false positives is real work, and a drift report
      that cries wolf is worse than one with a documented gap.
- [ ] **Defaults, indexes, and foreign keys are not compared either, and that gap has already cost
      something.** The 2026-08-30 report came back with only three nullability findings, while
      `SatiDemo` was missing a DEFAULT constraint the chain declares — found later by the
      reconciliation's own proofs. A clean report means "no table or column is missing or
      differently nullable", not "the schema matches the chain", and it should not be read as the
      latter. Extending the comparison to constraints is the obvious next increment; until then the
      proofs in `Apply-DemoHistoryReconciliation.ps1` are the more thorough instrument.

### Phase 1 — Reconcile once, per environment
The `SatiDemo` report was taken 2026-08-30 from `GET /api/v1/admin/schema-drift` against deployment
`5ff1a9d9a9c44f8088863badb6761c1a` (contract revision `7C6F00E77F6E`), with no firewall rule opened.
Result: **0 blocking differences, 3 nullability findings, and 4 migration-history discrepancies.**
Every one is the history being wrong rather than the database being wrong, which makes the
reconciliation history-row surgery rather than corrective DDL — the best case the plan allowed for.

That sentence was retracted on 2026-08-30 and then reinstated the same evening. The retraction
claimed `Users.AgencyId` was missing the constant default `AddAgencyId` declares, and therefore that
the reconciliation needed corrective DDL. **That was wrong. The original sentence stands: the
findings were history being wrong, not the database.**

The default constraint `DF__Users__AgencyId__57DD0BE4`, definition `((1))`, has existed since
2026-08-11. The proof reported it absent because the App Service managed identity held only
`db_datareader` and `db_datawriter`, and neither carries `VIEW DEFINITION`. Without that permission
a principal sees the table and its columns but not its constraint rows in `sys.default_constraints`.
Granting `ALTER` on `dbo.Users` implies `VIEW DEFINITION`, and the same proof passed immediately
afterwards against an unchanged database.

The lesson is a real one and worth more than the wasted evening: **a proof that reads catalog views
cannot distinguish "the object is absent" from "the object is invisible to me", and this one
reported the second as the first.** Anything asserting on schema through `sys.*` needs either a
principal with `VIEW DEFINITION` or an explicit check that it can see what it is about to judge.
`SchemaComparison` still does not compare default constraints, indexes, foreign keys, or store
types, so a clean Phase 0 report still means "no table or column is missing or differently nullable"
rather than "the schema matches the chain" — but nothing was found hiding behind that gap.

Two ids were applied under a timestamp that was later regenerated, so the objects exist while the
history row points at an id no longer in the chain. This is the documented SQL 2705 cause: EF
believes the surviving id never ran and an idempotent script tries to recreate columns that are
already there.

| Applied to SatiDemo | Superseded by, in the chain |
|---|---|
| `20260416005941_AddingAgencyId` | `20260416011235_AddAgencyId` |
| `20260825155740_AddConsumerEmail` | `20260825163103_AddConsumerEmail` |

Two have no history row on Demo, and their objects are already present:

- `20260812090000_TenantScopeSettingsAndProviders` adds `AgencyId` to `Settings` and `Providers`.
  `ApiDbContext` maps both as non-nullable `int` and the report shows nothing blocking, which proves
  the columns exist.
- `20260816120000_AddNoteMinutesAndStartTime` is already written guarded, and says so in its own
  comment: a bare `AddColumn` would fail with SQL 2705 on databases that predate it.

**Correction to the original reading of the first of those.** It was recorded here as "in the chain
but not applied". It was not in the chain at all: the source file carried neither `[DbContext]` nor
`[Migration]`, so EF never enumerated it regardless of a correct-looking filename. The original
classification came from listing filenames rather than asking EF, and a filename is not membership.
Both attributes were restored during the 2026-08-30 persistence move, and
`PersistenceAssemblyBoundaryTests` now pins the discoverable count at 80 so the gap cannot silently
reopen. The reconciliation still needs its history row; the reason it was missing is different from
what was first written down.

- [x] Write the `SatiDemo` reconciliation: insert history rows for
      `20260416011235_AddAgencyId`, `20260825163103_AddConsumerEmail`,
      `20260812090000_TenantScopeSettingsAndProviders`, and
      `20260816120000_AddNoteMinutesAndStartTime`, and remove the two superseded rows only after
      confirming each surviving id's objects match the expected semantics rather than merely the
      expected name. Keep the discipline `Apply-ProviderDirectoryMigrations.ps1` already has: fail
      closed on `DB_NAME()` and `SatiDatabaseIdentity`, guard every statement on the actual schema,
      stay rerunnable. Drafted as `scripts/Apply-DemoHistoryReconciliation.ps1`; its PowerShell
      parser is clean, but it has deliberately not been run against any database.
- [x] Ran against live `SatiDemo` 2026-08-30 through the WebJob, at Josh's direction and without the
      restored-copy rehearsal. The dry run refused on the first failed proof; a second run in
      `-ProofsOnly` enumerated the full set. **Exactly one proof fails.**
- [x] ~~`Users.AgencyId` has no constant default of 1.~~ **Retracted. The constraint was never
      missing.** `DF__Users__AgencyId__57DD0BE4`, definition `((1))`, created 2026-08-11. The proof
      reported it absent because the managed identity held only `db_datareader`/`db_datawriter` and
      neither carries `VIEW DEFINITION`, without which constraint rows do not appear in
      `sys.default_constraints`. `GRANT ALTER ON OBJECT::dbo.Users` implies `VIEW DEFINITION`, and
      the proof passed immediately afterwards against an unchanged database. Verified by reading the
      constraint's `create_date` directly and by the history row count never moving from 80.

      Cost of the misdiagnosis: `Add-DemoUsersAgencyIdDefault.ps1`, the
      `demo-users-agencyid-default` WebJob, and the `ALTER` grant all exist to fix a problem that did
      not exist. See the open item below on whether to keep them.
- [x] **Reconciliation applied 2026-08-30.** Four semantic proofs verified, 2 surviving history rows
      written, 2 superseded rows removed, committed. A second run wrote 0 and removed 0, proving
      idempotency. Direct query confirms 80 rows with all four surviving ids present and both
      superseded ids gone. `/health/ready` Healthy afterwards. Run entirely through the WebJob: **no
      firewall rule was opened for any part of it.**
- [x] Rehearsal against a restored copy was skipped at Josh's explicit direction, against the
      script's own recommendation. Recorded rather than quietly omitted. Nothing was lost, but the
      run that misdiagnosed the constraint is a fair argument for rehearsing next time.
- [x] **Constraint job removed 2026-08-30.** `Add-DemoUsersAgencyIdDefault.ps1`, the
      `demo-users-agencyid-default` WebJob, and its packaging are gone; the API package now carries
      only `demo-history-reconciliation`. A job able to perform DDL against a live database, kept
      against a possibility, is a standing surface for no benefit. If a genuine constraint
      divergence ever appears, write the corrective script then.
- [x] **Phase 5 shipped 2026-08-30 (A and B).** `MigrationEffectAnalyzer` in `Sati.Persistence`
      classifies each pending migration against the live schema before anything is written.
      `LocalDatabaseUpdater` records the provable case, still refuses the ambiguous one, and now
      names the migration instead of surfacing SQL 2705. Verified end to end against a real SQL
      Server database, not only with fakes. Reasoning and what was rejected in `DECISIONS.md`.
- [ ] **The migration chain does not replay on an empty database.** A migration reads
      `dbo.SatiDatabaseIdentity`, which is created outside the chain, so `MigrateAsync` against a
      fresh database fails with `Invalid object name`. Real installs create that table first, so
      nothing is broken today, but the chain alone cannot reconstruct a database and the
      rehearsal harness had to work around it. The unmerged `second-machine-setup` branch carries
      a commit named for exactly this; worth reviewing rather than solving twice.
- [ ] **Narrow the grant to `VIEW DEFINITION`.** The proofs read `sys.default_constraints`, which
      `db_datareader`/`db_datawriter` cannot see; that is the whole reason `ALTER` appeared to be
      needed. `ALTER` additionally lets the identity change the table, and with the constraint job
      gone nothing requires it. Commands and ordering in `OPERATIONS.md` — grant `VIEW DEFINITION`
      before revoking `ALTER`, or the proofs fail again for the same invisible reason. A person makes
      the grant; no workflow, script, or agent does.
- [x] Tighten the API model rather than the database for the three nullability findings. The
      database is the stricter side in all three, so the model was the loose one and the fix does not
      touch schema. `ServerPerson.FirstName` and `ServerPerson.LastName` were declared `string?` and
      `ServerClaimLine.Units` was `decimal?`, while `SatiDemo` has all three `NOT NULL`. Before they
      agreed, the API could attempt a null write and take a constraint violation at run time. This is
      drift between the two hand-maintained models over one database, which is the third axis
      Phase 0 was extended to expose, and it found instances on its first real run.
- [ ] Run the same report against `SatiProduction`. It needs the desktop-side reader from Phase 5;
      the API route covers Demo only.
- Exit: the idempotent script runs clean twice against a restored copy of each database and the
  Phase 0 report is empty in both directions. This is the last release that opens the SQL firewall.

### Phase 1.5 — Free the migration chain from the desktop project
Discovered while building Phase 0, and a hard prerequisite for Phase 2. All 80 migrations belong to
`SatiContext` in `Sati.csproj`, which is `net10.0-windows` with `UseWPF`. A migrator that references
it inherits WPF and can only ever run on Windows, which forecloses the Linux-container option in
Phase 3 before it is chosen. `ApiDbContext` is a second, hand-maintained model over the same tables
with no chain of its own, so it cannot substitute.
- [x] Move the entities and `SatiContext` behind a platform-neutral `net10.0` project that the
      desktop, the API, and a migrator can all reference.
- [x] Keep the desktop `LocalDatabaseUpdate` path working unchanged; it migrates the live
      `SatiProduction` at startup and is the highest-regression-risk part of this move.
- [x] Make all 80 migration ids discoverable from `Sati.Persistence`. The hand-authored
      `20260812090000_TenantScopeSettingsAndProviders` source lacked its migration/context
      attributes and was therefore invisible to EF despite being described as part of the chain;
      only that metadata was added.
- [ ] Rehearse the unchanged desktop migration path against a restored `SatiProduction` copy. The
      assembly boundary, EF discovery, full build, and sequence tests are local evidence; they do
      not substitute for the restored-copy exit check.
- Exit: `dotnet ef migrations list` resolves against a project with no WPF reference, and the
  desktop still migrates a restored `SatiProduction` copy cleanly.

### Phase 2 — Sati.Migrator
- [ ] Console project with three modes: `plan` (default; prints pending migration ids and the DDL,
      changes nothing), `apply` (requires a matching environment marker and an explicit
      `--authorized-by`, fails closed otherwise), and `verify` (re-runs the Phase 0 comparison,
      non-zero exit on any drift).
- [ ] Write an `AuditEvent` on apply recording migration ids, authorizer, source commit, and the
      resulting schema fingerprint. This is the integrity evidence `REGULATORY_CONCERNS.md` wants.
- Exit: plan/apply/verify reproduces the Phase 1 end state on a restored copy from both an empty and
  a current database, and a second apply is a no-op.

### Phase 3 — Run it where access already exists
**This must be in place before `SatiProduction` moves to the cloud, and it is a release gate rather
than a preference.** While Demo holds only synthetic data the workstation hole is a proportionate
risk and the cheaper option below is defensible. The moment a cloud database holds real consumer
records, an identity that can alter schema is a different category of exposure and the enforced
boundary stops being optional. A cloud Production deployment must not ship ahead of this phase.

**Decided 2026-08-30: triggered WebJob now, Container Apps Job before cloud Production.** The
original recommendation deferred this phase entirely on cost grounds, which optimised for
proportionate security spend rather than for removing the recurring firewall step — and removing
that step is the actual goal. Because Phase 1.5 made the runner host-agnostic, choosing the cheap
host now locks in nothing; hosting is a thin, swappable layer. Reasoning in `DECISIONS.md`.

- [x] `Sati.Api/WebJobs/demo-history-reconciliation/run.ps1`, packaged by `Sati.Api.csproj` to
      `App_Data/jobs/triggered/demo-history-reconciliation/` alongside the reconciliation script.
      Verified present in the publish output.
- [x] `-UseManagedIdentity` on `Apply-DemoHistoryReconciliation.ps1` acquires the SQL token from the
      App Service identity endpoint, and throws when that endpoint is absent rather than falling
      back to integrated security — off-host that would silently connect as the signed-in developer.
      Verified: it throws from a workstation without attempting a connection.
- [x] Fail-safe default. Anything other than the exact app setting `SATI_RECONCILIATION_MODE=apply`
      is a rollback-only dry run. Manual trigger only; no `settings.job` schedule.
- [x] Deployed 2026-08-30 from `89db2d8` (deployment `97da77ed76c140eaa7974fd1b42efc6e`, API ZIP
      SHA-256 `81AB984D0BA35D52E164CAF20ECE2B9394BC58F454BEC800969A1FE40181E03E`). Health live and
      Healthy, contract revision unchanged at `7C6F00E77F6E`. The job registers as
      `demo-history-reconciliation` with `runCommand: run.ps1`. No firewall rule was opened.
- [ ] ~~Grant the App Service managed identity DDL rights.~~ **Corrected: this job most likely needs
      no new grant.** The reconciliation issues only `INSERT`/`DELETE` on
      `dbo.__EFMigrationsHistory` plus catalog reads — no `CREATE`, `ALTER`, or `DROP` — which is
      `db_datawriter`, already required to serve the API. `db_ddladmin` is owed when `Sati.Migrator`
      applies real schema migrations, not before. Establish the answer by running the dry run, which
      fails closed on a missing permission, rather than by granting speculatively. Any such grant
      stays a security setting a person makes; no workflow, script, or agent performs it.
- [ ] **Rehearse against a restored copy before the first live run.** The script says so in its own
      notes and Phase 1 repeats it. `-WhatIfOnly` rolls back but still connects to the live database
      and takes serializable locks, so the dry run is not free. Either rehearse on a copy, or record
      the decision to accept that risk against synthetic Demo data.
- [x] **The mechanism is proven.** On 2026-08-30 the job ran inside App Service, the managed
      identity authenticated to `SatiDemo` and executed the full proof phase, dry-run mode was
      selected from the absent app setting, and it failed closed with exit code 1 on the one failing
      proof. A second run in `proofs` mode completed and reported. The allow-list held exactly its
      three `sati-demo-api-outbound-*` rules before, during, and after: **no temporary firewall rule
      was opened at any point.** That is the phase's whole proposition, demonstrated end to end.
- [x] The permission question is partly answered. The identity connected and read schema with no new
      grant, so `db_ddladmin` was not needed to get this far. Neither run reached the write phase, so
      `INSERT`/`DELETE` on `__EFMigrationsHistory` remains unproven.
- [x] Real run and idempotency run completed 2026-08-30 through the job, no firewall rule opened.
      Details in the Phase 1 entries above. The permission question is fully answered: the identity
      needed no grant beyond the `db_datawriter` it already held to write history, and the `ALTER`
      grant that was made turned out to matter only for catalog visibility.
- [ ] Once that lands, rewrite `RELEASE_PLAYBOOK.md` section 6 so the migration step stops implying
      a workstation connection, and stop reporting the workstation's public address in preflight.

- [ ] Superseded, kept for the reasoning: the open decision as originally framed.
      - Triggered WebJob: no new infrastructure and already inside the SQL allow-list, but it runs
        under the App Service managed identity. Identity is scoped to the resource, not the process,
        so anything in that site can request a token for any identity assigned to it, which means
        the internet-facing API would effectively hold DDL rights on `SatiDemo`. That is a standing
        privilege escalation on the most exposed component, in exchange for removing a temporary,
        human-supervised hole. Acceptable only while the data is synthetic.
      - Container Apps Job: its own resource and therefore its own identity, genuinely out of reach
        of the API. Recommended, and required once real records are involved.
- [ ] `sati-demo-api-satilogica` runs on `asp-sati-demo-central-f1` — **F1 Free tier, Windows,
      alwaysOn false**. Free tier has no VNet integration, so a private endpoint to SQL is not
      available on the App Service path, and the F1 60-minute daily CPU quota plus site sleep make a
      WebJob fragile for anything long-running. A tier change is part of the real cost of the WebJob
      option.
- [ ] Correct the earlier exit criterion recorded here: a Container Apps Job has its own egress and
      would need a **fourth** permanent allow-list entry, or a VNet with a NAT gateway, or a private
      endpoint. The gain is replacing a recurring, human-opened, workstation-scoped hole with one
      standing rule for a non-interactive service — the same shape as the three App Service rules
      already trusted. It is not the elimination of firewall rules.
- Exit: a schema release completes end to end with no temporary rule added or removed, and the
  allow-list holds only service-scoped entries.

### Phase 4 — Fold into DATT
- [ ] `RELEASE_PLAYBOOK.md` section 6 becomes plan, show the DDL, obtain explicit authorization,
      apply, then verify against `/health/ready`. The human authorization gate does not move; only
      the network hole disappears.
- Exit: the playbook contains no firewall instruction, and `AGENTS.md` item 5 carve-out is no longer
  on the normal path.

### Phase 5 — SatiProduction
`SatiProduction` is local, not Azure: `LocalDatabaseUpdate` calls
`SqlLocalDatabaseMaintenance.MigrateAsync`, which calls `Database.MigrateAsync()` at desktop
startup. No firewall is involved, but the same history drift is, applied automatically to the live
working tool with no plan step and no gate.
- [ ] Share `SchemaSnapshotReader` rather than writing a second one. It lives in `Sati.Api` today
      because nothing else could hold it; `Sati.Contracts` cannot take EF Core. The Phase 1.5
      platform-neutral project is the natural home. A second hand-written reader is a defect.
- [ ] Run the Phase 0 comparison before `MigrateAsync` and refuse with a legible message naming the
      offending object when the pending chain would collide with drift, rather than failing partway
      through a multi-migration chain.
- [ ] Reconcile it in Phase 1 alongside Demo.
- Exit: a schema-adding release either applies cleanly at next launch or refuses with a reason.
  Never a half-applied database.

## Release 1.2.30 — 2026-08-28

Medical provider directory and consumer provider lists. Includes the Admin test-data deletion
work tracked as "Unreleased" below, which shipped in this release.

### Provider directory hierarchy
- [x] `Provider.MedicalKind` (`Individual | Practice | Network`) and a single
      `ParentProviderId` self-reference. Not two typed columns: a hospitalist belongs to a
      network with no practice between, and a second column could disagree with the first.
- [x] `ProviderAffiliation` in `Sati.Contracts.V1` owns the tier rule, loop rejection, depth
      bound, ancestor walk, picker filter, and the delete refusal, so the desktop and the API
      cannot answer differently.
- [x] Deleting an entry with entries beneath it, or on any consumer record, is refused with a
      count and never consumer names.

### Consumer provider list
- [x] `PersonProvider` stores the link and the relationship's own fields, and no copy of the
      practice or network — those derive from the directory on every read.
- [x] `EndDate` alone says whether a link is current; ending keeps the row. No cap on the list.
- [x] At most one current primary care provider and one current link per provider, enforced by
      `ConsumerProviderRules` and filtered unique indexes.

### Superseding the pre-directory fields
- [x] `LegacyProviderLinking` matches the free-text provider fields to directory entries —
      exact only, ambiguity refused rather than resolved — and proposes; a case manager
      confirms. No bulk backfill runs over live consumer records.

### Documents
- [x] `AssessmentNeed` freezes the resolved provider, practice, and network at the moment of
      choosing. The one place the chain is copied rather than derived, so an approved
      assessment keeps saying what it said.

### Shared agency directory curation
- [x] Any caseload role may add and correct directory entries; only an Admin may remove or
      merge them, enforced in both paths.
- [x] A same-name entry warns without blocking.
- [x] `ProviderContact` holds several named people per entry, alongside the organization's
      general directory line.
- [x] An Admin can merge two entries; documents that named the merged entry are left alone.

### Admin test-data deletion
- [x] Shipped as described in the superseded section above, including the
      `AddTestConsumerMarker` migration and its Demo-only backfill.

### Validation
- [x] Full solution builds in Release configuration.
- [x] 828 desktop/domain tests pass, 1 documented opt-in local-AI test skipped.
- [x] 243 API integration tests pass.
- [x] 4 Carika tests pass.

### Deployment and artifact evidence
- [x] Source release commit `afea910` pushed to `origin/master` without rewriting history.
- [x] Applied the SatiDemo schema with `scripts/Apply-ProviderDirectoryMigrations.ps1`: 2 tables,
      3 columns, 7 indexes, 1 foreign key, 4 `__EFMigrationsHistory` rows, and the Demo-only
      backfill marked 177 consumers as test data. A rollback-only dry run preceded it and a
      second run changed nothing, proving idempotency. Existence-guarded rather than EF's
      generated script, because SatiDemo's history and schema disagree in both directions.
- [x] Published `Sati.Api-1.2.30.zip` (9,215,613 bytes; SHA-256
      `40741170E749191182A1054EE01C36215BCAEF1924A6A0E2E41F0732CA936FAD`) from the pushed commit
      to the existing Demo API only, with OneDeploy deployment
      `03cc6a69372247f1bbb061e1e29ca8d6`. The package contains no private desktop settings or
      credential markers.
- [x] Liveness healthy, readiness healthy — `SchemaDriftHealthCheck` therefore confirms the
      database satisfies the deployed model. `/health/version` reports product `Sati.Api` and
      release 1.2.30, and deployed contract revision `58E5DFFE4966` exactly matches the client.
- [x] Generated and accepted `SatiDemoSetup-1.2.30.exe` (100,356,096 bytes; SHA-256
      `552B25007716707BF86EB3758E5BB5BBBF1925D8C1C0C043A892CA907FCB72A7`): five responsive
      launches, graceful closes, zero exit codes, version 1.2.30.0, isolated cleanup passed.
- [x] Generated and accepted `SatiLocalSetup-1.2.30.exe` (202,419,978 bytes; SHA-256
      `B9EBDBAE3ABFA8DEF66C0AEF13255E458BFD3BB40420E9C13EA78E296CE58FE2`): version 1.2.30.0,
      integrated security confirmed, isolated cleanup passed. The embedded `SqlLocalDB.msi`
      carries a valid Microsoft Corporation Authenticode signature.
- [x] Published both installers and their `.sha256` files by verified copy-then-rename to
      `…\SatiLogica - Documents\Sati Desktop` (Local) and
      `…\SatiLogica - Documents\SatiLogica Demo Files` (Demo). No existing file was replaced.
- [x] The authenticated Demo Admin extension was skipped: no synthetic Demo credentials are
      configured in this Windows session. All required public release checks passed.

### Follow-up
- [x] Removed the temporary `datt-workstation-temp` firewall rule. Verified absent 2026-08-30 on
      the SQL server `sati-demo-satilogica-central` (the rule lives on the SQL logical server, not
      on the `sati-demo-api-satilogica` App Service named in the original note).
- [ ] `SatiProduction` has not received these four migrations. The desktop applies them on its
      next direct connection; given that database's own history drift, watch that first launch.

## Admin test-data deletion — shipped in 1.2.30

- [x] Add a clearly labeled Admin-only “Delete test consumer” action to the agency Person directory.
- [x] Require an explicit destructive confirmation with the requested test-only affirmation and
      guidance for duplicate or inactive consumers; Cancel and a missing view handler fail closed.
- [x] Enforce Admin role, agency ownership, exact versioned attestation, and optimistic concurrency
      in both local and Demo/API service paths rather than relying on button visibility.
- [x] Let an Admin mark a consumer as synthetic test data only while creating the record, display a
      clear `TEST` badge in the Admin directory, and make that marker immutable after creation.
- [x] Require the durable test-data marker as well as the final deletion attestation. Existing
      Production/local rows remain unmarked; the migration backfills existing rows only when the
      validated database identity is exactly `SatiDemo` / `Demo`.
- [x] Delete the complete consumer-owned test graph in one serializable transaction, retain the
      append-only audit ledger, and add a PHI-minimized `test-data.consumer-deleted` event.
- [x] Block deletion when any note has a billing claim line; do not delete financial, EDI, or audit
      records through this command.
- [x] Add focused local, API, ViewModel, confirmation, rollback, tenant-isolation, concurrency,
      billing-protection, audit, and accessible-interface tests.
- [x] Re-run the complete solution validation after the test-data marker and provider-directory
      curation work: the solution builds; 828 desktop/domain tests pass with one documented opt-in
      local-AI test skipped; all 243 API integration tests and all 4 shared-solution Carika tests
      pass.

## Release 1.2.29 — 2026-08-28

- [x] Allow an editable saved note to be reassigned with the existing Client selector without
      creating a duplicate note.
- [x] Ask the case manager, “Are you sure you want to reassign this note from [name] to [name]?”,
      default the popup to No, and restore the original selection when the move is declined.
- [x] Enforce current-note and target-client ownership in Local and Demo/API, retain workflow and
      optimistic-concurrency protection, and record a PHI-minimized `note.reassigned` audit event
      in the same save transaction.
- [x] Make both scratchpad tabs use the active theme's primary text and caret colors, including dark
      themes, with a rendered Harbor Night regression check.
- [x] Add focused ViewModel, local persistence, API integration, tenant-isolation, audit,
      concurrency, and WPF theme coverage.
- [x] Advance the desktop, API, installer builders, readiness checks, examples, and Settings release
      tracker to 1.2.29 together. No database migration is required for this release.
- [x] Run the complete Release build and every test project: 631 desktop/domain tests passed with
      one documented opt-in local-AI test skipped, all 199 API integration tests and all 4
      authorized shared-solution Carika tests passed, and all 80 focused note-reassignment and
      scratchpad checks passed. All 74 migrations replayed from empty with zero problems, and the
      resulting disposable schema matched all 362 model columns with no drift.
- [x] Commit and push the verified 1.2.29 source release as
      `e329af12dada557a56203aa56411cdf02c375948` on `master` without rewriting history.
- [x] Publish `Sati.Api-1.2.29.zip` (9,302,353 bytes; SHA-256
      `A7C23E88F0079F3F03F896B2811F0328FBC6FA872016A3D844485365FDC270D6`) from the pushed source
      commit to the existing Demo API only with OneDeploy deployment
      `f1303da3894c41f5b5a657f4e007a2ab`. Liveness and readiness are healthy,
      `/health/version` reports product `Sati.Api` and release 1.2.29, and deployed contract
      revision `EE21C645AB81` exactly matches the client. The package contains no private desktop
      settings or credential markers. The optional authenticated Admin extension was skipped
      because no synthetic Demo credentials were configured in this Windows session; all required
      public release checks passed. Release 1.2.28 deployment
      `d06af5344f8543d497727f02338474aa` and its API ZIP remain available as prior known-healthy
      evidence.
- [x] Generate and accept `SatiDemoSetup-1.2.29.exe` (100,311,040 bytes; SHA-256
      `F8A76038BA687DC88F82B4D172E149AF97E370D43FA42D7B8E77477E87A111BC`). It passed five
      responsive sign-in launches, normal closes, exact version 1.2.29.0, public-only
      configuration, and isolated cleanup. The installer and checksum were independently
      hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] Generate and accept `SatiLocalSetup-1.2.29.exe` (202,633,482 bytes; SHA-256
      `8585A1E32F6E57B8067F32B5D09FB282958D9CF40D79D9A8FA976FB564A183DA`) from Microsoft-signed
      `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`). It passed exact
      version 1.2.29.0, Windows integrated-security, credential-rejection, and isolated cleanup
      checks. The installer and checksum were independently hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`.
- [x] Merge or delete no branches. Retain linked worktree `claude/cool-jang-f6b3c4` at `c439fa8`
      because it contains an untracked instruction file; retain `second-machine-setup` at
      `f31fdf0` and `origin/claude/local-vs-github-workflow-dlcqpb` at `62b2f83` because they have
      unique setup work unrelated to this release.
- [x] Commit and push final release evidence as
      `b0f204eb3408ffd50d7765d79a2e1fe87975552b`; after this release-index update, confirm a clean
      `master` exactly matching `origin/master`.

## Release 1.2.28 — 2026-08-27

- [x] Correct Add Person email handling so a blank value is genuinely optional while a supplied
      malformed address still receives a specific validation message.
- [x] Expose the email field in the active inline client editor and label it optional.
- [x] Mark first name, last name, date of birth, and biography with accessible required-field
      asterisks; mark the representative-payee fields only where their conditional requirement is
      active.
- [x] Add a compact, non-color-only completion guide that changes required items from orange to
      green as meaningful values are entered and explicitly states that other details are optional.
- [x] Add desktop editor, notification, and shared validation regression coverage for blank,
      whitespace, malformed, and completed-field cases.
- [x] Run the complete Release build; 623 desktop/domain tests passed with one documented opt-in
      local-AI test skipped, all 196 API integration tests and all 4 authorized shared-solution
      Carika tests passed, all 54 focused Add Person checks passed, and 74 migrations replayed with
      zero problems.
- [x] Advance the desktop, API, installer builders, readiness checks, examples, and Settings release
      tracker to 1.2.28 together.
- [x] Commit and push the verified 1.2.28 source release as
      `90d47d764392d20992e306e2eef9ee4d033d40f2` on `master` without rewriting history.
- [x] Publish `Sati.Api-1.2.28.zip` (9,300,111 bytes; SHA-256
      `83AC37E1B38286A0FAF7261900464DF84B54F330152054442CFAAD57FF04EEB9`) from the pushed source
      commit to the existing Demo API only with OneDeploy deployment
      `d06af5344f8543d497727f02338474aa`. Liveness and readiness are healthy,
      `/health/version` reports product `Sati.Api` and release 1.2.28, and deployed contract
      revision `EE21C645AB81` exactly matches the client. The package contains no private settings
      or credential markers; release 1.2.27 deployment `071e2f7b46ff4ac4903d6073747b74e3`
      and its API ZIP remain available as prior known-healthy evidence.
- [x] Generate and accept `SatiDemoSetup-1.2.28.exe` (100,302,848 bytes; SHA-256
      `213298C98CE014B30F5AF0D284A3E0B9A766250EAA1025C765E2D03DD514B5D9`). It passed five
      responsive sign-in launches, normal closes, exact version 1.2.28.0, public-only
      configuration, and isolated cleanup. The installer and checksum were independently
      hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] Generate and accept `SatiLocalSetup-1.2.28.exe` (202,357,002 bytes; SHA-256
      `C6BCE962ED4ED0CB7D9CE32F2D9B697CBCCA54023B740CD140EBDCC27AACF072`) from
      Microsoft-signed `SqlLocalDB.msi` (SHA-256
      `224D483992EF60368DAC70CEA174DCFAF43A3CA06ADA331C67DC6119A26490F6`). It passed exact
      version 1.2.28.0, Windows integrated-security, credential-rejection, and isolated cleanup
      checks. The installer and checksum were independently hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`.
- [x] Merge or delete no branches. Retain linked worktree `claude/cool-jang-f6b3c4` at `c439fa8`
      because it contains an untracked instruction file; retain `second-machine-setup` at
      `f31fdf0` and `origin/claude/local-vs-github-workflow-dlcqpb` at `62b2f83` because they have
      unique setup work unrelated to this release.
- [x] Commit and push final release evidence as
      `5f6a7acaebf6e58ff557ed078584a476f4cc853e`; after this release-index update, confirm a clean
      `master` exactly matching `origin/master`.

## Release 1.2.27 — 2026-08-27

- [x] Correct billing compliance so only enabled, incomplete documents whose due date has passed
      can block; use completion dates rather than the mutable compliance flag, and apply one shared
      rule to current queues, historical service dates, billing, and loss reports in desktop and API.
- [x] Add Admin-only agency settings for 90-day reviews, PCP, Comprehensive Assessment,
      Reclassification, Safety Plan, Privacy Practices, and Agency/DHHS/Medical releases. Preserve
      the former intended set as the migration default and validate unsupported setting bits.
- [x] Add regression and matrix coverage for future, due-today, overdue, completed, prior-cycle,
      disabled, unknown, and historical-window cases, including API queue/approval and Settings
      authorization, tenancy, validation, concurrency, and audit behavior.
- [x] Move AT Requests from the Case Management section bar to the case-manager dashboard bar;
      add Authorized Rep and Releases beside it while reusing the Clients-page document workspaces.
      Keep the existing DHHS Forms, Agency Release, and AT Requests workspaces under Clients.
- [x] Give Notes filter inputs one rendered height and baseline above the data grid.
- [x] Add Pine Coast, Blueberry Mist, and Harbor Night themes with the full required resource set.
- [x] Audit Add Person end to end. Contain command, settings-load, and post-save workspace failures;
      remove the pre-save form deletion; make the Person/forms/lifecycle/audit graph one local
      transaction; and avoid a second API read after a successful create commit.
- [x] Give client-creation failures an accessible three-part explanation of what was saved, what
      failed, and the safest next action. Preserve field-specific server validation and distinguish
      definitely-unsent requests from an uncertain network response so duplicate clients are not
      created during recovery.
- [x] Centralize Person persistence validation in `Sati.Contracts.V1.PersonSaveRules`, enforce the
      authenticated owner/agency at the local seam, and cover required fields, every SQL length,
      enum/date/form invariants, transaction rollback, tenant ownership, API mapping, and crash
      containment with desktop and API tests.
- [x] Run the complete Release validation: the solution build passed; 617 desktop tests passed with
      one opt-in local-AI test skipped; all 196 API tests and all 4 authorized shared-solution Carika
      tests passed; and 74 migrations replayed from empty with zero problems.
- [x] Apply and verify `20260827141239_AddBillingComplianceRequirements` against the
      identity-validated Local `SatiProduction` and synthetic Azure `SatiDemo` targets. Both report
      no pending code migrations, valid default requirement bits, and the new non-null column and
      history row; Demo retained all 177 synthetic People and its temporary exact-IP rule was
      removed and verified absent.
- [x] Advance the desktop, API, installer builders, release checks, examples, and Settings release
      tracker to 1.2.27 together.
- [x] Commit and push the verified 1.2.27 source release as
      `c18f0001de80fc51eaabc502cacc2322026c3a59` on `master` without rewriting history.
- [x] Publish `Sati.Api-1.2.27.zip` (9,300,104 bytes; SHA-256
      `54AA740C74FED38E51D4EEBC09C9C9A13A33BBCBFAC08763AF66809D34E5A8B1`) to the existing Demo API
      only with OneDeploy deployment `071e2f7b46ff4ac4903d6073747b74e3`. Liveness and readiness are
      healthy, `/health/version` reports product `Sati.Api` and release 1.2.27, and deployed contract
      revision `EE21C645AB81` exactly matches the client.
- [x] Generate and accept `SatiDemoSetup-1.2.27.exe` (100,282,368 bytes; SHA-256
      `1B0DBEEECE5BD5EEC18E57B7AFDD5B097E2C134726E28FCD15C3538B2061BD10`). It passed five responsive
      sign-in launches, normal closes, exact version 1.2.27.0, and isolated cleanup, then the installer
      and checksum were hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`.
- [x] Generate and accept `SatiLocalSetup-1.2.27.exe` (202,356,490 bytes; SHA-256
      `954A837CC0A0E76D2B976E30190FDA17C7032A30A0895B9159C6C3395424A43E`) from the one valid
      Microsoft-signed `SqlLocalDB.msi`. It passed exact version 1.2.27.0, Windows integrated-security,
      and isolated cleanup checks, then the installer and checksum were hash-verified at
      `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`.
- [x] Delete fully merged, inactive feature branches `codex/repository-stabilization` at `74ea991`
      and `reminder-note-type` at `1fd3d5f` locally and remotely. Retain the linked
      `claude/cool-jang-f6b3c4` worktree plus `second-machine-setup` and
      `origin/claude/local-vs-github-workflow-dlcqpb` because they contain active, untracked, or
      unique work unrelated to this release.
- [x] Commit and push the final release evidence as
      `19455e3fece1322877e1dc0d8d8d7231eb254a0c`; after this release-index update, confirm a clean
      working tree and exact equality between `master` and `origin/master`.

## Release 1.2.26 — 2026-08-26

- [x] Restore an accessible, vertically centered calendar button inside every DatePicker.
- [x] Allow multiple Visit Setting, Appearance, Participation, and Health/Safety observations while
      keeping historical single-choice Visit JSON readable.
- [x] Add personal Win+Shift+1 through Win+Shift+0 typing shortcuts for every role, scoped to the
      note narrative and Scratchpad and kept separate by Windows profile, user, and environment.
- [x] Advance the desktop, API, installer builders, readiness gate, and Settings release tracker to
      1.2.26 together.
- [x] Publish API 1.2.26 with OneDeploy deployment `a479609851bd47f1934a9f75a4770433`.
      Liveness and readiness are healthy, `/health/version` reports 1.2.26, and deployed contract
      revision `15D50B6C6B29` matches the client. API ZIP SHA-256 is
      `AFEBAC10D7BA0496743F147FE18591E66C35B235A5C66D5A429D859A02C1CE3C`.
- [x] Generate and acceptance-test `SatiDemoSetup-1.2.26.exe` (SHA-256
      `2b62e9c8434d70ccbb822d733ce4dc3fb52b530c63385909aae341f2835e5d24`) and
      `SatiLocalSetup-1.2.26.exe` (SHA-256
      `3e09b4be02946a63356511c0c9a0477db8568bc56d25e051e62de61989757341`). Demo acceptance passed
      five responsive launches, normal closes, version 1.2.26.0, and cleanup; Local acceptance
      passed version, Microsoft-signed LocalDB, integrated-security, and cleanup checks.

## Release 1.2.25 — 2026-08-26

- [x] Add consumer email, the focused calendar-day note view, calendar failure containment, and
      future-dated non-billable reminders, with shared persistence rules and regression coverage.
- [x] Correct the Local login failure discovered after installing 1.2.24: Today's Work and
      Tomorrow's Agenda now load sequentially and publish independently, notes-log reads no longer
      fan out across every consumer, and both areas contain failures behind accessible Retry
      actions. The Settings release log describes the recovery behavior. Release verification
      passed 502 desktop and 177 API tests; hotfix source commit `b957506`.
- [x] Advance the client, API, installer builders, release notes, and release tests together to
      1.2.25 so the earlier 1.2.24 Local artifact is never replaced by different bytes.
- [x] Apply and verify migration `20260825163103_AddConsumerEmail` against identity-validated
      Azure `SatiDemo`. The email column already existed, so the guarded operation wrote only its
      missing history row, verified 177 synthetic consumers / 73 migrations, and removed the
      temporary exact-IP rule. Publish API 1.2.25 with OneDeploy deployment
      `29def577984e466db587a9b9632958aa`; live and ready are healthy, release version is 1.2.25, and
      deployed contract revision `15D50B6C6B29` exactly matches the client. API ZIP SHA-256 is
      `956FF72E1874236F7E6FB0E8D8A2A0E1C264740916359CA8A2ADB5B96B382642`.
- [x] Generate and validate the final post-hotfix `SatiDemoSetup-1.2.25.exe` (SHA-256
      `57a4edc651b1b9ade1cc5db53c61ab918303e93125e81552c861b3b586f6010e`) and
      `SatiLocalSetup-1.2.25.exe` (SHA-256
      `34ec15eced890a60c176111ef11cfdd3e4a0cfd58b8dd6e93670c604aebd7fab`). The earlier 1.2.25
      candidate hashes recorded in commit `cd09fd0` are superseded and those candidate artifacts
      must not be distributed. Demo acceptance passed five responsive launches, normal closes,
      version 1.2.25.0, and cleanup; Local acceptance passed version, Microsoft-signed LocalDB,
      integrated-security, and cleanup checks. The unchanged deployed API remains live and ready at
      release 1.2.25 with contract revision `15D50B6C6B29`.

## Calendar entry, Visit selections, and personal typing shortcuts — 2026-08-26

- [x] Restore a themed, vertically centered calendar button to the global DatePicker template,
      preserving the real WPF popup part, keyboard behavior, and an accessible name.
- [x] Replace the four Visit observation dropdowns with checkboxes; store multiple selections in
      additive note-owned JSON while retaining the legacy singular values so older notes still load.
- [x] Teach the local AI fact compiler and concern validation to consume every effective checked
      Visit selection, with regression coverage for old and new JSON.
- [x] Add personal Win+Shift+1 through Win+Shift+0 text snippets under Settings for every role,
      limited to 200 characters and inserted only in an editable note narrative or Scratchpad box.
- [x] Keep shortcut preferences client-local and separate by Windows profile, Sati user, and
      Demo/Production environment; preserve normal Windows behavior outside the marked text boxes.

## Future-dated calendar reminders — 2026-08-26, refined 2026-09-05

- [x] Store an explicitly selected future Reminder as a Scheduled Reminder through one shared
      contracts rule, repeated by the desktop-local and API persistence boundaries.
- [x] Preserve an explicit Reminder's date and narrative while removing service time, minutes,
      form/visit facts, and justification so it cannot drift into review, productivity, or billing.
- [x] Refine non-Reminder future work on 2026-09-05: preserve its selected service/form type and
      estimated minutes as a Scheduled plan, while withholding actual start time and completed facts.
- [x] Refresh the calendar after note saves and show the reminder on its dated calendar entry with
      explicit non-billable wording; keep undated Reminders on the existing journal-only path.
- [x] Cover UI conversion, rendered date access, local persistence, calendar retrieval, API
      normalization, tenant-scoped reads, and the undated-row guard with automated tests.

## Calendar stability and focused day — 2026-08-26

- [x] Contain calendar load, exemption-update, and downstream refresh failures so they produce an
      inline retryable message instead of escaping through WPF's dispatcher and crashing Sati.
- [x] Protect year navigation from stale async responses, invalid bounds, and missing sessions;
      preserve the selected date when refreshed calendar objects replace the prior year model.
- [x] Add an accessible focused-day view showing the service-date notes, client, narrative,
      service time, duration, status, and daily totals, with keyboard-operable day buttons.
- [x] Correct local monthly and yearly note boundaries to include the entire final day, and enforce
      the signed-in user's scope in the local exemption service as the API already does.
- [x] Cover the ViewModel, rendered WPF view, local data services, and API calendar boundaries with
      regression, concurrency, failure-containment, accessibility, and tenant-isolation tests.

## Release 1.2.23 — 2026-08-25

- [x] Add consumer-profile flags for case-manager DHHS representation and Modivcare,
      plus caseload filters for those responsibilities, representative payee, VR, and
      the existing waiver/employment service flags.
- [x] Add and validate the additive People migration. The guarded, exact-IP Demo
      operation verified `SatiDemo`/`Demo`, added both columns for 177 consumers,
      wrote migration `20260825144021_AddConsumerNavigationFlags`, and removed the
      temporary SQL firewall rule immediately afterward.
- [x] Deploy API 1.2.23 with OneDeploy deployment
      `14bfdc40a2ec44b9be517c1c7884cdd9`. `/health/live` and `/health/ready` returned
      HTTP 200; `/health/version` reports 1.2.23 and contract revision `9F387A68FF69`.
- [x] Package `SatiDemoSetup-1.2.23.exe` and `SatiLocalSetup-1.2.23.exe`. Record final
      installer acceptance evidence before distributing outside this workstation.

## Release 1.2.22 — 2026-08-23

- [x] Bump the client, API, and Carika to 1.2.22 and rewrite the Settings release notes around the
      single note panel, session continuity, and the journal-reminder route.
- [x] Confirm the Demo API needs no publish: its `/health/version` reports
      `contractRevision 9F387A68FF69` and `releaseVersion 1.2.21`, and the committed
      `ApiSurface.Revision` is byte-for-byte the same fingerprint over 102 routes, including
      `POST /api/v1/auth/renew`. The deployed surface already matches this source.
- [x] Package `SatiDemoSetup-1.2.22.exe`
      (sha256 `605ff0634d485cdc3610d71608b3665992c1418d3d7e04f454251c71762be484`) and pass
      installer acceptance: five launches, each responding, closing gracefully, exit code 0,
      installed version 1.2.22.0, acceptance copy removed.
- [ ] **Publish the API at 1.2.22.** Not required for function — the route surface is identical, so
      the compatibility check stays quiet — but `/health/version` will keep reporting
      `releaseVersion 1.2.21` until it is redeployed, which makes the number a poor way to tell what
      is running. Deploy from
      `Sati.Api/Properties/PublishProfiles/sati-demo-api-satilogica - Zip Deploy.pubxml`.
- [x] Package `SatiLocalSetup-1.2.22.exe`
      (sha256 `aef3463029ad16bf589f5af00e8c86a758f1c46a2a94333b916e718aac3c8242`) and pass payload
      validation: version 1.2.22.0, integrated security confirmed, acceptance copy removed. The
      embedded LocalDB prerequisite is Microsoft-signed `SqlLocalDB.msi`, sha256
      `0891BF47652D88F06D76A339A5DB37DDC9C801D1E973E14B3B551609F1CFA4CB`.
- [ ] **Keep a durable copy of `SqlLocalDB.msi`.** The 1.2.22 build sourced it from a
      `%TEMP%\SatiInstallerInspect-*` folder left by an earlier installer inspection. The signature
      check makes that safe — a modified MSI cannot carry a valid Microsoft Authenticode signature,
      and it was verified before use — but a release input that lives in a directory Windows may
      clear at any time is not a reproducible one. Archive it somewhere permanent and record the
      hash above.
- [ ] **Decide whether machine-local commentary belongs in a shipped installer.** The Local build
      packages the gitignored `appsettings.json` verbatim, including its `//production` note
      describing this laptop's database history. It carries no credential and no client data, and
      the builder already rejects SQL credentials, but it is internal commentary that reaches an
      end user's disk.

## Ended Demo session — 2026-08-23

- [x] Treat a refused `POST /api/v1/auth/renew` as terminal: latch the client shut, raise
      `CloudApiClient.SessionEnded` once, and fail every later authenticated call locally with
      `CloudSessionEndedException` instead of repeating the rejected renewal per screen.
- [x] Clear the latch when a new token is set, so signing in again reopens the same client.
- [x] Surface an ended session in the Switch User dialog as a stated expiry rather than an empty
      account list, and repair the error line, which was collapsed by a `Visibility` binding that
      outranked its own style and ran a string through `BoolToVisibilityConverter`.
- [x] Renew on the token's own schedule (`SessionKeepAlive`), waking at `expiry - RenewalMargin`
      rather than waiting for a request to land inside a five-minute window that an idle app never
      enters. A fixed poll cannot substitute: a twenty-minute interval steps over the window and
      wakes holding an expired token.
- [x] Gate renewal on real user input, so an active desktop keeps working to the server's
      twelve-hour cap while an unattended workstation lapses after one token lifetime. The idle
      allowance is the renewal gap, not the token lifetime — measuring against the lifetime can
      never close the gate on the first cycle and silently doubles the timeout.
- [x] Offer re-authentication in place: the shell prompts on `ISessionLifetime.SessionEnded`. The
      same person signing back in is not an account switch, so nothing reinitializes and unsaved
      agenda text survives to be saved; a different account takes the existing switch path.
- [ ] Translate `CloudSessionEndedException` into `SessionExpiredException` across the remaining
      cloud services. Only `CloudUserService.GetAllAsync` does so today, so other screens still
      report an ended session through their generic failure path.

## Representative-payee profile — 2026-08-22

- [x] Add `CaseManagerIsRepPayee`, monthly income, and regular check-request needs to the
      authoritative Person profile, API contracts, lifecycle history, and Local/Demo persistence.
- [x] Add accessible Yes/No Profile controls with conditional, validated amount and recurring-needs
      fields; selecting No clears financial details that no longer apply.
- [x] Add the additive People migration and test validation, tenant isolation, audit/version history,
      contract mapping, migration shape, API compatibility, and accessible XAML controls.
- [x] Apply the guarded migration to identity-validated Local `SatiProduction` and Azure `SatiDemo`,
      deploy the matching API, and package both 1.2.21 installers (2026-08-23).
- [ ] Design the later billing-department check-release notification as its own audited workflow with
      request state, amount, purpose, due date, requester, approval/release evidence, and idempotency.
      A representative-payee profile edit must never itself authorize or initiate payment.

## Database wait feedback — 2026-08-22

- [x] Add one payload-free, reference-counted activity tracker for Demo HTTP and Local Production EF
      calls, including success, failure, cancellation, and reader-disposal cleanup.
- [x] Spin the accessible watercolor Bodhi leaf immediately while data calls are active.
- [x] After eight uninterrupted seconds, show a modeless patience window that closes automatically
      when the final overlapping request completes and never appears late after a short request.
- [x] Add deterministic timing, overlap, HTTP failure, EF reader-lifecycle, and XAML accessibility
      coverage.
- [x] Add an all-role Settings preview that holds the same activity tracker for 12 seconds without
      querying a database, so the immediate leaf and eight-second patience window can be tested.
- [x] Classify Demo connectivity failures, retry only proven DNS failures where no request was
      sent, and keep ambiguous writes from being repeated after timeouts or connection loss.
- [x] Present safe, specific Scratchpad recovery guidance without logging or displaying note text,
      and remove expected spinner-timer cancellation exceptions from debugger output.

## Carika limited client — 2026-08-21

- [x] Add an Avalonia client using safe contracts and the API, without EF/SQL/LocalDB.
- [x] Add authenticated caseload profile display and API-mediated draft-note creation.
- [x] Add contract-backed note type, workflow status, and conditional form-type selections, with
      stale draft-load and transcription suppression when the selected person or narrative changes.
- [x] Refresh the limited client's visual hierarchy and accessible note-entry status messaging.
- [x] Add DPAPI-protected, actor/person-bound optional local drafts.
- [x] Add local-only Whisper transcription of an existing WAV with no cloud fallback or auto-download.
- [ ] Add ephemeral microphone capture after privacy indicators, cancellation, device selection,
      audio-lifetime behavior, and accessibility are designed and tested.
- [ ] Add session expiry/re-authentication, sign-out/zeroization, note editing/concurrency UI,
      integration tests, packaging, threat modeling, model controls, and deployment review.

A WPF MVVM case-management desktop app built with EF Core, CommunityToolkit MVVM, and SQL LocalDB.

---

## Demo hardening - 2026-08-13

- [x] Stop calendar-day selection from repeatedly raising layout error dialogs. Display-only
      `Run.Text` bindings are explicitly one-way, repeated identical UI failures are shown once
      per process, and safe XAML location metadata is included in the local technical log.
- [x] Reproduce calendar-day rendering in a real WPF window and cover the binding rule with
      automated regression tests.
- [x] Make the Demo readiness preflight distinguish an older healthy deployment from a matching
      release and report the exact deployment-parity remedy instead of a raw web exception.
- [x] Build and isolated-launch-test the version 1.2.2 installer containing the completed billing
      client, deploy the matching 1.2.2 API to Azure Demo, and verify live/ready/version health.
- [x] Apply the incident migration to Azure Demo, provision the encrypted-credential Global Admin,
      deploy matching API/client 1.2.3, verify least-privilege platform access, and isolated-launch-
      test the versioned installer.
- [x] Repair incident coverage and the supervisory billing handoff in release 1.2.5: durable retry
      outbox, platform-scoped Global Admin reporting, unclean-shutdown detection, No telemetry
      health state, reentrant window-close protection, explicit Save as Draft / Submit for
      Supervisor Review actions, and an end-to-end draft-to-test-837 integration gate. Apply the
      guarded Azure migration, deploy API 1.2.5, and isolated-launch-test its exact installer.
- [x] Correct the asynchronous save-on-close handoff and permit narrowly scoped Global Admin
      self-service password changes in release 1.2.6, with regression coverage proving agency
      user-password resets remain forbidden.
- [x] Route Global Admin account switching through neutral credential entry in release 1.2.7,
      preserving the agency-directory prohibition and containing ordinary picker load failures.
- [x] Show assigned supervisor names explicitly in both administrative and personal user profiles,
      and replace the legacy artwork with a multi-resolution professional application icon in
      release 1.2.8.
- [x] Complete the local release 1.2.9 durability slice: refine the darker Bodhi-leaf icon, finish
      the accessibility audit, make theme resources host-independent, runtime-render the feature
      views, and require repeated normal window shutdown in installer acceptance.
- [x] Regenerate and visually inspect all ten pages of the version-matched offline Demo fallback.
- [ ] Complete authenticated agency-Admin preflight, external-machine installer attestation,
      presenter rehearsal, and final evidence binding for the exact 1.2.9 installer.

---

## Phase 1 — Fix the Foundation ✅
*Goal: App starts, login works, no crashes*

- [x] Fix double `mainWindow.Show()` in `App.xaml.cs`
- [x] Implement `CaseManagerDashboardViewModel.Initialize(user)` — store logged-in user, trigger initial load
- [x] Fix `NewUserViewModel` missing null-conditional on `CloseWindowRequested` event
- [x] Make `IUserService` public
- [x] Remove hardcoded seed user from `LoginWindowViewModel`
- [x] Fix `MainPage_Activated` firing `LoadPeopleAsync()` on every focus — load once only
- [x] Add `OnModelCreating` to `SatiContext` with explicit keys and relationships

---

## Phase 2 — Remove Service Locator, Tighten DI ✅
*Goal: No more `((App)Application.Current).Services`*

- [x] Remove service locator from `LoginWindow.xaml.cs`
- [x] Use `Func<T>` factory injection for window creation throughout
- [x] Audit all remaining `GetRequiredService` calls in view code-behind

---

## Phase 3 — Complete Person/Client Management ✅
*Goal: Add, view, edit, delete clients with validation*

- [x] Add edit support to `NewClientViewModel`
- [x] Add input validation using `[NotifyDataErrorInfo]`
- [x] Fix `RemoveSelectedPerson` — was not awaiting `DeletePersonAsync`
- [x] Eager-load `Forms` and `Notes` in `PersonService.GetAllPeopleAsync`
- [x] Add compliance review dialog on client creation
- [x] EffectiveDate replaced with MM/DD TextBox with CustomValidation and waiver-gating

---

## Phase 4 — Complete Notes Workflow ✅
*Goal: Notes are fully usable — create, edit, delete, filter*

- [x] Add delete note command to `CaseManagerDashboardViewModel`
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
- [x] Upcoming Tasks panel split into two columns (forms left, visits/contacts right)
- [x] Sort radio buttons wired via SortByDate computed property
- [x] Forms checklist bound to real Form entities via ToggleFormCommand
- [x] Compliance flags computed per FormType using GetCurrentCycleForm
- [x] GetCurrentCycleForm on Person model replaces FirstOrDefault throughout
- [x] EnumDescriptionConverter, BoolToVisibilityConverter, InverseBoolConverter
- [x] Description attributes on FormType enum for human-readable display
- [x] Enums moved to top-level namespace
- [x] UserId foreign key on Person, GetAllPeopleAsync filtered by userId

---

## Phase 7 — Note Polish and Client Detail ✅
*Goal: Note workflow reliability, scheduled events in upcoming panel*

- [x] IsEditing reset on client switch
- [x] Form clears on client selection
- [x] In-memory Person.Notes sync for upcoming events
- [x] NoteType persisted to database with migration
- [x] Scheduled visits and contacts appearing in Upcoming Tasks panel
- [x] ContactEvents column added

---

## Phase 8 — Polish and Portfolio Packaging ✅
*Goal: Looks good, handles errors gracefully, ships cleanly*

- [x] Global exception handling in App.xaml.cs with flat-file error log
- [x] User-facing error dialogs
- [x] ScratchpadHistoryWindow with ICollectionView search and full-content preview
- [x] Scratchpad save bug resolved via cancel/reclose pattern on Closing event
- [x] Out-of-month warning for EventDate
- [x] Font configuration — Inter globally, Cambria for narrative/scratchpad fields
- [x] Font size A/A buttons for narrative and scratchpad
- [x] Ctrl+Enter timestamp insertion in scratchpad
- [x] WPF native spell check on narrative and scratchpad fields
- [x] README.md written and pushed
- [x] Self-contained single-file executable published
- [x] SmartScreen blocking resolved

---

## Recently Resolved Bugs
*Verified before the current platform work*

- [x] **Productivity threshold ignores scheduler** — `Incentive.Threshold` hardcodes `* 19`
  instead of using `Settings.ProductivityThreshold`; panel doesn't refresh after scheduler closes
  - Fix 1: Add `UnitsPerDay` snapshot field to `Incentive` model + migration
  - Fix 2: Set `UnitsPerDay = settings.ProductivityThreshold` in `GetOrCreateAsync`
  - Fix 3: `OnIsSchedulerOpenChanged(false)` calls `RefreshIncentiveAsync()` in CaseManagerDashboardViewModel

- [x] **Refactor all services to use IDbContextFactory<SatiContext>** — current pattern holds
  a DbContext open for the entire session, causing change tracker collisions and memory bloat.
  Replace constructor-injected SatiContext with IDbContextFactory<SatiContext> across all
  services. Swap AddDbContext for AddDbContextFactory in App.xaml.cs. Each method creates
  and disposes its own context via `await using var context = _contextFactory.CreateDbContext()`.
  Do before adding any new features.
- [x] **`GetOrCreateAsync` always returns `wasCreated = false`** — new month records never
  trigger the scheduler prompt correctly; newly-created branch should return `true`
- [x] **NoteType edit not persisting** — suspected EF Core tracking issue; NoteType changes
  on existing notes not being written to DB on save
- [x] **Edit form not populating NoteType** — reopening an existing note doesn't restore
  the current NoteType value; initialization/binding bug
- [x] **Stale data in Upcoming Tasks after failed note edit** — downstream of NoteType
  persistence failure
- [x] **Missing "Scheduled" filter in AllNotes combobox** — straightforward omission
- [x] **ExcludeDayAfterThanksgiving unhandled by IsExcludedHoliday**

---

## Deferred Bugs
*Known issues, not blocking daily use*

- [ ] Scheduler day-of-week column alignment shifts month to month — tiles render
  sequentially rather than snapping to fixed Mon–Fri grid positions
- [ ] Stale ExcludedDates entries persist on Incentive after weekday exclusion is removed
  from Settings
- [x] Note abandonment threshold hardcoded to 8 days — wired to
  `SettingsService.AbandonedAfterDays`
- [x] Settings are global rather than per-user — replaced with agency-scoped settings;
  user overrides remain deferred until a concrete requirement exists

---

## Pre-Release Fixes
*Must address before shipping to team or OADS*

- [ ] Annual form regeneration — when a client's anniversary rolls over, generate new
  Form records for the new compliance cycle
- [ ] First login / scheduler prompt — verify `wasCreated` behavior across month boundaries
  once `GetOrCreateAsync` bug is fixed
- [x] Accessibility audit — icon-only buttons expose accessible names;
  compliance checkboxes are labeled; overdue matrix cells include a visible text status
- [ ] Verify migrations apply to an *empty* database before each release. A working
  database only ever receives new migrations and has been hand-patched over time, so two
  classes of breakage stay invisible locally and appear only when someone builds from
  zero — a new machine, a new agency, or a fresh installer test. Both were found
  2026-08-16 and are now fixed:
  1. *Migrations replaying earlier ones* — recorded as applied, so they never re-run
     locally. `UnitstoDecimal` re-added `Notes.ReturnedById` (SQL 2705); `SyncModelState`
     repeated every operation in `AddSupervisorFieldsToNote`. Both bodies are emptied,
     with the files kept so the migration IDs stay in the chain.
  2. *Model/schema drift* — `Notes.Minutes` and `Notes.StartTime` were in the model and in
     every working database, but no migration created them, so a fresh database produced a
     `Notes` table the model could not query.
     `20260816120000_AddNoteMinutesAndStartTime` adds them, `COL_LENGTH`-guarded so it is a
     no-op on databases that already have the columns without a history row for it.
  `Add-Migration` cannot catch case 2: it diffs the model against
  `SatiContextModelSnapshot`, and the snapshot already listed both properties. The gap is
  between the snapshot and what the migration files actually build.
  Run `scripts/Test-MigrationChain.ps1` (no database needed) and
  `scripts/Test-SchemaDrift.ps1` (against a *from-scratch* database) before each release.
  Verified 2026-08-16: 69 migrations replay clean, and a database built from empty reaches
  the login window with all 349 model columns present.

---

## Phase 9 — Cloud Platform Foundation
*Goal: establish the boundary on which every external deployment and future client depends.*

### Current next slice — concurrency and audit-operation breadth

- [ ] Extend revision/concurrency tokens from assessments, notes, AT requests, settings, and scratchpads to
  other records where simultaneous edits could silently lose work.
- [ ] Extend the friendly desktop conflict handling now used by notes, settings, and scratchpads to the remaining concurrent
  records instead of presenting generic API errors.
- [x] Add idempotency keys for externally retried commands beyond the database-enforced claim-line rule.
- [x] Define audit retention, legal-hold gate, controlled export, SQL-principal permissions, and monitoring.
- [ ] Implement legal-hold enforcement, production SQL grants/denies, retention jobs, and external alert routing.
- [ ] Review and remove the pre-existing Azure SQL firewall rule
      `ClientIPAddress_2026-8-12_14-41-29` if no active operator still owns it; the August 13 billing
      deployment's temporary rules were removed, but unrelated pre-existing access was not changed.

### Next major slice after billing — tenant-safe incident and health pipeline

- [x] Capture structured client and API incidents with UTC time, release, agency, actor role,
      operation, correlation/reference ID, severity, exception fingerprint, recurrence count, and
      lifecycle status; never capture note narratives, passwords, tokens, connection strings, or
      unrestricted exception messages.
- [x] Deduplicate repeated failures into stable incident groups while retaining occurrence counts,
      first/last-seen times, affected releases, and a bounded diagnostic envelope.
- [x] Add an Admin-only agency incident table showing severity, status, release, operation,
      date, and fingerprint. Enforce agency scope at the API and data boundaries, not only in the UI.
- [x] Introduce a separately named platform-operator capability for cross-tenant incident visibility.
      Do not treat an ordinary agency Admin as a master account, and audit every cross-tenant view
      or export.
- [x] Define transparent, versioned agency and platform Incident Health v1 scores from recorded
      severity, recurrence, and unresolved age; show every penalty and state explicitly that v1
      does not claim crash-free-session, availability, or job-failure coverage.
- [x] Add desktop incident search/severity/status filters, audited status-edit controls, explicit
      alert thresholds, and concurrency-safe aggregation with exact-count integration coverage.
- [ ] Add safe session denominators, API availability, and scheduled-job outcomes.
- [ ] Add retention, legal-hold, access-review, alerting, and runbook requirements; prove PHI/PII
      minimization, tenant isolation, bounded queries, concurrency, and score calculations with
      automated tests before enabling production collection.

### Completed 2026-08-13 — billing gate and 837P hardening slice

- [x] Replace the obsolete hardcoded queue code with agency-scoped procedure/modifier/rate,
      submitter, payer, and contact configuration, enforced by Admin-only API and service boundaries.
- [x] Revalidate note approval, current compliance, historical billing windows, subscriber fields,
      provider NPI/address/tax fields, and EDI configuration immediately before claim creation.
- [x] Apply the current Section 13 minimum/partial-unit rule and keep service units separate from
      calculated monetary charges in claim lines and 837P CLM/SV1 segments.
- [x] Require submit-and-lock before EDI generation, preserve retry idempotency, generate ISA16,
      calculate SE01 from ST through SE, and validate fixed ISA length and service-line structure.
- [x] Freeze subscriber, provider, submitter, and payer values in an immutable per-claim snapshot so
      later Person or Agency edits cannot silently rewrite a financial record.
- [x] Add structured subscriber claim-address fields and an accessible editor; generate 2010BA
      N3/N4 segments rather than attempting to parse a free-form address.
- [x] Add an idempotent Demo-only seed with three ready and seven deliberately blocked examples;
      verify all ten through the real billing service and keep ready rows first in the queue.
- [x] Back up and verify local Demo and Production databases before applying the migration; seed
      synthetic rows only in Demo.
- [x] Apply the additive migration and the same ten-row seed to Azure `SatiDemo`, verify 3 ready / 7
      blocked through the real service, deploy the matching Demo API, and remove temporary firewall rules.
- [x] Add a test-only synthetic claim exchange that consumes Sati's test 837P, correlates accepted
      representative 999/277CA responses, produces a balanced synthetic 835, and refuses production
      interchanges. This is an internal workflow test, not transport, import, or payer certification.
- [x] Add append-only tenant-owned submission-event and remittance-claim-outcome read models,
      Admin-only local/cloud grids, retry-safe Generated event recording, and a deterministic
      Demo-only catalog covering eight submission and six remittance contingencies. Every seeded
      exchange row is visibly synthetic and the seed remains hard-limited to `SatiDemo`.
- [ ] Obtain the agency's authoritative fee/code configuration and payer enrollment identifiers,
      then pass clearinghouse test-file validation and payer-specific acceptance. Generated 837P
      structure is tested, but Sati is not yet certified for live claims.
- [ ] Implement 999/277CA acknowledgments, claim rejection correction, 835 remittance import,
      payment/reconciliation, void/replacement claims, and operational submission transport.

### Completed 2026-08-13 -- presenter acceptance kit

- [x] Add secret-free JSON evidence output to authenticated Demo API readiness and isolated
  installer acceptance without treating health-only or same-machine results as final proof.
- [x] Add a final verifier that binds fresh API, external-machine, rehearsal, fallback, release,
  and artifact-hash evidence to the exact installer being presented.
- [x] Produce and visually verify a ten-page offline PDF fallback using only synthetic and
  explicitly representative material, with a presenter approval area and honest limitations.
- [x] Document the external-machine, rehearsal, fallback approval, and final acceptance workflow;
  the matching 1.2.3 API deployment, Global Admin verification, and isolated installer launch are
  complete, while the authenticated agency-Admin run, external-machine run, and human attestation
  remain open.

### Completed 2026-08-13 -- release notes and 1.2.0 packaging

- [x] Add a Settings **Release notes** tab tied to the installed assembly version and summarize
  Admin/audit, safety/reliability, Demo/support, and remaining production work.
- [x] Version Debug, Release, Demo, and the installer consistently as 1.2.0 and add a regression test
  so the in-app notes cannot silently drift from the packaged assembly.
- [x] Compile Debug and Demo with zero warnings and pass 78/78 API, authorization, integration,
  migration, reporting, and domain tests before producing the installer.
### Completed 2026-08-12 -- Demo recovery and acceptance tooling

- [x] Replace user-visible exception/stack-trace dumps with calm reference-number messages and a
  PHI-minimized local JSON-lines diagnostic record that excludes exception messages.
- [x] Add a reproducible self-contained Demo publish script that refuses dirty output folders,
  requires the tracked HTTPS endpoint, hashes the executable, and rejects private appsettings.
- [x] Add read-only deployed health/Admin preflight tooling, a ten-minute company-demo runbook,
  recovery guardrails, and explicit final acceptance gates.
- [x] Compile the real `Demo` configuration in CI and locally; produce and inspect a 263 MB
  self-contained artifact; confirm deployed liveness/readiness after an 80-second Free-tier wake-up.
- [ ] Deploy the current client/API pair, run authenticated preflight, launch the package on a clean
  external Windows machine, rehearse the designated synthetic path, and prove canonical reset.
### Completed 2026-08-12 -- local authorization parity

- [x] Require the signed-in case manager at the local assessment service boundary; reject caller ID
  spoofing, unassigned People, cross-agency People, and attempts to alter another author's draft.
- [x] Restrict local supervisor queues and note decisions to the signed-in reviewer, their agency,
  and either assigned supervisees or agency-wide Director/Admin scope.
- [x] Record successful local assessment and supervisor transitions in the same database save as the
  protected state change.
- [x] Add SQLite service-level regression tests for author, reviewer, assignment, agency, and audit
  boundaries while preserving the cloud API as the production authority.
### Completed 2026-08-12 -- operations visibility and records governance

- [x] Add an Admin-only, agency-scoped operations status view with database status, retained audit/EDI
  counts, oldest-record timestamps, and explicit audit/EDI retention policy values.
- [x] Add a reason-gated, one-year/10,000-row bounded audit CSV export; mark it no-store and audit the
  export without copying its business reason into server metadata.
- [x] Publish `OPERATIONS.md` with the legal-hold prerequisite, production SQL-principal separation,
  monitoring/alert expectations, operator checks, and an honest `PolicyOnly` enforcement state.
- [x] Preserve local-development/cloud parity while keeping retention destructive actions disabled
  until legal-hold controls and production authorization are reviewed.
### Completed 2026-08-12 -- billing retry safety

- [x] Give each WPF EDI-generation attempt a stable retry key and preserve it across ambiguous
  failures until the exact file has been returned and saved.
- [x] Persist the exact EDI response behind a tenant-, actor-, and key-scoped unique index so a
  repeated API request replays one file and creates only one success audit event.
- [x] Reject reuse of an EDI retry key for a different period or test/production mode.
- [x] Make billing-period submission repeatable: an already-submitted period returns its original
  success state, while a database concurrency token prevents simultaneous submissions from
  producing duplicate state changes or audit events.
- [x] Carry the retry contract through the shared API contracts and local service boundary without
  beginning the deliberately post-pilot MAUI/Avalonia business-logic extraction.

### Completed 2026-08-20 -- Tomorrow's Agenda

- [x] Add Tomorrow's Agenda beside Today's Work in the existing scratchpad panel, with both drafts
  included in autosave, app-close, and account-switch flushes.
- [x] Store the agenda as the next workday's existing per-user Scratchpad row so it becomes Today's
  Work automatically without a midnight copy or duplicate-promotion state.
- [x] Put the Friday/Saturday/Sunday-to-Monday rule in `Sati.Contracts.V1.WorkAgendaDates` and use
  the API's agency-local clock to select the cloud row; holidays remain deferred until Sati has an
  authoritative agency holiday-calendar policy.
- [x] Preserve revision conflicts independently for both tabs and cover the calendar rule, route
  ownership, stable row identity, and cross-user isolation with automated tests.
- [x] Deploy the matching API surface to hosted Demo with OneDeploy
  `2bf8bd7fb3ea4ca39595a87da836f727`; readiness returned 200 and the live contract revision
  `A4FB297F7FE6` matched the release build.

### Completed 2026-08-12 -- scratchpad concurrency boundary

- [x] Protect each user's daily Scratchpad with a `Revision` token across local SQL, cloud
  contracts, and the API; reject stale and older-client autosaves with `409 stale_scratchpad`.
- [x] Treat unchanged ten-minute autosaves as no-ops so they create neither revision churn nor
  misleading audit activity.
- [x] Preserve the unsaved text after a conflict, stop repeated autosave warnings, block shutdown
  and user switching from discarding it, and provide an explicit Reload Latest recovery action.
- [x] Record only accepted content changes in the PHI-minimized audit envelope and prove stale,
  legacy, cross-user, no-op, migration, and recovery-boundary behavior.

### Completed 2026-08-12 -- settings concurrency boundary

- [x] Add an agency Settings `Revision` token through local SQL, cloud contracts, and the API.
- [x] Reject stale and legacy settings saves with `409 stale_settings` before shared agency policy
  can be silently overwritten; advance the revision only after a successful save.
- [x] Keep the Settings window open with a friendly warning when another administrator saved first,
  preserving the attempted values for comparison with the latest agency settings.
- [x] Prove one success audit event per accepted save, no false success event for rejected attempts,
  older-client fail-closed behavior, and isolation of another agency's settings.

### Completed 2026-08-12 — first audit and concurrency slice

- [x] Add the PHI-minimized `AuditEvent` envelope, migration, action catalog, and Admin-only,
  agency-scoped audit query documented in `AUDIT_EVENTS.md`.
- [x] Record successful authentication, account changes, supervisor note decisions, assessment
  writes/submission, settings changes, billing submission, and EDI generation.
- [x] Commit each protected state transition and its audit event in the same database save/transaction.
- [x] Add an assessment `Revision` concurrency token and reject stale save/submit requests with HTTP 409.
- [x] Make claim-line creation atomic and enforce one claim line per service note with a unique index.
- [x] Test audit minimization/immutability/tenant scope, stale writes, and repeated billing commands.

### Completed 2026-08-12 — note concurrency slice

- [x] Add a Note `Revision` concurrency token and require the revision read by the caller on every
  edit, delete, supervisor approve/override/return, and automated abandonment transition.
- [x] Reject stale Note operations with HTTP 409 before they can overwrite, delete, or supersede a
  newer copy; increment revisions for every successful state transition.
- [x] Preserve an open editor's draft after a conflict, identify fields that differ from the latest
  saved copy, and refresh Notes Log and supervisor queues before another action.
- [x] Load full Note records in the Notes Log instead of caseload summaries so IDs, narratives,
  people, and revisions remain available through the cloud API transition.
- [x] Prove stale note edits, deletes, and supervisor decisions leave the newer record intact.

### Completed 2026-08-12 — AT request concurrency boundary

- [x] Treat an AT request and all of its line items as one revisioned financial aggregate.
- [x] Require the caller's expected revision for update and delete through local and cloud
  persistence; reject stale or legacy writes with `409 stale_at_request`.
- [x] Replace line items and increment the parent revision in one EF transaction so a stale
  aggregate cannot partially alter vendor, money, status, dates, or item details.
- [x] Add a typed desktop-service conflict for the future Save/Open/Delete workflow without
  prematurely separating Save from the deliberately bundled PDF-publishing feature slice.
- [x] Prove stale aggregate replacement and deletion preserve the newer request and its items.

### Completed 2026-08-12 — Person lifecycle audit

- [x] Preserve an append-only, compressed snapshot and field-level change set for every successful
  Person create, profile edit, and journal edit.
- [x] Record actor, agency, UTC timestamp, request correlation ID, and monotonically increasing
  Person revision; reject stale profile saves with HTTP 409.
- [x] Add Admin-only, agency-scoped history and auditor-PDF endpoints with no-store response headers
  and audit events for viewing/exporting the history.
- [x] Give pre-existing People an explicit current-state tracking baseline without pretending older
  changes can be reconstructed.
- [x] Verify cross-agency denial, append-only enforcement, stale-write rejection, and rendered PDF output.

### Completed 2026-08-12 — visible Admin dashboard

- [x] Add a top-level Admin destination that is hidden for every non-Admin role.
- [x] Show agency user, Person, active-user, sign-in, Person-change, and daily audit-event metrics
  without requiring Azure Portal access.
- [x] Add a readable 30-day activity feed with actor, action, resource, and local timestamp.
- [x] Add an agency Person directory, lifecycle timeline, and protected PDF download workflow.
- [x] Support both the cloud API and the transitional local-development database through
  `IAdminService`; preserve server-side Admin and tenant enforcement.
- [x] Add API integration coverage for dashboard scope and non-Admin rejection.

### Completed 2026-08-12 — tenant-enforcement breadth

- [x] Inventory every protected API route and document its authoritative tenant owner in
  `API_AUTHORIZATION.md`.
- [x] Add cross-agency rejection tests for reports, billing exports, AT requests, assessments,
  supervisor actions, and generated files.
- [x] Revalidate the token's user, agency, and role against the database on every protected request.
- [x] Centralize actor, caseload, supervisor, and assessment-authorship checks in `TenantAccess`.
- [x] Prevent supervisors from authoring a case manager's assessment while preserving read/review access.
- [x] Revalidate every billing-export source note and person against the exporting agency.

### Completed 2026-08-12 — API boundary verification

- [x] Add a dedicated cross-platform API integration/authorization test project.
- [x] Prove unauthenticated requests are rejected at the protected API boundary.
- [x] Prove cross-agency reads and writes are rejected for users, people, and providers.
- [x] Keep the API test project independent from WPF and desktop persistence.
- [x] Run the API tests in CI alongside the existing domain/migration tests.

### Repository structure — targeted follow-up, not a reshuffle

The solution-level boundaries (`Sati`, `Sati.Api`, `Sati.Contracts`, desktop/domain tests, and
API integration tests) are coherent. Avoid a broad folder move while the API transition is active.
Address these pressure points when the affected code is next changed:

- [ ] Split the large `Sati.Api/Endpoints/ApiEndpoints.cs` by feature route group while preserving
  one `/api/v1` composition point.
- [ ] Eliminate schema drift between `SatiContext` and `ApiDbContext`; make server-side persistence
  and migrations authoritative before removing local EF from the distributed WPF client.
- [ ] Rename/move the root WPF project to an explicit `Sati.Wpf` project only after direct cloud
  database responsibilities are removed; doing it now would create churn without changing coupling.
- [ ] Archive historical session material out of this active agenda once the current platform
  priorities are stable; keep a short current-work section at the top.

### Architecture and solution structure

- [x] Add `Sati.Api` ASP.NET Core project.
- [x] Add shared, versioned request/response contracts that do not expose EF entities.
- [ ] Move EF Core, migrations, password hashing, and authoritative domain operations behind the API.
- [ ] Implement HTTP-backed desktop services behind the existing service interfaces where the
  contracts remain appropriate.
- [ ] Remove direct database connectivity and `Database.Migrate()` from distributed clients.
- [x] Add API health checks, structured logs, correlation IDs, and startup validation.
- [ ] Add operational metrics and alert wiring.

### Identity and authorization

- [x] Move Sati credential verification server-side; return a safe profile and short-lived token.
- [x] Never return `PasswordHash` or `Salt` to a client.
- [ ] Add token expiration, revocation, secure recovery, and brute-force/rate-limit controls.
- [ ] Define capabilities independently from menu visibility and coarse job titles.
- [x] Derive actor, tenant, role, and caseload server-side instead of trusting supplied IDs.
- [ ] Evaluate Microsoft Entra ID/External ID and MFA for production organizations.

### Tenant model

- [ ] Decide shared-database, database-per-tenant, or hybrid production isolation.
- [x] Define the authoritative tenant owner for every protected aggregate.
- [x] Replace global settings with tenant-scoped settings; add user overrides only where required.
- [ ] Centralize tenant enforcement with query filters/interceptors and command authorization.
- [x] Add automated cross-tenant read/write/export rejection tests.
- [ ] Add tenant provisioning, suspension, migration, export, and deletion/retention procedures.

### Records, audit, and concurrency

- [x] Create append-only `AuditEvent` records for protected actions without copying
  unrestricted narrative PHI into log messages.
- [ ] Define which sensitive read events require auditing without creating an unusable volume of noise.
- [ ] Add immutable document versions, amendments, attestations, and electronic signatures.
- [ ] Define retention and legal-hold behavior by record class.
- [ ] Extend optimistic concurrency tokens and user-facing conflict resolution beyond the completed
  assessment, Person, Note, and AT persistence records. AT desktop conflict recovery lands with its
  still-deferred Save/Open/Delete and PDF-publishing workflow.
- [ ] Make commands retry-safe and idempotent where duplicate execution would cause harm.
- [ ] Move billing, approval, and submission transitions into explicit server transactions.

### Testing and delivery

- [x] Add unit, API integration, authorization, and migration-consistency tests.
- [ ] Add end-to-end tests for the packaged Demo client and deployed API.
- [x] Establish CI build and test validation across the solution.
- [ ] Add CI dependency scanning, deployment migration validation, and artifact creation.
- [ ] Establish controlled database deployment and rollback; clients never migrate cloud schemas.
- [ ] Add backup verification, point-in-time recovery exercises, disaster-recovery objectives, and
  incident alerts.
- [ ] Produce a signed self-contained Demo installer after API access is working.
- [ ] Test installation, authentication, updates, and removal on a clean non-developer machine.

### Azure Demo milestone

- [x] Split local Production and Demo databases and add fail-closed identity markers.
- [ ] Extract a versioned canonical superhero/sitcom Demo seed independent of migrations.
- [x] Provision `SatiDemo` in Azure SQL without moving production data.
- [x] Host the Demo API with managed identity and least-privilege SQL access.
- [x] Restrict Azure SQL so tester devices do not connect directly.
- [x] Deploy and live-test a daily canonical caseload refresh with rolling dates, a separate managed
      identity, six deliberate teaching exceptions, profile/claim validation, and repeat-run safety.
- [ ] Extend the refresh into a full baseline reset that pauses mutations, removes arbitrary Demo
      changes, resets demonstration logins, and sends failures to an approved notification target.
- [ ] Complete an API inventory and migrate all workflows included in the colleague Demo.
- [ ] Run security, tenant-boundary, concurrency, reset, and clean-install acceptance tests.

### Production readiness gate

Production cloud migration is not authorized by completion of the Demo milestone. It requires a
separate risk assessment, architecture review, operational runbook, BAA/vendor review, penetration
testing plan, user-access process, incident-response process, and explicit approval to move real
working data.

## Mobile and Avalonia Strategy
*Decision: no bottom-up Avalonia rewrite before the cloud platform boundary exists.*

WPF remains Sati's full-power, data-dense Windows client. Cross-platform reach will come first from
the shared API and portable contracts, not from forcing the current desktop shell onto phones.
Avalonia remains a candidate for a focused mobile client and, only if real demand emerges, a future
cross-platform desktop client.

### Prerequisites

- [ ] Complete the API foundation for authentication, people, notes, and upcoming work.
- [ ] Extract portable projects that do not depend on WPF, EF Core, desktop dialogs, or local
  filesystem paths. Initial target structure:
  - `Sati.Contracts` — versioned API request/response DTOs
  - `Sati.Domain` — portable domain concepts and authoritative pure calculations
  - `Sati.Client` — authentication, HTTP transport, and shared client services
  - `Sati.Api` — server authority, EF Core, authorization, audit, and integrations
  - `Sati.Wpf` — existing Windows professional client
- [ ] Keep platform-neutral service contracts free of `Window`, `Dispatcher`, `SecureString`,
  EF entities, and other Windows/persistence-specific types.
- [ ] Define mobile security, session, offline-storage, synchronization, and device-loss rules
  before storing any protected data on a phone.

### Define the first mobile product

Do not treat the desktop application as the mobile specification. Validate the smallest useful
field-work scope, likely:

- secure login and session expiration;
- today's work and upcoming deadlines;
- client lookup and essential contact details;
- quick visit/contact note capture;
- interruption-safe local drafts and later synchronization;
- optional camera/document capture; and
- future EVV check-in/out if required.

Billing administration, EDI, provider maintenance, system settings, dense compliance matrices,
and broad supervisory analytics are not assumed to belong in the first mobile client.

### One-week Avalonia spike — after API prerequisites

- [ ] Create separate Avalonia core and Android projects; do not replace `Sati.Wpf`.
- [ ] Implement login, client list, one client summary, and basic note capture against the API.
- [ ] Approximate the Sati theme without attempting full WPF style parity.
- [ ] Test touch targets, navigation, interruption recovery, slow/offline behavior, and session
  expiration on a physical Android device—not only an emulator.
- [ ] Record porting friction around XAML styles, converters, accessibility, dialogs, and shared
  ViewModels.
- [ ] Estimate iOS requirements separately, including macOS/Xcode, signing, provisioning, and
  distribution.
- [ ] Decide whether to proceed based on field usability and maintenance cost rather than the fact
  that the sample compiles.

### Explicit non-goals

- No week-long attempt at whole-application Avalonia feature parity.
- No mobile client that connects directly to SQL or embeds a database credential.
- No virtualized desktop-window interface presented as a finished phone experience.
- No parallel copy of authoritative business rules in WPF and Avalonia.
- No abandonment of WPF unless cross-platform desktop demand and a measured migration case justify
  it.
## Billing Pipeline (historical plan; superseded by the completed hardening slice above)

### Data model changes needed
- [ ] Add `CompletedDate DateTime?` to `Form` + migration
- [ ] Add `IsBillable bool` to `Note` + migration (default true, computed at creation)
- [ ] Add `BillingStatus` enum: `Pending | Approved | Rejected | Queued`
- [ ] Add `SupervisorApprovedById int?` and `SupervisorApprovedAt DateTime?` to `Note`

### Logic
- [ ] Add `FormComplianceStatus` enum: `NotYetDue | InWindow | CompliantOnTime | CompliantLate | Overdue | NoForm`
- [ ] Add `GetComplianceStatus(FormType, DateTime, Settings)` to `Person`
- [ ] Wire `IsBillable` computation into `SubmitNewNoteAsync` — check all required forms at `EventDate`
- [ ] `IsBillable == false` notes route to supervisor review queue, not billing queue

### Supervisor dashboard
- [ ] Add billing review queue sub-view — shows all `IsBillable == false` notes
- [ ] Supervisor approve → `IsBillable = true`, set `SupervisorApprovedById` and `SupervisorApprovedAt`
- [ ] Supervisor reject → note stays non-billable, logged in audit trail

### Rules
- Compliant on time + in window = billable
- Compliant late = compliant on paper, gap period unbillable
- Overdue = not billable until resolved and supervisor-approved
- Notes persist regardless of billability — always a valid service record
---

## Future Roadmap

### Upcoming-item rule ownership

- [ ] Reconcile the deliberately different scopes of `IUpcomingEventService` (the settings-driven
  open/late window used by dashboards and suggestions) and
  `NewClientViewModel.RefreshUpcomingItems` (all non-compliant forms on the selected-client page).
  Decide whether both scopes remain product requirements, then give each a named shared owner so a
  third hand-written upcoming-item calculation cannot appear.

### Local AI case-note drafting

Closed-world revision shipped 2026-08-22: prior notes, assessments, Bio, deadlines, contacts, and
other historical records were removed from AI context. The selected-client route now returns only
the own-caseload person's ID and first name and never receives rough-note text. Every rough fragment
and every selected Visit checkbox, selector value, attendee, or detail becomes a stable required
fact. The model must return fact-cited JSON; shared deterministic rules reject the whole result for
omission, selector-value loss, wrong-section use, or unsupported names, numbers, quotations,
negation, and content words. `Not documented`, `Not assessed`, and unchecked controls assert nothing.

The original narrative remains unchanged until explicit acceptance. A source fingerprint and latest-
request identity prevent a result from publishing or being accepted after the person, narrative, or
template state changes. Consumer presence is explicitly selected instead of defaulting to present.
Cross-consumer model switching fails closed if unload fails. Follow-up is supported by a current fact
or exactly `No follow-up was documented.`; historical form deadlines are no longer inferred.
The model can explicitly select Sati's validated deterministic renderer instead of risking an
unsupported rewrite. Runtime failures and rejected model output use the same renderer with a visible
warning. The opt-in device gate requires safe completion of all synthetic scenarios through the real
local runtime; explicit safe deferral is accepted rather than forcing unsupported prose.

Before any shared or production release:

- Obtain the authoritative agency case-note policy and replace/refine `AI_CASE_NOTE_RULES.md`.
- Assemble at least 50-100 de-identified rough-note/approved-note examples spanning visits,
  contacts, forms, sparse notes, ambiguity, quotations, negative statements, and safety content.
- Define and pass acceptance thresholds for zero invented facts, retained attribution/negation,
  required formatting, latency, memory use, and accessibility.
- Run the separately controlled local-model/device evaluation suite; deterministic compiler,
  grounding, tenant-scope, and stale-client regressions are now automated. The representative
  target-device smoke gate runs only with `SATI_RUN_LOCAL_AI_MODEL_EVAL=1` so ordinary CI cannot
  acquire model weights implicitly.
- Persist an audit record for accepted AI drafts: source, draft, final user-edited text, rule-set
  version, model alias/version/hash, user, timestamps, and explicit acceptance. Decide retention and
  access rules before adding this to the database.
- Add model-download/retry controls; test first-run, cached/offline, low-disk, unavailable-model,
  corrupt-cache, and runtime-unload behavior on supported devices. In-flight generation now cancels
  when its selected client or source inputs change.
- Complete privacy, security, clinical/documentation, labor, and records-retention review. Confirm
  runtime telemetry is disabled or contains no PHI and that no cloud fallback can occur.
- Pin the approved model variant and license terms; prevent an unreviewed catalog update from
  changing production output.
- Add an administrative release gate so production builds default to disabled until formally
  approved, even if a development `appsettings.json` is copied accidentally.
*Parked for post-OADS or v2.0+*

- [ ] **Historical productivity viewer** — query past Incentive rows paired with monthly
  note data to display a full productivity history per user; infrastructure already exists
- [ ] Per-client detail view — all notes, forms, compliance status, and upcoming events
  scoped to one client
- [ ] User management / admin panel — add, edit, deactivate users
- [ ] Soft-delete recycle bin for notes
- [ ] DataGrid column cleanup — replace AutoGenerateColumns with explicit column definitions
- [ ] Sati.Core extraction — shared class library for models, services, EF context
- [ ] Sati.Api — ASP.NET Core Web API (GET /clients, POST /notes)
- [ ] Sati.Mobile — MAUI app for field note entry and upcoming task visibility
- [ ] Azure Cognitive Services Speech-to-Text for field note dictation
- [ ] reMarkable integration — push visit PDFs, pull annotated docs

---

## Comprehensive Assessment, PCP, and OADS Workflow
*Goal: Replace Evergreen with a person-centered, waiver-agnostic assessment and plan workflow that is practical for case managers, reviewable by supervisors and OADS, and safe for billing.*

### Comprehensive Assessment — first functional slice shipped 2026-08-07

- [x] Replace the client-workspace placeholder with a desktop-oriented assessment editor.
- [x] Add eight navigable domains with an initial set of practical questions.
- [x] Add expandable guidance to every question: why it is asked, what a complete
  answer includes, and what to avoid.
- [x] Persist drafts in `ComprehensiveAssessments` and auto-save after edits.
- [x] Store the assessment body as one versioned JSON aggregate while workflow and
  ownership fields remain relational/queryable.
- [x] Record report contributors separately from the assigned case-manager author.
- [x] Use one team assessment by default with optional dissenting perspectives.
- [x] Model support as combinable characteristics, not a false linear scale.
- [x] Enforce logical exclusions: "No support currently needed" clears active support
  selections; `Varies` requires at least one concrete support and an explanation.
- [x] Require every question to be addressed before submission; follow-up-required
  answers do not count as complete.
- [x] Add structured identified needs with type, desired result, and optional provider
  association.
- [x] Enforce authoring by caseload ownership. Supervisor status alone does not grant
  permission to rewrite another case manager's answers.
- [x] Add submission to supervisor review and immutable approved/superseded states in
  the domain model.
- [x] Change the new/default Comprehensive Assessment deadline from 120 to 60 days
  before the PCP anniversary.
- [x] Add migration `20260807120000_AddComprehensiveAssessments`.

### Comprehensive Assessment — content and usability follow-up

- [ ] Flesh out the assessment with the additional specific questions identified by
  case-manager and OADS review; keep questions waiver-agnostic.
- [ ] Add a realistic good-answer example to every question, alongside the existing
  inclusion and avoidance guidance.
- [ ] Review every question for plain language, practical answerability, duplication,
  trauma-informed wording, dignity, and relevance to authorization.
- [ ] Decide which questions are conditional and implement branching without hiding
  previously entered answers.
- [ ] Add question-level comments, supervisor flags, resolution state, and return reason.
- [ ] Add a visible validation summary with links that move focus directly to every
  incomplete or contradictory answer.
- [ ] Validate contributor rows, identified needs, provider associations, and dissenting
  opinions—not only the core question set—before submission.
- [ ] Add need urgency, current supports, unmet component, health/safety implication,
  responsible next action, status, and resolution history.
- [ ] Replace the temporary provider-name entry with selection from the future
  consumer/provider association model while retaining a document snapshot.
- [ ] Add `Not assessed` follow-up ownership and due date; permit completion only through
  an explicitly documented supervisor exception where policy allows.
- [ ] Add autosave retry/recovery, unsaved-change shutdown flush, concurrency handling,
  and protection against two sessions editing the same draft.
- [ ] Remove the service-locator construction in
  `ComprehensiveAssessmentWorkspace.xaml.cs`; inject/factory-create the workspace and
  ViewModel consistently with Sati's DI rule.
- [ ] Perform keyboard-only, JAWS, high-contrast, 200% scaling, and 1280x768 layout QA.
- [ ] Add unit tests for completion rules, support-selection exclusions, ownership,
  version immutability, serialization compatibility, and submission transitions.

### Supervisor assessment workflow

- [ ] Add a supervisor review queue for Comprehensive Assessments.
- [ ] Allow supervisors to flag individual sections/questions, comment, and return a
  submission without rewriting the author's answers.
- [ ] Make supervisor approval wholesale, with all unresolved flags blocking approval.
- [ ] Permit supervisors who carry a caseload to author only their own assigned clients'
  assessments; keep the author and reviewer capabilities separate.
- [ ] Record submission, return, resubmission, approval, actor, timestamps, reason, and
  exact version in append-only audit history.
- [ ] On approval, lock the version and mark the matching legacy `Form` complete through
  the existing `Form` invariant rather than writing compliance fields directly.

### Documents, signatures, and versions

- [ ] Publish a version-identified Comprehensive Assessment PDF.
- [ ] Retain both the generated unsigned PDF and uploaded physically signed scan.
- [ ] Record signer, role, signature method, upload actor, and timestamps against the
  exact frozen version.
- [ ] Add the same publish/print/upload workflow to the PCP.
- [ ] Ensure any post-publication edit creates a new version and signature cycle; never
  replace or silently mutate a signed or approved version.
- [ ] Establish secure document storage, malware scanning, retention, download/export
  authorization, and accessible PDF generation.

### Electronic signature portal — vetted direction

The checklist below describes the broader regulated feature. The synthetic implementation and
remaining activation work are recorded under **Electronic signature handoff — implemented synthetic
scope and activation work (2026-09-06)** near the end of this document. Its completed pieces do not
clear real-use, multi-signer, program-acceptance, or operating requirements in this broader list.

The signature feature should be a secure Sati web portal reached from an email
notification—not a Sati-operated mail server and not an email reply treated as the
authoritative signature. Email is the delivery channel; Sati owns the identity check,
review, intent-to-sign action, immutable evidence, and resulting signed artifact.

#### Policy gates before implementation

- [ ] Obtain written OADS/OMS confirmation that the MaineCare electronic-signature
  notice supersedes the OADS PCP manual's physical-signature instructions for the PCP
  Face Sheet and for annual/reversioned plans.
- [ ] Confirm separately whether electronic signing is accepted for provider/team
  Agreement Sheet signatures; the current MaineCare notice expressly discusses member
  signatures, while the published OADS manual still says implementing Team Members must
  physically sign the Agreement Sheet.
- [ ] Confirm what exact evidence Resource Coordinators must receive and whether a
  generated signed PDF plus audit certificate qualifies as the retained original.
- [ ] Confirm the required signers and signature meaning for the Comprehensive Assessment
  independently from the PCP workflow.
- [ ] Document the authority and identity-proofing rules for a Person, guardian,
  authorized representative, case manager, provider implementer, supervisor, and OADS
  Resource Coordinator. A proxy must sign in the proxy's own name and capacity, never as
  though they were the Person.
- [ ] Preserve a paper, in-person, and accessible assisted-signature path. Electronic
  transactions must be consensual and must not become a condition of receiving services.

Policy basis reviewed 2026-08-07:

- [MaineCare's electronic-signature notice](https://www1.maine.gov/dhhs/oms/providers/provider-bulletins/notice-regarding-electronic-signatures-2024-09-16)
  permits member electronic signatures under enforcement discretion when the system
  authenticates the signer, prevents signing an incomplete document, complies with
  privacy/security requirements including HIPAA, and retains proof plus the signed record.
- [Maine UETA](https://legislature.maine.gov/statutes/10/title10ch1051sec0.html)
  recognizes an electronic process adopted with intent to sign, requires agreement to
  transact electronically, permits codes/security procedures as attribution evidence,
  and requires an accurate record that remains accessible for later reference.
- [42 CFR 441.301(c)(2)(ix)](https://www.ecfr.gov/current/title-42/chapter-IV/subchapter-C/part-441/subpart-G/section-441.301)
  requires the PCP to be finalized with the individual's informed written consent and
  signed by the individual and all people/providers responsible for implementation.
- The [published OADS PCP manual](https://www.maine.gov/dafs/bablo/sites/maine.gov.dhhs/files/documents/PCPManualpdf.pdf)
  still instructs case managers to obtain physical signatures and maintain originals;
  this unresolved conflict is why written OADS/OMS direction is a release gate.

#### Signature workflow and domain model

- [ ] Freeze a complete, version-identified document before creating any signature
  request. Store the final PDF, document version, cryptographic hash, and publication
  timestamp; never allow the published content to mutate in place.
- [ ] Model `SignatureEnvelope`, `SignatureRequest`, `RequiredSigner`, and append-only
  `SignatureEvent` records separately from document approval and authorization state.
- [ ] Create one uniquely addressed request per required signer. Do not treat a shared
  family email, provider group mailbox, meeting attendance, or document contribution as
  proof that a particular person signed.
- [ ] Track signer name, role/capacity, organization, required/optional status, delivery
  status, authentication method, signed/declined/requested-changes state, and timestamps.
- [ ] Give the signer three explicit outcomes after reviewing the exact frozen document:
  sign/agree, decline, or request changes. Capture the exact intent and consent language
  displayed at the moment of signature.
- [ ] Keep signature completion, supervisor approval, OADS wholesale approval,
  Classification, service authorization, and billability as distinct states. One must
  never silently imply another.
- [ ] Require a new document version and signature cycle after any substantive change.
  Retain the prior version, its signatures, and the reason for supersession.
- [ ] Generate a signed PDF or audit certificate logically associated with the frozen
  PDF, and retain both the original final artifact and complete signature evidence.
- [ ] Distribute a downloadable/printable copy to the Person/guardian and other entitled
  participants after completion, using the same access controls as the signing portal.

#### Email delivery and authentication

- [ ] Use a managed outbound transactional-email service under the required HIPAA
  agreement and configure domain authentication/deliverability. Do not operate inbound
  SMTP or require the signer to reply to an email.
- [ ] Keep notification emails generic and free of assessment/PCP content and unnecessary
  identifiers. Do not automatically attach PHI; the opaque, random, single-use link opens
  the protected portal where the document is shown after authentication.
- [ ] Confirm the email address and preferred confidential communication method during
  enrollment, and support correction/revocation without silently retargeting an existing
  signature request.
- [ ] Make link tokens high-entropy, single-use, short-lived, revocable, rate-limited,
  and stored only as hashes. Never place a person ID, document ID, MaineCare ID, or other
  meaningful identifier in a URL.
- [ ] If a pre-established signing code is retained, establish it only after verified
  enrollment, hash it using the password-storage standard, never reveal it to staff, and
  rate-limit/lock failed attempts. Do not send the code through the same email as the link.
- [ ] Prefer an existing authenticated provider account, passkey, or short-lived code
  delivered through an independent verified channel. NIST does not treat email as an
  acceptable independent out-of-band authentication channel.
- [ ] Design a verified recovery and assisted-signing process for people who forget a
  code, share an email account, lack a mobile phone, use supported decision making, or
  cannot independently operate the portal. Recovery must not be easier to exploit than
  the normal signature flow.

#### Security, evidence, operations, and accessibility

- [ ] Put the public signing surface behind a trusted web application/API boundary;
  the WPF client and direct database access cannot serve as the Internet-facing security
  boundary. Authorize every request by capability, organization, signer, document version,
  and workflow state.
- [ ] Record append-only evidence including document hash/version, signer identity and
  capacity, authentication method, consent/intent text, issuance/view/sign timestamps,
  delivery events, failed attempts, revocation/expiration, and administrative actions.
- [ ] Decide through privacy/security review whether IP address and user-agent evidence is
  necessary and proportionate; document retention and access rules for that metadata.
- [ ] Encrypt PHI in transit and at rest, segregate signing secrets from document storage,
  prevent replay, scan uploaded physical-signature fallbacks for malware, and include the
  portal/vendor in risk analysis, incident response, breach response, and business-
  associate agreements.
- [ ] Add reminders, expiration, resend, email-bounce handling, signer replacement,
  revocation, and escalation without changing the frozen document or losing history.
- [ ] Make the signing page work at common mobile and desktop sizes with keyboard-only
  navigation, screen readers, magnification, high contrast, plain language, limited-
  English support, and an accessible downloadable document.
- [ ] Add automated tests for incomplete-document blocking, wrong signer, shared email,
  expired/replayed links, brute-force limits, version mismatch, signer replacement,
  decline/request-changes, partial multi-signer completion, post-signature mutation,
  audit immutability, and separation from OADS approval/authorization.

This is a medium-to-large architecture feature despite its intentionally simple signer
experience. The email sender is a small component; the public portal, trusted API,
identity and authority proof, immutable document/version handling, multi-signer state,
audit evidence, accessibility, and policy acceptance are the substantive work.

### Person-Centered Plan

- [ ] Build the PCP at intake and annually, using the approved assessment and live
  consumer profile as sources without silently mutating approved plan versions.
- [ ] Record all PCP meeting participants, roles/relationships, invitation/attendance
  status, attendance method, and signature/acknowledgment state.
- [ ] Default the assigned case manager as meeting organizer, with an audited override.
- [ ] Add profile-to-PCP change rules: informational, potentially material, material,
  and authorization-affecting.
- [ ] Generate a reviewable PCP change set or amendment from material profile changes;
  require case-manager confirmation and supervisor review for important changes.
- [ ] Add OADS Resource Coordinator review with section flags/comments but wholesale
  approval/return of the PCP.
- [ ] Add authorized services as a deliberately unfinished section boundary now; later
  connect it to Providers, authorization periods, units, frequency, duration, funding,
  assessed needs, and goals using immutable snapshots.
- [ ] Preserve the distinction between assessment facts, supervisor attestation, PCP,
  OADS decision, classification, and authorization.

### Classification and future OADS access

- [ ] Keep Comprehensive Assessment and PCP waiver-agnostic.
- [ ] Implement waiver/level-of-care determination in Classification, including future
  Lifespan Waiver support.
- [ ] Add the OADS Resource Coordinator role and narrow capabilities rather than relying
  on menu visibility or broad job-title permissions.
- [ ] Record approval, denial, return, effective/expiration dates, cited evidence,
  decision-maker, and immutable decision history.
- [ ] Add assignment, delegation, temporary coverage, reassignment, and recusal workflows.

### Deadlines, reminders, reviews, and billing

- [ ] Reconcile already-generated Comprehensive Assessment `Form.DueDate` values from
  the legacy 120-day offset to the agreed 60-day-before-PCP rule through an inspected
  dry run; the migration changes the setting/default but deliberately does not guess at
  existing records.
- [ ] Continue using the existing Form/ReviewItem reminder calculations as the canonical
  deadline source; do not create parallel assessment date arithmetic.
- [ ] Block PCP submission when its Comprehensive Assessment is overdue, with a documented
  supervisor-or-higher override containing reason, actor, timestamp, expiration, and
  affected version.
- [ ] At midnight after PCP expiration, mark subsequently submitted case notes permanently
  unbillable; there is no grace period and later PCP completion is not retroactive.
- [ ] Apply the same permanent billing-gap rule to overdue 90-day reviews, using Sati's
  existing review due-date calculations.
- [ ] Preserve all concurrent unbillable reasons on the note and show them before note
  submission as well as in billing exports.
- [ ] Define and implement the exact instant at which billability resumes after a late
  PCP or 90-day review is completed.
- [ ] Prevent unbillable notes from entering MIHMS claim generation while retaining them
  as valid service documentation.
- [ ] Add regression tests around midnight boundaries, back-entered notes, multiple
  simultaneous compliance failures, overrides, and permanent non-retroactivity.

## Session Log

| Date | Phase | What was done |
|------|-------|---------------|
| 3/19 | Ph5 | Settings model, migration, ISettingsService/SettingsService, SettingsViewModel, wired into CaseManagerDashboardViewModel |
| 3/20 | Ph5 | Scratchpad model/service/migration, auto-save timer, NoteType enum, template insertion |
| 3/21 | Ph5 | NoteType radio buttons, EnumToBoolConverter, Incentive model/service/migration, productivity dashboard, weekday/holiday exclusion flags |
| 3/22 | Ph5 | SchedulerViewModel, WorkdayTile, Incentive ExcludedDates, ISessionService singleton, scheduler popup XAML |
| 3/23 | Ph5 | Scheduler popup fully working — tile toggling, month navigation, DaysScheduled persistence |
| 3/23 | Ph5 | SettingsWindow fully wired — billing, templates, weekday/holiday flags, auto-save on close |
| 3/23 | Ph5 | Day after Thanksgiving flag, tuple return from GetOrCreateAsync, custom new month prompt |
| 3/25 | Ph6 | SafetyPlan + PrivacyPractices added to FormType. 18 form deadline offset properties in Settings. UpcomingEventService created. |
| 3/26 | Ph6 | UpcomingEventService wired into CaseManagerDashboardViewModel, UserId FK on Person, GetAllPeopleAsync filtered by userId |
| 3/28 | Ph6 | Upcoming Tasks split into two columns, sort radio buttons, EffectiveDate refactor, form note templates, FormType on Note with migration |
| 3/29 | Ph6 | MarkFormCompleteRequested wired end to end, compliance checklist bound to real data, GetCurrentCycleForm added, ComplianceReviewWindow on client creation |
| 3/29 | Ph7 | Note workflow fixes — IsEditing reset, form clears, NoteType persistence, scheduled visits/contacts in Upcoming Tasks |
| 4/8  | Bug | Diagnosed productivity threshold bug — hardcoded * 19, stale _incentive after scheduler closes. Fix plan: UnitsPerDay snapshot field, OnIsSchedulerOpenChanged refresh, GetOrCreateAsync wasCreated fix. Historical productivity viewer scoped as future feature. |
| 8/6  | AT | AT Request item entry (slice 1c) — item cards with Name/URL/Cost/Qty on the editor left pane, live subtotal/passthrough/total readout, `ATRequestItemEditorViewModel` write-through with parent total-change callback. Added `Url` field to `ATRequestItem` (+migration). Provider slice 1: `Provider` model, `ProviderType`/`WaiverService` enums, `Settings.SalesTaxRate` + `DefaultPassthroughProviderId`, migration `AddProviderAndSalesTax`, Maine AT Solutions seeded. |
| 8/7  | AT | Provider slice 2: `IProviderService`/`ProviderService`, `ProviderEditorViewModel` (bit-per-checkbox flags + passthrough reveal), `ProvidersViewModel` master-detail, `ProvidersView`, Providers tab grafted into CM sub-nav, DI wired. Settings window gained sales-tax-rate box + default-passthrough-provider dropdown (SelectedValue→int? FK). |
| 8/7  | Assessment | First functional Comprehensive Assessment slice: versioned JSON-backed draft, autosave, eight-domain desktop editor, practical per-question guidance, contributors, dissent, combinable support characteristics, structured needs, caseload ownership, completion gate, supervisor submission state, migration, and 60-day default offset. |



## From note-entry extraction + per-consumer Journal session (2026-07-29)

### Data integrity
- [ ] **Duplicate forms surfacing in billing window.** `EvaluateBillingWindow`
      displayed the same PCP three times for one client (Christian Bobe) — it
      iterates `Forms.Where(gated)` with no dedup, so duplicate/multi-cycle PCP
      rows all match. Not cosmetic: duplicate `Form` rows on a Person could skew
      compliance elsewhere. Trace where the triplicate came from (form generation?
      rollover?) before deduping the display — the display is the symptom, the
      rows are the bug.

### Data loss risk
- [x] **App-close flush for Journal + Scratchpad.** Quitting via the window X
      within 2s of typing loses the Journal tail (debounce hasn't fired). Same
      gap likely affects Scratchpad — its only shutdown save is in
      `ShellViewModel.ReinitializeAsync` (user-switch), not true app-close. Fix:
      `ShellWindow.OnClosing` handler calling `_notesViewModel.Clients.FlushJournalAsync()`
      and the scratchpad save. User-switch is already covered.

### Consolidation left unfinished
- [ ] **NotesLog has two compliance dialogs.** The extracted entry module carries
      its own (entry-form path); the old host-level overlay in `NotesLogView.xaml`
      (`Grid.ColumnSpan="5"`, fixed `Width="460"`, no height bound) still serves the
      context-menu "Mark Note Logged" path via `NotesWindowViewModel`. Both work,
      driven by different VMs, but it's one screen with two dialog definitions.
      Fold the context-menu path onto the module's dialog, delete the host overlay.
- [ ] **User-switch doesn't reset the NotesLog entry module's draft.** Dashboard's
      `Reset()` cascades to its `NoteEntry.Reset()`; NotesLog's copy only gets
      `SetPeople` on reload (which clears selection but not a half-typed narrative).
      Add `NotesLog.NoteEntry.Reset()` to the reinit path.

### Cleanup
- [ ] **Delete dead `NotesWindowViewModel.LoadAsync`.** Superseded by `ReloadAsync`
      (which also does sentinel reset + property notifications). Nothing calls the
      private `LoadAsync` — confirmed via Shell lifecycle. Shadow copy of load logic.
- [ ] **`SendToSupervisor`/`Cancel` event asymmetry in `NotesWindowViewModel`.**
      `Cancel` fires `NoteStatusChanged`; `SendToSupervisor` doesn't. Looks
      backwards. Verify intent, align.

### Architectural debt (widened, not fixed)
- [ ] **Tiered loading — `GetAllPeopleAsync` still fat-loads.** Base row now pulls
      `Bio` AND `Journal` (both `nvarchar(max)`) plus the full Notes/Forms graph via
      dual `.Include` + `.AsSplitQuery()`. Journal's on-demand methods dodge this,
      but the base load is unchanged. Still the `RESOURCE_SEMAPHORE` culprit; now
      with one more unbounded column riding along. Projection-to-summary-DTO remains
      the fix.

## From AT Requests + Provider Directory session (2026-08-07)

### Shipped
- [x] **AT Request item entry (slice 1c).** Editor left pane now has an "Items or
      Services" section: one card per line item with Name, URL, Cost, and Quantity,
      plus an Add Item button and per-card Remove. `ATRequestItemEditorViewModel`
      is the write-through row wrapper; cost/qty edits fire a parent callback that
      re-raises the request totals. Live subtotal / 15% passthrough / total readout
      under the sales-tax box mirrors the form preview.
- [x] **`ATRequestItem.Url`.** Nullable string, stored on the item now, destined for
      the future screenshots-with-clickable-links page 2. NOT rendered on the page-1
      OADS form. URL extraction from retailer pages was scoped and **rejected** —
      scraping is fragile and out of place in a case-management app. (+migration)
- [x] **Provider directory (slices 1-2).** New `Provider` model (structured address,
      `[Flags] WaiverService OfferedServices`, `ProvidesPassthroughService` bool,
      flat passthrough billing strings). `ProviderType`/`WaiverService` enums.
      Providers tab under CM sub-nav - master-detail CRUD, passthrough checkbox
      reveals the three billing fields. Maine AT Solutions seeded as the passthrough
      default. `IProviderService`/`ProviderService`, `ProviderEditorViewModel`,
      `ProvidersViewModel`, `ProvidersView`, DI, migration `AddProviderAndSalesTax`.
- [x] **Settings: sales tax + default passthrough provider.** `Settings.SalesTaxRate`
      (0.055 default, a rate not an amount) and nullable `DefaultPassthroughProviderId`
      FK. Settings window gained a tax-rate box and a provider dropdown
      (`SelectedValue`->int? FK, `SelectedValuePath=Id`).

### Remaining on this feature (slices 3-4)
- [ ] **AT page passthrough dropdown (slice 3).** Dropdown of passthrough providers,
      pre-selected to `Settings.DefaultPassthroughProviderId`. On select, snapshot-copy
      the provider's Name/BillingLocationEis/ProgramContact/BillingContact onto the
      request's `Vendor*` fields (same freeze-at-select semantics as client/CM). Keep
      a nullable `ProviderId` FK on `ATRequest` alongside the snapshot.
- [ ] **Item numbers on the OADS form.** 1, 2, 3... in the form's Item # column via
      WPF `AlternationIndex` + a +1 converter. No data change.
- [x] **Sales-tax freeze (slice 4).** Landed 2026-08-15. Tax is calculated from
      `Settings.SalesTaxRate` and frozen as an amount on the request, with a per-request
      manual override.
- [x] **AT request Save + Publish PDF.** Landed 2026-08-15 as one slice, as intended.
      Open/Save/Publish/Reopen/Export, attestation, publication lock, and the generated
      PDF. See the decision entry of the same date.

### Deferred (needs a later slice)
- [x] **Retain the executed AT request PDF — decided against, 2026-08-15.** Not deferred;
      rejected. A published request cannot change, so the PDF is a pure function of a frozen
      record and storing it would duplicate that record, screenshots and all. See the decision
      entry "The PDF is regenerated, never retained." The one residual risk is exporter code
      changes, noted on the OADS-layout item below.
- [ ] **Match the OADS Authorized Payment Information Form layout.** The generated PDF is a
      Sati document carrying the same information, not a reproduction of the state form.
      Requires the blank form. Layout change only — `ATRequestPdfExporter` is the sole owner.
      NOTE when this lands: PDFs are regenerated rather than retained, so changing the layout
      changes how every historical request re-renders. The figures stay correct; the
      presentation will not match what was submitted at the time. Accepted, but decide
      deliberately rather than discovering it afterwards.
- [ ] **Decide whether an electronic-signature standard applies to AT requests.** Sati records
      an attestation, and says so on the document. Whether OADS requires a signature meeting a
      specific standard is a question for counsel and OADS, not for the repository.
- [ ] **Client<->provider association.** The AT dropdown can only list *all* passthrough
      providers - Sati has no link from a consumer to *their* home/community-support
      provider, so it can't pre-select "this client's agency." That association is its
      own model + slice. `OfferedServices` (the four waiver flags) is inert until it lands.
- [ ] **AT Assessments as a waiver service.** Maine AT Solutions actually offers it;
      left out of `WaiverService` until something consumes the offering data.
- [ ] **Providers tab governance.** Provider directory is agency-level shared reference
      data but currently lives under CM sub-nav. In the multi-user future it should be
      admin-curated to prevent duplicate provider rows across CMs.

### Tech debt confirmed against disk this session
- [ ] **Dead files still present:** `ViewModels/SchedulerViewModel.cs` and
      `Models/WorkdayTile.cs` - delete together. (`Models/Event.cs` already removed.)
- [ ] **Filename typo:** `Data/Billing/IdeService.cs` should be `EdiService.cs`
      (class name is fine; only the file is misnamed).

## From MVVM / SOLID architecture audit (2026-08-07)

The current architecture has a sound base: ViewModels do not access EF directly,
services generally use injected interfaces and per-method `IDbContextFactory` contexts,
the composition root is centralized in `App.xaml.cs`, and important compliance behavior
already lives in `Person` and `Form`. The items below are targeted refactors, not grounds
for a rewrite.

### P1 — security and active correctness

- [ ] **Enforce assessment authorship on every write at the service boundary.**
      `ComprehensiveAssessmentService.SaveDocumentAsync` accepts only an assessment ID
      and document, so it can update another author's editable draft if invoked outside
      the current UI path. Require a trusted authenticated actor/capability and verify
      ownership, assignment, organization, and workflow status before saving. Apply the
      same rule to submission instead of treating a caller-supplied user ID as identity.
- [ ] **Enforce supervisory authorization inside `SupervisorService`.**
      `ApproveNoteAsync`, `ApproveWithOverrideAsync`, and `ReturnNoteAsync` trust the
      supplied `supervisorId`; the queue also accepts an `allSupervisees` switch. Verify
      the authenticated actor's role/capabilities, agency scope, and actual supervisory
      relationship before reading or changing a note. Before OADS or other external users
      enter Sati, place these checks behind a trusted application/API boundary rather than
      relying on a desktop UI with direct database access.
- [x] **Fix the new-account `AssignedAgency` null path.** `NewUserViewModel.CreateUser`
      dereferences `AssignedAgency.Id`, but `AssignedAgency` is never initialized and
      `NewUserWindow` has no agency selector. Add the intended agency source/selection,
      validate it before user creation, and cover the flow with a test. This is the
      nullable warning currently emitted at `NewUserViewModel.cs:70`.

### P2 — MVVM boundaries and maintainability

- [ ] **Remove the Comprehensive Assessment service locator.**
      `ComprehensiveAssessmentWorkspace` constructs its own ViewModel through
      `Application.Current.Services`. Create the workspace/ViewModel through the
      composition root or a typed injected factory so dependencies are explicit and the
      workspace is testable.
- [ ] **Move concrete dialogs and WPF application access out of ViewModels.** Several
      ViewModels directly call `MessageBox.Show`, `Application.Current`, `ShowDialog`, or
      depend on a concrete `UserMessageDialog`. Replace these with narrow interaction,
      navigation, notification, and dispatcher/scheduler abstractions—or events handled
      by the View. Keep purely visual window ownership in code-behind.
- [ ] **Make View event subscriptions detachable.** `ClientsView.OnDataContextChanged`
      attaches an anonymous `ComplianceReviewRequested` handler without removing it from
      the previous ViewModel. Use a named handler or
      an explicit attach/detach lifecycle. On unload, also detach
      `ComprehensiveAssessmentWorkspace` from its parent ViewModel. Verify that view
      recreation or user switching cannot produce duplicate dialogs or retained views.
- [ ] **Split `CaseManagerDashboardViewModel` by feature responsibility.** It is roughly
      900 lines with about sixteen constructor dependencies and currently coordinates
      notes, forms, upcoming work, incentives, clients, statistics, reviews, providers,
      calendar, and compliance effects. Retain it as a thin dashboard/module coordinator
      while moving feature state and commands into focused child ViewModels/application
      services.
- [ ] **Split `NewClientViewModel` by feature responsibility.** It is roughly 830 lines
      and owns client CRUD/editing, notes loading, forms, reviews, appointments,
      healthcare reference data, and journal autosave. Extract focused client-profile,
      journal, appointment, and compliance/document components while preserving the
      current single Overview experience.
- [ ] **Extract a versioned Comprehensive Assessment definition catalog.**
      `ComprehensiveAssessmentViewModel.BuildSections` currently owns question text,
      guidance, support applicability, validation, navigation, mapping, persistence, and
      autosave. Move the content/schema into a dedicated, versioned definition provider
      so questions can grow or branch without modifying the editor and so existing JSON
      answers remain reproducible against the definition version that created them.
- [x] **Add an automated domain/workflow test project.** Prioritize
      `EvaluateComplianceGate`, `EvaluateBillingWindow`, midnight and back-entry rules,
      permanent unbillability, override separation, assessment support exclusions,
      completion rules, ownership, serialization compatibility, and workflow transitions.
      No test project was found during this audit.

### P3 — cleanup and consistency

- [ ] **Inject `IPasswordHasher` into `AuthService`.** It is already registered, but
      `AuthService.AuthenticateAsync` constructs `PasswordHasher` directly.
- [ ] **Align DI registrations with effective lifetimes.** `ScratchpadViewModel`,
      `NewClientViewModel`, `UserManagementViewModel`, and `PendingApprovalsViewModel` are
      registered transient but captured by singleton parents. Decide whether each is
      intentionally session-long; register it accordingly or create it through an
      explicit scope/factory.
- [ ] **Delete the hidden legacy client-entry panel and stale state.** `ClientsView.xaml`
      still contains the old entry form and chevron in two zero-width columns, while
      `NewClientViewModel` retains `IsEntryPanelOpen`, `ToggleEntryPanel`, and legacy
      naming. Remove the duplicate markup/commands after confirming the inline Overview
      editor covers add, edit, cancel, and delete.
- [x] **Resolve the disabled cycle-form feature switch.** Done 2026-09-01: the flag is removed
      and generation runs again. It was suppressing a race that the unique index on
      `dbo.Forms (PersonId, Type, DueDate)` now decides, and leaving it off was quietly starving
      the caseload of future cycle forms. See the duplicate-form entry at the top of this file.
- [ ] **Make the README architectural claim accurate.** It currently says ViewModels
      have no knowledge of Views and window creation uses factories throughout. Update it
      after the boundary work above, or describe the remaining pragmatic exceptions until
      they are removed.

### Audit verification

- [x] Rebuilt the current working tree successfully to an isolated output directory while
      the running Sati process held the normal output DLL. The rebuild completed with no
      errors and two distinct warnings: the `AssignedAgency` nullable dereference and the
      constant-false unreachable code described above.

### Deferred from the service-day work (2026-08-14)

- [ ] **Surface service start times in the remaining note lists.** The focused calendar day now
      shows the service-time range through the shared `ServiceTimeline` rule. `NotesLogView` and
      `ClientsView`'s note grid still show only date and minutes; a time column there would let a
      case manager spot a gap or a clash without opening the entry panel.
- [ ] **Extend the overlap rule to the other note-creating paths.** `NewClientViewModel` and any
      other flow that calls `INoteService.AddNoteAsync` directly writes a note with no start time.
      Those notes claim no time and conflict with nothing, which is correct but means the day bar
      does not show them. Decide whether those flows should collect a start time.
- [x] **Decide whether Scheduled notes should reserve time softly.** Resolved 2026-09-05 for future
      plans: estimated minutes remain, actual start time stays empty, and starting work edits the
      Scheduled row in place and selects the earliest open window. Historical or same-day Scheduled
      rows that already carry a start still represent a claimed window.

### Deferred from the API security audit (2026-08-14)

See `API_SECURITY_AUDIT.md` for the full findings. Items reviewed and consciously left alone:

- [ ] **Decide the sign-in lockout policy deliberately.** `LoginAttemptGuard` allows 12 attempts per
      username per minute, so anyone who knows a username can hold that person out of the system.
      Every lockout design trades this against credential stuffing; this one has not been chosen on
      the record. Options include per-IP limits alongside per-username, exponential backoff instead
      of a hard window, or an unlock path for a supervisor.
- [ ] **Give the sign-in guard shared state before running more than one API instance.**
      `LoginAttemptGuard` is per-process, so the effective attempt limit multiplies by the instance
      count. Acceptable for single-instance Demo; not for a scaled deployment.
- [ ] **Make person lookups consistently use `TenantAccess.OwnsPersonAsync`.** `POST /at-requests`,
      `GET /people/{personId}/reviews`, and `GET /people/{personId}/appointments/latest` resolve the
      person by id and authorize on the owning user rather than also asserting
      `person.AgencyId == actor.AgencyId`. Cross-agency access is blocked today because
      `CanAccessUserAsync` requires the target user to be in the actor's agency, so this is
      consistency and defense in depth rather than an open hole. Needs a test covering a person row
      whose agency disagrees with its owner's.

### Documentation and naming cleanup (2026-08-15)

- [ ] **Rename `Data/Cloud/CloudUnavailableServices.cs`.** The filename is a historical misnomer.
      It now contains twelve fully migrated HTTP service implementations (`CloudUserService`,
      `CloudSupervisorService`, `CloudBillingService`, and others), not unavailable stubs. Anyone
      reading the file list will draw the wrong conclusion about how much of Demo is migrated.
- [x] **Settle the company name's casing.** Resolved 2026-08-15: `SatiLogica` is the formal
      rendering everywhere in code, documentation, and user-visible strings. `Satilogica` remains
      acceptable in informal prose. Azure resource names stay lowercase because DNS requires it.
      Applied across the `<Company>` property, installer and uninstaller, Start Menu folder,
      registry publisher, `%LOCALAPPDATA%` paths, and scripts. No upgrade story was needed:
      Windows paths and registry keys are case-insensitive, so existing installs and stored data
      under `Satilogica\` resolve unchanged.
- [ ] **Introduce the program names only as each ships.** `SatiLogica` is the platform;
      `Sati` is the case-management program. `Karuna` (service-provider documentation) and
      `Upekkha` (OADS-facing waiver management) are roadmap names for work that does not exist
      yet and should stay internal until the quarter before each ships.
- [ ] **Reconcile the live Demo API version.** `DATABASE_ENVIRONMENTS.md` last recorded the deployed
      API at 1.2.8 while the packaged client is 1.2.17. Deploy and verify a matched pair before
      collecting final company-demo evidence, then update the release-history note.

### Organization identity and the Karuna handoff (2026-08-15)

Design recorded in `DECISIONS.md`, "Provider directory entries are local knowledge about a shared
organization". The identifier capture landed on 2026-08-15 because it is the only part that cannot
be added retroactively. Everything below waits for Karuna.

- [x] **Capture durable identifiers on provider directory entries.** `Provider.Npi` and
      `Provider.MaineCareProviderId`, NPI check-digit validated, unique per agency via filtered
      indexes, enforced in both the API and the transitional local service.
- [ ] **Introduce the Organization registry.** Platform-wide canonical identity — legal name and
      external identifiers only. Add `Provider.OrganizationId` as a nullable link in the same
      migration. Deliberately not created yet: do not add a column pointing at a table that
      does not exist.
- [ ] **Build the match-and-link flow for onboarding.** Exact match on identifier first, then a
      reviewed candidate list for name/address near-matches. Linking must never merge or delete a
      directory entry, and must never repoint an existing foreign key.
- [ ] **Model `AgencyRelationship`.** Which case-management agencies have an active passthrough
      relationship with which organization. Without it, an organization going live would appear in
      every agency's passthrough picker — both wrong billing options and a cross-tenant
      disclosure.
- [ ] **Add the published passthrough contact set,** maintained by the organization's own tenant.
      An explicit outward-facing payload, never a projection of internal contact records. Validate
      it for completeness the same way the local form is validated: one bad publish would degrade
      every linked agency's billing contacts at once.
- [ ] **Add contact resolution with local override.** Published wins by default once linked; local
      values are demoted rather than deleted so an agency can re-assert its own named contact in
      one click.
- [ ] **Notify and audit the swap.** A notice to each affected agency when it lands, a quiet
      indication at point of use, and audit events on both sides — these contacts feed a financial
      document.
- [ ] **Flag stale drafts rather than re-snapshotting.** An AT request drafted before a swap and
      submitted after must report which vendor fields changed and let the user decide, following
      the note conflict-reconcile idiom. Never silently rewrite a financial document.
- [ ] **Consider backfilling identifiers for existing directory entries.** Rows created before
      2026-08-15 have none. A one-time prompt when a provider is next edited would close the gap
      without a bulk data exercise.

## Note pipeline — outstanding after the 2026-08-17 review

- [ ] **Give an approved note an amendment path.** Approved is terminal for every actor, so a
      supervisor who approves in error has no remedy even before a claim line exists. The right
      shape is an immutable approved version plus a linked amending note, not an un-approve that
      rewrites the record. Touches the claim-line linkage and the 837P path, which is why it was
      not folded into the workflow-table work. See `DECISIONS.md`.
- [ ] **Audit the abandonment sweep.** Neither `NoteService.UpdateAbandonedNotesAsync` nor
      `POST /notes/abandon-overdue` records an audit event, so a status change the system makes on
      its own is the one transition with no trail. Bulk writes need a summary event rather than
      one per note.
- [ ] **Make the overdue sweep respect the concurrency token.** The API route uses
      `ExecuteUpdateAsync`, which bypasses `Revision`, so a note being edited at that moment can be
      abandoned underneath its author. Bounded today by the `Pending`-only filter, but it is the
      one write in the pipeline that ignores optimistic concurrency.
- [ ] **Close the create-time gap on overlapping service time.** `AddNoteAsync` checks for an
      overlapping block and then saves in a separate step, with no transaction spanning the two.
      Two concurrent saves can both pass the check. The unique index that protects claim lines has
      no equivalent here; a database-level exclusion constraint would be the durable fix.
- [ ] **Give the local tenant rules one owner.** `Data/LocalTenantAccess.cs` mirrors
      `Sati.Api.Security.TenantAccess` by hand because the two query different entity types against
      different contexts. Two hand-written copies of a scope rule is exactly what the platform rule
      about single ownership warns against; it is tolerable only while the desktop keeps a local
      EF path at all.
- [ ] **Decide whether the desktop may assume Eastern time.** The desktop review path uses
      `DateTime.Today` where the API now uses the agency clock. Correct on a Maine workstation,
      wrong anywhere else.

## Hosted Demo migration deployment — found live on 2026-08-17

- [x] **Detect a database behind the model.** `SchemaDriftHealthCheck` compares the API model's
      tables and columns against the database and fails `/health/ready` naming what is missing,
      instead of letting the gap surface as a 500 from whichever feature touches the new column
      first.
- [ ] **Decide how SatiDemo actually receives migrations.** Nothing advances it today. The desktop
      runs `Database.Migrate()` (`App.xaml.cs:238`) but only when connected straight to SQL, and in
      Demo it goes through the API over HTTP; `Sati.Api` never migrates; `scripts/Publish-Demo.ps1`
      has no database step. A release that adds a column therefore ships code the database cannot
      satisfy. On 2026-08-17 that took out `GET /providers` — and with it AT request creation —
      and `POST /incidents`, so the telemetry channel could not report the outage either.
      The detector above makes this visible; it does not fix it.
- [ ] **Reconcile migration history with reality on the long-lived databases.** SatiDemo and
      SatiProduction have acquired columns outside the chain, so `__EFMigrationsHistory` and the
      actual schema disagree in both directions. EF's idempotent script guards only on history and
      fails with SQL 2705 on a column that exists without its history row. Until the two are
      reconciled, applying migrations to those databases needs existence-guarded scripts rather
      than the generated one.

## Journal reminders — outstanding after the 2026-08-18 change

- [x] **Deploy the API so the reminder route exists.** Done by the 1.2.21 deployment and confirmed
  2026-08-23 by unauthenticated probe: `people/{id}/journal/entries`, `people/{id}/ssn`,
  `people/{id}/forms.pdf`, and `people/{id}/agency-release.pdf` now all answer 401, where the last
  three answered 404 on 2026-08-19. The original note follows for the record.
  The hosted Demo API was release 1.2.17 on
  2026-08-18, which predates `POST /people/{personId}/journal/entries`. Until it is published from
  `Sati.Api/Properties/PublishProfiles/sati-demo-api-satilogica - Zip Deploy.pubxml`, every Demo
  reminder takes the transitional whole-journal fallback and the client page says so. Verify with an
  unauthenticated POST to that route: 401 means the route is present, 404 means the server is still
  behind.
  **The same deployment now gates four more routes.** Confirmed live on 2026-08-19 by unauthenticated
  probe — `people/{id}/notes` and `people/{id}/contacts` answer 401 while `people/{id}/ssn`,
  `people/{id}/forms.pdf`, and `people/{id}/agency-release.pdf` answer 404. In the DHHS wizard that
  404 surfaces as "the record was not found or is outside your caseload", which points the case
  manager at a caseload problem that does not exist. This is the third time a behind-server has been
  diagnosed as something else; see the startup version-comparison item below, which would replace
  per-route handling with one check.
- [x] **Remove the whole-journal fallback once nothing predates the route.** Done 2026-08-23 once
  the probe above showed the route live everywhere it needed to be. The 404 `catch`,
  `JournalReminderResult.UsedLegacyJournalWrite`, the client-page warning band text it drove, and
  `Sati.Tests/JournalReminderFallbackTests.cs` are removed; `ApplyExternalJournal` now clears a
  stale warning instead of setting one. See `DECISIONS.md`.
- [x] **Detect a behind-server generally.** Built 2026-08-19 and extended 2026-08-22. **Comparing the release number would
  not have worked** — on the day this was written the hosted API and the client both reported
  1.2.17 while the server was missing five routes, because a release is numbered when it is cut and
  not when a route is added. The comparison is therefore over the route and persistence-contract
  manifest: `ApiSurface` in `Sati.Contracts.V1` holds the generated route list, named contract-shape
  revisions, and a fingerprint of both,
  `/health/version` reports the fingerprint (never the list — that is a map of the attack surface),
  and `IApiCompatibilityService` compares once at sign-in and raises a "SERVER OUT OF DATE" banner
  with the cause. `ApiSurfaceTests` fails the build if the route manifest drifts from the API's real
  endpoint table and proves that a contract-shape change alters the fingerprint. This prevents a
  newer client from silently sending profile fields an older server would ignore. The check never throws and never blocks
  sign-in: an unreachable server is a network problem other screens report better.
- [ ] **Audit other `GetAsync<T>` calls for legitimately empty bodies.** `GetJournalAsync` threw on
  any client whose journal was never written, because `GetAsync<string?>` treats a null result as an
  empty response. Fixed there with `GetStringOrNullAsync`; other nullable-scalar routes may carry
  the same latent fault.
- [x] **Distinguish journal reminders from dated calendar reminders.** An undated Reminder remains a
  stamped journal entry and is not duplicated. A future-dated Reminder is stored once as a
  non-billable Scheduled note so the calendar, note history, and upcoming-event views can find it.

## DHHS form fill — remaining verification and profile work

The official-form filler, encrypted cloud SSN envelope, audited API routes, local
and cloud service implementations, migrations, and desktop workflow are now built.
See `DECISIONS.md`, "An official DHHS form is filled, never redrawn" and "An SSN is
cloud-only".

- **Profile gap the forms exposed.** `Person` has no email. The Release form's
  optional combined telephone/email box receives the phone number when present;
  email remains blank for hand-completion until the profile has an appropriate
  email field.
- **Field rendering is unverified against DHHS.** The byte comparison proves the
  page is unchanged; it does not prove a DHHS intake worker accepts the field
  appearance. The forms set `/NeedAppearances` so the viewer rebuilds appearance
  streams. Worth one printed submission before this is used in earnest.

## Production behind the API, without losing local Production (2026-08-18)

Direction set by Josh: "move local Production behind the API" means **adding
API-backed access to the Production database while preserving both operating
modes**, not retiring the local one. The desktop must continue to support Demo and
Production as explicitly selected data sources.

Requirements for that work:

- Explicit environment selection, extending the existing bootstrap chooser and the
  validated hard-coded environment mapping in `DATABASE_ENVIRONMENTS.md`.
- Separate credentials, configuration, service identities, and Key Vault keys per
  environment.
- Authorization checks on the API-backed Production path equal to Demo's —
  `TenantAccess` plus `ValidatedActorFilter`, not a relaxed variant.
- Conspicuous UI labeling, as the Demo indicator does today, so the operating mode
  is never ambiguous on screen.
- Safeguards against cross-environment reads and writes. `DatabaseIdentityValidator`
  and `dbo.SatiDatabaseIdentity` already gate this at connection time; per-environment
  Key Vault keys extend it to the data itself, since one environment's ciphertext is
  inert against the other's vault.

**Where plaintext SSNs exist, by mode** (see `DECISIONS.md`, "An SSN is cloud-only"):

| Mode | Plaintext SSN |
|---|---|
| Demo (API-backed) | Only in API process memory during entry and form fill, and inside the generated PDF. Demo data is synthetic. |
| Production via local EF (today) | None. SSN is cloud-only; the column is never populated or read on this path. |
| Production via API (planned) | Same as Demo: API process memory, and the generated PDF. |

Never at rest outside ciphertext, never in a DTO, an EF entity exposed to a client,
a log, telemetry, an exception, a cache, or a backup.

**Document generation splits by where the protected data is, not by document.**
Superseded by Josh's 2026-08-18 direction that local Production keep generation on
the workstation: `DhhsFormFiller` lives in the shared `Sati.Forms` library and runs
in whichever process holds the data. On the cloud path it runs server-side, because
that is where the decryptable SSN is and it must not travel. On the local path it
runs on the workstation with no network and no SSN at all. One implementation of the
stamping, two callers — a second copy would be the duplication CLAUDE.md forbids.
The AT request PDF carries no protected field and stays where it is.

**The generated PDF is itself plaintext.** The form has an SSN box, so the finished
document contains the number in the clear by design. Encryption protects the
database; it cannot protect the artifact the fill produces. Once a case manager
saves, prints, emails, or uploads that PDF, the number is loose in whatever handled
it. The controls that matter there are the BitLocker requirement in
`OPERATIONS.md`, agency-approved storage locations, and the audit event on
generation — not anything in the crypto.

### DHHS form work status

Everything below the UI is built and tested as of 2026-08-18: the encrypted columns
and the `AddEncryptedSsn` migration, the audited `POST /people/{personId}/forms.pdf`,
the SSN read and write routes, log-redaction enforcement, and both
`IDhhsFormService` implementations.

- **Migration applied 2026-08-19.** `AddEncryptedSsn` and the remaining queued schema
  migrations were applied to `SatiDemo`; local `SatiProduction` was already current.
  Controlled migration deployment remains manual; see the hosted-Demo item above.
- **The Demo Key Vault key was provisioned 2026-08-20.** The Demo API now receives
  the versionless `Ssn__KeyUri` for `ssn-demo` in the purge-protected
  `sati-demo-kv-satilogica` vault. Its system-assigned identity has only `wrapKey`
  and `unwrapKey`. Production still requires a separate key before the Production
  API path stores SSNs; never reuse the Demo vault or key there.
- **Synthetic Demo SSNs were seeded 2026-08-20.** The Admin-only operational route
  encrypted deterministic synthetic values for all 177 agency People through the
  Demo Key Vault and recorded `person.ssn-updated` per Person. The route remains
  effective only when startup validates exactly `SatiDemo` / `Demo`; ordinary SSN
  routes remain own-caseload only. `scripts/Seed-DemoSsns.ps1` is the repeatable
  wrapper for a future approved Demo reset. Do not write SSN columns directly in
  Azure SQL.
- **Demo users and agencies carry no synthetic representative information,** so every
  Demo fill currently reports the representative boxes as needing hand-completion.
- **Desktop UI completed 2026-08-19.** The selected consumer now has a `DHHS Forms`
  workspace covering both official forms, grouped consumer-directed selections,
  masked SSN status/update on the cloud path, local-Production explanation, PDF save,
  missing-field warnings, automation names, keyboard reachability, live status text,
  and selection clearing when the consumer changes. Signatures and signing dates are
  intentionally left to the fillable PDF rather than treated as ordinary data entry.
- **Local Production always prints a blank SSN box,** because SSNs are cloud-only and
  that path has no key. Reported through `DhhsFormResult.BlankFields` rather than
  left for the case manager to notice on paper. Confirmed with Josh 2026-08-18.
- **`SsnMask.IsWellFormed` is a shape check, not proof of ownership.** Nothing local
  can establish that a number belongs to the consumer.

## Agency releases and transportation documents (2026-08-19)

- **Agency release completed.** The selected-consumer workspace, shared validation contract,
  local/cloud service seam, audited no-store API route, two-page Sati PDF, staff-attestation
  confirmation, and automated desktop/API tests are in place. Consumer signatures are deliberately
  left for the document rather than represented as ordinary data entry.
- **Transportation source forms analyzed; implementation is next.** The ModivCare Standing Order
  and LogistiCare Single Trip PDFs supplied on 2026-08-19 are both one-page, flat PDFs with zero
  AcroForm fields or widgets. They cannot use the DHHS field-filling path.
- **Preserve the official/vendor page.** Build a coordinate-overlay definition for each exact source
  revision and prove that the original page content stream remains unchanged, rather than redrawing
  a lookalike. Put both behind an `ITransportationFormService` local/cloud seam, derive consumer,
  agency, and logged-in requestor identity on the authoritative side, and audit generation. Before
  operational use, print one sample of each and confirm acceptance plus the required MaineCare
  billing-section interpretation with the transportation broker.

## Local schema updates — outstanding after the 2026-08-19 safety net

See `DECISIONS.md`, "The desktop backs up before it migrates a database with records
in it".

- **No audit event for a schema change.** The startup migration runs before sign-in,
  so there is no actor and `LocalAuditTrail.Record` cannot be called. The backup file
  is the only trace. Options: attribute to a system actor, or defer the event until
  the first sign-in after an applied migration and record it then.
- **Backups are never pruned.** Every migration on a database with records writes a
  new `.bak` under `%LOCALAPPDATA%\Sati\schema-backups` and nothing removes old ones.
  Fine at the current rate; wrong once several people are running it for a year.
- **The backup is not verified.** `BACKUP DATABASE` returning without error is taken
  as success. A `RESTORE VERIFYONLY` would prove it is readable before the migration
  proceeds, which is the entire point of taking it.
- **Untested against a real diverged database.** The diverged-history path is covered
  by a fake that throws the right exception. Nothing has exercised it against an
  actual database whose `__EFMigrationsHistory` disagrees with its schema — and the
  other Windows login's `SatiProduction` is the most likely place that is true.

## SSN panel — outstanding after the 2026-08-19 profile work

- **`DhhsFormsViewModel` still has its own SSN code.** `SsnPanelViewModel` is now the
  shared owner and the consumer profile uses it, but the forms workspace was not
  refactored onto it in the same pass. Two implementations of "how do we show and
  store an SSN" is the duplication this class was created to remove; finish the move
  and delete the older copy. The forms workspace's `SsnStorageExplanation` is already
  stale — it still says local Production does not store numbers.
- **A revealed number does not time out.** It clears when the consumer changes, when
  Hide is pressed, and on any failure, but a panel left open on a locked-away
  workstation keeps showing it. A short auto-hide would close that.
- **The reveal is not rate limited.** Nothing stops a bulk read of every consumer's
  number one profile at a time. Each read is audited, so it is visible after the
  fact, but nothing makes it slow or noisy while it happens.

## Brochure source pipeline — outstanding after the 2026-08-22 recovery

The brochure now builds from `marketing/brochure/brochure.html`; see `DECISIONS.md`. What the
recovery did not settle:

- The build is verified by eye. There is no check that a slide's copy still fits its panel, and
  SVG text does not wrap, so a lengthened line silently overruns. A width assertion per text run
  would catch it.
- `scripts/build-brochure.ps1` depends on a Chromium browser being installed and on Segoe UI and
  Georgia being present. Both hold on the current build machine and neither is asserted.
- The remaining ten slides still use the original screenshot crops, several of which are stale
  relative to release 1.2.20. Reshooting them is separate work.
- The recovered source reproduces the original slide 1 layout apart from the leaf. Nothing has
  been reviewed for message, ordering, or claim accuracy, and `REGULATORY_CONCERNS.md` has not
  been applied to the brochure copy.

The pre-recovery ReportLab PDF is deliberately not retained. The HTML source is the baseline;
there is no earlier version to fall back to, and none is wanted.

## Brochure restructure — outstanding after the 2026-08-22 rewrite

The deck is 14 slides. The intended five movements were interrogative intro (1-3), case-manager workflow
(4-6), billing gates (7-9), admin, security and platform (10-12), and Carika plus direction
(13-14). Every headline is a question; slide 14 answers them. Outstanding:

- Slides 11, 12 and 13 carry dashed placeholders, not artwork. Slide 11 wants the environment
  chooser or the Demo-indicated sign-in, slide 12 wants an architecture diagram, slide 13 wants
  Carika showing a profile beside a transcript awaiting review. Each label says what to supply.
- Slide 14 now carries the roadmap and the close on one page. If the closer needs room to
  breathe, split it into a fifteenth slide.
- The layout guard in `brochure.html` (`checkLayout()`, auto-run into the browser console) is
  not enforced by `scripts/build-brochure.ps1`. A silent overrun would still ship.
- Slide 13's claims are scoped to the current Carika slice: profile display and note drafting,
  imported audio rather than live capture, separately provisioned models. Re-check that slide
  against `Carika/README.md` whenever the slice moves.
- The interrogative pass rewrote every headline. Copy has not been reviewed against
  `REGULATORY_CONCERNS.md`, and slide 9's "lost units were lost visits" asserts a relationship
  between billing data and client contact that nothing in the deck substantiates.

## Brochure slide order — parked 2026-08-23

Guided forms was moved from position 5 to position 10 and everything between shifted up one. The
current order is: cover, day in view, consumer record, note capture, review tracking, supervisor
workflow, billing gates, productivity, audit and administration, guided forms, security, platform,
Carika, close.

This crosses two of the movements the deck was structured around. Guided forms is case-manager
work sitting inside the admin, security and platform run, and the "do the work without the detour"
construction introduced on slide 4 no longer has its two intended partners adjacent to it. The
move was made deliberately and marked as temporary; either the movements or the frame needs
revisiting before the deck is final.

`checkLayout()` in `brochure.html` now also verifies that each slide's position, `data-slide`
attribute and footer number agree, and that element ids are unique across slides.

## Notes page consolidation — outstanding after the 2026-08-23 change

The notes log's Note Detail panel is gone; the shared entry panel now shows a selected note in a
locked View Note mode with a padlock toggle into Edit Note, and filters moved to a band above the
grid. Left open:

- **A note is only checked for staleness when it is unlocked.** `VerifyLoadedNoteIsCurrentAsync`
  covers the case that matters — finding out before editing rather than after — but a note left
  open in View Note for an hour still shows the copy it was loaded with, and nothing announces a
  supervisor's return while it sits there. Deliberate: polling every open panel to catch an
  uncommon event was rejected (see `DECISIONS.md`). If notes start being changed underneath
  case managers often enough to matter, the next step is a push or a refresh-on-window-activation,
  not a timer.

- **The panel has never been opened in a running client since the change.** `NotePanelRenderTests`
  now loads the real views against the real resource dictionary and asserts the resulting element
  state, which covers the parts a human would otherwise have had to check — but not how any of it
  looks. The filter band's `WrapPanel` reflow points and the note panel's column width in
  particular are guesses that only a person at the screen can judge.

Closed since that entry was written:

- ~~Nothing warns when the note being viewed has since changed on the server.~~ Unlocking now
  re-reads the note and compares `Revision`, behind the unlock so the panel never freezes on it.
  An untouched panel reloads to the current version; a panel with unsaved typing is warned and left
  alone; a removed note and a failed read each say so. The save-time concurrency check is unchanged
  and still authoritative.
- ~~No entry point back to a blank New Note from the dashboard.~~ `StartNewNoteCommand` on the
  module is offered as an always-visible New Note button in the panel header and as Escape, bound
  both on the module and on each host page. It keeps the selected client, which matters most on the
  dashboard, where that property scopes the whole page. The notes log's Deselect Note button was
  removed as a leftover of the two-panel design.
- ~~The dashboard host does not ask before replacing a draft.~~ Both hosts now route their
  double-click through `NoteEntryViewModel.OpenForEdit`, which owns the unlock-or-load-or-ask
  decision. Neither host repeats it, so they cannot drift apart again.
- ~~Layout is verified structurally, not visually.~~ `NotePanelRenderTests` loads `NotesLogView`
  and `NoteEntryView` on a real STA thread with the application's resources and asserts runtime
  grid placement, that a locked narrative is `IsReadOnly` while staying enabled and focusable,
  that pickers and radio buttons are disabled, that the save button is collapsed, and that the
  attendee checkboxes inside the `ItemsControl` lock too. Each assertion was confirmed to fail
  against a deliberately broken view.
- ~~Notes filter inputs relied on mismatched implicit heights and margins.~~ ComboBoxes, search,
  date pickers, the units summary, and attention actions now share an explicit 36-pixel height;
  the WPF render test measures their actual heights and row baselines at runtime.

### One WPF Application per test process

`WpfUiHarness` now owns the assembly's only `Application` and the STA thread it runs on. WPF's
one-per-AppDomain flag is never cleared — not even by `Application.Shutdown()` — so a second
creator does not merely conflict, it fails permanently, and *which* test fails depends on run
order. `StabilizationTests.ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread` used to
build its own; it now borrows the harness and installs its host through `RunWithHost`. Any future
test that needs a real view must go through the harness rather than constructing an `Application`.

## Provider hierarchy and consumer provider list — designed 2026-08-28

Design recorded in `DECISIONS.md`: "Provider affiliation is one parent link, not three typed tiers"
and "A consumer's provider list stores the link, never the resolved chain". Nothing below is built.

This supersedes the older **Client↔provider association** item from the 2026-08-07 AT session —
that link is slice 2 here, and is deliberately not medical-only.

### Slice 1 — Directory hierarchy ✅ (2026-08-28)

- [x] Add `Provider.MedicalKind` (`Individual | Practice | Network`, nullable; required when
      `Type == Healthcare`) and `Provider.ParentProviderId` self-reference. `OnDelete(Restrict)`
      plus an explicit refusal in both services that names the affiliated entries, rather than
      `SetNull` silently promoting a subtree to top level.
- [x] Add `ProviderAffiliation` to `Sati.Contracts.V1` as the single owner of the tier rule, the
      ancestor-loop rejection, the depth bound (`MaxDepth = 10`), the chain resolution both clients
      render, the parent-picker filter, and the delete-refusal text.
- [x] Migration `20260828180603_AddProviderAffiliation`. Both columns nullable with no backfill:
      every existing row is legitimately unaffiliated, and guessing a tier from a name is the fuzzy
      matching the durable identifiers exist to avoid.
- [x] `ProviderEditorViewModel`: designation combo, parent picker filtered to legal parents only,
      affiliation revealed only for healthcare entries, a derived read-only chain, and an
      explanation when no legal parent exists. Changing tier or leaving medical clears a selection
      that is no longer legal instead of leaving it to fail at save.
- [x] `ProviderDto` / `SaveProviderRequest` gain optional `MedicalKind` and `ParentProviderId`,
      following the optional-parameter pattern `Npi` already uses. API validates the same rule
      through the same contracts type; `ProviderDto.Type` is unchanged.
- [x] `ProvidersViewModel` gained a `SaveError` surface. The directory's refusals — duplicate
      identifier, illegal affiliation, delete with entries beneath — previously had nowhere to go
      and were thrown into an unobserved task. A refused save keeps the editor open with the
      entered values intact.
- [x] Tests: 31 rule, 9 local-service, 13 view-model, 13 API. The cross-agency, loop, and
      delete-guard tests were each confirmed to fail against the guard removed, including one
      view-model test that was rewritten after it turned out to be passing on the tier rule rather
      than the loop check it claimed to cover.

**Outstanding from this slice**

- [ ] `ProvidersView` has no *structural* render test. `StabilizationTests`'
      `ParameterlessFeatureViewsCanOpenRenderAndCloseOnAnStaThread` already loads, measures, and
      arranges it, so the XAML is known to parse and its resources to resolve — but it runs with no
      DataContext, so no binding path is exercised. `NotePanelRenderTests` is the pattern for the
      affiliation reveal, the filtered picker, and the error banner.
- [ ] A long-lived database whose `__EFMigrationsHistory` has diverged needs the same treatment
      `Apply-ProviderDurableIdentifiersMigration.ps1` gave the identifier columns. The desktop
      startup path backs up and migrates normally; only a diverged database needs the runner.
- [ ] A clinician's affiliation is not versioned. Moving a physician between practices rewrites
      what every consumer profile displays, with no record of the previous affiliation. Correct for
      live profile data, and the reason documents must snapshot in slice 4 — but if "who was her
      practice in March" is ever asked of the directory itself, this is where it gets answered.

### Slice 2 — Consumer provider list ✅ (2026-08-28)

- [x] `PersonProvider` model: `ProviderId`, role, `IsPrimaryCare`, start/end dates, release-on-file,
      display order. No cap. **No active flag** — `EndDate` is the only fact that says a link is
      current; see `DECISIONS.md`. A free-text note was deliberately deferred rather than dropped.
- [x] At-most-one current primary care provider per consumer, and one current link per provider —
      both in `ConsumerProviderRules`, both backed by filtered unique indexes, neither a UI
      convention. Both filter on `EndDate IS NULL`, so a consumer may return to a provider they left.
- [x] `IConsumerProviderService` with the transitional local EF implementation and a `Cloud*` HTTP
      one. Four routes gated through `TenantAccess.OwnsPersonAsync`, inventoried in
      `API_AUTHORIZATION.md`, declared in `ApiSurface`.
- [x] The interface takes `personId` alongside `linkId` on end and remove. Reading the consumer off
      the row would let a caller-supplied link id select the scope it is then validated against.
- [x] Profile UI: picker leading with individuals, the selected provider's chain shown before
      committing, derived practice and network read-only on each row, primary care pinned first,
      past providers collapsed behind a disclosure.
- [x] Accessibility — automation names on every repeater row carrying provider, role, and status;
      the past-provider disclosure is a keyboard-reachable `Expander`; "Ended 4 Mar 2026" is text,
      so status never depends on noticing a shade of grey.
- [x] The panel is its own `ConsumerProvidersView`, not markup inside `ClientsView`. It binds only
      to `ConsumerProvidersViewModel`, and a control that loads on its own can be asserted on its
      own — which is what made the per-row command bindings provable rather than assumed.
- [x] Tests: 14 rules, 13 local-service, 13 view-model, 5 WPF render, 11 API. The cross-agency,
      link-scope, and primary-care guards were each confirmed to fail with the guard removed, as
      was the row command binding. The stale-load test was rewritten after the first version could
      have passed whenever the newer load happened to finish second, which would have proved
      nothing about the request tracker.
- [x] A directory entry on any consumer record cannot be deleted — found by a test that failed on
      its first run, because only the foreign key was stopping it. The refusal carries a count and
      never consumer names.

**Outstanding from this slice**

- [x] **The Admin test-data deletion command explicitly handles `PersonProviders`.** Both service
      paths delete and count the rows, and the result contract and PHI-minimized audit metadata
      report that count instead of relying on an unreported cascade.
- [ ] **No audit event on a consumer provider change.** Matches `PersonContact`, which also records
      none, but removal is the one operation here that destroys a record and is the obvious first
      candidate for an audited profile-child event.
- [ ] **The panel is render-tested; its host is not.** `ConsumerProvidersViewRenderTests` loads
      `ConsumerProvidersView` with a DataContext and asserts the row commands, the disabled Add
      button, the derived affiliation being text rather than an input, the collapsed disclosure,
      and the assertive live region. What stays unverified is that `ClientsView` hands it the
      right DataContext — the smoke test loads `ClientsView` without one.
- [ ] **`SortOrder` has no interface.** The column, the rule, and the ordering all exist and are
      tested; nothing yet lets a case manager reorder the list, so every row is added at the end.

### Slice 3 — Reconcile the superseded fields ✅ (2026-08-28)

- [x] **No backfill.** The plan said "backfill by name match"; that became a per-consumer linking
      prompt instead. A bulk write across live consumer medical records should not run unreviewed,
      and the failure mode is asymmetric — unlinked is visibly unfinished, wrong looks finished.
      See `DECISIONS.md`, "The legacy provider fields are linked by hand, never backfilled".
- [x] `LegacyProviderLinking` in `Sati.Contracts.V1`: exact trimmed case-insensitive matching only,
      an explicit `Ambiguous` outcome that refuses to pick between duplicate names, and guidance
      text that names the next action for each outcome.
- [x] The provider panel offers the link when free text names a primary care provider and no
      current link says the same; one click creates the `PersonProvider` row with `IsPrimaryCare`
      set and no invented start date. Where the typed healthcare system disagrees with the derived
      network, the panel says so rather than preferring either.
- [x] **No schema change was needed.** The target of `PrimaryCareProvider` is a `PersonProvider`
      row, and of `HealthcareSystemName` the derived network — a column for either would have been
      the fourth copy this work exists to remove. Both legacy strings are kept and never cleared.
- [x] `PersonContactKind.HealthcareProvider` redefined as a human contact *at* a provider rather
      than retired; existing rows are real people and the directory does not answer "who do I
      phone at that office".
- [x] Tests: 15 matcher, 8 panel-linking, 3 render. The near-miss cases a fuzzy matcher would get
      wrong are named explicitly, and one test asserts that merely opening a consumer writes
      nothing.

**Outstanding from this slice**

- [ ] **Retire the Settings-managed `HealthcareSystems` JSON list.** Deliberately still in place:
      the field it feeds is still the only record for consumers nobody has linked yet, so it cannot
      go until the reconciliation is finished. Retiring it early would strand those consumers.
- [ ] **No agency-wide view of what is still unlinked.** The per-consumer prompt is enough to
      finish the work but not to plan it; a supervisor cannot see how much is left.
- [ ] **Nothing marks a consumer as deliberately not linkable.** A consumer whose free text names
      somebody who will never be in the directory keeps its prompt forever, and there is no way to
      say "reviewed, leaving as text".

### Slice 4 — Documents ✅ (2026-08-28)

- [x] `AssessmentNeed` gained `ProviderPracticeSnapshot` and `ProviderNetworkSnapshot` beside the
      existing name and id, frozen by `ProviderAffiliation.Snapshot` at the moment of choosing.
      The document is stored as JSON, so this needed no migration and older documents deserialize
      unchanged.
- [x] The free-text provider box on a need is replaced by a picker over the consumer's **current**
      linked providers, closing the deferred "replace the temporary provider-name entry" item.
      A need whose provider was typed before the directory, or who has since left the consumer's
      list, still renders exactly what it recorded.
- [x] The Person-Centered Plan quotes the frozen triple rather than the bare name.
- [x] Tests: 9 covering the snapshot, including that an already-taken snapshot does not move when
      the directory does, that a hospitalist with no practice does not render a dangling separator,
      and that a need written before this change still reads correctly.
- [x] `StabilizationTests`' feature-view smoke host gained the two new service registrations, so
      the assessment workspace stays covered rather than being skipped.

**Outstanding from this slice**

- [ ] **Fixed-row forms still do not take N providers in the case manager's order.** The rule and
      the ordering exist in `ConsumerProviderRules.OrderForDisplay`, and `SortOrder` is stored, but
      no document currently renders a provider table — so there is nothing yet to apply it to. It
      lands with the first form that has provider rows.
- [ ] **The assessment workspace resolves services in its view constructor.** Pre-existing
      service-locator usage that this slice added two more entries to, and the reason the smoke
      test needs a host at all. Worth moving to constructor injection when that view is next
      touched properly.
- [ ] **No test covers the assessment need picker end to end.** The snapshot function and the model
      are tested; the picker binding and the freeze-on-select path are not.

### Prerequisite promoted by this design

- [x] **Provider directory governance.** Previously deferred as tidiness. Once entries have parents,
      a duplicate network row splits the tree with no view that reveals it, so admin curation and a
      merge path for duplicates become a precondition for slice 3 rather than later polish. The
      role split, same-name warning, named contacts, Admin merge, audit event, UI, and focused tests
      are now in place.

### Open, deliberately not designed yet

- [ ] A clinician affiliated with two practices — hospital privileges plus a private practice. A
      single parent says one. Accepted for now; if it becomes real, the location belongs on the
      `PersonProvider` link rather than as a second parent, which would reintroduce the
      disagreement the single parent exists to prevent.
- [ ] Waiver-side tier vocabulary. The parent link, cycle guard, and resolution walk are already
      general; only the `Individual | Practice | Network` naming is medical.

## Provider directory as a shared agency rolodex — assessed 2026-08-28

Asked whether providers created by one case manager become an agency-wide pool everyone draws from.
**They already do.** `Provider.AgencyId` scopes every directory entry to the agency, `GetAllAsync`
returns the whole agency's directory, and both the Providers tab and the consumer profile's picker
read from it. Nothing is per-user. What is missing is not sharing — it is the governance a shared
pool needs to stay usable.

- [x] **Writes now use the same role policy in both paths.** Case managers, supervisors, directors,
      and Admins may add/correct shared directory entries; deletion and merge are Admin-only and
      are enforced below the interface.
- [x] **Duplicate detection warns on the way in.** Uniqueness is enforced only on `Npi` and
      `MaineCareProviderId`, both optional and both usually absent when an entry is created from a
      phone call. Two case managers each typing "MaineHealth" get two rows. Since the affiliation
      work, that no longer merely clutters the list — it splits the hierarchy, with half the
      practices hanging off each row. The editor now shows a normalized same-name warning without
      blocking legitimate organizations that happen to share a name.
- [x] **Admin merge is implemented.** It repoints
      `ParentProviderId`, `PersonProvider.ProviderId`, `Settings.DefaultPassthroughProviderId`, and
      `AssessmentNeed.ProviderId` — except the last, which must **not** move: a document froze that
      entry deliberately. It also moves named contacts, refuses ambiguous live consumer-link
      conflicts, runs transactionally, and records a PHI-minimized `provider.merged` event.
- [x] **The directory has curation tools.** The shared Providers interface now includes the warning,
      named-contact editor, and Admin-only merge confirmation workflow.
- [ ] **Cross-agency sharing is a different problem and already designed.** Each agency holding its
      own Spurwink row is correct, not redundant — see `DECISIONS.md`, "Provider directory entries
      are local knowledge about a shared organization". A directory shared *between* agencies waits
      on the canonical Organization registry, and reconciliation there links rather than swaps.

## HANDOFF RESOLVED — provider directory curation completed 2026-08-28

The earlier handoff below was completed in the same working branch. Provider contacts and the
test-consumer marker have migrations generated but deliberately not applied by ordinary feature
work; deployment and migration remain release-playbook actions.

### What is finished and working

- **Role split.** `ProviderDirectoryRules.CanCreateOrEdit` (CaseManager/Supervisor/Director/Admin)
  and `CanDeleteOrMerge` (Admin only), applied in *both* paths. This closed the live inconsistency
  where local Production let any case manager delete while the API returned 403 on create.
- **Same-name detection.** `ProviderDirectoryRules.SameNameWarning` — normalized (trim, collapse
  whitespace, case-insensitive), warns and never blocks, because two real organizations can share
  a name.
- **Multiple named contacts.** `ProviderContact` model, EF config both sides, migration
  `20260828193518_AddProviderContacts`, service methods, four API routes, DTOs, cloud client,
  `ApiSurface` updated and its test passing. Deliberately *separate* from
  `Provider.PrimaryContact`/`Phone`, which stay as the organization's general directory line —
  those are facts about the organization, contacts are facts about people who work there.
- **Merge.** `ProviderDirectoryRules.ValidateMerge` plus implementations in both paths. Moves
  affiliated entries, consumer links, and contacts to the survivor; adopts identifiers and parent
  only where the survivor has none; refuses on conflicting NPI/MaineCare ids, mismatched tiers, and
  loops. **Deliberately does NOT repoint `AssessmentNeed.ProviderId`** — a document froze that
  entry and rewriting it would change what an approved assessment says.
- Three API tests updated to the new policy, and two of the earlier delete-guard tests moved onto
  an Admin session.

### Completion of the handed-off work

1. **UI complete.** The provider editor presents a non-blocking same-name warning, named-contact
   maintenance, and an Admin-only merge review with a destructive confirmation that fails closed.
2. **Tests complete.** Shared rules, both merge paths, contacts, stale selection loads, confirmation,
   authorization/tenancy, frozen assessment references, and rendered WPF controls have focused
   coverage.
3. **Merge audit complete.** Both persistence paths retain `provider.merged` with IDs and counts,
   never consumer names.
4. **Documentation complete.** Architecture, route authorization, audit catalog, and durable design
   decisions describe the implemented boundary.

## Daily sign-in agenda — completed 2026-09-01

- [x] Show a theme-aware, accessible agenda after scratchpad and caseload initialization, including
      overdue forms, upcoming work, a quiet-period Comprehensive Assessment suggestion, and the
      permanent Demo indicator.
- [x] Store the enabled toggle and once-per-day marker locally per environment and Sati user; an
      opted-out or already-shown user reaches no agenda data source.
- [x] Initially keep the feature read-only except for explicit selected-line appends to Today's
      Work, with ordinary navigation to the existing form surface and no compliance transition.
- [x] Replace selected-line appends on 2026-09-05 with structured Scheduled Form notes. The existing
      note lifecycle already owns client linking, type, status, authorization, audit, concurrency,
      and retention, so no second task entity or scratchpad-text parser is needed.
# Safety-plan authoring (2026-09-03)

The shared, versioned Safety Plan structure is implemented in source with a Draft → Ready for review → Approved/Returned workflow and WPF authoring/review controls. Approval requires a non-author supervisor whose actual caseload scope includes the consumer; agency equality alone is not sufficient. Unapproved PDF output is marked draft and cannot satisfy the annual Safety Plan prerequisite. The scaffolded migrations have not been deployed to Demo or Production.

## Ordinary-consumer deletion, archive status, and bulk-import dedupe (2026-09-03)

Full write-up in `DECISIONS.md`, "Ordinary-client deletion within a 20-day window, and a real
(narrow) legal-hold registry." Design in `HANDOFF_CLIENT_DELETION_POLICY.md`, now updated to
match what shipped.

- [x] Bulk Credible import dedupe checks CredibleClientId, then MaineCareId, then normalized
      name+DOB, matching CREDIBLE_IMPORT_DESIGN.md's specified match order — previously
      CredibleClientId only, which is why the workflow demo's re-import created duplicates for
      consumers who predate Credible import.
- [x] Credible import now maps `address1` to `Person.Address` as well as `Person.BillingStreet` —
      previously only the claim-address field was populated, which the demo also surfaced.
- [x] `Person.CreatedAtUtc` (immutable, migrated), `Person.Status` archive field, and their
      exclusion from `GetAllPeopleAsync` and everything generated from it.
- [x] `ConsumerDeletionRules` (window + A1 billing-integrity gate), `AdminService.DeleteConsumerInWindowAsync`
      and the matching API route, itemized audit tombstone with a PHI-exclusion test.
- [x] `PersonStatusRules`, `AdminService.SetPersonStatusAsync` and the matching API route —
      case manager may set NoLongerServed/Deceased on their own caseload, only Admin may set Ghost.
- [x] A real, minimal `ILegalHoldRegistry` (`LocalLegalHoldRegistry` / `ApiLegalHoldRegistry`) over
      a new `LegalHold` table, plus Admin place/release actions and API routes. Deliberately
      narrower than `OPERATIONS.md`'s full record-class/scope hold model — see the two items below.
- [x] Admin dashboard: a typed-name confirmation dialog (`TypedConfirmationDialog`, Confirm
      disabled until the exact consumer name is typed) for rule-3 deletion, alongside the existing
      test-consumer-delete action. Not yet visually exercised in a running app — the ViewModel
      command logic has full test coverage; the XAML has not been rendered/clicked through.
- [ ] **Dual-control legal-hold release.** `OPERATIONS.md`'s legal-hold gate requires a second
      approver to release a hold; the shipped registry is single-admin release. Explicitly scoped
      out at implementation time (Josh's call) rather than an oversight — needs a real design pass
      (who the second approver is, whether it blocks or just double-records) before building.
- [ ] **Legal-hold registry is scoped to gating consumer deletion only.** It does not implement
      `OPERATIONS.md`'s general record-class/scope hold model and does not by itself satisfy that
      gate for any other retention or purge job — those items (line ~2364, ~2653 above) remain open.
- [ ] Show record counts to the Admin *before* confirming rule-3 deletion, per
      HANDOFF_CLIENT_DELETION_POLICY.md's audit section. Shipped without a pre-count preview
      endpoint; the confirmation dialog names the categories of data that will be deleted but not
      exact counts. The success notice after deletion does show exact counts.
- [ ] `address2` is not mapped on Credible import — no fixture or real export confirms Credible's
      actual label text for it, and the mapper's design is to never guess a label.

## Client edit incident follow-up (2026-09-05)

- [ ] Identify the original exception behind local Production support reference 54546DF49635.
      The screenshot alone does not establish whether persistence or the subsequent UI refresh
      threw. Confirmed post-save failures now report saved status correctly. The local incident
      reporter retains a fingerprint but no exception stack; correlate safe diagnostic details
      with the displayed reference as part of the logging work. No Production record was queried
      or changed during this investigation.

## Pending approvals performance and threshold batch action (2026-09-05)

- [x] Load the first 10 review notes from the database, then load further pages on downward scroll
      or Load more. Preserve the case-manager filter and reject obsolete load results.
- [x] Add explicit "Approve all within threshold", default 4 units per note. No approval occurs on
      load, scroll, or threshold edits. Traverse unloaded notes in bounded pages and retain normal
      authorization, compliance, revision, time-conflict and audit checks on each approval.
- [x] Verify pagination after earlier approvals, threshold boundaries, user-triggered behavior,
      and stale-load suppression. Permission, threshold and stale-load tests fail when their
      respective guards are removed. Final targeted suites: 132 API and 52 desktop/domain passed.
      No live approvals or database migration was performed.
- [x] Published the API-only Demo filter hotfix on 2026-09-06. The 8,777,987-byte package has
      SHA-256 `CE17C1B5DD49F29795576B38741FB5758E69FC99D673A2B1CCB78433A40EAC79`;
      OneDeploy deployment `753b493bd57e4d38b17af6714d8b8d19` succeeded. Liveness returned
      `live`, readiness returned `Healthy`, and `/health/version` reported `Sati.Api` 1.2.48 with
      contract revision `4FF4AE13D9DC`. The new protected filter route returned HTTP 401 without
      a login instead of 404, confirming it is hosted. No Production service, database migration,
      firewall rule, or installer was touched.

## Adaptive display modes — implemented 2026-09-05

Full handoff and acceptance criteria: `DISPLAY_MODES_DESIGN.md`. Work Agenda is the default center
workspace. Easy Eyes remains a single personal switch; fitting the layout is automatic.

- [x] Replace one-time physical-resolution selection with the specified finite-viewport policy,
      including scaling, responsive transitions, center-default migration and preference isolation.
- [x] Implement Wide/Balanced/Compact placements, labeled access to every supporting feature,
      existing navigation overflow and a single live agenda host that remains reachable outside Overview.
- [x] Adapt the shared note editor for short windows and add explicit Focus note, preserving
      commands, conditional fields, validation, drafts, caret/selection/undo and keyboard access.
- [x] Add safe empty/loading/error states and explicit scope labels. Keep the existing palette, font
      choices, Easy Eyes and editor text controls.
- [x] Render and inspect the four layout tiers, run responsive/editor/preference tests, and update
      ARCHITECTURE.md with implemented ownership. Full desktop/domain suite: 1,325 passed and one
      optional local-AI competence test skipped. A release run remains separate.
- [ ] If user testing shows a need, add constrained user-resizable pane widths and a reset action.
- [ ] At extremely narrow widths, consider replacing the existing horizontal navigation overflow
      with a labeled selector. All destinations remain keyboard reachable through the current strip.
- [ ] Later, separately consider replacing explicit font sizes with shared typography resources.
      This is not required for the adaptive-layout implementation and must preserve the one-toggle
      Easy Eyes experience if undertaken.

No database change, version bump, deployment, or DATT release action was performed.

## Team chat — reviewed synthetic-data implementation (2026-09-05)

See `TEAM_CHAT_DESIGN.md`, `TEAM_CHAT_REVIEW.md`, `TEAM_CHAT_GUIDE.md` and
`TEAM_CHAT_VALIDATION.md`. The preceding no-database-change statement belongs to adaptive layout.

- [x] Implement the reviewed API-only synthetic chat, explicit consumer-scoped membership,
      durable history, release evidence, retained redaction, session/privacy protections and WPF
      workspace. Full solution: 1,808 passed, zero failed, one optional local-AI test skipped.
- [ ] Repeat dependency advisory lookup before release; the full build could not reach NuGet's
      vulnerability feed, so a fresh package vulnerability assessment was not completed.

- [ ] Resolve existing empty-database migration failures found during the isolated chat rehearsal:
      generated SQL batches fail in `20260419144051_AddNoteApprovalFields` when a just-added Npi
      column is referenced in the same batch; direct EF migration stops in
      `20260828195515_AddTestConsumerMarker` when a conditional statement references absent
      `dbo.SatiDatabaseIdentity`. Prior migrations were not edited in the chat implementation.
      Record full-chain replay evidence before deployment; isolated chat-table success is narrower.

- [ ] Rehearse controlled Demo deployment, compatibility and two-workstation reconnect/access
      removal before enabling hosted synthetic chat. Complete real assistive-technology acceptance.
- [ ] Approve agency HIPAA/Maine applicability, permitted audiences, restricted-record handling,
      business associate responsibilities, training and privacy-incident procedures.
- [ ] Establish chat retention, broader preservation/hold scope and controlled discovery/export
      including hidden originals, misfiled/general-room content and backups. No automated deletion
      until those controls exist and are reviewed.
- [ ] Complete account disablement and immediate session invalidation across the platform.
      Password reset does not revoke every current session; room removal is chat-specific only.
- [ ] Complete approved API-backed Production, runtime append-only SQL permissions, restore
      evidence and routed monitoring before real-client chat is considered.
- [ ] If cross-caseload consumer coordination is needed, design an approved access process;
      do not weaken the existing consumer-access gate to deliver it.
- [ ] Consider private messages, delegated room management, historical-access grants, attachments,
      note promotion, presence and multi-instance latency as separately reviewed additions.

### Electronic signature handoff — implemented synthetic scope and activation work (2026-09-06)

- [x] Shared document-purpose/code/workflow rules; exact complete original freezing; immutable
      signer-specific requests, consent, events, completions and separate derived PDF packages.
- [x] Authenticated staff routes and Annual Documents workspace; public portal with isolated
      identity design, narrow SQL model/grant script, request/session checks, CSRF, secure cookies,
      short leases, code lockout, explicit consent/intent and paper/assistance choices.
- [x] Private Azure storage/key/email adapters, encrypted durable outbox, guarded package/mail
      workers, exact retry identities, and honest suppressed/submitted/provider-result reporting.
- [x] Synthetic SQL migration, role-denial, lock/concurrency and rollback rehearsal; automated
      regression and deliberately removed-guard evidence in `SIGNATURE_PORTAL_VALIDATION.md`.
- [x] Plain-language findings and legal/operating outline in `SIGNATURE_PORTAL_REVIEW.md` and
      `SIGNATURE_PORTAL_GUIDE.md`; original proposal labeled as superseded where it conflicts.
- [ ] Apply a separately authorized controlled migration/deployment. Provision and verify the
      environment marker first. Direct EF migration of a disposable marked database passed all
      96 migrations; this does not repair the older generated-SQL-script Npi batch defect recorded
      during chat work. Do not describe the old script or an unmarked database as validated.
- [ ] Provision separate portal hosting/managed identity/private storage/key rights and exact
      runtime SQL grants. Verify actual deployed identities, network limits and token-free edge
      logs; code and a disposable-role rehearsal do not prove the deployed permissions.
- [ ] Complete approved synthetic mail testing, current DNS/sender verification, bounce/delivery
      event integration and external alert routing. No email was sent during this implementation.
- [ ] Complete hands-on browser/mobile/keyboard/screen-reader acceptance and an accessible PDF
      strategy. Generated evidence is not a tagged PDF; assisted/paper alternatives remain necessary.
      The browser screenshot launch was blocked by automatic approval review; automated DOM and
      HTTP checks are not a substitute for that acceptance work.
- [ ] Obtain reviewed agency release/notice/disclosure wording, authority/identity procedures,
      MaineCare/OADS/state-form decisions where applicable, and special-record/minor/multiple-signer
      handling. Part 2 and Maine confidentiality rules are not satisfied by a generic signature.
- [ ] Complete contracts/BAAs, risk assessment, incident response, tested restore, retention/holds,
      evidence discovery/export, later-copy delivery and training before considering real PHI.
      Signature history is retained and test-consumer deletion is blocked when it exists.
- [ ] Establish an approved API-mediated Production implementation separately. Current Production
      remains hard-disabled; no real-use switch or legal-clearance setting is provided by this build.
