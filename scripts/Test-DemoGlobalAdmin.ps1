param(
    [string]$BaseAddress = 'https://sati-demo-api-satilogica.azurewebsites.net/',
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedReleaseVersion = '1.2.46',
    [string]$CredentialPath = (Join-Path $env:LOCALAPPDATA 'SatiLogica\Sati\Credentials\demo-global-admin.xml'),
    [string]$EvidencePath
)

$ErrorActionPreference = 'Stop'
$baseUri = [Uri]$BaseAddress
if (-not $baseUri.IsAbsoluteUri -or $baseUri.Scheme -cne 'https') {
    throw 'BaseAddress must be an absolute HTTPS address.'
}
$resolvedCredential = [System.IO.Path]::GetFullPath($CredentialPath)
if (-not (Test-Path -LiteralPath $resolvedCredential -PathType Leaf)) {
    throw "Encrypted Global Admin credential not found: $resolvedCredential"
}

function Get-DemoUri([string]$RelativePath) {
    return [Uri]::new($baseUri, $RelativePath)
}

$version = Invoke-RestMethod -Uri (Get-DemoUri 'health/version') -TimeoutSec 90
if ($version.releaseVersion -cne $ExpectedReleaseVersion) {
    throw "Demo API release '$($version.releaseVersion)' does not match '$ExpectedReleaseVersion'."
}

$credential = Import-Clixml -LiteralPath $resolvedCredential
$plainPassword = $credential.GetNetworkCredential().Password
try {
    $loginBody = @{
        username = $credential.UserName
        password = $plainPassword
    } | ConvertTo-Json -Compress
    $login = Invoke-RestMethod `
        -Uri (Get-DemoUri 'api/v1/auth/login') `
        -Method Post `
        -ContentType 'application/json' `
        -Body $loginBody `
        -TimeoutSec 90
    if ($login.user.role -cne 'PlatformOperator') {
        throw "The Global Admin account returned unexpected role '$($login.user.role)'."
    }
    $headers = @{ Authorization = "Bearer $($login.accessToken)" }
    $dashboard = Invoke-RestMethod `
        -Uri (Get-DemoUri 'api/v1/platform/incidents?days=30&take=100') `
        -Headers $headers `
        -TimeoutSec 90

    $agencyRouteForbidden = $false
    try {
        Invoke-WebRequest -Uri (Get-DemoUri 'api/v1/providers') -Headers $headers `
            -UseBasicParsing -TimeoutSec 90 | Out-Null
    }
    catch {
        $agencyRouteForbidden = $_.Exception.Response.StatusCode.value__ -eq 403
    }
    if (-not $agencyRouteForbidden) {
        throw 'The Global Admin account was not forbidden from the agency provider route.'
    }

    # Use a deliberately incorrect current password so this verifies that the
    # self-service route is reachable without changing the stored credential.
    $selfPasswordRouteReachable = $false
    try {
        $passwordProbe = @{
            currentPassword = 'deliberately-incorrect-probe'
            newPassword = 'ProbePassword123!'
        } | ConvertTo-Json -Compress
        Invoke-WebRequest -Uri (Get-DemoUri 'api/v1/users/me/password') -Headers $headers `
            -Method Put -ContentType 'application/json' -Body $passwordProbe `
            -UseBasicParsing -TimeoutSec 90 | Out-Null
    }
    catch {
        $selfPasswordRouteReachable = $_.Exception.Response.StatusCode.value__ -eq 400
    }
    if (-not $selfPasswordRouteReachable) {
        throw 'The Global Admin self-service password route was not reachable.'
    }

    $result = [ordered]@{
        SchemaVersion = 1
        Gate = 'GlobalAdminApiReadiness'
        Passed = $true
        CapturedUtc = [DateTime]::UtcNow.ToString('O')
        BaseAddress = $baseUri.AbsoluteUri
        ReleaseVersion = $version.releaseVersion
        Username = $credential.UserName
        Role = $login.user.role
        AgencyHealthRows = @($dashboard.agencies).Count
        IncidentGroups = @($dashboard.incidents).Count
        OverallScore = $dashboard.overallHealth.score
        AgencyBusinessRoute = 'Forbidden'
        SelfPasswordRoute = 'Reachable; non-mutating invalid-current-password probe returned HTTP 400'
        CredentialStorage = 'Windows user-bound encrypted CLIXML; secret omitted'
    }

    if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
        $resolvedEvidence = [System.IO.Path]::GetFullPath($EvidencePath)
        $evidenceParent = [System.IO.Path]::GetDirectoryName($resolvedEvidence)
        if ([string]::IsNullOrWhiteSpace($evidenceParent)) {
            throw 'EvidencePath must include a parent directory.'
        }
        [System.IO.Directory]::CreateDirectory($evidenceParent) | Out-Null
        if (Test-Path -LiteralPath $resolvedEvidence) {
            throw "Refusing to overwrite Global Admin evidence: $resolvedEvidence"
        }
        $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $resolvedEvidence -Encoding UTF8
        Write-Output "GLOBAL_ADMIN_EVIDENCE_WRITTEN path=$resolvedEvidence"
    }

    [pscustomobject]$result | Format-List
}
finally {
    $plainPassword = $null
    $loginBody = $null
    $login = $null
}
