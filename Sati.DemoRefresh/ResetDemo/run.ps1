using namespace System.Net

param($Request, $TriggerMetadata)

$ErrorActionPreference = 'Stop'
$server = $env:SATI_DEMO_SQL_SERVER
$requestId = [Guid]::Empty
$actorUserId = 0
$stage = 'ValidateRequest'
if ([string]::IsNullOrWhiteSpace($server) -or
    -not [Guid]::TryParse([string]$Request.Body.requestId, [ref]$requestId) -or
    -not [int]::TryParse([string]$Request.Body.actorUserId, [ref]$actorUserId) -or
    $actorUserId -lt 1) {
    Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = [HttpStatusCode]::BadRequest
        Body = @{ error = 'A valid reset request is required.' }
    })
    return
}

function Get-SqlToken {
    $identityEndpoint = $env:IDENTITY_ENDPOINT
    $identityHeader = $env:IDENTITY_HEADER
    if ([string]::IsNullOrWhiteSpace($identityEndpoint) -or
        [string]::IsNullOrWhiteSpace($identityHeader)) {
        throw 'The Function App managed-identity endpoint is unavailable.'
    }
    $resource = [Uri]::EscapeDataString('https://database.windows.net/')
    $separator = if ($identityEndpoint.Contains('?')) { '&' } else { '?' }
    $tokenUri = "$identityEndpoint${separator}api-version=2019-08-01&resource=$resource"
    $result = Invoke-RestMethod -Method Get -Uri $tokenUri -Headers @{
        'X-IDENTITY-HEADER' = $identityHeader
        'Metadata' = 'true'
    }
    if ([string]::IsNullOrWhiteSpace([string]$result.access_token)) {
        throw 'Managed identity did not return an Azure SQL access token.'
    }
    return [string]$result.access_token
}

try {
    $stage = 'AcquireManagedIdentityToken'
    $token = Get-SqlToken
    $stage = 'OpenDemoDatabase'
    $connection = [System.Data.SqlClient.SqlConnection]::new(
        "Server=$server;Database=SatiDemo;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;")
    $connection.AccessToken = $token
    $connection.Open()
    try {
        $stage = 'AcquireExclusiveResetLock'
        $lock = $connection.CreateCommand()
        $lock.CommandTimeout = 70
        $lock.CommandText = @'
DECLARE @result int;
EXEC @result=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Session', @LockTimeout=60000;
SELECT @result;
'@
        if ([int]$lock.ExecuteScalar() -lt 0) { throw 'The Demo is busy; reset did not begin.' }

        $stage = 'RestoreCanonicalBaseline'
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 900
        $command.CommandText = 'EXEC dbo.SatiResetToCanonicalBaseline @RequestId, @ActorUserId;'
        [void]$command.Parameters.AddWithValue('@RequestId', $requestId)
        [void]$command.Parameters.AddWithValue('@ActorUserId', $actorUserId)
        [void]$command.ExecuteNonQuery()

        $stage = 'RollShowcaseDates'
        $seed = Join-Path $PSScriptRoot 'Seed-DemoShowcaseData.ps1'
        & $seed -SqlServer $server -Database 'SatiDemo' -AccessToken $token -AsOfDate ([DateTime]::Today)
        $stage = 'Completed'
        Write-Host "Full Demo reset completed. RequestId=$requestId ActorUserId=$actorUserId"
    }
    finally {
        if ($connection.State -eq [System.Data.ConnectionState]::Open) {
            $release = $connection.CreateCommand()
            $release.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'SatiDemo.FullReset', @LockOwner=N'Session';"
            [void]$release.ExecuteNonQuery()
        }
        $connection.Dispose()
    }

    Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = [HttpStatusCode]::OK
        Body = @{ requestId = $requestId; status = 'Completed' }
    })
}
catch {
    $exceptionTypes = [System.Collections.Generic.List[string]]::new()
    $sqlErrorNumbers = [System.Collections.Generic.List[int]]::new()
    $exception = $_.Exception
    while ($null -ne $exception) {
        $exceptionTypes.Add($exception.GetType().FullName)
        if ($exception -is [System.Data.SqlClient.SqlException]) {
            foreach ($sqlError in $exception.Errors) {
                $sqlErrorNumbers.Add([int]$sqlError.Number)
            }
        }
        $exception = $exception.InnerException
    }
    $safeTypes = @($exceptionTypes | Select-Object -Unique)
    $safeSqlNumbers = @($sqlErrorNumbers | Select-Object -Unique)
    Write-Error "Demo reset failed. RequestId=$requestId Stage=$stage ExceptionTypes=$($safeTypes -join ',') SqlErrorNumbers=$($safeSqlNumbers -join ',')"
    Push-OutputBinding -Name Response -Value ([HttpResponseContext]@{
        StatusCode = [HttpStatusCode]::InternalServerError
        Body = @{
            requestId = $requestId
            error = 'The Demo reset did not complete. Review the protected operation log before retrying.'
            failureStage = $stage
            exceptionTypes = $safeTypes
            sqlErrorNumbers = $safeSqlNumbers
        }
    })
}
