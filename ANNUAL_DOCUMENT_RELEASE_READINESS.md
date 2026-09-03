# Annual-document release preparation — 2026-09-03

## Outcome

The compliance/annual-document handoff is implemented in source, including the previously
unfinished safety-plan desktop workflow, privacy acknowledgment, packet, reminder, verifier and
records-request recipient. This is **source preparation, not a completed release**. No version
bump, new commit/push, runtime migration, deployment, installer or distribution publication was
performed during this preparation pass. The source assembly versions remain 1.2.41.

Josh confirmed that medical-records requests are downloads only, after the medical release is
attested, with staff responsible for sending them. The generic privacy notice remains provisional.

## Completed scope

- Shared safety-plan transitions, assigned-author editing, caseload-scoped nonself supervisor
  approval/return, locked submitted/reviewed versions and new-version revision workflow.
- WPF/local/HTTP safety-plan and annual-document services, cycle selection protection, meaningful
  automation names, theme-aware controls, and preserved server-provided DRAFT PDF filenames.
- Append-only privacy receipt/effort records tied to the exact current generated artifact;
  regeneration requires another acknowledgment. Generation never attests a form.
- Configurable 30-day-default annual window; original-enrollment leap-year boundaries; profile
  and dashboard read-time reminders; ZIP with constituent hashes, source/template versions,
  outstanding work, omissions and blank fields.
- Staff-selected-file fingerprint/length verification against live or historical artifact records,
  without uploading bytes. No original PDF storage was introduced.
- Primary-care recipient resolution with inherited organization address/phone. The records request
  is omitted unless the medical release is attested and a current primary-care provider exists.
- Packet facts and writes share one serializable transaction. Protected API writes establish a
  single-attempt EF execution scope, fixing explicit-transaction failure under SQL retry settings
  without replaying ambiguous commits. Read-only requests retain database retries.
- Authorized consumer deletion now includes safety plans and receipt rows, with counts retained
  in the audit ledger. Existing Person lifecycle/legal-hold/deletion work was preserved.
- Route inventory reconciled mechanically: 141 protected routes, matching the live API manifest.

## Verification

- Full solution Release build: succeeded, zero errors.
- Demo desktop configuration build: succeeded, zero errors.
- Release tests: 1,266 desktop/domain passed, 374 API passed, 4 Carika passed.
- One legitimate skip: the opt-in `SATI_RUN_LOCAL_AI_MODEL_EVAL` test. No model evaluation was run.
- `dotnet ef migrations has-pending-model-changes` from `Sati.Persistence` with the same startup
  project: no pending model changes. This inspects metadata; it does not apply migrations.
- `git diff --check`: passed.
- Synthetic PDF previews inspected for all six packet document kinds (nine pages total), including
  both pages of each release. WPF safety/annual workspaces were realized and visually inspected.
  The PDF skill guided layout checks; safety headings now stay with their following content and
  theme inheritance was corrected. These are synthetic QA outputs, not consumer documents or
  installer acceptance.
- Fail-first evidence: old same-agency-only supervisor review accepted the wrong caseload;
  removing the safety revision guard accepted a stale save; removing packet ownership exported a
  foreign consumer; removing either receipt append-only guard allowed a historical edit; changing
  cycles previously retained stale UI actions; the retry-shaped API test returned 500 before the
  write execution scope was added. Each protection was restored and tests passed afterward.
- Test logs: each project's `TestResults/annual-document-final-prep.trx` (ignored build output).

Warnings retained, not concealed: NuGet vulnerability-feed availability (NU1900); the existing
`SqlLocalDatabaseMaintenance` dynamic-SQL analyzer warning; pre-existing nullable/xUnit analyzer
warnings in other tests. A successful build is not a fresh package-vulnerability audit.

## Release gates still required

1. Obtain explicit authorization for the controlled **Demo-only** schema migration. The pending
   September 3 chain includes `AddFormAttestations`, `AddDocumentArtifacts`,
   `AddPersonCreatedAtAndStatus`, `AddLegalHolds`, `AddDocumentTemplatesAndSafetyPlans` (templates
   only despite its historical name), `AddSafetyPlans`, and `CompleteAnnualDocumentWorkflow`.
   The newest migration is `20260903200511_CompleteAnnualDocumentWorkflow`; there are 94 migrations
   in the source chain. Do not rewrite already committed migration identifiers.
2. Josh adds a temporary exact-IP firewall rule if running the migration from this workstation.
   Public IP observed during preparation: `72.95.106.10` (recheck when releasing). The agent must
   never add, change or remove the rule. Josh removes it afterward.
3. Follow `RELEASE_PLAYBOOK.md`: actual-schema existence/semantics guards, rollback-only dry run,
   authorized application, rerun/idempotency proof, then dependent Demo API publication and health/
   contract checks. No API requiring the new tables should be deployed before that update.
4. Reconcile the known Local Production machines and their installed versions. Each local database
   migrates only when its desktop launches; a Demo migration does not update those machines.
5. Select an unused release version; update all version/release-note owners; commit/push verified
   scope; build and acceptance-test both installers; publish only to the playbook's exact folders.
   None of those release actions is implied by this preparation record.

## Explicit limits and outside-scope work

- Completed/external releases must be retrieved from their original saved/signed copies; the ZIP
  does not pretend a regenerated PDF is that original. Unsaved release-editor choices are not a
  persisted packet source. Saved safety-plan content is.
- Privacy receipt is staff-recorded provenance, not an electronic consumer signature. The generic
  privacy/medical wording and shared safety schema need agency/legal/program review before real
  clinical use. Hash matching does not establish content validity or valid authorization.
- No sending integration, background packet job, full retention enforcement or HIPAA-compliance
  claim was added. Existing legal-hold dual-control, general retention scope and Admin pre-deletion
  count-preview items remain explicitly deferred in `AGENDA.md`, not silently included here.
- `LOGGING_DESIGN.md` remains an unrelated, untracked proposal. Preserve it; do not accidentally
  include it in a release commit. Concurrent Admin UI/deletion documentation changes remain intact.
