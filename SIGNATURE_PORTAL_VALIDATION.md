# Electronic signature portal - validation evidence and limits

Review date: September 5, 2026 (America/New_York); execution also spans September 6 UTC.
The user authorized review and implementation. This record describes the completed local checks,
not approval for real-client signing. No release, deployment, real email delivery, cloud change,
or migration of an existing user database was performed. The SQL rehearsals used newly created
synthetic local databases, which were deleted and verified absent afterward.

The original proposal and handoff were read in full. Implemented behavior and remaining operating
requirements are explained in [SIGNATURE_PORTAL_GUIDE.md](SIGNATURE_PORTAL_GUIDE.md), design changes
in [SIGNATURE_PORTAL_REVIEW.md](SIGNATURE_PORTAL_REVIEW.md), and setup in
[Sati.Portal/README.md](Sati.Portal/README.md). Existing unrelated worktree changes were preserved.

## Final automated verification

The final full solution run in the Release configuration passed:

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Desktop and domain (`Sati.Tests`) | 1,420 | 0 | 1 |
| API integration (`Sati.Api.Tests`) | 486 | 0 | 0 |
| Shared signing services (`Sati.Signatures.Tests`) | 118 | 0 | 0 |
| Public portal HTTP checks (`Sati.Portal.Tests`) | 8 | 0 | 0 |
| Companion client (`Carika.Tests`) | 4 | 0 | 0 |
| **Total** | **2,036** | **0** | **1** |

The skipped case is the optional configured local-AI model competence test,
`ConfiguredModelCompletesGroundedWorkflowAcrossRepresentativeCurrentNoteInputs`.
Final TRX records are retained under `tmp/signature-final-solution/`. These totals include existing
features as well as signing; they are not counts of newly added signing tests. The final run
includes the corrected read-only display bindings and human-readable signer roles.

The Demo desktop build also succeeded, with zero errors and two existing EF1002 warnings in
`Data/SqlLocalDatabaseMaintenance.cs`, line 90. Final `git diff --check` passed; Git reported
line-ending notices. Hosted CI and installer acceptance were not run.

The shipped portal script separately passed all **9** checks in
`Sati.Portal.Tests/portal-ui.test.cjs`. Its local command,
`node --test Sati.Portal.Tests/portal-ui.test.cjs`, has been added to the existing CI workflow. These tests use
a simulated page and network surface to exercise the actual script. They do not run a real browser.

Two separate read-only checks also passed:

- The protected API manifest and its documentation each contain **168 unique routes**, with no
  missing, extra or duplicate entries. Evidence:
  `tmp/signature-negative/api-route-documentation-check.json`. This comparison excludes anonymous
  login and health routes; the public signing portal has separate HTTP security tests.
- The current dependency-advisory check covered **13 restored projects**, exited successfully with
  empty error output, and reported no known vulnerable direct or transitive packages. Evidence:
  `tmp/signature-negative/dependency-vulnerabilities.json` and
  `dependency-vulnerabilities.stderr.txt`. This checks the restored package versions against the
  available advisory feed; it is not an exhaustive security assessment.

## What the focused checks cover

Focused selections also passed before the final solution run. Their counts overlap the final
suite totals and one another; they must not be added to produce a second overall test count.

- **68 API/persistence/route checks** covered current staff permissions and consumer ownership,
  foreign agency/contact refusal, matching the displayed signer details when issuing an invitation,
  original-file verification, staff download audit, retained-history deletion refusal, and profile
  or contact changes. Evidence: `tmp/signature-negative/signature-owner-final.trx`.
- **71 desktop/signature/annual-document/creation checks** covered the affected local services,
  current permissions, signer-change cancellation, transaction rollback, late responses, changed
  selections/accounts, clearing private file bytes, and the signing workspace. Evidence:
  `tmp/signature-negative/signature-local-owner-final.trx`.
- **28 persistence checks** covered matching models across contexts, restrictive relationships,
  immutable source/consent/completion records, stale writes, durable PIN attempts, session purpose,
  external-access withdrawal, one transaction for signer changes, UTC timestamps and rollback guards.
- **9 signed-copy checks** covered original-byte preservation, matching consent and signing-session
  evidence, repeat-safe preparation, bounded certificate content, full disclosure pagination,
  protected receipt preparation, and recovery after a simulated protection-key failure.
- Provider and notification-worker checks used controlled credentials, responses and local data.
  They covered private storage checks, write-once requests, content-size limits, key and request
  binding, separate key roles, environment and recipient restrictions, durable delivery claims,
  attempt limits, delayed retries, uncertain outcomes and provider operation identifiers. They
  did not contact live Azure services or send mail.
- Deadline and privacy checks used deterministic delayed operations to cross an expiration during
  PIN verification, document release, consent, signing or extension. They also checked that changed
  recipient access cannot reuse an old receipt link/session or queue a new receipt notification.

The completed record remains signed after a signer record changes. Its separate external-access
withdrawal stops old receipt links and sessions without rewriting the outcome, consent, original
document or signed copy. Staff access to retained records remains subject to normal authorization.
This is distinct from withdrawing a release's permission to disclose information.

## Deliberately weakened protections

Tests were rerun with selected protections temporarily removed, then with the exact original
sources restored and corrected assemblies rebuilt. The following are **expected failing case
outcomes**, not unique test counts or an exhaustive collection of attacks. A case can run under
several guard changes. Do not add these numbers to the final solution totals.

| Area | Expected failing case outcomes | Retained evidence |
|---|---:|---|
| Initial staff API boundaries | 9 | `tmp/signature-negative/signature-boundary-negative.trx` |
| Initial desktop state/privacy boundaries | 5 | `tmp/signature-negative/signature-client-negative.trx` |
| Current API ownership and signer-change handling | 12 | `tmp/signature-negative/signature-owner-negative.trx` |
| Local ownership, permissions, signer changes and rollback | 22 | `tmp/signature-negative/signature-local-owner-negative.trx` |
| Core workflow, secret binding and portal request guards | 17 | `tmp/signature-core-proof/results.json` |
| Storage, key and email provider protections | 36 | `tmp/signature-provider-proof/results.json` |
| Notification-worker claims, retries and uncertain outcomes | 20 | `tmp/signature-mail-proof/results.json` |
| Deadlines and stopped external access | 12 | `tmp/signature-race-proof/results.json` |
| Persistence, signed-copy integrity and related boundaries | 31 | Local SQL/automated reports identified below |
| Portal script session header, PDF request and changed-response handling | 5 across three variants | `tmp/signature-negative/portal-session-*-negative.log` |
| Server refusal of a different browser tab's signing session | 1 | `tmp/signature-binding-proof/guard-removed.trx` |

Some grouped mutation runs deliberately left unrelated cases passing. For example, removing a
new ownership guard did not remove a separate pre-existing agency check; changing a phone number
did not require revoking an otherwise unchanged signing identity. The retained TRX records show
both the expected failures and these passing controls.

The final cross-tab server check specifically observed the wrong success response when its
session-binding guard was removed: the test required HTTP Conflict, while the weakened route
returned OK. The exact source was restored, and all **8** portal HTTP cases passed afterward in
`tmp/signature-binding-proof/restored.trx`. No temporary weakened source remained for the final
solution run.

## SQL Server migration, permissions and concurrency

Both rehearsals used Windows SQL Server LocalDB **17.0.4025.3**, instance
`(localdb)\MSSQLLocalDB`. Each target was a new database containing only synthetic fixtures.
A synthetic `SatiDatabaseIdentity` marker with environment `Testing` was created **before**
applying the migration chain. The connection named the exact disposable database explicitly.

The final schema rehearsal applied all **96 migrations using direct EF execution**, including
the single new `AddSignatureEvidence` migration. The final signing schema has:

- Eight signing tables, 18 restrictive foreign keys, 18 check constraints and 41 indexes.
- Both external-access withdrawal columns, preserving the separate signed outcome.
- The narrow source-document and environment views needed by the portal.

The final canonical model also passed the no-pending-model-changes check. This verifies that the
model and the migration snapshot agree; it does not apply a migration to any deployed environment.

`scripts/Grant-SignaturePortal.sql` was tested only in the disposable database. A test user without
a login belonged solely to the portal role. Narrow document/environment reads, signing-request
reads and a permitted workflow-column update succeeded. **16 prohibited operations were denied**,
including reads of clinical/user/chat/outbox tables, changes to secrets or frozen evidence,
session deletion, and changes to either external-access withdrawal column. A cross-agency
signing-session insert was also rejected by a foreign key.

These checks establish the tested table/column permission boundary. They do not establish
per-request isolation after compromise of the portal's database identity: that identity can read
the signing tables. No cloud identity assignment or existing security setting was changed.

The earlier disposable rehearsal also established these behaviors:

- Five overlapping SQL sessions took the request's update lock inside transactions. Each observed
  the next failure count, from zero through four. Exactly five failures and five events persisted;
  the fifth set the durable lock and advanced the authentication version. An earlier session no
  longer matched. A sixth attempt observed five and made no further change.
- A stale revision writer waited **1,823 milliseconds**, then updated zero rows. The winning
  revision remained intact.
- Downgrading a populated signing schema failed with SQL error **51001** before any of the eight
  signing tables were dropped. After removing only the synthetic fixture as its database owner,
  the same downgrade succeeded, leaving no signing tables/views and 95 migrations.

The concurrent SQL commands exercised the workflow's locking pattern and actual SQL permissions.
They were not concurrent HTTP requests, a multi-host deployment, or a live cloud load test.

### Evidence locations and cleanup

Both exact test databases were dropped. A subsequent `DB_ID` check returned NULL for each:

- `SatiSignatureValidation_3b692364a9de4b6897bd86413e894d38`: concurrency, rollback and initial
  permission proof, plus the earlier automated negative controls.
- `SatiSignatureValidation_894ba18f41344e55a822ea5a92a15dc2`: final schema and permission reproof
  after external-access withdrawal was added, plus the final restored automated results.

Their non-PHI reports and logs remain under
`C:/Users/SatiLogica/AppData/Local/Sati/SignatureValidation/`, in folders with those exact names.
Each contains `validation-report.txt` and `cleanup.txt`. The final report supersedes the earlier
schema's count of 17 checks; the completed schema has 18. Package rollback tests deliberately left
the generated but unreferenced copy in the test storage while rolling back the database changes.
The feature implements no automatic deletion of corresponding real storage objects; their
recovery and retention process remains an operating requirement.

### Older migration defects remain

Successful direct EF execution with the marker pre-created does **not** mean the old full generated
SQL script has been repaired, or that an unprepared database can be upgraded successfully.
Earlier chat rehearsal evidence established two pre-existing failures:

1. Generated SQL from `20260419144051_AddNoteApprovalFields.cs`, lines 25-42, adds `Agencies.Npi`
   and references it within the same batch, causing SQL 207. Direct EF uses separate commands and
   gets past this point.
2. Direct EF execution without `dbo.SatiDatabaseIdentity` reaches
   `20260828195515_AddTestConsumerMarker.cs`, lines 23-35, whose conditional still binds the missing
   table, causing SQL 208.

Neither older migration was edited. The successful signature rehearsals supplied the documented
environment marker first. The exact deployment path, backup/restore process, and upgrade from
each supported existing database state still require controlled rehearsal before deployment.

## Visual review and remaining acceptance

The reviewer rendered and inspected all **seven pages of the latest synthetic PDF** after the
final certificate wording changes. Page one is intentionally blank because the test original is
a blank synthetic page. Pages two through seven contain the signature evidence, with readable
pagination and no observed clipping or overlap. Final images `final-page-1.png` through
`final-page-7.png`, and `synthetic-signed-evidence.pdf`, are under
`C:/Users/SatiLogica/AppData/Local/Sati/SignatureValidation/pdf/`.

The certificate includes the complete frozen disclosure and signing statement. It selects a
bounded set of records for the final signing session; the complete event ledger remains in the
database. It does not claim a cryptographic PDF signature seal or independent proof of legal
authority. The retained original is separate from the derived signed package.

A browser screenshot attempt was rejected by automatic approval review with the reason
**"blocked by policy"**. Interactive computer-use automation was unavailable for this check.
Consequently, actual-browser appearance and end-to-end human signing acceptance were not completed.
The passing simulated-page checks, HTTP tests and PDF renders must not be presented as that
acceptance.

Further checks remain necessary before real use:

- Real browsers and devices, keyboard-only operation, screen readers, enlarged text, printing,
  download/save behavior and user comprehension. The generated PDF is currently untagged;
  PDF accessibility conformance has not been established. Windows Arial font handling was used;
  a Linux font configuration was not verified.
- Hosted multiple-instance behavior, operational rate limits, secret rotation, logging redaction,
  private storage access, durable storage preservation, backup/restore, monitoring and recovery.
  A successful test provider response is not proof of real Azure configuration or inbox delivery.
- A practical agency process for identity and representative authority checks, paper alternatives,
  copies after link expiry, changed recipients, withdrawal, incident response, retention and holds.
  Form-specific legal acceptance and applicable healthcare/privacy requirements require qualified
  review as described in the implementation guide.

The feature, background processing and email sending remain separately disabled by default.
Production cannot be enabled by this feature flag. Passing these checks does not establish HIPAA
compliance, legal sufficiency, successful deployment or approval to use real client information.
