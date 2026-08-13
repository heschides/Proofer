# Company Demo Runbook

This is the repeatable operator path for demonstrating Sati to an agency. It is a product demo,
not a production-security or regulatory certification.

## Guardrails

- Use only the synthetic `Demo` environment. The shell must display its permanent `DEMO` marker.
- Never enter a real person's name, clinical facts, MaineCare identifier, or contact information.
- Keep EDI generation in test mode. Do not describe the current 837P output as payer-certified.
- Describe retention as `PolicyOnly`; automated destruction and legal-hold enforcement are not live.
- Do not bypass a failed Demo API by selecting Local Production.

## Preflight

Run these checks before the meeting, not while attendees wait:

```powershell
dotnet restore .\Sati.slnx
dotnet build .\Sati.slnx --configuration Debug --no-restore
dotnet test .\Sati.slnx --configuration Debug --no-build --no-restore
dotnet build .\Sati.csproj --configuration Demo --no-restore
```

Build a distributable into a new, empty folder:

```powershell
.\scripts\Publish-Demo.ps1 -OutputDirectory C:\DemoArtifacts\Sati
```

Check the deployed API without credentials:

```powershell
.\scripts\Test-DemoReadiness.ps1 -HealthOnly
```

For the complete read-only check, put the designated synthetic Admin credentials in process
environment variables. Do not place passwords in the command line, script, source tree, or notes:

```powershell
$env:SATI_DEMO_USERNAME = '<synthetic-admin-username>'
$env:SATI_DEMO_PASSWORD = '<synthetic-admin-password>'
.\scripts\Test-DemoReadiness.ps1
Remove-Item Env:SATI_DEMO_USERNAME
Remove-Item Env:SATI_DEMO_PASSWORD
```

The authenticated check reads health, Admin overview, operations, Person-directory, and recent
activity endpoints. It does not export files or mutate a business record.

## Ten-minute walkthrough

1. **Frame the product (one minute).** Sati is a human-services case-management platform moving
   from a WPF pilot client to an API-authoritative, multi-tenant product. Point out the `DEMO`
   marker and say that all visible records are synthetic.
2. **Admin visibility (two minutes).** Open **Admin**. Show agency usage, recent protected activity,
   database health, retained audit/EDI counts, and the explicit retention mode. Select a synthetic
   Person and show the lifecycle timeline and auditor PDF.
3. **Controlled export (one minute).** Enter a real business purpose such as `Company demo of
   internal compliance review`, download the bounded audit CSV, and point out the new
   **Exported audit activity** entry. Treat the downloaded CSV as sensitive and delete the local
   demo copy after the meeting.
4. **Case-manager workflow (three minutes).** Use the designated synthetic Case Manager account.
   Show the caseload, upcoming compliance work, one existing note, the scratchpad, and a
   Comprehensive Assessment. Prefer existing records; make changes only on the designated
   rehearsal Person.
5. **Supervision and billing safety (two minutes).** Use the designated synthetic reviewer to show
   assigned approval queues and conflict-safe note decisions. Show billing in test mode and explain
   that duplicate commands and ambiguous EDI retries cannot create a second successful result.
6. **Close honestly (one minute).** Distinguish company-demo readiness from commercial production.
   Call out remaining identity/MFA, legal-hold enforcement, external alerting, backup drills,
   payer certification, and production deployment work.

## Recovery during a meeting

- A Free-tier Demo API may need a cold-start interval. Run preflight shortly before the meeting.
- If readiness fails, stop the workflow; do not switch to real data or attempt an emergency
  database connection.
- If the client displays an error reference, record only that reference. Diagnostic JSON lines are
  under `%LOCALAPPDATA%\Satilogica\Sati\Logs` and omit exception messages by design.
- If a demonstration mutation succeeds, record the synthetic account, Person ID, and action so the
  canonical Demo dataset can be restored deliberately.
- Keep a PDF or screenshots of the synthetic workflow available as a presentation fallback; never
  use screenshots containing real client data.

## Canonical local Demo reset

After approving the exact synthetic dataset used for rehearsal, capture its immutable baseline once:

```powershell
.\scripts\Seed-DemoShowcaseData.ps1
.\scripts\Reset-LocalDemoData.ps1 -Action CaptureBaseline
```

The baseline is stored outside the repository under
`%LOCALAPPDATA%\Satilogica\Sati\DemoBaseline`. The tool refuses any database except `SatiDemo`
with the `Demo` identity marker, verifies SQL backup checksums and a SHA-256 manifest, and will not
silently replace an existing baseline.

Close Sati, then restore after a rehearsal:

```powershell
.\scripts\Reset-LocalDemoData.ps1 -Action VerifyBaseline
.\scripts\Reset-LocalDemoData.ps1 -Action RestoreBaseline
```

`DEMO_RESTORE_VERIFIED` is the acceptance marker. `-ReplaceBaseline` archives the previous backup
and must be used only when intentionally approving a new canonical dataset. The deployed Azure Demo
database requires the same snapshot/restore control in its deployment runbook; this local command
never connects to Azure.

## Final acceptance gates

Company-demo readiness reaches 100% only when all of these are true for the exact artifact being
shown:

- [ ] The current Demo client and API changes are deployed together.
- [ ] Health and authenticated readiness checks pass against that deployment.
- [ ] The packaged artifact launches on a clean Windows machine outside the development network.
- [ ] The ten-minute path is rehearsed with the designated synthetic accounts and records.
- [x] A canonical local Demo restore/reset procedure is available for rehearsal mutations; the
  deployed database procedure is an explicit deployment gate.
- [ ] The presenter has an approved fallback and knows the product/production limitations above.
