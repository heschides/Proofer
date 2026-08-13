[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [string]$FallbackPath,

    [Parameter(Mandatory)]
    [string]$EvidenceDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Presenter,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{7,40}$')]
    [string]$ReleaseCommit,

    [string]$BaseAddress = 'https://sati-demo-api-satilogica.azurewebsites.net/',

    [Parameter(Mandatory)]
    [switch]$ClientApiParityConfirmed,

    [Parameter(Mandatory)]
    [switch]$WalkthroughRehearsed,

    [Parameter(Mandatory)]
    [switch]$SyntheticDataOnly,

    [Parameter(Mandatory)]
    [switch]$FallbackApproved,

    [Parameter(Mandatory)]
    [switch]$LimitationsPresented,

    [switch]$Replace
)

$ErrorActionPreference = 'Stop'

$installer = [System.IO.Path]::GetFullPath($InstallerPath)
$fallback = [System.IO.Path]::GetFullPath($FallbackPath)
$evidenceRoot = [System.IO.Path]::GetFullPath($EvidenceDirectory)
$baseUri = [Uri]$BaseAddress

if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) {
    throw "Installer not found: $installer"
}
if (-not (Test-Path -LiteralPath $fallback -PathType Leaf)) {
    throw "Fallback not found: $fallback"
}
if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -cne 'https') {
    throw 'BaseAddress must be an absolute HTTPS address.'
}
if ([string]::IsNullOrWhiteSpace($Presenter)) {
    throw 'Presenter is required.'
}
$installerName = [System.IO.Path]::GetFileName($installer)
if ($installerName -notmatch '^SatiDemoSetup-(\d+\.\d+\.\d+)\.exe$') {
    throw "Unexpected installer name: $installerName"
}
$releaseVersion = $Matches[1]

$requiredConfirmations = [ordered]@{
    ClientApiParityConfirmed = $ClientApiParityConfirmed.IsPresent
    WalkthroughRehearsed = $WalkthroughRehearsed.IsPresent
    SyntheticDataOnly = $SyntheticDataOnly.IsPresent
    FallbackApproved = $FallbackApproved.IsPresent
    LimitationsPresented = $LimitationsPresented.IsPresent
}
$missing = @($requiredConfirmations.GetEnumerator() | Where-Object { -not $_.Value } | Select-Object -ExpandProperty Key)
if ($missing.Count -ne 0) {
    throw "Every presenter confirmation is required. Missing: $($missing -join ', ')"
}

[System.IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
$evidencePath = Join-Path $evidenceRoot 'presenter-attestation.json'
if ((Test-Path -LiteralPath $evidencePath) -and -not $Replace) {
    throw "Refusing to overwrite presenter evidence without -Replace: $evidencePath"
}

$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$fallbackHash = (Get-FileHash -LiteralPath $fallback -Algorithm SHA256).Hash.ToLowerInvariant()

[ordered]@{
    SchemaVersion = 1
    Gate = 'PresenterRehearsalAndFallback'
    Passed = $true
    CapturedUtc = [DateTime]::UtcNow.ToString('O')
    Presenter = $Presenter.Trim()
    ReleaseCommit = $ReleaseCommit.ToLowerInvariant()
    BaseAddress = $baseUri.AbsoluteUri
    InstallerFileName = $installerName
    InstallerSha256 = $installerHash
    ReleaseVersion = $releaseVersion
    FallbackFileName = [System.IO.Path]::GetFileName($fallback)
    FallbackSha256 = $fallbackHash
    ClientApiParityConfirmed = $true
    WalkthroughRehearsed = $true
    SyntheticDataOnly = $true
    FallbackApproved = $true
    LimitationsPresented = $true
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $evidencePath -Encoding UTF8

Write-Output "PRESENTER_ATTESTATION_WRITTEN path=$evidencePath"
Write-Output "PRESENTER_ARTIFACTS installerSha256=$installerHash fallbackSha256=$fallbackHash"
