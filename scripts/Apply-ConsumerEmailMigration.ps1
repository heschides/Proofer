<#
.SYNOPSIS
    Applies and verifies the additive consumer-email migration.

.DESCRIPTION
    This controlled runner checks the exact database and environment marker,
    applies only the nullable People.Email column, verifies its SQL shape, and
    records the EF migration only after the schema is confirmed. It is
    transactional and safe to inspect or rerun.
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
$migrationId = '20260825163103_AddConsumerEmail'
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
IF DB_NAME() <> @expectedDatabase THROW 51610, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51611, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL THROW 51612, 'dbo.People does not exist.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL THROW 51613, 'The EF migration history table is missing.', 1;
SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
       CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END AS bit) AS HistoryPresent,
       CAST(CASE WHEN COL_LENGTH(N'dbo.People', N'Email') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS EmailPresent;
'@
    $reader = $preflight.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration preflight row was not returned.' }
    $result = [ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        PersonCount = $reader.GetInt64(2)
        HistoryPresent = $reader.GetBoolean(3)
        EmailPresent = $reader.GetBoolean(4)
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
IF DB_NAME() <> @expectedDatabase THROW 51610, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51611, 'The database identity marker does not match the requested environment.', 1;

DECLARE @columnAdded bit = 0;
DECLARE @historyBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END;
BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.People', N'Email') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD Email nvarchar(254) NULL;
    SET @columnAdded = 1;
END;
IF NOT EXISTS (SELECT 1 FROM sys.columns c INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People') AND c.name = N'Email'
      AND t.name = N'nvarchar' AND c.max_length = 508 AND c.is_nullable = 1)
    THROW 51614, 'Email exists with an unexpected SQL definition.', 1;
IF @historyBefore = 0
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion) VALUES (@migrationId, N'10.0.10');
COMMIT TRANSACTION;
SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @columnAdded AS ColumnAdded,
       CAST(CASE WHEN @historyBefore = 0 THEN 1 ELSE 0 END AS bit) AS HistoryRowWritten,
       (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration verification row was not returned.' }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        ColumnAdded = $reader.GetBoolean(2)
        HistoryRowWritten = $reader.GetBoolean(3)
        MigrationCount = $reader.GetInt64(4)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
