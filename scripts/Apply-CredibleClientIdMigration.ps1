<#
.SYNOPSIS
    Applies the 2026-09-01 Credible client identifier column to a long-lived Sati database.

.DESCRIPTION
    Covers one migration:

      20260901232228_AddPersonCredibleClientId
          People.CredibleClientId nvarchar(32) NULL

    The column is the dedupe and idempotency key for Credible export import: re-running
    an onboarding folder must report rather than duplicate. It is additive, nullable,
    and backfills nothing, so this is the least invasive kind of schema change Sati has.

    It still gets a guarded script rather than EF's generated idempotent one. SatiDemo
    and SatiProduction have acquired columns outside the migration chain, so
    __EFMigrationsHistory and the actual schema disagree in both directions, and the
    generated script fails with SQL 2705 on a column that exists without its history
    row. Every statement here guards on the real schema instead.

    Rerunnable, and it verifies semantics rather than names: a CredibleClientId that
    already exists must be nvarchar(32) and nullable, or this refuses. A column of the
    right name and the wrong shape is how a dedupe key silently truncates an identifier
    or rejects a null, and neither failure announces itself.

    ASCII only. Windows PowerShell 5.1 reads a .ps1 without a BOM as ANSI, so a stray
    non-ASCII character corrupts a string literal elsewhere in the file.

.NOTES
    NOT UNIQUE, DELIBERATELY. Two agencies run separate Credible instances whose client
    ids collide numerically and mean different people, so uniqueness could only ever be
    per agency. A filtered unique index on (AgencyId, CredibleClientId) is the eventual
    shape; it is not created here because bulk import reports duplicates rather than
    relying on the database to refuse them, and an index added before that behaviour is
    exercised would turn a reported skip into an unhandled write failure.

    The 32-character bound exists so that index does not need a narrowing migration
    first. Form.Type had to be narrowed from nvarchar(max) in a later migration for
    exactly that reason.

.EXAMPLE
    ./scripts/Apply-CredibleClientIdMigration.ps1 -DatabaseName SatiProduction -WhatIfOnly

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net `
        --query accessToken --output tsv
    ./scripts/Apply-CredibleClientIdMigration.ps1 `
        -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net `
        -AccessToken $token -WhatIfOnly
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
    $command.CommandTimeout = 300
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', $expectedEnvironment) | Out-Null
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Fail closed on identity ----------------------------------------------------
IF DB_NAME() <> @expectedDatabase
    THROW 51700, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51701, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51702, 'dbo.People does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51703, 'dbo.__EFMigrationsHistory does not exist; the history row could not be written.', 1;

DECLARE @columnsAdded int = 0;
DECLARE @historyRowsWritten int = 0;

BEGIN TRANSACTION;

-- 1. The column ---------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.People') AND name = N'CredibleClientId')
BEGIN
    ALTER TABLE dbo.People ADD CredibleClientId nvarchar(32) NULL;
    SET @columnsAdded = 1;
END
ELSE
BEGIN
    -- Present already. Prove it is the column this migration means, not merely one
    -- wearing the name. A shorter type truncates an identifier that is the dedupe key;
    -- a NOT NULL one rejects every consumer who did not come from Credible.
    IF NOT EXISTS (
        SELECT 1
        FROM sys.columns c
        JOIN sys.types t ON t.user_type_id = c.user_type_id
        WHERE c.object_id = OBJECT_ID(N'dbo.People')
          AND c.name = N'CredibleClientId'
          AND t.name = N'nvarchar'
          AND c.max_length = 64      -- nvarchar(32) stores 2 bytes per character
          AND c.is_nullable = 1)
        THROW 51704, 'People.CredibleClientId exists with an unexpected type, length, or nullability.', 1;
END;

-- 2. History ------------------------------------------------------------------
INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
SELECT N'20260901232228_AddPersonCredibleClientId', N'10.0.5'
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory e
    WHERE e.MigrationId = N'20260901232228_AddPersonCredibleClientId');
SET @historyRowsWritten = @@ROWCOUNT;

IF @whatIfOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @columnsAdded AS ColumnsAdded,
    @historyRowsWritten AS HistoryRowsWritten,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The Credible client identifier migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName       = $reader.GetString(0)
        EnvironmentName    = $reader.GetString(1)
        ColumnsAdded       = $reader.GetInt32(2)
        HistoryRowsWritten = $reader.GetInt32(3)
        RolledBack         = $reader.GetBoolean(4)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
