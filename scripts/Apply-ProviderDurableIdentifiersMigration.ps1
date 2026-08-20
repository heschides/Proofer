<#
.SYNOPSIS
    Repairs and verifies the Provider durable-identifier schema on a long-lived
    Sati database.

.DESCRIPTION
    SatiDemo and SatiProduction have schema changes that were applied outside the
    EF migration chain, so __EFMigrationsHistory is not a reliable statement of
    which columns and indexes exist. This runner guards on the actual schema and
    reconciles migration 20260815184142_AddProviderDurableIdentifiers only after
    both columns and both filtered unique indexes are present and verified.

    The script is rerunnable. It adds only missing objects, refuses to build a
    unique index when duplicate non-null identifiers exist, and fails closed on a
    database or environment identity mismatch.

.EXAMPLE
    ./scripts/Apply-ProviderDurableIdentifiersMigration.ps1 -DatabaseName SatiProduction

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net `
        --query accessToken --output tsv
    ./scripts/Apply-ProviderDurableIdentifiersMigration.ps1 `
        -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net `
        -AccessToken $token
#>
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken
)

$ErrorActionPreference = 'Stop'
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
    THROW 51400, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51401, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.Providers', N'U') IS NULL
    THROW 51402, 'dbo.Providers does not exist; this is not the expected Sati schema.', 1;

DECLARE @migrationId nvarchar(150) = N'20260815184142_AddProviderDurableIdentifiers';
DECLARE @historyBefore bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END;
DECLARE @columnsAdded int = 0;
DECLARE @indexesAdded int = 0;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Providers', N'MaineCareProviderId') IS NULL
BEGIN
    ALTER TABLE dbo.Providers ADD MaineCareProviderId nvarchar(30) NULL;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.Providers', N'Npi') IS NULL
BEGIN
    ALTER TABLE dbo.Providers ADD Npi nvarchar(10) NULL;
    SET @columnsAdded += 1;
END;

-- The columns may have been added earlier in this same batch. Use dynamic SQL for
-- every statement that names them so SQL Server compiles it after ALTER TABLE,
-- rather than rejecting the whole batch during its initial name-binding pass.
-- A same-agency duplicate would make CREATE UNIQUE INDEX fail with an opaque
-- value-bearing SQL error. Refuse it explicitly without printing an identifier.
DECLARE @hasDuplicateNpi bit = 0;
EXEC sys.sp_executesql
    N'SELECT @found = CASE WHEN EXISTS (
          SELECT 1 FROM dbo.Providers
          WHERE Npi IS NOT NULL
          GROUP BY AgencyId, Npi
          HAVING COUNT_BIG(*) > 1) THEN 1 ELSE 0 END;',
    N'@found bit OUTPUT',
    @found = @hasDuplicateNpi OUTPUT;
IF @hasDuplicateNpi = 1
    THROW 51403, 'Duplicate non-null Provider NPI values exist within an agency; no index was created.', 1;

DECLARE @hasDuplicateMaineCareId bit = 0;
EXEC sys.sp_executesql
    N'SELECT @found = CASE WHEN EXISTS (
          SELECT 1 FROM dbo.Providers
          WHERE MaineCareProviderId IS NOT NULL
          GROUP BY AgencyId, MaineCareProviderId
          HAVING COUNT_BIG(*) > 1) THEN 1 ELSE 0 END;',
    N'@found bit OUTPUT',
    @found = @hasDuplicateMaineCareId OUTPUT;
IF @hasDuplicateMaineCareId = 1
    THROW 51404, 'Duplicate non-null Provider MaineCare IDs exist within an agency; no index was created.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Providers')
      AND name = N'IX_Providers_AgencyId_Npi')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_Providers_AgencyId_Npi
        ON dbo.Providers(AgencyId, Npi)
        WHERE Npi IS NOT NULL;');
    SET @indexesAdded += 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Providers')
      AND name = N'IX_Providers_AgencyId_MaineCareProviderId')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_Providers_AgencyId_MaineCareProviderId
        ON dbo.Providers(AgencyId, MaineCareProviderId)
        WHERE MaineCareProviderId IS NOT NULL;');
    SET @indexesAdded += 1;
END;

-- An object with the expected name but weaker semantics is drift, not success.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Providers')
      AND name = N'IX_Providers_AgencyId_Npi'
      AND is_unique = 1
      AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            filter_definition, N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'') = N'NpiISNOTNULL'
      AND 2 = (SELECT COUNT(*) FROM sys.index_columns
               WHERE object_id = OBJECT_ID(N'dbo.Providers') AND index_id = sys.indexes.index_id)
      AND EXISTS (SELECT 1 FROM sys.index_columns
                  WHERE object_id = OBJECT_ID(N'dbo.Providers') AND index_id = sys.indexes.index_id
                    AND key_ordinal = 1 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Providers'), N'AgencyId', N'ColumnId'))
      AND EXISTS (SELECT 1 FROM sys.index_columns
                  WHERE object_id = OBJECT_ID(N'dbo.Providers') AND index_id = sys.indexes.index_id
                    AND key_ordinal = 2 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Providers'), N'Npi', N'ColumnId')))
    THROW 51405, 'The Provider NPI index exists but does not have the expected unique filtered definition.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Providers')
      AND name = N'IX_Providers_AgencyId_MaineCareProviderId'
      AND is_unique = 1
      AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
            filter_definition, N'[', N''), N']', N''), N'(', N''), N')', N''), N' ', N'') = N'MaineCareProviderIdISNOTNULL'
      AND 2 = (SELECT COUNT(*) FROM sys.index_columns
               WHERE object_id = OBJECT_ID(N'dbo.Providers') AND index_id = sys.indexes.index_id)
      AND EXISTS (SELECT 1 FROM sys.index_columns
                  WHERE object_id = OBJECT_ID(N'dbo.Providers') AND index_id = sys.indexes.index_id
                    AND key_ordinal = 1 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Providers'), N'AgencyId', N'ColumnId'))
      AND EXISTS (SELECT 1 FROM sys.index_columns
                  WHERE object_id = OBJECT_ID(N'dbo.Providers') AND index_id = sys.indexes.index_id
                    AND key_ordinal = 2 AND column_id = COLUMNPROPERTY(OBJECT_ID(N'dbo.Providers'), N'MaineCareProviderId', N'ColumnId')))
    THROW 51406, 'The Provider MaineCare ID index exists but does not have the expected unique filtered definition.', 1;

IF @historyBefore = 0
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');

COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @columnsAdded AS ColumnsAdded,
    @indexesAdded AS IndexesAdded,
    CAST(CASE WHEN @historyBefore = 0 THEN 1 ELSE 0 END AS bit) AS HistoryRowWritten,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The Provider migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName      = $reader.GetString(0)
        EnvironmentName   = $reader.GetString(1)
        ColumnsAdded      = $reader.GetInt32(2)
        IndexesAdded      = $reader.GetInt32(3)
        HistoryRowWritten = $reader.GetBoolean(4)
        MigrationCount    = $reader.GetInt64(5)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
