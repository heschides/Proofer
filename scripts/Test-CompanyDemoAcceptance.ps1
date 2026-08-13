[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [string]$FallbackPath,

    [Parameter(Mandatory)]
    [string]$EvidenceDirectory,

    [string]$BaseAddress = 'https://sati-demo-api-satilogica.azurewebsites.net/',

    [ValidateRange(1, 168)]
    [int]$MaxEvidenceAgeHours = 72
)

$ErrorActionPreference = 'Stop'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Read-GateEvidence([string]$FileName, [string]$ExpectedGate) {
    $path = Join-Path $evidenceRoot $FileName
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Missing evidence file: $path"
    $evidence = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Assert-True ($evidence.SchemaVersion -eq 1) "$FileName has an unsupported schema version."
    Assert-True ($evidence.Gate -ceq $ExpectedGate) "$FileName has gate '$($evidence.Gate)', expected '$ExpectedGate'."
    Assert-True ([bool]$evidence.Passed) "$FileName does not record a passing result."

    $captured = [DateTimeOffset]::Parse(
        [string]$evidence.CapturedUtc,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal)
    Assert-True ($captured -le [DateTimeOffset]::UtcNow.AddMinutes(5)) "$FileName has a future timestamp."
    Assert-True ($captured -ge [DateTimeOffset]::UtcNow.AddHours(-$MaxEvidenceAgeHours)) "$FileName is older than $MaxEvidenceAgeHours hours."
    return $evidence
}

$installer = [System.IO.Path]::GetFullPath($InstallerPath)
$fallback = [System.IO.Path]::GetFullPath($FallbackPath)
$evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
$baseUri = [Uri]$BaseAddress

Assert-True (Test-Path -LiteralPath $installer -PathType Leaf) "Installer not found: $installer"
Assert-True (Test-Path -LiteralPath $fallback -PathType Leaf) "Fallback not found: $fallback"
Assert-True ($baseUri.IsAbsoluteUri -and $baseUri.Scheme -ceq 'https') 'BaseAddress must be an absolute HTTPS address.'
Assert-True (Test-Path -LiteralPath $evidenceRoot -PathType Container) "Evidence directory not found: $evidenceRoot"

$installerName = [System.IO.Path]::GetFileName($installer)
Assert-True ($installerName -match '^SatiDemoSetup-(\d+\.\d+\.\d+)\.exe$') "Unexpected installer name: $installerName"
$expectedVersion = $Matches[1]
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$fallbackHash = (Get-FileHash -LiteralPath $fallback -Algorithm SHA256).Hash.ToLowerInvariant()

$checksumPath = "$installer.sha256"
Assert-True (Test-Path -LiteralPath $checksumPath -PathType Leaf) "Installer checksum not found: $checksumPath"
$checksumLine = (Get-Content -LiteralPath $checksumPath -Raw).Trim()
Assert-True ($checksumLine -match '^([0-9a-fA-F]{64})\s+(.+)$') 'Installer checksum file has an invalid format.'
Assert-True ($Matches[1].ToLowerInvariant() -ceq $installerHash) 'Installer checksum does not match the installer.'
Assert-True ($Matches[2].Trim() -ceq $installerName) 'Installer checksum names a different file.'

Assert-True ([System.IO.Path]::GetExtension($fallback) -ceq '.pdf') 'The approved fallback must be a PDF.'
Assert-True ((Get-Item -LiteralPath $fallback).Length -gt 10000) 'The fallback PDF is unexpectedly small.'
$fallbackHeader = [Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($fallback), 0, 5)
Assert-True ($fallbackHeader -ceq '%PDF-') 'The fallback file does not have a PDF header.'

$api = Read-GateEvidence 'api-readiness.json' 'AuthenticatedApiReadiness'
Assert-True ([bool]$api.Authenticated) 'API evidence contains only an unauthenticated health check.'
Assert-True ($api.AccountRole -ceq 'Admin') 'API readiness was not performed with the designated synthetic Admin.'
Assert-True ($api.BaseAddress -ceq $baseUri.AbsoluteUri) 'API evidence targets a different base address.'
Assert-True ($api.Live -ceq 'Healthy' -and $api.Ready -ceq 'Healthy') 'API evidence does not show healthy liveness and readiness.'
Assert-True ($api.ReleaseVersion -ceq $expectedVersion) 'The deployed API release version does not match the installer.'

$machine = Read-GateEvidence 'clean-machine.json' 'CleanMachineLaunch'
Assert-True ($machine.InstallerSha256 -ceq $installerHash) 'Clean-machine evidence used a different installer.'
Assert-True ([bool]$machine.AppResponding) 'Clean-machine evidence does not show a responding application.'
Assert-True ([bool]$machine.ExternalMachineConfirmed) 'The installer run was not confirmed as an external machine.'
Assert-True (-not [bool]$machine.SourceTreeDetected) 'The clean-machine run detected the Sati source tree.'
Assert-True ([string]$machine.InstalledVersion -like "$expectedVersion.*") 'The installed version does not match the installer version.'

$presenter = Read-GateEvidence 'presenter-attestation.json' 'PresenterRehearsalAndFallback'
Assert-True ($presenter.InstallerSha256 -ceq $installerHash) 'Presenter rehearsal used a different installer.'
Assert-True ($presenter.FallbackSha256 -ceq $fallbackHash) 'Presenter approved a different fallback PDF.'
Assert-True ($presenter.BaseAddress -ceq $baseUri.AbsoluteUri) 'Presenter attestation targets a different API address.'
Assert-True ($presenter.ReleaseVersion -ceq $expectedVersion) 'Presenter rehearsal used a different release version.'
Assert-True ([string]$presenter.ReleaseCommit -match '^[0-9a-f]{7,40}$') 'Presenter evidence does not identify a release commit.'
foreach ($confirmation in @(
    'ClientApiParityConfirmed',
    'WalkthroughRehearsed',
    'SyntheticDataOnly',
    'FallbackApproved',
    'LimitationsPresented')) {
    Assert-True ([bool]$presenter.$confirmation) "Presenter confirmation '$confirmation' is missing."
}

[pscustomobject]@{
    Result = 'Ready'
    Installer = $installerName
    InstallerSha256 = $installerHash
    Fallback = [System.IO.Path]::GetFileName($fallback)
    FallbackSha256 = $fallbackHash
    ApiBaseAddress = $baseUri.AbsoluteUri
    ReleaseCommit = $presenter.ReleaseCommit
    ExternalMachine = $machine.MachineName
    Presenter = $presenter.Presenter
    EvidenceMaximumAgeHours = $MaxEvidenceAgeHours
} | Format-List

Write-Output 'COMPANY_DEMO_ACCEPTANCE_PASSED'
