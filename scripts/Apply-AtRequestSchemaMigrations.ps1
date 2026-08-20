<#
.SYNOPSIS
    Repairs and verifies the post-provider AT Request schema on a long-lived Sati
    database.

.DESCRIPTION
    Applies four additive AT migrations by inspecting the schema rather than
    trusting __EFMigrationsHistory. The script is transactional, rerunnable, and
    refuses a mismatched database/environment before making changes.

    Covered migrations:
      20260815192035_AddAtRequestSalesTaxOverride
      20260815212109_AddAtRequestAttestation
      20260815223729_AddAtRequestItemScreenshot
      20260815230835_AddAtRequestPassthroughRate
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
    THROW 51500, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51501, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.ATRequests', N'U') IS NULL
    THROW 51502, 'dbo.ATRequests does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.ATRequestItems', N'U') IS NULL
    THROW 51503, 'dbo.ATRequestItems does not exist; this is not the expected Sati schema.', 1;

DECLARE @columnsAdded int = 0;
DECLARE @historyRowsWritten int = 0;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.ATRequests', N'SalesTaxOverridden') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests
        ADD SalesTaxOverridden bit NOT NULL
            CONSTRAINT DF_ATRequests_SalesTaxOverridden DEFAULT (0) WITH VALUES;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.ATRequests', N'AttestationStatement') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests ADD AttestationStatement nvarchar(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.ATRequests', N'SignedAtUtc') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests ADD SignedAtUtc datetime2 NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.ATRequests', N'SignedByName') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests ADD SignedByName nvarchar(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.ATRequests', N'SignedByRole') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests ADD SignedByRole nvarchar(max) NULL;
    SET @columnsAdded += 1;
END;
IF COL_LENGTH(N'dbo.ATRequests', N'SignedByUserId') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests ADD SignedByUserId int NULL;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.ATRequestItems', N'ScreenshotPng') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequestItems ADD ScreenshotPng varbinary(max) NULL;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.ATRequests', N'PassthroughRate') IS NULL
BEGIN
    ALTER TABLE dbo.ATRequests ADD PassthroughRate decimal(5,4) NULL;
    SET @columnsAdded += 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260815192035_AddAtRequestSalesTaxOverride')
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260815192035_AddAtRequestSalesTaxOverride', N'10.0.5');
    SET @historyRowsWritten += 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260815212109_AddAtRequestAttestation')
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260815212109_AddAtRequestAttestation', N'10.0.5');
    SET @historyRowsWritten += 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260815223729_AddAtRequestItemScreenshot')
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260815223729_AddAtRequestItemScreenshot', N'10.0.5');
    SET @historyRowsWritten += 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260815230835_AddAtRequestPassthroughRate')
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260815230835_AddAtRequestPassthroughRate', N'10.0.5');
    SET @historyRowsWritten += 1;
END;

COMMIT TRANSACTION;

IF COL_LENGTH(N'dbo.ATRequests', N'SalesTaxOverridden') IS NULL
    OR COL_LENGTH(N'dbo.ATRequests', N'AttestationStatement') IS NULL
    OR COL_LENGTH(N'dbo.ATRequests', N'SignedAtUtc') IS NULL
    OR COL_LENGTH(N'dbo.ATRequests', N'SignedByName') IS NULL
    OR COL_LENGTH(N'dbo.ATRequests', N'SignedByRole') IS NULL
    OR COL_LENGTH(N'dbo.ATRequests', N'SignedByUserId') IS NULL
    OR COL_LENGTH(N'dbo.ATRequestItems', N'ScreenshotPng') IS NULL
    OR COL_LENGTH(N'dbo.ATRequests', N'PassthroughRate') IS NULL
    THROW 51504, 'One or more AT Request columns are still missing after the migration ran.', 1;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @columnsAdded AS ColumnsAdded,
    @historyRowsWritten AS HistoryRowsWritten,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The AT Request migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName       = $reader.GetString(0)
        EnvironmentName    = $reader.GetString(1)
        ColumnsAdded       = $reader.GetInt32(2)
        HistoryRowsWritten = $reader.GetInt32(3)
        MigrationCount     = $reader.GetInt64(4)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
