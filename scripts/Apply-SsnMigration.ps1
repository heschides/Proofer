<#
.SYNOPSIS
    Applies 20260818220245_AddEncryptedSsn to a long-lived Sati database.

.DESCRIPTION
    The generated EF migration cannot be used on SatiDemo or SatiProduction. Both
    have acquired columns outside the migration chain, so __EFMigrationsHistory and
    the real schema disagree in both directions, and EF's idempotent script guards
    only on history — it fails with SQL 2705 on a column that already exists without
    its history row. See AGENDA.md, "Hosted Demo migration deployment".

    This script guards on the SCHEMA instead, column by column, and reconciles the
    history row separately. Running it twice is a no-op. Running it against a
    database that already has some of the columns adds only the missing ones.

    BOTH databases need this migration, including SatiProduction, even though a
    Production SSN is never stored. SatiContext declares the columns as shadow
    properties, so EF includes them in every People query; a database without them
    fails the desktop's ordinary person reads. Cloud-only describes what is written,
    not what exists.

    The API's managed identity is db_datareader/db_datawriter and deliberately has no
    DDL rights, so this runs under your own administrative login, not the API's.

.EXAMPLE
    ./scripts/Apply-SsnMigration.ps1 -DatabaseName SatiProduction

.EXAMPLE
    $token = (az account get-access-token --resource https://database.windows.net --query accessToken -o tsv)
    ./scripts/Apply-SsnMigration.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -AccessToken $token

    Azure SatiDemo is Entra-only and its firewall allows the three App Service
    outbound IPs, not this laptop. A temporary exact-IP rule is needed for the run
    and should be removed afterwards, as the August 2026 deployments did.
#>
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken
)

$ErrorActionPreference = 'Stop'

# The environment a database claims must match the one asked for. A mis-pointed
# server name is the failure this catches, and it is the one that would matter.
$expectedEnvironment = if ($DatabaseName -ceq 'SatiDemo') { 'Demo' } else { 'Production' }

$connectionString = if ([string]::IsNullOrWhiteSpace($AccessToken)) {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
}

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
    $connection.AccessToken = $AccessToken
}
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> @expectedDatabase
    THROW 51300, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51301, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51302, 'dbo.People does not exist; this is not a Sati database.', 1;

DECLARE @migrationId nvarchar(150) = N'20260818220245_AddEncryptedSsn';
DECLARE @historyBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END;
DECLARE @columnsAdded int = 0;

BEGIN TRANSACTION;

-- Guarded per column rather than per migration. These two databases disagree with
-- their own history in both directions, so "has the history row" is not a reliable
-- statement about whether the column is there.
IF COL_LENGTH(N'dbo.People', N'SsnCiphertext') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD SsnCiphertext varbinary(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.People', N'SsnNonce') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD SsnNonce varbinary(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.People', N'SsnTag') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD SsnTag varbinary(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.People', N'SsnWrappedKey') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD SsnWrappedKey varbinary(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.People', N'SsnKeyId') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD SsnKeyId nvarchar(400) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.People', N'SsnLastFour') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD SsnLastFour nvarchar(4) NULL;
    SET @columnsAdded += 1;
END;

IF @historyBefore = 0
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');

COMMIT TRANSACTION;

-- Every column must exist when this returns, whether this run added it or a previous
-- one did. A partial result here is the thing that takes an endpoint down later.
IF COL_LENGTH(N'dbo.People', N'SsnCiphertext') IS NULL
    OR COL_LENGTH(N'dbo.People', N'SsnNonce') IS NULL
    OR COL_LENGTH(N'dbo.People', N'SsnTag') IS NULL
    OR COL_LENGTH(N'dbo.People', N'SsnWrappedKey') IS NULL
    OR COL_LENGTH(N'dbo.People', N'SsnKeyId') IS NULL
    OR COL_LENGTH(N'dbo.People', N'SsnLastFour') IS NULL
    THROW 51303, 'One or more SSN columns are still missing after the migration ran.', 1;

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
        DatabaseName      = $reader.GetString(0)
        EnvironmentName   = $reader.GetString(1)
        ColumnsAdded      = $reader.GetInt32(2)
        HistoryRowWritten = $reader.GetBoolean(3)
        MigrationCount    = $reader.GetInt64(4)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
