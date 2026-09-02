<#
.SYNOPSIS
    Applies the Credible profile policy and VR assignment schema to SatiDemo.

.DESCRIPTION
    Covers two additive migrations:

      20260902140636_AddCredibleProfileUpdateSetting
      20260902142303_AddVocationalRehabilitationAssignments

    The runner fails closed on database and environment identity, guards every
    change against the actual schema, verifies SQL types and defaults, and is
    rerunnable. Use -WhatIfOnly first to execute all checks and roll back.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net `
        --query accessToken --output tsv
    .\scripts\Apply-CredibleProfileAndVrMigrations.ps1 `
        -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net `
        -AccessToken $token -WhatIfOnly
#>
[CmdletBinding()]
param(
    [ValidateSet('SatiDemo')]
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [Parameter(Mandatory = $true)]
    [string]$SqlServer,

    [Parameter(Mandatory = $true)]
    [string]$AccessToken,

    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$connectionString = "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.AccessToken = $AccessToken
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 300
    $command.Parameters.AddWithValue('@expectedDatabase', $DatabaseName) | Out-Null
    $command.Parameters.AddWithValue('@expectedEnvironment', 'Demo') | Out-Null
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() COLLATE Latin1_General_100_BIN2 <> @expectedDatabase
    THROW 51800, 'The connected database is not exactly SatiDemo.', 1;
IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL OR NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName COLLATE Latin1_General_100_BIN2 = @expectedEnvironment)
    THROW 51801, 'The database identity marker is not exactly Demo.', 1;
IF OBJECT_ID(N'dbo.Settings', N'U') IS NULL OR OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51802, 'The required Sati tables are missing.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51803, 'The EF migration history table is missing.', 1;

DECLARE @columnsAdded int = 0;
DECLARE @historyRowsWritten int = 0;

BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.Settings', N'AllowCredibleProfileUpdates') IS NULL
BEGIN
    ALTER TABLE dbo.Settings ADD AllowCredibleProfileUpdates bit NOT NULL
        CONSTRAINT DF_Settings_AllowCredibleProfileUpdates DEFAULT (0) WITH VALUES;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.Settings', N'VrAssistantTitle') IS NULL
BEGIN
    ALTER TABLE dbo.Settings ADD VrAssistantTitle nvarchar(100) NOT NULL
        CONSTRAINT DF_Settings_VrAssistantTitle DEFAULT (N'VSA') WITH VALUES;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.People', N'VrAssistantName') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD VrAssistantName nvarchar(150) NULL;
    SET @columnsAdded += 1;
END;

IF COL_LENGTH(N'dbo.People', N'VrCounselorName') IS NULL
BEGIN
    ALTER TABLE dbo.People ADD VrCounselorName nvarchar(150) NULL;
    SET @columnsAdded += 1;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'AllowCredibleProfileUpdates'
      AND t.name = N'bit' AND c.max_length = 1 AND c.is_nullable = 0)
    THROW 51804, 'AllowCredibleProfileUpdates has an unexpected SQL definition.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'VrAssistantTitle'
      AND t.name = N'nvarchar' AND c.max_length = 200 AND c.is_nullable = 0)
    THROW 51805, 'VrAssistantTitle has an unexpected SQL definition.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People')
      AND c.name = N'VrAssistantName'
      AND t.name = N'nvarchar' AND c.max_length = 300 AND c.is_nullable = 1)
    THROW 51806, 'VrAssistantName has an unexpected SQL definition.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.People')
      AND c.name = N'VrCounselorName'
      AND t.name = N'nvarchar' AND c.max_length = 300 AND c.is_nullable = 1)
    THROW 51807, 'VrCounselorName has an unexpected SQL definition.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.default_constraints d ON d.object_id = c.default_object_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'AllowCredibleProfileUpdates'
      AND REPLACE(REPLACE(REPLACE(d.definition, N'(', N''), N')', N''), N' ', N'') = N'0')
    THROW 51808, 'AllowCredibleProfileUpdates does not have the required false default.', 1;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.default_constraints d ON d.object_id = c.default_object_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Settings')
      AND c.name = N'VrAssistantTitle'
      AND REPLACE(REPLACE(REPLACE(d.definition, N'(', N''), N')', N''), N' ', N'') IN (N'N''VSA''', N'''VSA'''))
    THROW 51809, 'VrAssistantTitle does not have the required VSA default.', 1;

DECLARE @blankTitles bigint;
EXEC sys.sp_executesql
    N'SELECT @count = COUNT_BIG(*) FROM dbo.Settings WHERE NULLIF(LTRIM(RTRIM(VrAssistantTitle)), N'''') IS NULL;',
    N'@count bigint OUTPUT', @count = @blankTitles OUTPUT;
IF @blankTitles <> 0
    THROW 51810, 'A Settings row has a blank VR assistant title.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260902140636_AddCredibleProfileUpdateSetting')
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260902140636_AddCredibleProfileUpdateSetting', N'10.0.5');
    SET @historyRowsWritten += 1;
END;

IF NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory
               WHERE MigrationId = N'20260902142303_AddVocationalRehabilitationAssignments')
BEGIN
    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (N'20260902142303_AddVocationalRehabilitationAssignments', N'10.0.5');
    SET @historyRowsWritten += 1;
END;

IF @whatIfOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @columnsAdded AS ColumnsAdded,
       @historyRowsWritten AS HistoryRowsWritten,
       @blankTitles AS BlankTitles,
       @whatIfOnly AS RolledBack,
       (SELECT COUNT_BIG(*) FROM dbo.Settings) AS SettingsRows,
       (SELECT COUNT_BIG(*) FROM dbo.People) AS PersonRows;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration verification row was not returned.' }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        ColumnsAdded = $reader.GetInt32(2)
        HistoryRowsWritten = $reader.GetInt32(3)
        BlankTitles = $reader.GetInt64(4)
        RolledBack = $reader.GetBoolean(5)
        SettingsRows = $reader.GetInt64(6)
        PersonRows = $reader.GetInt64(7)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
