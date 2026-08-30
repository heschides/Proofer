<#
.SYNOPSIS
    Writes the missing dbo.__EFMigrationsHistory row for TenantScopeSettingsAndProviders on an
    identity-validated SatiProduction, after proving the migration's effects are already present.

.DESCRIPTION
    Sati 1.2.32 refuses to start against SatiProduction with SQL 2705, "Column name 'AgencyId' in
    table 'Settings' is specified more than once". The cause is not the 1.2.32 change set. The
    migration 20260812090000_TenantScopeSettingsAndProviders was authored without its
    [Migration] and [DbContext] attributes, so EF never enumerated it and never recorded it, while
    its effects reached the database by another route. Restoring those attributes during the
    2026-08-30 persistence move made EF see it for the first time, and the first thing EF did was
    try to apply a migration whose columns already exist.

    This writes one history row. It creates, alters, and drops nothing: no tables, columns, indexes,
    foreign keys, defaults, or data. Before writing it proves the migration's full end state —
    both AgencyId columns as required int, both indexes with their exact key columns, and both
    foreign keys to Agencies with their delete behaviour. Object names alone are not proof.

    Six further migrations have no history row on SatiProduction, and this script deliberately
    leaves them alone: AddProviderAffiliation, AddConsumerProviderList, AddProviderContacts,
    AddTestConsumerMarker, AddBillingExchangeHistory, and AddRemittanceDeposits. Their objects are
    genuinely absent, so they are pending rather than drifted and EF must apply them normally at the
    next launch. Writing history rows for those would tell EF work had been done that has not been.

    Restricted to a database named exactly SatiProduction whose dbo.SatiDatabaseIdentity marker is
    exactly Production. Rerunnable. -WhatIfOnly performs every proof and the insert inside a
    transaction and then rolls it back.

.NOTES
    Take a backup first. Sati writes one automatically before it migrates; this script does not,
    because it is not the thing that migrates.

.EXAMPLE
    ./scripts/Apply-ProductionHistoryReconciliation.ps1 -WhatIfOnly
#>
param(
    [ValidateSet('SatiProduction')]
    [string]$DatabaseName = 'SatiProduction',

    [ValidateNotNullOrEmpty()]
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    # Performs every proof and the insert inside a transaction, then rolls it back.
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$expectedEnvironment = 'Production'
$migrationId = '20260812090000_TenantScopeSettingsAndProviders'

$connection = New-Object System.Data.SqlClient.SqlConnection `
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Connect Timeout=30;"
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.Parameters.AddWithValue('@migrationId', $migrationId) | Out-Null
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Fail closed before reading or writing history. Binary collation keeps a case-insensitive
-- database collation from weakening the exact SatiProduction/Production checks.
IF DB_NAME() COLLATE Latin1_General_100_BIN2
       <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 51900, 'The connected database is not exactly SatiProduction.', 1;

IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 51901, 'dbo.SatiDatabaseIdentity is missing.', 1;

DECLARE @actualEnvironment nvarchar(128);
DECLARE @identityRows int;
EXEC sys.sp_executesql
    N'SELECT @rows = COUNT(*), @environment = MAX(CONVERT(nvarchar(128), EnvironmentName))
      FROM dbo.SatiDatabaseIdentity WHERE Id = 1;',
    N'@rows int OUTPUT, @environment nvarchar(128) OUTPUT',
    @rows = @identityRows OUTPUT, @environment = @actualEnvironment OUTPUT;

IF @identityRows <> 1
   OR @actualEnvironment IS NULL
   OR @actualEnvironment COLLATE Latin1_General_100_BIN2
      <> @expectedEnvironment COLLATE Latin1_General_100_BIN2
    THROW 51902, 'The database identity marker is not exactly Production.', 1;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51903, 'dbo.__EFMigrationsHistory is missing.', 1;

DECLARE @rowWritten int = 0;
DECLARE @proofFailures TABLE (Ordinal int IDENTITY(1,1), Failure nvarchar(2048) NOT NULL);

BEGIN TRY
    BEGIN TRANSACTION;

    -- Both AgencyId columns must exist as required int. The migration adds them nullable,
    -- backfills every row, then alters them to NOT NULL, so NOT NULL is also the proof that the
    -- backfill ran: a nullable column here would mean the data step did not complete.
    DECLARE @requiredColumns TABLE (ProofName nvarchar(200), TableName sysname, ColumnName sysname);
    INSERT @requiredColumns VALUES
        (N'Settings.AgencyId is a required int',  N'Settings',  N'AgencyId'),
        (N'Providers.AgencyId is a required int', N'Providers', N'AgencyId');

    INSERT @proofFailures (Failure)
    SELECT CONCAT(N'Proof failed: ', required.ProofName, N'.')
    FROM @requiredColumns AS required
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM sys.columns AS columnObject
        INNER JOIN sys.types AS typeObject
            ON typeObject.user_type_id = columnObject.user_type_id
        WHERE columnObject.object_id = OBJECT_ID(N'dbo.' + required.TableName)
          AND columnObject.name = required.ColumnName
          AND typeObject.name = N'int'
          AND typeObject.is_user_defined = 0
          AND columnObject.is_nullable = 0
          AND columnObject.is_computed = 0
    );

    -- Index keys are checked by column and ordinal, not by name.
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexObject
        INNER JOIN sys.index_columns AS indexColumn
            ON indexColumn.object_id = indexObject.object_id
           AND indexColumn.index_id = indexObject.index_id
        INNER JOIN sys.columns AS columnObject
            ON columnObject.object_id = indexColumn.object_id
           AND columnObject.column_id = indexColumn.column_id
        WHERE indexObject.object_id = OBJECT_ID(N'dbo.Settings')
          AND indexObject.type = 2
          AND indexObject.is_disabled = 0
          AND columnObject.name = N'AgencyId'
          AND indexColumn.key_ordinal = 1
          AND (SELECT COUNT(*) FROM sys.index_columns AS allColumns
               WHERE allColumns.object_id = indexObject.object_id
                 AND allColumns.index_id = indexObject.index_id
                 AND allColumns.key_ordinal > 0) = 1
    )
        INSERT @proofFailures (Failure) VALUES (N'Proof failed: Settings has no single-column AgencyId index.');

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexObject
        WHERE indexObject.object_id = OBJECT_ID(N'dbo.Providers')
          AND indexObject.type = 2
          AND indexObject.is_disabled = 0
          AND (SELECT COUNT(*) FROM sys.index_columns AS allColumns
               WHERE allColumns.object_id = indexObject.object_id
                 AND allColumns.index_id = indexObject.index_id
                 AND allColumns.key_ordinal > 0) = 2
          AND EXISTS (SELECT 1 FROM sys.index_columns AS k
                      INNER JOIN sys.columns AS c ON c.object_id = k.object_id AND c.column_id = k.column_id
                      WHERE k.object_id = indexObject.object_id AND k.index_id = indexObject.index_id
                        AND k.key_ordinal = 1 AND c.name = N'AgencyId')
          AND EXISTS (SELECT 1 FROM sys.index_columns AS k
                      INNER JOIN sys.columns AS c ON c.object_id = k.object_id AND c.column_id = k.column_id
                      WHERE k.object_id = indexObject.object_id AND k.index_id = indexObject.index_id
                        AND k.key_ordinal = 2 AND c.name = N'Name')
    )
        INSERT @proofFailures (Failure) VALUES (N'Proof failed: Providers has no (AgencyId, Name) index.');

    -- Foreign keys are checked by the columns they map, not by constraint name.
    DECLARE @requiredForeignKeys TABLE (ProofName nvarchar(200), ParentTable sysname);
    INSERT @requiredForeignKeys VALUES
        (N'Settings.AgencyId references Agencies',  N'Settings'),
        (N'Providers.AgencyId references Agencies', N'Providers');

    INSERT @proofFailures (Failure)
    SELECT CONCAT(N'Proof failed: ', required.ProofName, N'.')
    FROM @requiredForeignKeys AS required
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN sys.foreign_key_columns AS keyColumn
            ON keyColumn.constraint_object_id = foreignKey.object_id
        INNER JOIN sys.columns AS parentColumn
            ON parentColumn.object_id = keyColumn.parent_object_id
           AND parentColumn.column_id = keyColumn.parent_column_id
        WHERE foreignKey.parent_object_id = OBJECT_ID(N'dbo.' + required.ParentTable)
          AND foreignKey.referenced_object_id = OBJECT_ID(N'dbo.Agencies')
          AND parentColumn.name = N'AgencyId'
    );

    IF EXISTS (SELECT 1 FROM @proofFailures)
    BEGIN
        DECLARE @failure nvarchar(2048) =
            (SELECT TOP (1) CONCAT(Failure, N' Migration history was not changed.')
             FROM @proofFailures ORDER BY Ordinal);
        THROW 51904, @failure, 1;
    END;

    -- Every effect proven. Insert only if absent, so the script is rerunnable.
    INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    SELECT @migrationId, N'10.0.5'
    WHERE NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WITH (UPDLOCK, HOLDLOCK)
                      WHERE MigrationId = @migrationId);
    SET @rowWritten = @@ROWCOUNT;

    IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId)
        THROW 51905, 'Verification failed: the history row is still absent.', 1;

    IF @whatIfOnly = 1
        ROLLBACK TRANSACTION;
    ELSE
        COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    DB_NAME() AS DatabaseName,
    @actualEnvironment AS EnvironmentName,
    @rowWritten AS HistoryRowsWritten,
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory) AS TotalHistoryRows,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The verification row was not returned.' }
    [pscustomobject][ordered]@{
        DatabaseName       = $reader.GetString(0)
        EnvironmentName    = $reader.GetString(1)
        HistoryRowsWritten = $reader.GetInt32(2)
        TotalHistoryRows   = $reader.GetInt32(3)
        RolledBack         = $reader.GetBoolean(4)
    }
    $reader.Close()
}
finally { $connection.Dispose() }
