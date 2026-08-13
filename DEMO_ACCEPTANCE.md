# Company Demo Acceptance Evidence

This document turns the final company-demonstration checklist into evidence that can be checked
against the exact installer being presented. It does not deploy software, grant access, store a
password, or certify Sati for commercial production.

## What the evidence proves

The final verifier requires three short JavaScript Object Notation (JSON) evidence files:

| File | Created by | What it proves |
| --- | --- | --- |
| `api-readiness.json` | `Test-DemoReadiness.ps1` | The deployed application programming interface (API) was healthy and its protected Admin read paths worked. |
| `clean-machine.json` | `Test-DemoInstaller.ps1` | The exact installer launched and remained responsive on a Windows machine outside the source tree. |
| `presenter-attestation.json` | `New-DemoPresenterAttestation.ps1` | The presenter rehearsed the path, used only synthetic data, approved the offline Portable Document Format (PDF) fallback, explained limitations, and confirmed client/API release parity. |

Evidence records hashes, versions, timestamps, machine identity, and pass/fail facts. It never
records a username, password, access token, client narrative, or other protected health information
(PHI). Evidence expires after 72 hours by default so an old successful rehearsal cannot silently
stand in for the current meeting. The API check also requires `/health/version` to report the same
release version encoded in the installer filename; a healthy older deployment cannot pass.

## 1. Authenticated API evidence

On the development/operator machine, set the designated synthetic Admin credentials only in the
current process, run the check, and remove them immediately:

```powershell
$evidence = 'C:\SatiDemoEvidence'
$env:SATI_DEMO_USERNAME = '<synthetic-admin-username>'
$env:SATI_DEMO_PASSWORD = '<synthetic-admin-password>'
.\scripts\Test-DemoReadiness.ps1 -EvidencePath "$evidence\api-readiness.json"
Remove-Item Env:SATI_DEMO_USERNAME
Remove-Item Env:SATI_DEMO_PASSWORD
```

An unauthenticated `-HealthOnly` result is useful diagnostics, but the final verifier rejects it.

## 2. Clean external Windows evidence

Copy these three items to a Windows machine that does not contain the Sati source tree:

- the exact `SatiDemoSetup-1.2.3.exe` installer;
- its adjacent `.sha256` checksum file;
- `scripts\Test-DemoInstaller.ps1`.

Run:

```powershell
.\Test-DemoInstaller.ps1 `
    -InstallerPath .\SatiDemoSetup-1.2.3.exe `
    -ExternalMachine `
    -EvidencePath C:\SatiDemoEvidence\clean-machine.json
```

The script installs into a disposable folder, verifies the installed version and public-only
configuration, watches the application for 15 seconds, records the machine and operating-system
facts, stops the exact process it launched, and removes the disposable copy. The
`-ExternalMachine` switch is an operator attestation, not an automated claim about the network.

## 3. Rehearsal and fallback approval

The checked-in builder reproduces the fallback from the approved synthetic screenshots and vector
explanations. Python 3 is required only to rebuild the PDF, not to present it:

```powershell
py -3 -m pip install -r .\scripts\requirements-demo-fallback.txt
py -3 .\scripts\Build-DemoFallback.py
```

The builder is deterministic: unchanged source and screenshots produce the same PDF hash. After any
content change, render and inspect every page before approving it.

Use the exact installer and fallback PDF intended for the meeting. Complete the ten-minute path in
`DEMO_RUNBOOK.md`, then run this command only if every named statement is true:

```powershell
.\scripts\New-DemoPresenterAttestation.ps1 `
    -InstallerPath .\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.3.exe `
    -FallbackPath .\output\pdf\Sati-Company-Demo-Fallback-1.2.3.pdf `
    -EvidenceDirectory C:\SatiDemoEvidence `
    -Presenter 'Joshua White' `
    -ReleaseCommit '<deployed-client-api-commit>' `
    -ClientApiParityConfirmed `
    -WalkthroughRehearsed `
    -SyntheticDataOnly `
    -FallbackApproved `
    -LimitationsPresented
```

`ReleaseCommit` identifies the source revision represented by the deployed client/API pair. Do not
use the confirmation switch until that exact parity has been checked through the deployment record.

## 4. Final verifier

Place all three evidence files in the same directory and run:

```powershell
.\scripts\Test-CompanyDemoAcceptance.ps1 `
    -InstallerPath .\artifacts\SatiDemoInstaller\SatiDemoSetup-1.2.3.exe `
    -FallbackPath .\output\pdf\Sati-Company-Demo-Fallback-1.2.3.pdf `
    -EvidenceDirectory C:\SatiDemoEvidence
```

The only full-pass marker is:

```text
COMPANY_DEMO_ACCEPTANCE_PASSED
```

If the verifier stops, the demo is not at 100 percent readiness for that artifact. Fix or repeat the
named gate; never edit an evidence file to make it pass.
