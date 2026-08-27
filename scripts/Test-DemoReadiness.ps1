param(
    [string]$BaseAddress = "https://sati-demo-api-satilogica.azurewebsites.net/",
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedReleaseVersion = '1.2.27',
    [switch]$HealthOnly,
    [string]$EvidencePath
)

$ErrorActionPreference = "Stop"
$baseUri = [Uri]$BaseAddress
if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -cne "https") {
    throw "BaseAddress must be an absolute HTTPS address."
}

function Get-DemoUri([string]$RelativePath) {
    return [Uri]::new($baseUri, $RelativePath)
}

function Assert-Health([string]$Path) {
    $response = Invoke-WebRequest -Uri (Get-DemoUri $Path) -Method Get -TimeoutSec 90 -UseBasicParsing
    if ($response.StatusCode -ne 200) {
        throw "$Path returned HTTP $($response.StatusCode)."
    }
}

function Write-ReadinessEvidence([System.Collections.IDictionary]$Evidence) {
    if ([string]::IsNullOrWhiteSpace($EvidencePath)) {
        return
    }

    $resolvedEvidence = [System.IO.Path]::GetFullPath($EvidencePath)
    $evidenceParent = [System.IO.Path]::GetDirectoryName($resolvedEvidence)
    if ([string]::IsNullOrWhiteSpace($evidenceParent)) {
        throw 'EvidencePath must include a parent directory.'
    }
    [System.IO.Directory]::CreateDirectory($evidenceParent) | Out-Null
    if (Test-Path -LiteralPath $resolvedEvidence) {
        throw "Refusing to overwrite readiness evidence: $resolvedEvidence"
    }
    $Evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resolvedEvidence -Encoding UTF8
    Write-Output "READINESS_EVIDENCE_WRITTEN path=$resolvedEvidence"
}

Write-Host "Checking Demo liveness and readiness..."
Assert-Health "health/live"
Assert-Health "health/ready"
try {
    $release = Invoke-RestMethod -Uri (Get-DemoUri "health/version") -Method Get -TimeoutSec 90
}
catch {
    $statusCode = $_.Exception.Response.StatusCode.value__
    if ($statusCode -eq 404) {
        throw "The Demo API passed live/ready health checks but does not expose /health/version. The deployed API predates the $ExpectedReleaseVersion acceptance contract; deploy the matching API release before collecting readiness evidence."
    }

    throw "The Demo API version check failed: $($_.Exception.Message)"
}
if ($release.product -cne 'Sati.Api') {
    throw "The Demo version endpoint reported unexpected product '$($release.product)'."
}
if ($release.releaseVersion -cne $ExpectedReleaseVersion) {
    throw "The Demo API release '$($release.releaseVersion)' does not match expected release '$ExpectedReleaseVersion'."
}

if ($HealthOnly) {
    $healthResult = [ordered]@{
        SchemaVersion = 1
        Gate = 'AuthenticatedApiReadiness'
        Passed = $true
        CapturedUtc = [DateTime]::UtcNow.ToString('O')
        BaseAddress = $baseUri.AbsoluteUri
        Live = "Healthy"
        Ready = "Healthy"
        ReleaseVersion = $release.releaseVersion
        Authenticated = $false
        AuthenticatedChecks = "Skipped"
    }
    Write-ReadinessEvidence $healthResult
    [pscustomobject]$healthResult | Format-List
    exit 0
}

$username = [Environment]::GetEnvironmentVariable("SATI_DEMO_USERNAME")
$password = [Environment]::GetEnvironmentVariable("SATI_DEMO_PASSWORD")
if ([string]::IsNullOrWhiteSpace($username) -or [string]::IsNullOrWhiteSpace($password)) {
    throw "Set SATI_DEMO_USERNAME and SATI_DEMO_PASSWORD for the designated synthetic Admin account, or use -HealthOnly."
}

Write-Host "Checking authenticated Admin read paths..."
$loginBody = @{ username = $username; password = $password } | ConvertTo-Json -Compress
$login = Invoke-RestMethod `
    -Uri (Get-DemoUri "api/v1/auth/login") `
    -Method Post `
    -ContentType "application/json" `
    -Body $loginBody `
    -TimeoutSec 90
if ([string]::IsNullOrWhiteSpace($login.accessToken)) {
    throw "The Demo login response did not contain an access token."
}
if ($login.user.role -cne "Admin") {
    throw "The readiness account must have the Admin role."
}

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$overview = Invoke-RestMethod -Uri (Get-DemoUri "api/v1/admin/overview") -Headers $headers -TimeoutSec 90
$operations = Invoke-RestMethod -Uri (Get-DemoUri "api/v1/admin/operations") -Headers $headers -TimeoutSec 90
$people = @(Invoke-RestMethod -Uri (Get-DemoUri "api/v1/admin/people") -Headers $headers -TimeoutSec 90)
$activity = @(Invoke-RestMethod -Uri (Get-DemoUri "api/v1/admin/activity?days=30&take=5") -Headers $headers -TimeoutSec 90)

if ($overview.agencyId -ne $login.user.agencyId) {
    throw "Admin overview agency did not match the authenticated session."
}
if ($operations.databaseStatus -cne "Healthy") {
    throw "Admin operations did not report a healthy database."
}
if ($operations.retentionEnforcementMode -cne "PolicyOnly") {
    throw "The Demo reported an unexpected retention enforcement mode."
}
if ($people.Count -lt 1) {
    throw "The Admin Person directory was empty."
}

$authenticatedResult = [ordered]@{
    SchemaVersion = 1
    Gate = 'AuthenticatedApiReadiness'
    Passed = $true
    CapturedUtc = [DateTime]::UtcNow.ToString('O')
    BaseAddress = $baseUri.AbsoluteUri
    Live = "Healthy"
    Ready = "Healthy"
    ReleaseVersion = $release.releaseVersion
    Authenticated = $true
    AccountRole = $login.user.role
    AgencyId = $overview.agencyId
    Users = $overview.userCount
    People = $overview.personCount
    RecentActivityRows = $activity.Count
    RetentionMode = $operations.retentionEnforcementMode
    AuditEvents = $operations.auditEventCount
    EdiReplayFiles = $operations.ediReplayCount
}
Write-ReadinessEvidence $authenticatedResult
[pscustomobject]$authenticatedResult | Format-List
