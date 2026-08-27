<#
.SYNOPSIS
    Applies and verifies the agency billing-compliance requirements migration.

.DESCRIPTION
    This controlled runner validates the exact Sati database identity, inspects the
    actual Settings schema as well as EF history, and applies only the additive,
    non-null integer requirement mask. It is transactional and rerunnable. Local
    data receives a full backup before the first change; Azure relies on its
    configured point-in-time recovery and requires an Entra access token.
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
$migrationId = '20260827141239_AddBillingComplianceRequirements'
$defaultRequirements = 31
$allSupportedRequirements = 511
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)
$connectionString = if ($usesAccessToken) {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
} else {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if ($usesAccessToken) { $connection.AccessToken = $AccessToken }
$connection.Open()

$backupPath = $null
try {
    $preflight = $connection.CreateCommand()
    $preflight.CommandTimeout = 120
    $preflight.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $preflight.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $preflight.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $preflight.CommandText = @'
SET NOCOUNT ON;
IF DB_NAME() <> @expectedDatabase
    THROW 51620, 'The connected database is not the one requested.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL OR NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51621, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.Settings', N'U') IS NULL
    THROW 51622, 'dbo.Settings does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51623, 'The EF migration history table is missing.', 1;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
       (SELECT COUNT_BIG(*) FROM dbo.Settings) AS SettingsCount,
       (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount,
       CAST(CASE WHEN EXISTS (
           SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
           THEN 1 ELSE 0 END AS bit) AS HistoryPresent,
       CAST(CASE WHEN COL_LENGTH(N'dbo.Settings', N'BillingComplianceRequirements') IS NOT NULL
           THEN 1 ELSE 0 END AS bit) AS ColumnPresent;
'@
    $preflightReader = $preflight.ExecuteReader()
    if (-not $preflightReader.Read()) { throw 'The migration preflight row was not returned.' }
    $result = [ordered]@{
        DatabaseName = $preflightReader.GetString(0)
        EnvironmentName = $preflightReader.GetString(1)
        PersonCount = $preflightReader.GetInt64(2)
        SettingsCount = $preflightReader.GetInt64(3)
        MigrationCount = $preflightReader.GetInt64(4)
        HistoryPresent = $preflightReader.GetBoolean(5)
        ColumnPresent = $preflightReader.GetBoolean(6)
    }
    $preflightReader.Close()

    if ($InspectOnly) { [pscustomobject]$result; return }

    $alreadyCurrent = $result.HistoryPresent -and $result.ColumnPresent
    if (-not $alreadyCurrent -and -not $usesAccessToken -and $result.PersonCount -gt 0) {
        $backupDirectory = Join-Path `
            ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
            'Sati\schema-backups'
        [System.IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
        $stamp = Get-Date -Format 'yyyy-MM-dd-HHmmss'
        $backupPath = Join-Path $backupDirectory "$DatabaseName-$stamp.bak"
        $escapedBackupPath = $backupPath.Replace("'", "''", [StringComparison]::Ordinal)
        $backup = $connection.CreateCommand()
        $backup.CommandTimeout = 600
        $backup.CommandText = "BACKUP DATABASE [$DatabaseName] TO DISK = '$escapedBackupPath' WITH INIT, SKIP, NOFORMAT;"
        $backup.ExecuteNonQuery() | Out-Null
    }

    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $command.Parameters.AddWithValue('@defaultRequirements', $defaultRequirements) | Out-Null
    $command.Parameters.AddWithValue('@allSupportedRequirements', $allSupportedRequirements) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> @expectedDatabase
    THROW 51620, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51621, 'The database identity marker does not match the requested environment.', 1;

DECLARE @columnAdded bit = 0;
DECLARE @historyBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END;

BEGIN TRANSACTION;
IF COL_LENGTH(N'dbo.Settings', N'BillingComplianceRequirements') IS NULL
BEGIN
    ALTER TABLE dbo.Settings
        ADD BillingComplianceRequirements int NOT NULL
            CONSTRAINT DF_Settings_BillingComplianceRequirements
            DEFAULT (31) WITH VALUES;
    SET @columnAdded = 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'BillingComplianceRequirements'
      AND t.name = N'int'
      AND c.max_length = 4
      AND c.is_nullable = 0)
    THROW 51624, 'BillingComplianceRequirements exists with an unexpected SQL definition.', 1;

DECLARE @invalidSettingsRows bigint;
EXEC sys.sp_executesql
    N'SELECT @invalidRows = COUNT_BIG(*)
      FROM dbo.Settings
      WHERE BillingComplianceRequirements < 0 OR
            (BillingComplianceRequirements & @supported) <> BillingComplianceRequirements;',
    N'@supported int, @invalidRows bigint OUTPUT',
    @supported = @allSupportedRequirements,
    @invalidRows = @invalidSettingsRows OUTPUT;
IF @invalidSettingsRows > 0
    THROW 51625, 'An existing billing compliance requirement mask contains unsupported values.', 1;

IF @historyBefore = 0
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.10');
COMMIT TRANSACTION;

DECLARE @historyWritten bit = CASE WHEN @historyBefore = 0 THEN 1 ELSE 0 END;
EXEC sys.sp_executesql
    N'SELECT DB_NAME() AS DatabaseName,
             (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
             @added AS ColumnAdded,
             @historyWritten AS HistoryRowWritten,
             (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount,
             (SELECT COUNT_BIG(*) FROM dbo.Settings
              WHERE BillingComplianceRequirements = @defaultMask) AS DefaultedSettingsRows,
             (SELECT COUNT_BIG(*) FROM dbo.Settings
              WHERE BillingComplianceRequirements < 0 OR
                    (BillingComplianceRequirements & @supported) <> BillingComplianceRequirements)
                 AS InvalidSettingsRows;',
    N'@added bit, @historyWritten bit, @defaultMask int, @supported int',
    @added = @columnAdded,
    @historyWritten = @historyWritten,
    @defaultMask = @defaultRequirements,
    @supported = @allSupportedRequirements;
'@
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration verification row was not returned.' }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        ColumnAdded = $reader.GetBoolean(2)
        HistoryRowWritten = $reader.GetBoolean(3)
        MigrationCount = $reader.GetInt64(4)
        DefaultedSettingsRows = $reader.GetInt64(5)
        InvalidSettingsRows = $reader.GetInt64(6)
        BackupPath = $backupPath
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
