<#
.SYNOPSIS
    Reconciles six known SatiDemo EF migration-history rows after proving the live schema.

.DESCRIPTION
    SatiDemo contains the effects of four surviving migrations, but its
    dbo.__EFMigrationsHistory table does not describe them correctly. Two older migration ids
    refer to the same changes under timestamps that were later superseded.

    This script changes migration history only. It does not create, alter, or drop application
    tables, columns, indexes, foreign keys, defaults, or data. Before touching history, it proves
    the expected column types and nullability, identity and default behavior, index keys and
    uniqueness, and foreign-key mappings and delete behavior. Object names alone are not proof.

    The script is restricted to a database named exactly SatiDemo whose
    dbo.SatiDatabaseIdentity marker is exactly Demo. It is rerunnable. -WhatIfOnly performs the
    same checks and history operations inside a transaction and then rolls the transaction back.

.NOTES
    -WhatIfOnly is a rollback-only dry run: it still connects to the requested SQL Server and
    takes database locks. This file must first be rehearsed against a restored copy. It is not a
    substitute for the release authorization required before changing the live SatiDemo database.

.EXAMPLE
    ./scripts/Apply-DemoHistoryReconciliation.ps1 `
        -DatabaseName SatiDemo `
        -SqlServer restored-copy.example.net `
        -AccessToken $token `
        -WhatIfOnly
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SatiDemo')]
    [ValidateScript({
        if ($_ -cne 'SatiDemo') {
            throw 'Apply-DemoHistoryReconciliation.ps1 is restricted to the exact database name SatiDemo.'
        }
        $true
    })]
    [string]$DatabaseName,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SqlServer,

    [string]$AccessToken,

    # Acquires the SQL access token from the host's managed identity instead of a workstation
    # sign-in. Valid only inside the App Service WebJob host, which reaches SatiDemo through the
    # outbound addresses already on the SQL allow-list, so no temporary firewall rule is involved.
    [switch]$UseManagedIdentity,

    # Reports the prospective history changes and rolls the transaction back.
    [switch]$WhatIfOnly,

    # Runs every schema proof, reports all of them that fail, and stops before any history change.
    # The default path stops at the first failed proof, which is right when the goal is to refuse
    # safely but wrong when the goal is to find out how far the database has drifted: fixing one
    # assertion at a time across a proof of this size is slow and hides the shape of the problem.
    # Implies -WhatIfOnly and never writes.
    [switch]$ProofsOnly
)

$ErrorActionPreference = 'Stop'
$expectedEnvironment = 'Demo'

function Get-ManagedIdentityAccessToken {
    # App Service exposes the identity endpoint through these two variables. Their absence means
    # this is not running under a managed identity, which is a hard error rather than a silent
    # fallback: falling back to integrated security here would connect as the workstation user and
    # defeat the point of the switch.
    $endpoint = $env:IDENTITY_ENDPOINT
    $header = $env:IDENTITY_HEADER
    if ([string]::IsNullOrWhiteSpace($endpoint) -or [string]::IsNullOrWhiteSpace($header)) {
        throw '-UseManagedIdentity requires the App Service identity endpoint; it is only valid inside the WebJob host.'
    }

    $uri = '{0}?resource=https%3A%2F%2Fdatabase.windows.net%2F&api-version=2019-08-01' -f $endpoint
    $response = Invoke-RestMethod -Uri $uri -Headers @{ 'X-IDENTITY-HEADER' = $header } -Method Get -TimeoutSec 60
    if ([string]::IsNullOrWhiteSpace($response.access_token)) {
        throw 'The managed-identity endpoint returned no access token for https://database.windows.net/.'
    }

    return $response.access_token
}

if ($UseManagedIdentity) {
    if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
        throw 'Pass either -AccessToken or -UseManagedIdentity, not both.'
    }
    # The token is never written to output, a file, or a command line.
    $AccessToken = Get-ManagedIdentityAccessToken
}

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
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    # -ProofsOnly implies -WhatIfOnly. Reporting drift must never be a path that can write.
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]($WhatIfOnly -or $ProofsOnly)) | Out-Null
    $command.Parameters.AddWithValue('@proofsOnly', [bool]$ProofsOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

-- Fail closed before inspecting or changing migration history. Binary collation prevents a
-- case-insensitive database collation from weakening the exact SatiDemo/Demo checks.
IF DB_NAME() COLLATE Latin1_General_100_BIN2
       <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 51700, 'The connected database is not exactly SatiDemo.', 1;

IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 51701, 'dbo.SatiDatabaseIdentity is missing.', 1;

IF COL_LENGTH(N'dbo.SatiDatabaseIdentity', N'Id') IS NULL
   OR COL_LENGTH(N'dbo.SatiDatabaseIdentity', N'EnvironmentName') IS NULL
    THROW 51702, 'dbo.SatiDatabaseIdentity does not have the expected identity columns.', 1;

DECLARE @actualEnvironment nvarchar(128);
DECLARE @identityRows int;
EXEC sys.sp_executesql
    N'SELECT @rows = COUNT(*), @environment = MAX(CONVERT(nvarchar(128), EnvironmentName))
      FROM dbo.SatiDatabaseIdentity
      WHERE Id = 1;',
    N'@rows int OUTPUT, @environment nvarchar(128) OUTPUT',
    @rows = @identityRows OUTPUT,
    @environment = @actualEnvironment OUTPUT;

IF @identityRows <> 1
   OR @actualEnvironment IS NULL
   OR @actualEnvironment COLLATE Latin1_General_100_BIN2
      <> @expectedEnvironment COLLATE Latin1_General_100_BIN2
    THROW 51703, 'The database identity marker is not exactly Demo.', 1;

IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51704, 'dbo.__EFMigrationsHistory is missing.', 1;

DECLARE @historyRowsWritten int = 0;
DECLARE @supersededRowsRemoved int = 0;
DECLARE @semanticProofsVerified int = 0;

-- Every failed schema proof, in the order the proofs run. The default path still stops at the
-- first one, because refusing safely does not require enumerating the rest. -ProofsOnly collects
-- all of them and writes nothing, so a drifted database can be assessed in one pass instead of
-- one assertion per round trip.
DECLARE @proofFailures TABLE
(
    Ordinal int IDENTITY(1, 1) NOT NULL,
    Failure nvarchar(2048) NOT NULL
);

BEGIN TRY
    BEGIN TRANSACTION;

    -- Column proof covers all four surviving migrations and the history table this script writes.
    -- MaxLength is bytes, as stored by sys.columns: nvarchar(100) = 200, nvarchar(254) = 508.
    DECLARE @requiredColumns TABLE
    (
        ProofName nvarchar(200) NOT NULL,
        TableName sysname NOT NULL,
        ColumnName sysname NOT NULL,
        TypeName sysname NOT NULL,
        MaxLength smallint NOT NULL,
        IsNullable bit NOT NULL,
        IsIdentity bit NOT NULL
    );

    INSERT @requiredColumns
        (ProofName, TableName, ColumnName, TypeName, MaxLength, IsNullable, IsIdentity)
    VALUES
        (N'AddAgencyId: Agencies.Id is an identity int primary-key column',
         N'Agencies', N'Id', N'int', 4, 0, 1),
        (N'AddAgencyId: Agencies.Name is required nvarchar(100)',
         N'Agencies', N'Name', N'nvarchar', 200, 0, 0),
        (N'AddAgencyId: Users.AgencyId is required int',
         N'Users', N'AgencyId', N'int', 4, 0, 0),
        (N'AddAgencyId: People.AgencyId is nullable int',
         N'People', N'AgencyId', N'int', 4, 1, 0),
        (N'AddAgencyId: Notes.AgencyId is nullable int',
         N'Notes', N'AgencyId', N'int', 4, 1, 0),
        (N'TenantScopeSettingsAndProviders: Settings.AgencyId is required int',
         N'Settings', N'AgencyId', N'int', 4, 0, 0),
        (N'TenantScopeSettingsAndProviders: Providers.AgencyId is required int',
         N'Providers', N'AgencyId', N'int', 4, 0, 0),
        (N'AddNoteMinutesAndStartTime: Notes.Minutes is nullable int',
         N'Notes', N'Minutes', N'int', 4, 1, 0),
        (N'AddNoteMinutesAndStartTime: Notes.StartTime is nullable int',
         N'Notes', N'StartTime', N'int', 4, 1, 0),
        (N'AddConsumerEmail: People.Email is nullable nvarchar(254)',
         N'People', N'Email', N'nvarchar', 508, 1, 0),
        (N'History: MigrationId is required nvarchar(150)',
         N'__EFMigrationsHistory', N'MigrationId', N'nvarchar', 300, 0, 0),
        (N'History: ProductVersion is required nvarchar(32)',
         N'__EFMigrationsHistory', N'ProductVersion', N'nvarchar', 64, 0, 0);

    -- Record every failing column proof rather than only the first. The predicate is written once:
    -- a second copy of it for reporting would be a rule with two owners, and the two would drift.
    INSERT @proofFailures (Failure)
    SELECT CONCAT(N'Semantic proof failed: ', required.ProofName, N'.')
    FROM @requiredColumns AS required
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM sys.tables AS tableObject
        INNER JOIN sys.schemas AS tableSchema
            ON tableSchema.schema_id = tableObject.schema_id
        INNER JOIN sys.columns AS columnObject
            ON columnObject.object_id = tableObject.object_id
        INNER JOIN sys.types AS typeObject
            ON typeObject.user_type_id = columnObject.user_type_id
        WHERE tableSchema.name = N'dbo'
          AND tableObject.name = required.TableName
          AND columnObject.name = required.ColumnName
          AND typeObject.name = required.TypeName
          AND typeObject.is_user_defined = 0
          AND columnObject.max_length = required.MaxLength
          AND columnObject.is_nullable = required.IsNullable
          AND columnObject.is_identity = required.IsIdentity
          AND columnObject.is_computed = 0
    )
    ORDER BY required.ProofName;

    -- Outside -ProofsOnly the default path is unchanged: any failed proof refuses here, so
    -- execution never reaches a later block with failures already collected.
    IF @proofsOnly = 0 AND EXISTS (SELECT 1 FROM @proofFailures)
    BEGIN
        DECLARE @columnFailure nvarchar(2048) =
            (SELECT TOP (1) CONCAT(Failure, N' Migration history was not changed.')
             FROM @proofFailures ORDER BY Ordinal);
        THROW 51705, @columnFailure, 1;
    END;

    -- AddAgencyId created an IDENTITY(1,1) Agencies key and a constant default of 1 for
    -- Users.AgencyId. Constraint names are deliberately ignored; their behavior is checked.
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.identity_columns AS identityColumn
        WHERE identityColumn.object_id = OBJECT_ID(N'dbo.Agencies')
          AND identityColumn.name = N'Id'
          AND CONVERT(bigint, identityColumn.seed_value) = 1
          AND CONVERT(bigint, identityColumn.increment_value) = 1
    )
        INSERT @proofFailures (Failure) VALUES (N'Semantic proof failed: Agencies.Id is not IDENTITY(1,1).');
        IF @proofsOnly = 0 THROW 51706, 'Semantic proof failed: Agencies.Id is not IDENTITY(1,1).', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS primaryIndex
        INNER JOIN sys.index_columns AS primaryColumn
            ON primaryColumn.object_id = primaryIndex.object_id
           AND primaryColumn.index_id = primaryIndex.index_id
           AND primaryColumn.key_ordinal = 1
        INNER JOIN sys.columns AS columnObject
            ON columnObject.object_id = primaryColumn.object_id
           AND columnObject.column_id = primaryColumn.column_id
        WHERE primaryIndex.object_id = OBJECT_ID(N'dbo.Agencies')
          AND primaryIndex.is_primary_key = 1
          AND primaryIndex.is_unique = 1
          AND primaryIndex.is_disabled = 0
          AND columnObject.name = N'Id'
          AND (SELECT COUNT(*)
               FROM sys.index_columns AS allPrimaryColumns
               WHERE allPrimaryColumns.object_id = primaryIndex.object_id
                 AND allPrimaryColumns.index_id = primaryIndex.index_id) = 1
    )
        INSERT @proofFailures (Failure) VALUES (N'Semantic proof failed: Agencies.Id is not the single-column primary key.');
        IF @proofsOnly = 0 THROW 51707, 'Semantic proof failed: Agencies.Id is not the single-column primary key.', 1;

    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.default_constraints AS defaultObject
        INNER JOIN sys.columns AS columnObject
            ON columnObject.object_id = defaultObject.parent_object_id
           AND columnObject.column_id = defaultObject.parent_column_id
        WHERE defaultObject.parent_object_id = OBJECT_ID(N'dbo.Users')
          AND columnObject.name = N'AgencyId'
          AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                  defaultObject.definition, N'(', N''), N')', N''), N' ', N''),
                  NCHAR(9), N''), NCHAR(13) + NCHAR(10), N'') = N'1'
    )
        INSERT @proofFailures (Failure) VALUES (N'Semantic proof failed: Users.AgencyId does not have a constant default of 1.');
        IF @proofsOnly = 0 THROW 51708, 'Semantic proof failed: Users.AgencyId does not have a constant default of 1.', 1;

    -- Required indexes are matched by ordered key columns, uniqueness, filter, and enabled state.
    -- A same-named index with different keys (or a differently named equivalent index) is handled
    -- according to behavior, not according to its name.
    DECLARE @requiredIndexes TABLE
    (
        ProofName nvarchar(200) NOT NULL,
        TableName sysname NOT NULL,
        IsUnique bit NOT NULL,
        KeyCount tinyint NOT NULL,
        Key1 sysname NOT NULL,
        Key2 sysname NULL
    );

    INSERT @requiredIndexes (ProofName, TableName, IsUnique, KeyCount, Key1, Key2)
    VALUES
        (N'AddAgencyId: Users has an AgencyId lookup index',
         N'Users', 0, 1, N'AgencyId', NULL),
        (N'AddAgencyId: People has an AgencyId lookup index',
         N'People', 0, 1, N'AgencyId', NULL),
        (N'AddAgencyId: Notes has an AgencyId lookup index',
         N'Notes', 0, 1, N'AgencyId', NULL),
        (N'TenantScopeSettingsAndProviders: Settings permits one row per agency',
         N'Settings', 1, 1, N'AgencyId', NULL),
        (N'TenantScopeSettingsAndProviders: Providers is indexed by agency then name',
         N'Providers', 0, 2, N'AgencyId', N'Name');

    -- Same shape as the column proof: collect every failure, refuse on the first outside
    -- -ProofsOnly. @proofFailures is still empty here on the default path.
    INSERT @proofFailures (Failure)
    SELECT CONCAT(N'Semantic proof failed: ', required.ProofName, N'.')
    FROM @requiredIndexes AS required
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS indexObject
        WHERE indexObject.object_id = OBJECT_ID(N'dbo.' + required.TableName)
          AND indexObject.type = 2
          AND indexObject.is_unique = required.IsUnique
          AND indexObject.is_disabled = 0
          AND indexObject.is_hypothetical = 0
          AND indexObject.has_filter = 0
          AND (SELECT COUNT(*)
               FROM sys.index_columns AS allIndexColumns
               WHERE allIndexColumns.object_id = indexObject.object_id
                 AND allIndexColumns.index_id = indexObject.index_id) = required.KeyCount
          AND EXISTS
          (
              SELECT 1
              FROM sys.index_columns AS firstKey
              INNER JOIN sys.columns AS firstColumn
                  ON firstColumn.object_id = firstKey.object_id
                 AND firstColumn.column_id = firstKey.column_id
              WHERE firstKey.object_id = indexObject.object_id
                AND firstKey.index_id = indexObject.index_id
                AND firstKey.key_ordinal = 1
                AND firstColumn.name = required.Key1
          )
          AND
          (
              (required.KeyCount = 1 AND required.Key2 IS NULL)
              OR EXISTS
              (
                  SELECT 1
                  FROM sys.index_columns AS secondKey
                  INNER JOIN sys.columns AS secondColumn
                      ON secondColumn.object_id = secondKey.object_id
                     AND secondColumn.column_id = secondKey.column_id
                  WHERE secondKey.object_id = indexObject.object_id
                    AND secondKey.index_id = indexObject.index_id
                    AND secondKey.key_ordinal = 2
                    AND secondColumn.name = required.Key2
              )
          )
    )
    ORDER BY required.ProofName;

    IF @proofsOnly = 0 AND EXISTS (SELECT 1 FROM @proofFailures)
    BEGIN
        DECLARE @indexFailure nvarchar(2048) =
            (SELECT TOP (1) CONCAT(Failure, N' Migration history was not changed.')
             FROM @proofFailures ORDER BY Ordinal);
        THROW 51709, @indexFailure, 1;
    END;

    -- Required foreign keys are matched by their one-column mapping, trusted/enabled state,
    -- and NO ACTION update/delete behavior. Constraint names are not treated as proof.
    DECLARE @requiredForeignKeys TABLE
    (
        ProofName nvarchar(200) NOT NULL,
        ParentTable sysname NOT NULL,
        ParentColumn sysname NOT NULL
    );

    INSERT @requiredForeignKeys (ProofName, ParentTable, ParentColumn)
    VALUES
        (N'AddAgencyId: Users.AgencyId references Agencies.Id with NO ACTION',
         N'Users', N'AgencyId'),
        (N'AddAgencyId: People.AgencyId references Agencies.Id with NO ACTION',
         N'People', N'AgencyId'),
        (N'AddAgencyId: Notes.AgencyId references Agencies.Id with NO ACTION',
         N'Notes', N'AgencyId'),
        (N'TenantScopeSettingsAndProviders: Settings.AgencyId references Agencies.Id with NO ACTION',
         N'Settings', N'AgencyId'),
        (N'TenantScopeSettingsAndProviders: Providers.AgencyId references Agencies.Id with NO ACTION',
         N'Providers', N'AgencyId');

    -- Same shape as the column proof: collect every failure, refuse on the first outside
    -- -ProofsOnly. @proofFailures is still empty here on the default path.
    INSERT @proofFailures (Failure)
    SELECT CONCAT(N'Semantic proof failed: ', required.ProofName, N'.')
    FROM @requiredForeignKeys AS required
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS foreignKey
        INNER JOIN sys.foreign_key_columns AS foreignKeyColumn
            ON foreignKeyColumn.constraint_object_id = foreignKey.object_id
           AND foreignKeyColumn.constraint_column_id = 1
        INNER JOIN sys.columns AS parentColumn
            ON parentColumn.object_id = foreignKeyColumn.parent_object_id
           AND parentColumn.column_id = foreignKeyColumn.parent_column_id
        INNER JOIN sys.columns AS referencedColumn
            ON referencedColumn.object_id = foreignKeyColumn.referenced_object_id
           AND referencedColumn.column_id = foreignKeyColumn.referenced_column_id
        WHERE foreignKey.parent_object_id = OBJECT_ID(N'dbo.' + required.ParentTable)
          AND foreignKey.referenced_object_id = OBJECT_ID(N'dbo.Agencies')
          AND parentColumn.name = required.ParentColumn
          AND referencedColumn.name = N'Id'
          AND foreignKey.delete_referential_action = 0
          AND foreignKey.update_referential_action = 0
          AND foreignKey.is_disabled = 0
          AND foreignKey.is_not_trusted = 0
          AND (SELECT COUNT(*)
               FROM sys.foreign_key_columns AS allForeignKeyColumns
               WHERE allForeignKeyColumns.constraint_object_id = foreignKey.object_id) = 1
    )
    ORDER BY required.ProofName;

    IF @proofsOnly = 0 AND EXISTS (SELECT 1 FROM @proofFailures)
    BEGIN
        DECLARE @foreignKeyFailure nvarchar(2048) =
            (SELECT TOP (1) CONCAT(Failure, N' Migration history was not changed.')
             FROM @proofFailures ORDER BY Ordinal);
        THROW 51710, @foreignKeyFailure, 1;
    END;

    -- The history table must enforce one row per migration id before this script relies on its
    -- rerunnable anti-join. As above, the primary-key constraint's name is irrelevant.
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.indexes AS primaryIndex
        INNER JOIN sys.index_columns AS primaryColumn
            ON primaryColumn.object_id = primaryIndex.object_id
           AND primaryColumn.index_id = primaryIndex.index_id
           AND primaryColumn.key_ordinal = 1
        INNER JOIN sys.columns AS columnObject
            ON columnObject.object_id = primaryColumn.object_id
           AND columnObject.column_id = primaryColumn.column_id
        WHERE primaryIndex.object_id = OBJECT_ID(N'dbo.__EFMigrationsHistory')
          AND primaryIndex.is_primary_key = 1
          AND primaryIndex.is_unique = 1
          AND primaryIndex.is_disabled = 0
          AND columnObject.name = N'MigrationId'
          AND (SELECT COUNT(*)
               FROM sys.index_columns AS allPrimaryColumns
               WHERE allPrimaryColumns.object_id = primaryIndex.object_id
                 AND allPrimaryColumns.index_id = primaryIndex.index_id) = 1
    )
        INSERT @proofFailures (Failure) VALUES (N'Semantic proof failed: migration history is not keyed by MigrationId.');
        IF @proofsOnly = 0 THROW 51711, 'Semantic proof failed: migration history is not keyed by MigrationId.', 1;

    -- -ProofsOnly stops here, before anything is written, whether or not any proof failed. It is a
    -- reporting mode: it must not be capable of changing history even on a database that passes.
    IF @proofsOnly = 1
    BEGIN
        ROLLBACK TRANSACTION;
        SELECT
            DB_NAME() AS DatabaseName,
            @actualEnvironment AS EnvironmentName,
            CASE WHEN EXISTS (SELECT 1 FROM @proofFailures) THEN 0 ELSE 4 END AS SemanticProofsVerified,
            0 AS SurvivingHistoryRowsWritten,
            0 AS SupersededHistoryRowsRemoved,
            CAST(1 AS bit) AS RolledBack;
        SELECT Ordinal, Failure FROM @proofFailures ORDER BY Ordinal;
        RETURN;
    END;

    SET @semanticProofsVerified = 4;

    -- All four surviving migrations have now been proven. Insert only absent rows. The serializable
    -- transaction and lock hints keep simultaneous reruns from racing each other.
    INSERT dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    SELECT pending.MigrationId, N'10.0.5'
    FROM
    (
        VALUES
            (N'20260416011235_AddAgencyId'),
            (N'20260812090000_TenantScopeSettingsAndProviders'),
            (N'20260816120000_AddNoteMinutesAndStartTime'),
            (N'20260825163103_AddConsumerEmail')
    ) AS pending(MigrationId)
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.__EFMigrationsHistory AS existing WITH (UPDLOCK, HOLDLOCK)
        WHERE existing.MigrationId = pending.MigrationId
    );
    SET @historyRowsWritten = @@ROWCOUNT;

    IF (SELECT COUNT(*)
        FROM dbo.__EFMigrationsHistory
        WHERE MigrationId IN
        (
            N'20260416011235_AddAgencyId',
            N'20260812090000_TenantScopeSettingsAndProviders',
            N'20260816120000_AddNoteMinutesAndStartTime',
            N'20260825163103_AddConsumerEmail'
        )) <> 4
        THROW 51712, 'Not all four surviving migration rows are present; superseded rows were retained.', 1;

    -- Superseded rows are removed last, only after the semantic proof and surviving-row check.
    DELETE historyRow
    FROM dbo.__EFMigrationsHistory AS historyRow
    WHERE historyRow.MigrationId = N'20260416005941_AddingAgencyId'
      AND EXISTS
          (SELECT 1 FROM dbo.__EFMigrationsHistory
           WHERE MigrationId = N'20260416011235_AddAgencyId');
    SET @supersededRowsRemoved += @@ROWCOUNT;

    DELETE historyRow
    FROM dbo.__EFMigrationsHistory AS historyRow
    WHERE historyRow.MigrationId = N'20260825155740_AddConsumerEmail'
      AND EXISTS
          (SELECT 1 FROM dbo.__EFMigrationsHistory
           WHERE MigrationId = N'20260825163103_AddConsumerEmail');
    SET @supersededRowsRemoved += @@ROWCOUNT;

    IF EXISTS
    (
        SELECT 1
        FROM dbo.__EFMigrationsHistory
        WHERE MigrationId IN
        (
            N'20260416005941_AddingAgencyId',
            N'20260825155740_AddConsumerEmail'
        )
    )
        THROW 51713, 'A superseded migration row remains; the reconciliation was rolled back.', 1;

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
    @semanticProofsVerified AS SemanticProofsVerified,
    @historyRowsWritten AS SurvivingHistoryRowsWritten,
    @supersededRowsRemoved AS SupersededHistoryRowsRemoved,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The Demo history reconciliation verification row was not returned.'
    }
    $summary = [pscustomobject][ordered]@{
        DatabaseName                   = $reader.GetString(0)
        EnvironmentName                = $reader.GetString(1)
        SemanticProofsVerified         = $reader.GetInt32(2)
        SurvivingHistoryRowsWritten    = $reader.GetInt32(3)
        SupersededHistoryRowsRemoved   = $reader.GetInt32(4)
        RolledBack                     = $reader.GetBoolean(5)
    }

    # -ProofsOnly returns a second result set naming every proof that failed. It is read before the
    # summary is emitted so the failures are still available if the caller stops at the first object.
    $failures = @()
    if ($ProofsOnly -and $reader.NextResult()) {
        while ($reader.Read()) {
            $failures += [pscustomobject][ordered]@{
                Ordinal = $reader.GetInt32(0)
                Failure = $reader.GetString(1)
            }
        }
    }
    $reader.Close()

    $summary
    if ($ProofsOnly) {
        if ($failures.Count -eq 0) {
            Write-Output 'All schema proofs passed. No history change was attempted in -ProofsOnly.'
        }
        else {
            Write-Output "$($failures.Count) schema proof(s) failed. No history change was attempted."
            $failures | ForEach-Object { Write-Output ('  {0}. {1}' -f $_.Ordinal, $_.Failure) }
        }
    }
}
finally {
    $connection.Dispose()
}
