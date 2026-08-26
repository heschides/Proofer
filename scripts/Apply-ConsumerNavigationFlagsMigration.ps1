<#
.SYNOPSIS
    Applies and verifies the additive consumer-navigation flags migration.

.DESCRIPTION
    This is deliberately not a raw EF script. Long-lived Sati databases have
    migration-history drift, so it checks the database/environment marker and
    actual columns, is transactional and rerunnable, and records the migration
    only after the schema is confirmed.
#>
[CmdletBinding()]
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken,

    [switch]$InspectOnly
)

$ErrorActionPreference = 'Stop'
$expectedEnvironment = if ($DatabaseName -ceq 'SatiDemo') { 'Demo' } else { 'Production' }
$migrationId = '20260825144021_AddConsumerNavigationFlags'
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)
$connectionString = if ($usesAccessToken) {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
} else {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if ($usesAccessToken) { $connection.AccessToken = $AccessToken }
$connection.Open()
try {
    $preflight = $connection.CreateCommand()
    $preflight.CommandTimeout = 120
    $preflight.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $preflight.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $preflight.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $preflight.CommandText = @'
SET NOCOUNT ON;
IF DB_NAME() <> @expectedDatabase THROW 51600, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51601, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL THROW 51602, 'dbo.People does not exist.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THROW 51603, 'The EF migration history table is missing.', 1;
SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END AS bit) AS HistoryPresent,
       CAST(CASE WHEN COL_LENGTH(N'dbo.People', N'CaseManagerIsDhhsRepresentative') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS DhhsRepresentativePresent,
       CAST(CASE WHEN COL_LENGTH(N'dbo.People', N'UsesModivcare') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS ModivcarePresent;
'@
    $reader = $preflight.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration preflight row was not returned.' }
    $result = [ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        PersonCount = $reader.GetInt64(2)
        HistoryPresent = $reader.GetBoolean(3)
        DhhsRepresentativePresent = $reader.GetBoolean(4)
        ModivcarePresent = $reader.GetBoolean(5)
    }
    $reader.Close()
    if ($InspectOnly) { [pscustomobject]$result; return }

    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> @expectedDatabase THROW 51600, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51601, 'The database identity marker does not match the requested environment.', 1;

DECLARE @columnsAdded int = 0;
DECLARE @historyBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.People', N'CaseManagerIsDhhsRepresentative') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD CaseManagerIsDhhsRepresentative bit NOT NULL
        CONSTRAINT DF_People_CaseManagerIsDhhsRepresentative DEFAULT (0) WITH VALUES;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.People', N'UsesModivcare') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD UsesModivcare bit NOT NULL
        CONSTRAINT DF_People_UsesModivcare DEFAULT (0) WITH VALUES;
    SET @columnsAdded += 1;
END;
IF NOT EXISTS (SELECT 1 FROM sys.columns c INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People') AND c.name = N'CaseManagerIsDhhsRepresentative'
      AND t.name = N'bit' AND c.is_nullable = 0)
    THROW 51604, 'CaseManagerIsDhhsRepresentative exists with an unexpected SQL definition.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.columns c INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People') AND c.name = N'UsesModivcare'
      AND t.name = N'bit' AND c.is_nullable = 0)
    THROW 51605, 'UsesModivcare exists with an unexpected SQL definition.', 1;
IF @historyBefore = 0
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion) VALUES (@migrationId, N'10.0.10');
COMMIT TRANSACTION;
SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @columnsAdded AS ColumnsAdded,
       CAST(CASE WHEN @historyBefore = 0 THEN 1 ELSE 0 END AS bit) AS HistoryRowWritten,
       (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration verification row was not returned.' }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        ColumnsAdded = $reader.GetInt32(2)
        HistoryRowWritten = $reader.GetBoolean(3)
        MigrationCount = $reader.GetInt64(4)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
