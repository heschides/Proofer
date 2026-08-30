<#
.SYNOPSIS
    Adds the constant default of 1 to dbo.Users.AgencyId on the identity-validated SatiDemo.

.DESCRIPTION
    20260416011235_AddAgencyId declares AddColumn<int>(... defaultValue: 1) for Users.AgencyId, so a
    database that ran that migration carries a DEFAULT constraint. SatiDemo does not: the column is
    present and a required int, but the constraint is absent. That is the single divergence the
    reconciliation's proofs found on 2026-08-30, and it is why the reconciliation refuses — writing a
    history row claiming AddAgencyId had been applied would make EF believe that migration ran and
    leave the divergence permanently unreconciled.

    This script exists separately from Apply-DemoHistoryReconciliation.ps1 on purpose. That script's
    contract is that it changes migration history only and never DDL; folding a schema change into it
    would falsify its own documentation and weaken a guard that is doing its job.

    Adding a default constraint does not read, modify, or rewrite existing rows. It affects only
    future inserts that omit the column, and EF always supplies AgencyId for a mapped required
    property, so nothing observable changes at run time. The point is to make the schema say what the
    chain says it says.

    The constraint is given the deterministic name DF_Users_AgencyId rather than the random
    DF__Users__AgencyId__xxxxxxxx that EF would have generated. The proof in the reconciliation
    ignores constraint names and checks the definition, and a deterministic name is what makes this
    script rerunnable.

.NOTES
    Requires ALTER on dbo.Users. The least-privilege grant is
    GRANT ALTER ON OBJECT::dbo.Users TO [sati-demo-api-satilogica-46417];
    which is narrower than db_ddladmin. That grant is a security setting a person makes; no script or
    agent performs it.

.EXAMPLE
    ./scripts/Add-DemoUsersAgencyIdDefault.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -UseManagedIdentity -WhatIfOnly
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('SatiDemo')]
    [ValidateScript({
        if ($_ -cne 'SatiDemo') {
            throw 'Add-DemoUsersAgencyIdDefault.ps1 is restricted to the exact database name SatiDemo.'
        }
        $true
    })]
    [string]$DatabaseName,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$SqlServer,

    [string]$AccessToken,

    # Acquires the SQL token from the App Service managed identity. Only valid inside the WebJob
    # host, which reaches SatiDemo from addresses already on the allow-list.
    [switch]$UseManagedIdentity,

    # Performs every check and the ALTER inside a transaction, then rolls it back.
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$expectedEnvironment = 'Demo'

function Get-ManagedIdentityAccessToken {
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
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Fail closed before any DDL. Binary collation prevents a case-insensitive database collation from
-- weakening the exact SatiDemo/Demo checks.
IF DB_NAME() COLLATE Latin1_General_100_BIN2
       <> @expectedDatabase COLLATE Latin1_General_100_BIN2
    THROW 51800, 'The connected database is not exactly SatiDemo.', 1;

IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
    THROW 51801, 'dbo.SatiDatabaseIdentity is missing.', 1;

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
    THROW 51802, 'The database identity marker is not exactly Demo.', 1;

-- Prove the target before altering it. A default belongs on a required int column that already
-- exists; adding one to anything else would mean this script has the wrong idea of the schema.
IF NOT EXISTS
(
    SELECT 1
    FROM sys.columns AS columnObject
    INNER JOIN sys.types AS typeObject
        ON typeObject.user_type_id = columnObject.user_type_id
    WHERE columnObject.object_id = OBJECT_ID(N'dbo.Users')
      AND columnObject.name = N'AgencyId'
      AND typeObject.name = N'int'
      AND typeObject.is_user_defined = 0
      AND columnObject.is_nullable = 0
      AND columnObject.is_computed = 0
)
    THROW 51803, 'dbo.Users.AgencyId is not a required, non-computed int column.', 1;

DECLARE @existingDefinition nvarchar(4000);
DECLARE @existingName sysname;
SELECT @existingName = defaultObject.name,
       @existingDefinition = defaultObject.definition
FROM sys.default_constraints AS defaultObject
INNER JOIN sys.columns AS columnObject
    ON columnObject.object_id = defaultObject.parent_object_id
   AND columnObject.column_id = defaultObject.parent_column_id
WHERE defaultObject.parent_object_id = OBJECT_ID(N'dbo.Users')
  AND columnObject.name = N'AgencyId';

DECLARE @normalized nvarchar(4000) =
    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
        @existingDefinition, N'(', N''), N')', N''), N' ', N''),
        NCHAR(9), N''), NCHAR(13) + NCHAR(10), N'');

DECLARE @action nvarchar(64);

IF @existingDefinition IS NULL
    SET @action = N'Added';
ELSE IF @normalized = N'1'
    -- Already correct. Rerunnable: say so and change nothing.
    SET @action = N'AlreadyPresent';
ELSE
BEGIN
    -- A different default is a decision, not a cleanup. Replacing one silently would discard
    -- whatever intent put it there.
    DECLARE @conflict nvarchar(2048) = CONCAT(
        N'dbo.Users.AgencyId already has default constraint ', @existingName,
        N' defined as ', @existingDefinition, N', which is not the constant 1. Nothing was changed.');
    THROW 51804, @conflict, 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    IF @action = N'Added'
        ALTER TABLE dbo.Users
            ADD CONSTRAINT DF_Users_AgencyId DEFAULT (1) FOR AgencyId;

    -- Re-prove against the same rule the reconciliation uses, inside the transaction, so a run that
    -- cannot satisfy the proof rolls back rather than reporting success.
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
        THROW 51805, 'Verification failed: Users.AgencyId still has no constant default of 1.', 1;

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
    @action AS Action,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The default-constraint verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName    = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        Action          = $reader.GetString(2)
        RolledBack      = $reader.GetBoolean(3)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
