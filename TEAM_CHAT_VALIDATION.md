# Team chat — validation evidence and limits

Review date: September 5, 2026 (America/New_York); local execution also spans September 6 UTC.
The user authorized design review and implementation. No release, deployment, existing-database
migration, cloud change, security-setting change or source commit was performed.

## Source provenance and scope

Both Claude handoffs were read in full: Team Chat at `team-chat-design` / `e7d80ca0`, and Electronic
Signature Portal at `signature-portal-design` / `e81348f`. Their statements that the sources were
on `master` did not match the checked branches. Only team chat was implemented. Unrelated existing
worktree edits were preserved; no branch merge, reset or checkout was used.

The current design is `TEAM_CHAT_DESIGN.md`; findings are in `TEAM_CHAT_REVIEW.md`. The user's
plain-language implementation and agency guide is `TEAM_CHAT_GUIDE.md`.

## Automated verification

The final `dotnet test Sati.slnx --no-restore` run completed successfully:

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Desktop/domain (`Sati.Tests`) | 1,381 | 0 | 1 |
| API integration (`Sati.Api.Tests`) | 423 | 0 | 0 |
| Companion client (`Carika.Tests`) | 4 | 0 | 0 |
| **Total** | **1,808** | **0** | **1** |

The skipped test is the optional configured local-AI model competence test. The final run includes
all completed membership/consumer response changes and the corrected long-message rendering test.
TRX evidence is retained under
`C:/Users/SatiLogica/AppData/Local/Temp/sati-chat-full-34f892`.

Build output included existing non-chat analyzer warnings and a NuGet vulnerability-feed warning:
the package advisory service at `api.nuget.org` could not be reached. The build/test run succeeded,
but it does **not** constitute a fresh dependency vulnerability assessment. Repeat that advisory
check when the service is available before release. Final `git diff --check` was clean.

Focused verification completed before the final solution run:

- 21 API chat cases plus 5 existing route-surface checks passed.
- 16 persistence/schema/relationship/concurrency checks passed.
- 27 desktop client, conversation-state and render cases passed; the final solution run includes
  the stronger long-text rendering assertion.
- 10 shared authorization cases and 4 local retained-chat deletion cases passed, all 14 failed
  under deliberately weakened rules/deletion guards, and all 14 passed after exact restoration.
- Route documentation was mechanically compared with the manifest: **157 protected routes in
  each, with no difference**. Live registration is separately checked by the API surface tests.

Negative controls were performed against real registered routes and implemented guards, not
merely against a missing feature. The API owner observed 17 expected case failures when endpoint,
audit, activation, session/protocol and deletion protections were removed. Persistence observed
14 expected failures for append-only/concurrency guards, restrictive/unique relationships and
populated rollback refusal. The desktop owner observed 14 expected failures for stale responses,
uncertain sends/account changes, secure/passive connections, stale edits and membership episodes.
Together with the root's 14 shared/local cases, these are **59 expected failing case outcomes**;
some tests were rerun under more than one grouped mutation. All mutant source was restored before
the final full-solution run. This is targeted regression evidence, not exhaustive mutation coverage.

The local root proof results are retained under `%TEMP%/sati-chat-root-proof-34f892` as baseline,
mutant and restored TRX files. No confidential or real-client data was used.

## Real SQL Server rehearsal

The persistence owner used SQL Server LocalDB 17.0.4025.3 and newly created disposable databases.
The new chat migration succeeded in isolation on the schema reached before older chain failures:

- Five chat tables, 16 restrictive foreign keys, 29 indexes and six check constraints.
- One writer advanced the room and saved one message plus its matching change in one transaction.
  A second writer using the old room revision blocked for 3,867 milliseconds, then affected zero
  rows after the first committed. Final state had one message and one matching change.
- Rollback of populated chat raised SQL error 51000 before any table drop; the room and message
  remained. An empty synthetic-parent fixture successfully applied and reversed the chat migration.
- Both exact disposable databases were dropped after name/path verification; subsequent existence
  checks returned no database. No Production, Demo or cloud database was accessed.

Detailed non-PHI SQL evidence remains at
`C:/Users/SatiLogica/AppData/Local/Sati/ChatValidation/SatiChatValidation_2f27bdb2a61a47b198faf16704cb67f4/validation-report.txt`.

**The complete existing migration chain did not pass.** Two older problems remain:

1. Generated SQL fails in `20260419144051_AddNoteApprovalFields.cs`, lines 25–42: a batch adds
   `Agencies.Npi` and then references it before a new batch begins (SQL 207). Direct EF migration
   uses separate commands and gets beyond this point.
2. Direct EF migration then fails in `20260828195515_AddTestConsumerMarker.cs`, lines 23–35:
   a conditional statement still references absent `dbo.SatiDatabaseIdentity` during SQL
   compilation (SQL 208).

The older migration sources were preserved. An isolated chat success and a symbolic chain check
do not establish successful clean-database or production-upgrade deployment. Repair and rehearse
the complete supported path before deployment; these blockers are recorded in `AGENDA.md`.

## Interface and remaining acceptance

Synthetic views were rendered at 640×480, 900×650 and 1400×900. Small and large previews were
visually inspected. The final wrapping check also verifies that long message text occupies
multiple lines and remains within the available width. Preview files are under `%TEMP%` with
names `sati-chat-preview-640.png`, `sati-chat-preview-900.png` and `sati-chat-preview-1400.png`.

Automated WPF rendering and control checks do not replace human screen-reader, keyboard-only,
high-contrast and enlarged-text acceptance. The existing theme and shell privacy-screen boundary
were retained; no desktop message previews or disk chat cache was introduced.

Hosted two-workstation behavior, blocked-notification fallback, multiple service instances,
operational monitoring, restore/recovery and controlled rollout remain separate acceptance work.
The feature stays disabled by default and cannot be enabled for Production by its feature flag.
Agency legal applicability, contracts, sensitive-record consent rules, broader preservation and
records recovery, account suspension/immediate session revocation and training remain prerequisites
for real-client use. Passing tests does not establish HIPAA compliance or legal approval.
