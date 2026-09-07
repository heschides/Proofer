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

$connection = [System.Data.SqlClient.SqlConnection]::new(
    "Server=$server;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;")
$connection.AccessToken = $token
$connection.Open()
try {
    $lock = $connection.CreateCommand()
    $lock.CommandTimeout = 70
    $lock.CommandText = @'
DECLARE @result int;
EXEC @result=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=60000;
SELECT @result;
'@
    if ([int]$lock.ExecuteScalar() -lt 0) { throw 'The Demo is busy; scheduled reset did not begin.' }

    $command = $connection.CreateCommand()
    $command.CommandTimeout = 900
    $command.CommandText = 'EXEC dbo.SatiResetToCanonicalBaseline @RequestId, @ActorUserId;'
    [void]$command.Parameters.AddWithValue('@RequestId', [Guid]::NewGuid())
    [void]$command.Parameters.AddWithValue('@ActorUserId', 0)
    [void]$command.ExecuteNonQuery()

    $seed = Join-Path $PSScriptRoot 'Seed-DemoShowcaseData.ps1'
    if (-not (Test-Path -LiteralPath $seed -PathType Leaf)) {
        throw "The versioned Demo seed is missing at '$seed'."
    }

    Write-Host "Starting rolling-date refresh after full baseline restoration for $([DateTime]::Today.ToString('yyyy-MM-dd'))."
    & $seed -SqlServer $server -Database 'SatiDemo' -AccessToken $token -AsOfDate ([DateTime]::Today)
    Write-Host 'Canonical Demo caseload refresh completed and passed validation.'
}
finally {
    if ($connection.State -eq [System.Data.ConnectionState]::Open) {
        $release = $connection.CreateCommand()
        $release.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'SatiDemo.FullReset', @LockOwner=N'Session';"
        [void]$release.ExecuteNonQuery()
    }
    $connection.Dispose()
}
