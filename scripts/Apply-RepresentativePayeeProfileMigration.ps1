<#
.SYNOPSIS
    Applies and verifies the representative-payee Person profile migration.

.DESCRIPTION
    Long-lived SatiDemo and SatiProduction databases have schema/history drift, so
    this runner inspects the actual schema instead of trusting only
    __EFMigrationsHistory. It is transactional, rerunnable, and refuses a database
    or environment identity mismatch before changing anything.

    Local databases containing Person records receive a full backup before the
    first schema change. Azure SQL uses its configured point-in-time recovery and
    must be reached with an Entra access token under the separate migration identity.

.EXAMPLE
    ./scripts/Apply-RepresentativePayeeProfileMigration.ps1 -DatabaseName SatiProduction

.EXAMPLE
    ./scripts/Apply-RepresentativePayeeProfileMigration.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net `
        -AccessToken $token
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
$migrationId = '20260822210734_AddRepresentativePayeeProfile'
$usesAccessToken = -not [string]::IsNullOrWhiteSpace($AccessToken)

$connectionString = if (-not $usesAccessToken) {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if ($usesAccessToken) {
    $connection.AccessToken = $AccessToken
}
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
    THROW 51600, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51601, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51602, 'dbo.People does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51603, 'The EF migration history table is missing.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonCount,
    CAST(CASE WHEN EXISTS (
        SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
        THEN 1 ELSE 0 END AS bit) AS HistoryPresent,
    CAST(CASE WHEN COL_LENGTH(N'dbo.People', N'CaseManagerIsRepPayee') IS NOT NULL
        THEN 1 ELSE 0 END AS bit) AS PayeeStatusPresent,
    CAST(CASE WHEN COL_LENGTH(N'dbo.People', N'RepPayeeMonthlyIncome') IS NOT NULL
        THEN 1 ELSE 0 END AS bit) AS MonthlyIncomePresent,
    CAST(CASE WHEN COL_LENGTH(N'dbo.People', N'RepPayeeRegularCheckRequestNeeds') IS NOT NULL
        THEN 1 ELSE 0 END AS bit) AS RegularNeedsPresent;
'@

    $preflightReader = $preflight.ExecuteReader()
    if (-not $preflightReader.Read()) {
        throw 'The migration preflight row was not returned.'
    }
    $databaseIdentity = $preflightReader.GetString(0)
    $environmentIdentity = $preflightReader.GetString(1)
    $personCount = $preflightReader.GetInt64(2)
    $historyPresent = $preflightReader.GetBoolean(3)
    $payeeStatusPresent = $preflightReader.GetBoolean(4)
    $monthlyIncomePresent = $preflightReader.GetBoolean(5)
    $regularNeedsPresent = $preflightReader.GetBoolean(6)
    $preflightReader.Close()

    if ($InspectOnly) {
        [pscustomobject][ordered]@{
            DatabaseName = $databaseIdentity
            EnvironmentName = $environmentIdentity
            PersonCount = $personCount
            HistoryPresent = $historyPresent
            PayeeStatusPresent = $payeeStatusPresent
            MonthlyIncomePresent = $monthlyIncomePresent
            RegularNeedsPresent = $regularNeedsPresent
        }
        return
    }

    $alreadyCurrent = $historyPresent -and $payeeStatusPresent -and
        $monthlyIncomePresent -and $regularNeedsPresent
    if (-not $alreadyCurrent -and -not $usesAccessToken -and $personCount -gt 0) {
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
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> @expectedDatabase
    THROW 51600, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51601, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51602, 'dbo.People does not exist; this is not the expected Sati schema.', 1;

DECLARE @columnsAdded int = 0;
DECLARE @historyBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
    THEN 1 ELSE 0 END;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.People', N'CaseManagerIsRepPayee') IS NULL
BEGIN
    ALTER TABLE dbo.People
        ADD CaseManagerIsRepPayee bit NOT NULL
            CONSTRAINT DF_People_CaseManagerIsRepPayee DEFAULT (0) WITH VALUES;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.People', N'RepPayeeMonthlyIncome') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD RepPayeeMonthlyIncome decimal(18,2) NULL;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.People', N'RepPayeeRegularCheckRequestNeeds') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD RepPayeeRegularCheckRequestNeeds nvarchar(2000) NULL;
    SET @columnsAdded += 1;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People')
      AND c.name = N'CaseManagerIsRepPayee'
      AND t.name = N'bit'
      AND c.is_nullable = 0)
    THROW 51604, 'CaseManagerIsRepPayee exists with an unexpected SQL definition.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People')
      AND c.name = N'RepPayeeMonthlyIncome'
      AND t.name = N'decimal'
      AND c.precision = 18
      AND c.scale = 2
      AND c.is_nullable = 1)
    THROW 51605, 'RepPayeeMonthlyIncome exists with an unexpected SQL definition.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    INNER JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People')
      AND c.name = N'RepPayeeRegularCheckRequestNeeds'
      AND t.name = N'nvarchar'
      AND c.max_length = 4000
      AND c.is_nullable = 1)
    THROW 51606, 'RepPayeeRegularCheckRequestNeeds exists with an unexpected SQL definition.', 1;

IF @historyBefore = 0
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');

COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @columnsAdded AS ColumnsAdded,
    CAST(CASE WHEN @historyBefore = 0 THEN 1 ELSE 0 END AS bit) AS HistoryRowWritten,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        ColumnsAdded = $reader.GetInt32(2)
        HistoryRowWritten = $reader.GetBoolean(3)
        MigrationCount = $reader.GetInt64(4)
        BackupPath = $backupPath
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
