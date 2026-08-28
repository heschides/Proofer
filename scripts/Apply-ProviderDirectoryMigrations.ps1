<#
.SYNOPSIS
    Applies the 2026-08-28 provider directory and consumer provider-list schema to a
    long-lived Sati database.

.DESCRIPTION
    Covers four migrations that EF cannot safely apply to SatiDemo or SatiProduction:

      20260828180603_AddProviderAffiliation   Providers.MedicalKind, Providers.ParentProviderId
      20260828182608_AddConsumerProviderList  dbo.PersonProviders
      20260828193518_AddProviderContacts      dbo.ProviderContacts
      20260828195515_AddTestConsumerMarker    People.IsTestData

    SatiDemo and SatiProduction have acquired columns outside the migration chain, so
    __EFMigrationsHistory and the actual schema disagree in both directions. EF's generated
    idempotent script guards only on history and fails with SQL 2705 on a column that already
    exists without its history row. Every statement here guards on the real schema instead.

    The script is rerunnable. It adds only what is missing, verifies that anything already
    present has the expected semantics rather than merely the expected name, and fails closed
    on a database or environment identity mismatch.

.NOTES
    IsTestData backfill. 20260828195515_AddTestConsumerMarker marks every consumer in an
    isolated Demo database as test data, because every Demo record is synthetic by design.
    That backfill is reproduced here under the same guard and runs ONLY when the connected
    database is SatiDemo and its identity marker says Demo. It is a data change, not a schema
    change: after it runs, every Demo consumer becomes eligible for the Admin test-data
    deletion command. It never runs against SatiProduction.

.EXAMPLE
    ./scripts/Apply-ProviderDirectoryMigrations.ps1 -DatabaseName SatiProduction

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net `
        --query accessToken --output tsv
    ./scripts/Apply-ProviderDirectoryMigrations.ps1 `
        -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net `
        -AccessToken $token
#>
param(
    [ValidateSet('SatiDemo', 'SatiProduction')]
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [string]$SqlServer = '(localdb)\MSSQLLocalDB',

    [string]$AccessToken,

    # Reports what would change and rolls back without committing.
    [switch]$WhatIfOnly
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
    $command.CommandTimeout = 180
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── Fail closed on identity ──────────────────────────────────────────────────
IF DB_NAME() <> @expectedDatabase
    THROW 51500, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51501, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.Providers', N'U') IS NULL
    THROW 51502, 'dbo.Providers does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51503, 'dbo.People does not exist; this is not the expected Sati schema.', 1;

DECLARE @columnsAdded int = 0;
DECLARE @tablesAdded int = 0;
DECLARE @indexesAdded int = 0;
DECLARE @foreignKeysAdded int = 0;
DECLARE @consumersMarkedAsTestData int = 0;

DECLARE @history TABLE (MigrationId nvarchar(150) PRIMARY KEY);
INSERT @history(MigrationId) VALUES
    (N'20260828180603_AddProviderAffiliation'),
    (N'20260828182608_AddConsumerProviderList'),
    (N'20260828193518_AddProviderContacts'),
    (N'20260828195515_AddTestConsumerMarker');

BEGIN TRANSACTION;

-- ── 1. Provider affiliation columns ──────────────────────────────────────────
IF COL_LENGTH(N'dbo.Providers', N'MedicalKind') IS NULL
BEGIN
    ALTER TABLE dbo.Providers ADD MedicalKind nvarchar(20) NULL;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.Providers', N'ParentProviderId') IS NULL
BEGIN
    ALTER TABLE dbo.Providers ADD ParentProviderId int NULL;
    SET @columnsAdded += 1;
END;

-- Dynamic SQL for everything that names a column added in this same batch, so SQL Server
-- compiles it after the ALTER rather than rejecting the batch during name binding.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.Providers') AND name = N'IX_Providers_ParentProviderId')
BEGIN
    EXEC(N'CREATE INDEX IX_Providers_ParentProviderId ON dbo.Providers(ParentProviderId);');
    SET @indexesAdded += 1;
END;

-- Restrict, not cascade: deleting a parent must never silently promote its subtree.
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys
               WHERE parent_object_id = OBJECT_ID(N'dbo.Providers')
                 AND name = N'FK_Providers_Providers_ParentProviderId')
BEGIN
    EXEC(N'ALTER TABLE dbo.Providers ADD CONSTRAINT FK_Providers_Providers_ParentProviderId
           FOREIGN KEY (ParentProviderId) REFERENCES dbo.Providers(Id);');
    SET @foreignKeysAdded += 1;
END;

IF EXISTS (SELECT 1 FROM sys.foreign_keys
           WHERE parent_object_id = OBJECT_ID(N'dbo.Providers')
             AND name = N'FK_Providers_Providers_ParentProviderId'
             AND delete_referential_action <> 0)
    THROW 51504, 'FK_Providers_Providers_ParentProviderId exists but does not use NO ACTION on delete.', 1;

-- ── 2. Consumer provider list ────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.PersonProviders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PersonProviders
    (
        Id int IDENTITY(1,1) NOT NULL,
        PersonId int NOT NULL,
        ProviderId int NOT NULL,
        Role nvarchar(80) NULL,
        IsPrimaryCare bit NOT NULL,
        StartDate datetime2 NULL,
        EndDate datetime2 NULL,
        HasActiveRelease bit NOT NULL,
        SortOrder int NOT NULL,
        CONSTRAINT PK_PersonProviders PRIMARY KEY (Id),
        CONSTRAINT FK_PersonProviders_People_PersonId
            FOREIGN KEY (PersonId) REFERENCES dbo.People(Id) ON DELETE CASCADE,
        CONSTRAINT FK_PersonProviders_Providers_ProviderId
            FOREIGN KEY (ProviderId) REFERENCES dbo.Providers(Id)
    );
    SET @tablesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PersonProviders')
                 AND name = N'IX_PersonProviders_OneCurrentLinkPerProvider')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_PersonProviders_OneCurrentLinkPerProvider
           ON dbo.PersonProviders(PersonId, ProviderId) WHERE EndDate IS NULL;');
    SET @indexesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PersonProviders')
                 AND name = N'IX_PersonProviders_OneCurrentPrimaryCare')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_PersonProviders_OneCurrentPrimaryCare
           ON dbo.PersonProviders(PersonId) WHERE [IsPrimaryCare] = 1 AND [EndDate] IS NULL;');
    SET @indexesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PersonProviders')
                 AND name = N'IX_PersonProviders_PersonId_EndDate')
BEGIN
    EXEC(N'CREATE INDEX IX_PersonProviders_PersonId_EndDate ON dbo.PersonProviders(PersonId, EndDate);');
    SET @indexesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PersonProviders')
                 AND name = N'IX_PersonProviders_ProviderId')
BEGIN
    EXEC(N'CREATE INDEX IX_PersonProviders_ProviderId ON dbo.PersonProviders(ProviderId);');
    SET @indexesAdded += 1;
END;

-- Both uniqueness rules are load-bearing: one current primary care provider per consumer,
-- one current link per provider. A same-named non-unique index is drift, not success.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PersonProviders')
                 AND name = N'IX_PersonProviders_OneCurrentPrimaryCare' AND is_unique = 1)
    THROW 51505, 'IX_PersonProviders_OneCurrentPrimaryCare exists but is not unique.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.PersonProviders')
                 AND name = N'IX_PersonProviders_OneCurrentLinkPerProvider' AND is_unique = 1)
    THROW 51506, 'IX_PersonProviders_OneCurrentLinkPerProvider exists but is not unique.', 1;

-- ── 3. Provider contacts ─────────────────────────────────────────────────────
IF OBJECT_ID(N'dbo.ProviderContacts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProviderContacts
    (
        Id int IDENTITY(1,1) NOT NULL,
        ProviderId int NOT NULL,
        Name nvarchar(150) NOT NULL,
        Role nvarchar(100) NULL,
        Phone nvarchar(30) NULL,
        Extension nvarchar(10) NULL,
        Email nvarchar(254) NULL,
        IsPrimary bit NOT NULL,
        SortOrder int NOT NULL,
        CONSTRAINT PK_ProviderContacts PRIMARY KEY (Id),
        CONSTRAINT FK_ProviderContacts_Providers_ProviderId
            FOREIGN KEY (ProviderId) REFERENCES dbo.Providers(Id) ON DELETE CASCADE
    );
    SET @tablesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ProviderContacts')
                 AND name = N'IX_ProviderContacts_OnePrimary')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_ProviderContacts_OnePrimary
           ON dbo.ProviderContacts(ProviderId) WHERE [IsPrimary] = 1;');
    SET @indexesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ProviderContacts')
                 AND name = N'IX_ProviderContacts_ProviderId_SortOrder')
BEGIN
    EXEC(N'CREATE INDEX IX_ProviderContacts_ProviderId_SortOrder
           ON dbo.ProviderContacts(ProviderId, SortOrder);');
    SET @indexesAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.ProviderContacts')
                 AND name = N'IX_ProviderContacts_OnePrimary' AND is_unique = 1)
    THROW 51507, 'IX_ProviderContacts_OnePrimary exists but is not unique.', 1;

-- ── 4. Test consumer marker ──────────────────────────────────────────────────
IF COL_LENGTH(N'dbo.People', N'IsTestData') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD IsTestData bit NOT NULL CONSTRAINT DF_People_IsTestData DEFAULT(0);
    SET @columnsAdded += 1;
END;

-- Demo only, and only when the identity marker agrees. Every record in the isolated Demo
-- environment is synthetic by design; Production purpose cannot be inferred from a row.
IF DB_NAME() = N'SatiDemo'
   AND EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
BEGIN
    EXEC sys.sp_executesql
        N'UPDATE dbo.People SET IsTestData = 1 WHERE IsTestData = 0;
          SELECT @marked = @@ROWCOUNT;',
        N'@marked int OUTPUT',
        @marked = @consumersMarkedAsTestData OUTPUT;
END;

-- ── History reconciliation ───────────────────────────────────────────────────
DECLARE @historyRowsWritten int = 0;
INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
SELECT h.MigrationId, N'10.0.5'
FROM @history h
WHERE NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory e WHERE e.MigrationId = h.MigrationId);
SET @historyRowsWritten = @@ROWCOUNT;

IF @whatIfOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @tablesAdded AS TablesAdded,
    @columnsAdded AS ColumnsAdded,
    @indexesAdded AS IndexesAdded,
    @foreignKeysAdded AS ForeignKeysAdded,
    @consumersMarkedAsTestData AS ConsumersMarkedAsTestData,
    @historyRowsWritten AS HistoryRowsWritten,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The provider directory migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName              = $reader.GetString(0)
        EnvironmentName           = $reader.GetString(1)
        TablesAdded               = $reader.GetInt32(2)
        ColumnsAdded              = $reader.GetInt32(3)
        IndexesAdded              = $reader.GetInt32(4)
        ForeignKeysAdded          = $reader.GetInt32(5)
        ConsumersMarkedAsTestData = $reader.GetInt32(6)
        HistoryRowsWritten        = $reader.GetInt32(7)
        RolledBack                = $reader.GetBoolean(8)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
