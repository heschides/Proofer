param($Timer)

$ErrorActionPreference = 'Stop'
$server = $env:SATI_DEMO_SQL_SERVER
if ([string]::IsNullOrWhiteSpace($server)) {
    throw 'SATI_DEMO_SQL_SERVER is required.'
}

$identityEndpoint = $env:IDENTITY_ENDPOINT
$identityHeader = $env:IDENTITY_HEADER
if ([string]::IsNullOrWhiteSpace($identityEndpoint) -or
    [string]::IsNullOrWhiteSpace($identityHeader)) {
    throw 'The Function App managed-identity endpoint is unavailable.'
}

$resource = [Uri]::EscapeDataString('https://database.windows.net/')
$separator = if ($identityEndpoint.Contains('?')) { '&' } else { '?' }
$tokenUri = "$identityEndpoint${separator}api-version=2019-08-01&resource=$resource"
$tokenResponse = Invoke-RestMethod -Method Get -Uri $tokenUri -Headers @{
    'X-IDENTITY-HEADER' = $identityHeader
    'Metadata' = 'true'
}
$token = [string]$tokenResponse.access_token
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Managed identity did not return an Azure SQL access token.'
}

$seed = Join-Path $PSScriptRoot 'Seed-DemoShowcaseData.ps1'
if (-not (Test-Path -LiteralPath $seed -PathType Leaf)) {
    throw "The versioned Demo seed is missing at '$seed'."
}

Write-Host "Starting canonical Demo caseload refresh for $([DateTime]::Today.ToString('yyyy-MM-dd'))."
& $seed -SqlServer $server -Database 'SatiDemo' -AccessToken $token -AsOfDate ([DateTime]::Today)
Write-Host 'Canonical Demo caseload refresh completed and passed validation.'
