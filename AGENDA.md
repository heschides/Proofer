

# Sati — Refactor Agenda

## Release 1.2.35 — 2026-09-01

Daily sign-in agenda, explicit quarterly-review attestation, human-accepted suggested follow-ups,
successful-save Notes filter clearing, and the fail-closed legal-hold boundary for ordinary-client
deletion. Twenty commits since the 1.2.34 evidence commit `51fd1aa`.

**This is not a schema-changing release.** No migration was added after 1.2.34. The API change adds
a narrow read over the existing Comprehensive Assessment table and does not depend on a new column
or table, so no Demo database migration or temporary SQL firewall rule applies.

### Validation
- [ ] Source release commit created and pushed to `origin/master` without rewriting history.
- [x] Complete Release build passes: 0 errors, 6 warnings (offline NuGet vulnerability feed, the
      existing guarded raw-SQL analyzer warning, and three test-code analyzer/nullability warnings).
- [x] 1,284 tests pass — 978 desktop/domain, 302 API integration, and 4 Carika. One documented
      opt-in local-AI model competence test is skipped because `SATI_RUN_LOCAL_AI_MODEL_EVAL=1`
      is absent.
- [x] 54 focused release regressions pass, including opposite-theme WPF rendering, agenda behavior,
      quarterly attestation, form-date validation, suggested-follow-up acceptance, Notes filter
      clearing/retention, API surface parity, and tenant-scoped assessment reads.

### Deployment and artifact evidence
- [ ] Demo API ZIP built from the pushed source commit; version, contents, bytes, and SHA-256 recorded.
- [ ] Demo API deployment succeeds; live, ready, release 1.2.35, and contract parity verified.
- [ ] Demo and Local 1.2.35 installers built without overwriting an existing artifact.
- [ ] Demo five-launch and Local isolated acceptance gates pass with cleanup verified.
- [ ] Installer bytes and SHA-256 values recorded and published to the two exact distribution folders.
- [ ] Final evidence commit pushed and local `master` confirmed equal to `origin/master`.

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
      `Form.MarkComplete`/`Reset` and `IFormService`.
- [x] Centralize the post-form-change cascade so dashboard flags, the caseload matrix, and
      `UpcomingEvents` refresh after dashboard, task-board, form-note, Clients, and Reviews paths.
- [x] Enforce the non-future completion rule in shared contracts, Local persistence, and the API;
      the API returns a validation problem without changing stored state.
- [x] Add regressions for copy, shared status ownership, explicit late dates, historical billing
      windows, rejected future dates, the no-auto-derive boundary, and all completion cascades.
- [ ] Before release, tell case managers that quarters tracked only as Review items remain open
      attestations and may form an operational backlog. Do not bulk-close them or invent dates.
- [ ] Review the older dashboard and Clients quick-toggle convention that records `DueDate` as an
      on-time completion assumption. It remains unchanged in this repair and is the weaker path.

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

## Future-dated calendar reminders — 2026-08-26

- [x] Convert any future note date to a Scheduled Reminder in one shared contracts rule, repeated
      by the desktop-local and API persistence boundaries.
- [x] Preserve the selected date and narrative while removing service time, minutes, form/visit
      facts, and justification so a reminder cannot drift into review, productivity, or billing.
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
- [ ] Implement a nightly reset job with its own managed identity, validation, and failure alert.
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
- [ ] **Resolve the disabled cycle-form feature switch.** The disabled
      `EnableEnsureCycleFormsOnLoad` path is still awaiting the duplicate-form reconciliation;
      the compiler warning has been removed. Either remove the obsolete path or replace it with a
      deliberate supported configuration after the duplicate-form reconciliation is
      settled.
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
- [ ] **Decide whether Scheduled notes should reserve time softly.** A scheduled note currently
      holds its minutes like any other commitment, so converting a plan into a separate logged
      note reports a conflict. Editing the scheduled note in place avoids it. If case managers
      routinely write a fresh note instead, a "planned" band that warns rather than blocks may fit
      the real workflow better.

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
- [x] Keep the feature read-only except for explicit selected-line appends to Today's Work, with
      ordinary navigation to the existing form surface and no compliance transition.
- [ ] Design a structured daily-task feature only if users need durable completion, assignment,
      linking, or reporting. It must start with its own entity, authorization, audit, concurrency,
      retention, and API contract; do not infer structure by parsing scratchpad text.
