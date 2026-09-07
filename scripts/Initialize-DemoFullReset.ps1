[CmdletBinding()]
param(
    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',
    [string]$Database = 'SatiDemo',
    [string]$ResetIdentityName = 'sati-demo-refresh-satilogica',
    [string]$ApiIdentityName = 'sati-demo-api-satilogica-46417',
    [switch]$ReplaceBaseline
)

$ErrorActionPreference = 'Stop'
if ($Database -cne 'SatiDemo') { throw 'This operation is restricted to SatiDemo.' }
if ($ResetIdentityName -cne 'sati-demo-refresh-satilogica' -or
    $ApiIdentityName -cne 'sati-demo-api-satilogica-46417') {
    throw 'The Demo reset principals do not match the reviewed managed identities.'
}
if (-not $ReplaceBaseline) {
    throw 'Capturing a full Demo baseline is deliberate and destructive to any older baseline. Pass -ReplaceBaseline after approving the current curated Demo data.'
}

$token = az account get-access-token --resource 'https://database.windows.net/' --query accessToken -o tsv
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($token)) {
    throw 'Could not obtain an Azure SQL access token.'
}

$connection = [System.Data.SqlClient.SqlConnection]::new(
    "Server=$SqlServer;Database=$Database;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;")
$connection.AccessToken = $token
$connection.Open()
try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 1200
    $command.CommandText = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'SatiDemo' OR NOT EXISTS
   (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N'Demo')
    THROW 51000, 'Refusing to capture a baseline outside the validated Demo database.', 1;

BEGIN TRANSACTION;
DECLARE @captureLockResult int;
EXEC @captureLockResult=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
    @LockMode=N'Exclusive', @LockOwner=N'Transaction', @LockTimeout=60000;
IF @captureLockResult < 0 THROW 51001, 'The Demo is busy; baseline capture did not begin.', 1;
IF SCHEMA_ID(N'demo_baseline') IS NULL EXEC(N'CREATE SCHEMA demo_baseline AUTHORIZATION dbo;');

DECLARE @drop nvarchar(max)=N'';
SELECT @drop += N'DROP TABLE demo_baseline.' + QUOTENAME(t.name) + N';'
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name=N'demo_baseline';
EXEC sys.sp_executesql @drop;

DECLARE @capture nvarchar(max)=N'';
SELECT @capture += N'SELECT * INTO demo_baseline.' + QUOTENAME(t.name) +
                   N' FROM dbo.' + QUOTENAME(t.name) + N';'
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
WHERE s.name=N'dbo' AND t.name NOT LIKE N'SatiDemoReset%';
EXEC sys.sp_executesql @capture;
COMMIT;

EXEC(N'
CREATE OR ALTER PROCEDURE dbo.SatiResetToCanonicalBaseline
    @RequestId uniqueidentifier,
    @ActorUserId int
WITH EXECUTE AS OWNER
AS
BEGIN
    SET NOCOUNT ON; SET XACT_ABORT ON;
    IF DB_NAME() <> N''''SatiDemo'''' OR NOT EXISTS
       (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id=1 AND EnvironmentName=N''''Demo'''')
        THROW 51000, ''''Refusing to reset outside the validated Demo database.'''', 1;
    IF EXISTS (
        SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE s.name=N''''dbo'''' AND t.name NOT LIKE N''''SatiDemoReset%''''
        EXCEPT
        SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE s.name=N''''demo_baseline''''
    ) OR EXISTS (
        SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE s.name=N''''demo_baseline''''
        EXCEPT
        SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        WHERE s.name=N''''dbo'''' AND t.name NOT LIKE N''''SatiDemoReset%''''
    )
        THROW 51002, ''''The Demo schema changed after baseline capture. Capture a reviewed replacement baseline before resetting.'''', 1;
    BEGIN TRANSACTION;
    DECLARE @lockResult int;
    EXEC @lockResult=sys.sp_getapplock @Resource=N''''SatiDemo.FullReset'''',
        @LockMode=N''''Exclusive'''', @LockOwner=N''''Transaction'''', @LockTimeout=60000;
    IF @lockResult < 0 THROW 51001, ''''The Demo is busy; reset did not begin.'''', 1;

    DECLARE @sql nvarchar(max)=N'''';
    SELECT @sql += N''''ALTER TABLE dbo.'''' + QUOTENAME(t.name) + N'''' NOCHECK CONSTRAINT ALL;''''
    FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE s.name=N''''dbo'''' AND t.name NOT LIKE N''''SatiDemoReset%'''';
    EXEC sys.sp_executesql @sql;

    SET @sql=N'''';
    SELECT @sql += N''''DELETE FROM dbo.'''' + QUOTENAME(t.name) + N'''';''''
    FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE s.name=N''''demo_baseline'''';
    EXEC sys.sp_executesql @sql;

    DECLARE tables CURSOR LOCAL FAST_FORWARD FOR
      SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
      WHERE s.name=N''''demo_baseline'''' ORDER BY t.name;
    DECLARE @table sysname, @columns nvarchar(max), @hasIdentity bit;
    OPEN tables; FETCH NEXT FROM tables INTO @table;
    WHILE @@FETCH_STATUS=0
    BEGIN
      SELECT @columns=STRING_AGG(CONVERT(nvarchar(max),QUOTENAME(c.name)), N'''','''') WITHIN GROUP (ORDER BY c.column_id),
             @hasIdentity=CONVERT(bit,MAX(CONVERT(int,c.is_identity)))
      FROM sys.columns c
      WHERE c.object_id=OBJECT_ID(N''''dbo.''''+QUOTENAME(@table))
        AND c.is_computed=0 AND c.system_type_id<>189;
      SET @sql=CASE WHEN @hasIdentity=1 THEN N''''SET IDENTITY_INSERT dbo.''''+QUOTENAME(@table)+N'''' ON;'''' ELSE N'''''''' END+
        N''''INSERT dbo.''''+QUOTENAME(@table)+N'''' (''''+@columns+N'''') SELECT ''''+@columns+
        N'''' FROM demo_baseline.''''+QUOTENAME(@table)+N'''';''''+
        CASE WHEN @hasIdentity=1 THEN N''''SET IDENTITY_INSERT dbo.''''+QUOTENAME(@table)+N'''' OFF;'''' ELSE N'''''''' END;
      EXEC sys.sp_executesql @sql;
      FETCH NEXT FROM tables INTO @table;
    END
    CLOSE tables; DEALLOCATE tables;

    SET @sql=N'''';
    SELECT @sql += N''''ALTER TABLE dbo.'''' + QUOTENAME(t.name) + N'''' WITH CHECK CHECK CONSTRAINT ALL;''''
    FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
    WHERE s.name=N''''dbo'''' AND t.name NOT LIKE N''''SatiDemoReset%'''';
    EXEC sys.sp_executesql @sql;
    UPDATE dbo.SatiDatabaseIdentity SET InstanceId=NEWID(), CreatedAtUtc=SYSUTCDATETIME()
      WHERE Id=1 AND EnvironmentName=N''''Demo'''';
    COMMIT;
END');

GRANT EXECUTE ON dbo.SatiResetToCanonicalBaseline TO [$ResetIdentityName];
DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::demo_baseline TO [$ResetIdentityName];
IF USER_ID(N'$ApiIdentityName') IS NOT NULL
    DENY SELECT, INSERT, UPDATE, DELETE ON SCHEMA::demo_baseline TO [$ApiIdentityName];
"@
    [void]$command.ExecuteNonQuery()
    Write-Output "DEMO_FULL_RESET_BASELINE_CAPTURED database=$Database resetIdentity=$ResetIdentityName"
}
finally {
    $connection.Dispose()
}
