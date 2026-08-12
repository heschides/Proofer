param(
    [string]$SourceDatabase = "Sati",
    [string]$ProductionDatabase = "SatiProduction",
    [string]$DemoDatabase = "SatiDemo",
    [int]$ProductionAgencyId = 1,
    [int]$DemoAgencyId = 2,
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$BackupDirectory = (Join-Path $env:LOCALAPPDATA "Sati\DatabaseSnapshots"),
    [string]$DatabaseFileDirectory = (Join-Path $env:LOCALAPPDATA "Sati\Databases")
)

$ErrorActionPreference = "Stop"

function Assert-SqlIdentifier([string]$Value, [string]$ParameterName) {
    if ($Value -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
        throw "$ParameterName must contain only letters, numbers, and underscores and must begin with a letter."
    }
}

function New-SqlConnection([string]$Database) {
    $connectionString = "Server=$SqlServer;Database=$Database;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
    return [System.Data.SqlClient.SqlConnection]::new($connectionString)
}

function Invoke-Sql([string]$Database, [string]$Sql, [hashtable]$Parameters = @{}) {
    $connection = New-SqlConnection $Database
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 300
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

function Invoke-SqlScalar([string]$Database, [string]$Sql, [hashtable]$Parameters = @{}) {
    $connection = New-SqlConnection $Database
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 300
        $command.CommandText = $Sql
        foreach ($entry in $Parameters.GetEnumerator()) {
            [void]$command.Parameters.AddWithValue("@$($entry.Key)", $entry.Value)
        }
        return $command.ExecuteScalar()
    }
    finally {
        $connection.Dispose()
    }
}

function Restore-DatabaseCopy([string]$TargetDatabase, [string]$BackupFile, [string]$LogicalDataName, [string]$LogicalLogName) {
    $dataFile = Join-Path $DatabaseFileDirectory "$TargetDatabase.mdf"
    $logFile = Join-Path $DatabaseFileDirectory "$($TargetDatabase)_log.ldf"
    $sql = @"
RESTORE DATABASE [$TargetDatabase]
FROM DISK = @BackupFile
WITH MOVE @LogicalDataName TO @DataFile,
     MOVE @LogicalLogName TO @LogFile,
     RECOVERY, CHECKSUM;
"@
    Invoke-Sql "master" $sql @{
        BackupFile = $BackupFile
        LogicalDataName = $LogicalDataName
        LogicalLogName = $LogicalLogName
        DataFile = $dataFile
        LogFile = $logFile
    } | Out-Null
}

function Set-DatabaseEnvironment([string]$Database, [string]$EnvironmentName, [int]$KeepAgencyId) {
    $sql = @"
SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE #ExcludedUsers (Id int NOT NULL PRIMARY KEY);
INSERT #ExcludedUsers (Id)
SELECT Id FROM dbo.Users WHERE AgencyId <> @KeepAgencyId;

CREATE TABLE #ExcludedPeople (Id int NOT NULL PRIMARY KEY);
INSERT #ExcludedPeople (Id)
SELECT Id
FROM dbo.People
WHERE UserId IN (SELECT Id FROM #ExcludedUsers);

DELETE claimLine
FROM dbo.ClaimLines claimLine
JOIN dbo.Notes note ON note.Id = claimLine.NoteId
WHERE note.PersonId IN (SELECT Id FROM #ExcludedPeople);

DELETE FROM dbo.BillingPeriods
WHERE UserId IN (SELECT Id FROM #ExcludedUsers);

DELETE FROM dbo.ComprehensiveAssessments
WHERE PersonId IN (SELECT Id FROM #ExcludedPeople)
   OR AuthorUserId IN (SELECT Id FROM #ExcludedUsers)
   OR ApprovedByUserId IN (SELECT Id FROM #ExcludedUsers);

DELETE FROM dbo.ATRequests
WHERE PersonId IN (SELECT Id FROM #ExcludedPeople);

DELETE FROM dbo.People
WHERE Id IN (SELECT Id FROM #ExcludedPeople);

DELETE FROM dbo.Scratchpad WHERE UserId IN (SELECT Id FROM #ExcludedUsers);
DELETE FROM dbo.ExemptDates WHERE UserId IN (SELECT Id FROM #ExcludedUsers);
DELETE FROM dbo.Incentives WHERE UserId IN (SELECT Id FROM #ExcludedUsers);

UPDATE dbo.Notes
SET ReturnedById = CASE WHEN ReturnedById IN (SELECT Id FROM #ExcludedUsers) THEN NULL ELSE ReturnedById END,
    ApprovedById = CASE WHEN ApprovedById IN (SELECT Id FROM #ExcludedUsers) THEN NULL ELSE ApprovedById END,
    OverrideApprovedById = CASE WHEN OverrideApprovedById IN (SELECT Id FROM #ExcludedUsers) THEN NULL ELSE OverrideApprovedById END;

UPDATE dbo.Users SET SupervisorId = NULL
WHERE SupervisorId IN (SELECT Id FROM #ExcludedUsers)
   OR Id IN (SELECT Id FROM #ExcludedUsers);

DELETE FROM dbo.Users
WHERE Id IN (SELECT Id FROM #ExcludedUsers);

IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SatiDatabaseIdentity
    (
        Id tinyint NOT NULL CONSTRAINT PK_SatiDatabaseIdentity PRIMARY KEY,
        EnvironmentName nvarchar(20) NOT NULL,
        InstanceId uniqueidentifier NOT NULL,
        CreatedAtUtc datetime2 NOT NULL,
        CONSTRAINT CK_SatiDatabaseIdentity_SingleRow CHECK (Id = 1)
    );
END;

DELETE FROM dbo.SatiDatabaseIdentity;
INSERT dbo.SatiDatabaseIdentity (Id, EnvironmentName, InstanceId, CreatedAtUtc)
VALUES (1, @EnvironmentName, NEWID(), SYSUTCDATETIME());

IF EXISTS (SELECT 1 FROM dbo.Users WHERE AgencyId <> @KeepAgencyId)
    THROW 51000, 'Unexpected user ownership remains after environment split.', 1;
IF EXISTS
(
    SELECT 1
    FROM dbo.People person
    JOIN dbo.Users owner ON owner.Id = person.UserId
    WHERE owner.AgencyId <> @KeepAgencyId
)
    THROW 51001, 'Unexpected person ownership remains after environment split.', 1;

COMMIT TRANSACTION;
"@
    Invoke-Sql $Database $sql @{ KeepAgencyId = $KeepAgencyId; EnvironmentName = $EnvironmentName } | Out-Null
}

Assert-SqlIdentifier $SourceDatabase "SourceDatabase"
Assert-SqlIdentifier $ProductionDatabase "ProductionDatabase"
Assert-SqlIdentifier $DemoDatabase "DemoDatabase"

if ($SourceDatabase -in @($ProductionDatabase, $DemoDatabase) -or $ProductionDatabase -eq $DemoDatabase) {
    throw "Source, production, and demo database names must all be different."
}

$sourceExists = [int](Invoke-SqlScalar "master" "SELECT COUNT(*) FROM sys.databases WHERE name = @Name" @{ Name = $SourceDatabase })
$productionExists = [int](Invoke-SqlScalar "master" "SELECT COUNT(*) FROM sys.databases WHERE name = @Name" @{ Name = $ProductionDatabase })
$demoExists = [int](Invoke-SqlScalar "master" "SELECT COUNT(*) FROM sys.databases WHERE name = @Name" @{ Name = $DemoDatabase })

if ($sourceExists -ne 1) { throw "Source database '$SourceDatabase' does not exist." }
if ($productionExists -ne 0) { throw "Target database '$ProductionDatabase' already exists. Nothing was changed." }
if ($demoExists -ne 0) { throw "Target database '$DemoDatabase' already exists. Nothing was changed." }

[System.IO.Directory]::CreateDirectory($BackupDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($DatabaseFileDirectory) | Out-Null

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $BackupDirectory "$SourceDatabase-before-environment-split-$timestamp.bak"

$logicalDataName = [string](Invoke-SqlScalar $SourceDatabase "SELECT TOP (1) name FROM sys.database_files WHERE type_desc = 'ROWS' ORDER BY file_id")
$logicalLogName = [string](Invoke-SqlScalar $SourceDatabase "SELECT TOP (1) name FROM sys.database_files WHERE type_desc = 'LOG' ORDER BY file_id")
if ([string]::IsNullOrWhiteSpace($logicalDataName) -or [string]::IsNullOrWhiteSpace($logicalLogName)) {
    throw "Could not resolve the source database logical file names."
}

Invoke-Sql "master" "BACKUP DATABASE [$SourceDatabase] TO DISK = @BackupFile WITH COPY_ONLY, INIT, CHECKSUM;" @{ BackupFile = $backupFile } | Out-Null
Invoke-Sql "master" "RESTORE VERIFYONLY FROM DISK = @BackupFile WITH CHECKSUM;" @{ BackupFile = $backupFile } | Out-Null

Restore-DatabaseCopy $ProductionDatabase $backupFile $logicalDataName $logicalLogName
Restore-DatabaseCopy $DemoDatabase $backupFile $logicalDataName $logicalLogName

Set-DatabaseEnvironment $ProductionDatabase "Production" $ProductionAgencyId
Set-DatabaseEnvironment $DemoDatabase "Demo" $DemoAgencyId

$productionUsers = Invoke-SqlScalar $ProductionDatabase "SELECT COUNT(*) FROM dbo.Users"
$productionPeople = Invoke-SqlScalar $ProductionDatabase "SELECT COUNT(*) FROM dbo.People"
$demoUsers = Invoke-SqlScalar $DemoDatabase "SELECT COUNT(*) FROM dbo.Users"
$demoPeople = Invoke-SqlScalar $DemoDatabase "SELECT COUNT(*) FROM dbo.People"

Write-Output "SOURCE_PRESERVED database=$SourceDatabase"
Write-Output "BACKUP_CREATED path=$backupFile"
Write-Output "PRODUCTION_READY database=$ProductionDatabase users=$productionUsers people=$productionPeople"
Write-Output "DEMO_READY database=$DemoDatabase users=$demoUsers people=$demoPeople"
