[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('CaptureBaseline', 'VerifyBaseline', 'RestoreBaseline')]
    [string]$Action,

    [string]$BaselineDirectory = (Join-Path $env:LOCALAPPDATA 'Satilogica\Sati\DemoBaseline'),

    [switch]$ReplaceBaseline
)

$ErrorActionPreference = 'Stop'
$sqlServer = '(localdb)\MSSQLLocalDB'
$database = 'SatiDemo'
$baselineFileName = 'SatiDemo.canonical.bak'
$manifestFileName = 'SatiDemo.canonical.json'

function New-SqlConnection([string]$DatabaseName) {
    $connectionString =
        "Server=$sqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
    return [System.Data.SqlClient.SqlConnection]::new($connectionString)
}

function Invoke-SqlNonQuery(
    [string]$DatabaseName,
    [string]$Sql,
    [hashtable]$Parameters = @{}) {
    $connection = New-SqlConnection $DatabaseName
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 600
        $command.CommandText = $Sql
        foreach ($entry in $Parameters.GetEnumerator()) {
            [void]$command.Parameters.AddWithValue("@$($entry.Key)", $entry.Value)
        }
        return $command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

function Get-DemoSnapshot {
    $connection = New-SqlConnection $database
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 120
        $command.CommandText = @"
SELECT
    DB_NAME() AS DatabaseName,
    identityRow.EnvironmentName,
    CONVERT(nvarchar(36), identityRow.InstanceId) AS InstanceId,
    identityRow.CreatedAtUtc,
    (SELECT COUNT_BIG(*) FROM dbo.Users) AS UserCount,
    (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
    (SELECT COUNT_BIG(*) FROM dbo.Notes) AS NoteCount,
    (SELECT COUNT_BIG(*) FROM dbo.ComprehensiveAssessments) AS AssessmentCount,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount
FROM dbo.SatiDatabaseIdentity identityRow
WHERE identityRow.Id = 1;
"@
        $reader = $command.ExecuteReader()
        if (-not $reader.Read()) {
            throw 'The Demo database identity marker is missing.'
        }

        return [pscustomobject]@{
            DatabaseName = $reader.GetString(0)
            EnvironmentName = $reader.GetString(1)
            InstanceId = $reader.GetString(2)
            CreatedAtUtc = $reader.GetDateTime(3).ToUniversalTime().ToString('O')
            UserCount = $reader.GetInt64(4)
            PersonCount = $reader.GetInt64(5)
            NoteCount = $reader.GetInt64(6)
            AssessmentCount = $reader.GetInt64(7)
            MigrationCount = $reader.GetInt64(8)
        }
    }
    finally {
        $connection.Dispose()
    }
}

function Assert-DemoIdentity($Snapshot) {
    if ($Snapshot.DatabaseName -cne $database -or $Snapshot.EnvironmentName -cne 'Demo') {
        throw "Refusing to operate on database '$($Snapshot.DatabaseName)' marked '$($Snapshot.EnvironmentName)'."
    }
    if (-not [Guid]::TryParse($Snapshot.InstanceId, [ref]([Guid]::Empty))) {
        throw 'The Demo database has an invalid instance identity.'
    }
}

function Get-BaselinePaths {
    if ([string]::IsNullOrWhiteSpace($BaselineDirectory)) {
        throw 'BaselineDirectory is required.'
    }

    $directory = [System.IO.Path]::GetFullPath($BaselineDirectory)
    $root = [System.IO.Path]::GetPathRoot($directory)
    if ($directory -eq $root) {
        throw 'BaselineDirectory cannot be a drive root.'
    }

    return [pscustomobject]@{
        Directory = $directory
        Backup = Join-Path $directory $baselineFileName
        Manifest = Join-Path $directory $manifestFileName
    }
}

function Read-ValidatedManifest($Paths, $CurrentSnapshot) {
    if (-not (Test-Path -LiteralPath $Paths.Backup -PathType Leaf) -or
        -not (Test-Path -LiteralPath $Paths.Manifest -PathType Leaf)) {
        throw "The canonical Demo baseline is incomplete under '$($Paths.Directory)'."
    }

    $manifest = Get-Content -LiteralPath $Paths.Manifest -Raw | ConvertFrom-Json
    if ($manifest.formatVersion -ne 1 -or
        $manifest.databaseName -cne $database -or
        $manifest.environmentName -cne 'Demo' -or
        -not [Guid]::TryParse([string]$manifest.instanceId, [ref]([Guid]::Empty))) {
        throw 'The canonical Demo baseline manifest is invalid.'
    }
    if ($manifest.instanceId -cne $CurrentSnapshot.InstanceId) {
        throw 'The baseline belongs to a different Demo database instance. Capture a deliberate new baseline instead.'
    }

    $actualHash = (Get-FileHash -LiteralPath $Paths.Backup -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne ([string]$manifest.sha256).ToLowerInvariant()) {
        throw 'The canonical Demo baseline checksum does not match. Nothing was restored.'
    }

    Invoke-SqlNonQuery 'master' 'RESTORE VERIFYONLY FROM DISK = @BackupFile WITH CHECKSUM;' @{
        BackupFile = $Paths.Backup
    } | Out-Null
    return $manifest
}

$paths = Get-BaselinePaths
$current = Get-DemoSnapshot
Assert-DemoIdentity $current

switch ($Action) {
    'CaptureBaseline' {
        [System.IO.Directory]::CreateDirectory($paths.Directory) | Out-Null
        $backupExists = Test-Path -LiteralPath $paths.Backup -PathType Leaf
        $manifestExists = Test-Path -LiteralPath $paths.Manifest -PathType Leaf
        if (($backupExists -or $manifestExists) -and -not $ReplaceBaseline) {
            throw 'A canonical Demo baseline already exists. Use -ReplaceBaseline only after deliberately approving the new state.'
        }
        if ($backupExists -ne $manifestExists) {
            throw 'The existing baseline is incomplete. Resolve it manually before capturing another baseline.'
        }

        if ($ReplaceBaseline -and $backupExists) {
            $archiveTag = Get-Date -Format 'yyyyMMdd-HHmmss'
            Move-Item -LiteralPath $paths.Backup -Destination (Join-Path $paths.Directory "SatiDemo.previous-$archiveTag.bak")
            Move-Item -LiteralPath $paths.Manifest -Destination (Join-Path $paths.Directory "SatiDemo.previous-$archiveTag.json")
        }

        $pendingTag = [Guid]::NewGuid().ToString('N')
        $pendingBackup = Join-Path $paths.Directory "SatiDemo.pending-$pendingTag.bak"
        $pendingManifest = Join-Path $paths.Directory "SatiDemo.pending-$pendingTag.json"
        try {
            Invoke-SqlNonQuery 'master' @"
BACKUP DATABASE [$database]
TO DISK = @BackupFile
WITH COPY_ONLY, INIT, CHECKSUM;
RESTORE VERIFYONLY FROM DISK = @BackupFile WITH CHECKSUM;
"@ @{ BackupFile = $pendingBackup } | Out-Null

            $hash = (Get-FileHash -LiteralPath $pendingBackup -Algorithm SHA256).Hash.ToLowerInvariant()
            $manifest = [ordered]@{
                formatVersion = 1
                databaseName = $current.DatabaseName
                environmentName = $current.EnvironmentName
                instanceId = $current.InstanceId
                capturedAtUtc = [DateTime]::UtcNow.ToString('O')
                databaseCreatedAtUtc = $current.CreatedAtUtc
                sha256 = $hash
                userCount = $current.UserCount
                personCount = $current.PersonCount
                noteCount = $current.NoteCount
                assessmentCount = $current.AssessmentCount
                migrationCount = $current.MigrationCount
            }
            Set-Content -LiteralPath $pendingManifest -Value ($manifest | ConvertTo-Json) -Encoding UTF8
            Move-Item -LiteralPath $pendingBackup -Destination $paths.Backup
            Move-Item -LiteralPath $pendingManifest -Destination $paths.Manifest
        }
        finally {
            if (Test-Path -LiteralPath $pendingBackup -PathType Leaf) {
                Remove-Item -LiteralPath $pendingBackup -Force
            }
            if (Test-Path -LiteralPath $pendingManifest -PathType Leaf) {
                Remove-Item -LiteralPath $pendingManifest -Force
            }
        }

        Write-Output "DEMO_BASELINE_CREATED path=$($paths.Backup) sha256=$hash"
        Write-Output "DEMO_BASELINE_COUNTS users=$($current.UserCount) people=$($current.PersonCount) notes=$($current.NoteCount) assessments=$($current.AssessmentCount) migrations=$($current.MigrationCount)"
    }

    'VerifyBaseline' {
        $manifest = Read-ValidatedManifest $paths $current
        Write-Output "DEMO_BASELINE_VERIFIED path=$($paths.Backup) sha256=$($manifest.sha256)"
        Write-Output "DEMO_CURRENT_IDENTITY instanceId=$($current.InstanceId) environment=$($current.EnvironmentName)"
    }

    'RestoreBaseline' {
        $manifest = Read-ValidatedManifest $paths $current
        $runningClients = @(Get-Process -Name 'Sati', 'Sati.Demo' -ErrorAction SilentlyContinue)
        if ($runningClients.Count -ne 0) {
            throw 'Close every Sati window before restoring the Demo baseline.'
        }

        try {
            Invoke-SqlNonQuery 'master' "ALTER DATABASE [$database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;" | Out-Null
            Invoke-SqlNonQuery 'master' @"
RESTORE DATABASE [$database]
FROM DISK = @BackupFile
WITH REPLACE, RECOVERY, CHECKSUM;
"@ @{ BackupFile = $paths.Backup } | Out-Null
        }
        finally {
            try {
                Invoke-SqlNonQuery 'master' "ALTER DATABASE [$database] SET MULTI_USER;" | Out-Null
            }
            catch {
                Write-Warning "SatiDemo could not be returned to MULTI_USER automatically: $($_.Exception.Message)"
            }
        }

        $restored = Get-DemoSnapshot
        Assert-DemoIdentity $restored
        $expectedCounts = @(
            @('users', [long]$manifest.userCount, $restored.UserCount),
            @('people', [long]$manifest.personCount, $restored.PersonCount),
            @('notes', [long]$manifest.noteCount, $restored.NoteCount),
            @('assessments', [long]$manifest.assessmentCount, $restored.AssessmentCount),
            @('migrations', [long]$manifest.migrationCount, $restored.MigrationCount)
        )
        foreach ($count in $expectedCounts) {
            if ($count[1] -ne $count[2]) {
                throw "The restored $($count[0]) count was $($count[2]); expected $($count[1])."
            }
        }
        if ($restored.InstanceId -cne $manifest.instanceId) {
            throw 'The restored database instance identity did not match the canonical baseline.'
        }

        Write-Output "DEMO_RESTORE_VERIFIED path=$($paths.Backup) sha256=$($manifest.sha256)"
        Write-Output "DEMO_RESTORED_COUNTS users=$($restored.UserCount) people=$($restored.PersonCount) notes=$($restored.NoteCount) assessments=$($restored.AssessmentCount) migrations=$($restored.MigrationCount)"
    }
}
