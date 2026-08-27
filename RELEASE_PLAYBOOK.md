# Sati DATT Release Playbook

This playbook defines the bounded release workflow invoked by the exact, case-insensitive command
`invoke DATT!`. It is designed to release verified Sati changes without silently discarding work,
publishing to Production, changing a cloud database, or overwriting an existing artifact.

## Invocation and authority

A DATT release begins only when the newest user message, after trimming surrounding whitespace,
equals `invoke DATT!` using a case-insensitive comparison. For example, `INVOKE datt!` is valid.
Mentioning or quoting the phrase inside any longer message is not valid.

On a valid invocation, first reply:

> DATT received. Starting release audit.

The invocation authorizes these actions for the current Sati repository:

- inspect, edit, build, and test the in-scope release;
- fetch Git state and reconcile completed, relevant branches;
- delete only branches that satisfy every safe-deletion rule below;
- create ordinary commits and push them to the resolved default branch;
- publish the Sati Demo API to its existing Demo Azure resource;
- build and acceptance-test new Demo and Local installers;
- publish the accepted installers and checksum files to the exact distribution folders below; and
- commit and push final release evidence.

The invocation does not authorize:

- a Production API or infrastructure deployment;
- a cloud database migration or any transformation of Production data;
- copying Production data into Demo;
- force-pushing, rewriting published history, discarding changes, or overwriting an artifact;
- deleting a protected, active, ambiguous, or uniquely valuable branch; or
- expanding the release to another product merely because it shares this repository.

Normal Codex, operating-system, Git host, and Azure approval prompts still apply.

## Success criteria

A DATT release is complete only when all applicable conditions are true:

- relevant completed work is present on the repository's resolved default branch;
- the working tree contains no unexplained or accidentally omitted changes;
- the version, Settings release tracker, builders, readiness checks, tests, and release documents
  agree on one new version;
- the full Release build and all available automated test projects pass;
- source commits are pushed without rewriting remote history;
- the Demo API reports healthy liveness and readiness, the new release version, and the expected
  contract revision;
- the Demo and Local installers are new, non-overwritten artifacts and pass their acceptance gates;
- the accepted Local and Demo installers and checksums are present in their designated distribution
  folders with hashes identical to the accepted build artifacts;
- deployment identifiers, test results, artifact paths, sizes, and SHA-256 hashes are recorded; and
- the final evidence commit is pushed and the local default branch matches its remote.

## 1. Preflight and repository audit

1. Read `AGENTS.md`, this playbook, and the current release section of `AGENDA.md`. Read other
   project instructions required by the scope.
2. Confirm the repository root, remotes, current branch, upstream, linked worktrees, and remote
   default branch. Do not assume that the default branch is named `main`; it is currently `master`.
3. Fetch current remote state before deciding whether anything is merged, obsolete, ahead, or
   behind. Do not use a force option.
4. Inspect staged, unstaged, untracked, ignored release artifacts, and recent commit state. Preserve
   unrelated changes and changes in other worktrees.
5. Identify the releasable change set. If there are no new releasable changes, report that result
   and stop without incrementing a version, committing, deploying, or packaging.
6. If a change's ownership or intent cannot be determined safely, stop and ask the smallest
   necessary question. Never hide uncertainty by stashing, resetting, or overwriting it.

## 2. Branch reconciliation and cleanup

Inspect each local and remote branch relative to the resolved default branch using ancestry,
unique commits, diffs, upstream state, and worktree ownership.

Merge a branch only when all of these are true:

- it contains completed work relevant to the current Sati release;
- its unique commits and diff have been reviewed;
- it is not an unrelated experiment, historical setup branch, or incomplete product slice;
- the merge can be performed without discarding or concealing working-tree changes; and
- conflicts can be resolved from clear project intent and verified afterward.

If a relevant merge is ambiguous or conflicts with unexplained changes, stop for direction. Do not
autostash, reset, force a merge, or choose one side merely to make the merge complete.

A branch may be deleted only when every condition below is proven:

- its tip is fully merged into the resolved default branch;
- it has no unique commits relative to that branch;
- it is not the default branch, a protected branch, a release branch, or otherwise designated for
  retention;
- it is not checked out by any linked worktree;
- it is not associated with active or uncertain work; and
- its name and tip commit are recorded before deletion.

Use safe local deletion, never forced deletion. Delete a remote branch only when the same conditions
are true for its remote tip and the branch is clearly an ephemeral completed feature branch. If any
condition is uncertain, keep the branch and report it instead.

## 3. Version and release record

Determine the next version from the latest committed release and the repository's established
convention. Use a patch increment by default. Use a minor or major increment only when the user's
request or a recorded product decision clearly requires it.

Update all coordinated owners before the source release commit, including as applicable:

- `Sati.csproj` and `Sati.Api/Sati.Api.csproj`;
- Demo, Local, and diagnostic installer builder defaults;
- Demo readiness expectations and explicit version assertions;
- `Services/ProductReleaseNotes.cs`, which supplies the Settings version tracker and release notes;
- current installer and runbook examples; and
- a new current-release section in `AGENDA.md` with deployment and artifact evidence still marked
  pending.

Do not bump Carika or another product merely because it is in the same solution. Include another
product only when its own release is clearly part of the releasable change set.

Never reuse a version whose API ZIP or installer already exists. Never replace different bytes under
an existing version number.

## 4. Release validation

1. Review the complete diff and run `git diff --check`.
2. Build the complete solution in Release configuration.
3. Run every test project in the solution, including Sati desktop/domain tests, API integration
   tests, and Carika tests when present. Run profile-dependent DPAPI, WPF, or Avalonia checks under
   the normal signed-in Windows profile when the sandbox cannot exercise them correctly.
4. Run additional focused tests required by the changed behavior and inspect relevant packaged UI
   behavior when automated coverage alone is insufficient.
5. Treat an optional test as skipped only when its documented external prerequisite is genuinely
   absent; report the skip explicitly.

Any real build or test failure stops the release. Fix it and repeat the affected gates before
continuing. Do not publish or package a knowingly failing source state.

## 5. Source commit and push

1. Fetch the remote default branch again and confirm it has not advanced unexpectedly.
2. Stage only the verified release scope. Review the staged diff and staged whitespace check.
3. Create a normal release commit that includes the coordinated version and Settings release notes.
4. Push to the resolved remote default branch without force.
5. Confirm the remote contains the exact source commit before producing deployment artifacts.

If the remote advanced, reconcile it safely and rerun affected validation. Never rewrite another
contributor's published history.

## 6. Demo API publication

Publish only the existing Sati Demo API resource. Build the API ZIP from the pushed source commit,
confirm its assembly version, confirm that no private desktop settings or reusable credentials are
packaged, and record its SHA-256 hash.

No database migration is implied by this workflow. If the release requires a cloud schema change,
stop and obtain explicit authorization for the controlled migration procedure before publishing a
dependent API.

After publication, verify:

- deployment success and deployment identifier;
- `/health/live` and `/health/ready`;
- `/health/version` product and release version; and
- client/API contract revision parity.

Retain the prior known-healthy API package and deployment information. If verification fails, stop
downstream work and report the failure. Do not improvise a Production deployment or an unapproved
database action. Ask before redeploying a rollback package unless prior instructions explicitly
authorize that rollback.

## 7. Demo and Local installers

Build installers only after the matching API and source commit pass their release gates.

- Build the Demo installer with `installer/Build-DemoInstaller.ps1`.
- Build the Local installer with `installer/Build-LocalInstaller.ps1`, using the durable repository
  prerequisite only after verifying that `SqlLocalDB.msi` has a valid Microsoft signature.
- Reject Local configuration containing SQL usernames or passwords; it must use Windows integrated
  security.
- Refuse to overwrite an installer or checksum bearing the selected version.
- Run the Demo isolated acceptance test for five responsive launches, normal closes, exact installed
  version, and cleanup.
- Run the Local isolated acceptance test for exact version, embedded LocalDB signature, integrated
  security, and cleanup.

Record each final artifact's absolute path, byte size, and SHA-256 hash. Generated installers are not
assumed to be code-signed merely because the embedded Microsoft LocalDB prerequisite is signed.

### Distribution publication

Only after both installer acceptance gates pass, publish the final installer executables and their
generated `.sha256` files as follows:

- Local installer and checksum:
  `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\Sati Desktop`
- Demo installer and checksum:
  `C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents\SatiLogica Demo Files`

Resolve and validate both absolute destinations before writing. They must remain within
`C:\Users\SatiLogica\RobinBradleyAMS\SatiLogica - Documents`. Create only the two exact destination
directories when one is missing; do not create a guessed or similarly named location.

Never overwrite a published file. If a destination filename already exists, compare its SHA-256
with the accepted artifact. Treat an identical file as already published. If the hashes differ,
stop and report the collision rather than replacing either file.

For a new destination file, copy to a uniquely named temporary sibling, verify the temporary copy's
SHA-256, and then rename it to the final versioned filename. Verify the final copy and checksum file
again after publication. On failure, remove only the exact temporary file created by this run; do
not remove an existing final file. Do not publish the API ZIP, LocalDB prerequisite, private
configuration, or any additional files to these folders.

## 8. Evidence commit and final report

Update the current `AGENDA.md` release section with:

- source and evidence commit identifiers;
- test totals and any legitimate skips;
- API ZIP hash, deployment identifier, health status, release version, and contract revision; and
- Demo and Local installer names, byte sizes, hashes, acceptance results, cleanup results, and
  verified distribution paths.

Commit and push that evidence to the resolved default branch. Finish by confirming a clean working
tree and equality with the remote branch.

The final user report must lead with whether the release completed. Include the version, commits,
API verification, installer links and hashes, tests, branches merged or deleted, branches retained
because of uncertainty, and any unresolved warning or manual follow-up.

## Stop conditions

Stop before the next side effect and explain the blocker when any of these occurs:

- unexplained or overlapping user changes cannot be preserved safely;
- a branch has unique or ambiguous work;
- the remote default branch cannot be identified or changed unexpectedly;
- a merge conflict lacks an evidence-backed resolution;
- the version is ambiguous or an artifact already uses it;
- a build, required test, security check, or acceptance gate fails;
- a secret, credential, private configuration, or unrestricted narrative appears in an artifact or
  log;
- a cloud database migration or Production deployment would be required;
- the deployed API is unhealthy or reports the wrong version or contract revision;
- the LocalDB prerequisite is missing or its Microsoft signature is invalid;
- a distribution path resolves outside the named documents root, cannot be written, or contains a
  same-named artifact with a different hash; or
- required authority or access is unavailable.

Do not mark a partial release complete. Report what succeeded, what did not occur, and the smallest
next action needed.
