<#
.SYNOPSIS
    Applies the 2026-08-30 per-user permission schema to a long-lived Sati database.

.DESCRIPTION
    Covers the two migrations EF cannot safely apply to SatiDemo or SatiProduction:

      20260830224423_AddUserPermissions          Users.Permissions, plus the legacy-role backfill
      20260830231500_SeparateAgencyWideSupervision  Director 7 -> 19, Admin 15 -> 31

    SatiDemo and SatiProduction have acquired columns outside the migration chain, so
    __EFMigrationsHistory and the actual schema disagree in both directions. EF's generated
    idempotent script guards only on history and fails with SQL 2705 on a column that already
    exists without its history row. Every statement here guards on the real schema instead.

    The script is rerunnable. It adds only what is missing, verifies that anything already
    present has the expected semantics rather than merely the expected name, and fails closed
    on a database or environment identity mismatch.

.NOTES
    Why the two data steps are gated on history rather than simply repeated.

    The backfill in AddUserPermissions writes Admin = 15 and Director = 7.
    SeparateAgencyWideSupervision then corrects those to 31 and 19. Re-running the backfill
    after the correction has landed would silently undo it, so the backfill runs only when its
    history row is absent AND no user carries a non-zero permission set. Both conditions, because
    on a drifted database the history row can be missing from a database that was already
    backfilled.

    The correction is gated the same way, for the opposite reason. Its UPDATEs are written
    WHERE Permissions = 7 / = 15, which is idempotent on the day it runs but not forever: after
    the upgrade, 7 and 15 are permission sets an administrator may legitimately choose by hand.
    Re-running unguarded would promote a deliberate later edit. So it runs once, keyed on its
    history row, exactly as EF would.

    The script therefore never repeats a data step. What it does instead is report proofs --
    Directors still at 7, Admins still at 15, and any user carrying bits outside the supported
    mask -- so a rerun tells you whether the data actually landed rather than asserting it did.

    Ordering is not optional for the Demo API. ValidatedActorFilter reads Users.Permissions on
    every authenticated request, so this must be applied BEFORE the dependent API is published.
    A readiness check that returns healthy afterwards is the real confirmation, because
    SchemaDriftHealthCheck compares the deployed model against the database.

.EXAMPLE
    # The three-pass sequence RELEASE_PLAYBOOK.md section 6 requires.
    $token = az account get-access-token --resource https://database.windows.net `
        --query accessToken --output tsv
    $common = @{
        DatabaseName = 'SatiDemo'
        SqlServer    = 'sati-demo-satilogica-central.database.windows.net'
        AccessToken  = $token
    }
    ./scripts/Apply-UserPermissionsMigrations.ps1 @common -WhatIfOnly   # 1. dry run, rolls back
    ./scripts/Apply-UserPermissionsMigrations.ps1 @common               # 2. for real
    ./scripts/Apply-UserPermissionsMigrations.ps1 @common               # 3. proves idempotency

    Pass 3 must report every Added/Written/Corrected count as 0 and every proof as 0.

.EXAMPLE
    ./scripts/Apply-UserPermissionsMigrations.ps1 -DatabaseName SatiProduction
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
    THROW 51520, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51521, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51522, 'dbo.Users does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    THROW 51523, 'dbo.__EFMigrationsHistory does not exist; this database has no migration chain.', 1;

DECLARE @addUserPermissions      nvarchar(150) = N'20260830224423_AddUserPermissions';
DECLARE @separateAgencyWideSuper nvarchar(150) = N'20260830231500_SeparateAgencyWideSupervision';

DECLARE @columnsAdded            int = 0;
DECLARE @backfillRows            int = 0;
DECLARE @directorsCorrected      int = 0;
DECLARE @adminsCorrected         int = 0;
DECLARE @historyRowsWritten      int = 0;
DECLARE @backfillSkipped         bit = 0;

DECLARE @backfillApplied bit =
    CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @addUserPermissions)
         THEN 1 ELSE 0 END;
DECLARE @correctionApplied bit =
    CASE WHEN EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @separateAgencyWideSuper)
         THEN 1 ELSE 0 END;

-- The correction cannot be recorded ahead of the migration it corrects. That ordering is not
-- a preference; applying them out of order leaves Admins at 15 with no step left to fix them.
IF @correctionApplied = 1 AND @backfillApplied = 0
    THROW 51524, 'History records the agency-wide supervision correction without AddUserPermissions. Reconcile the history table before applying this.', 1;

BEGIN TRANSACTION;

-- ── 1. The Permissions column ────────────────────────────────────────────────
IF COL_LENGTH(N'dbo.Users', N'Permissions') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD Permissions int NOT NULL CONSTRAINT DF_Users_Permissions DEFAULT(0);
    SET @columnsAdded += 1;
END;

-- Present is not the same as correct. A nullable or non-integer column of the right name would
-- satisfy EF's history check and then fail at the first authenticated request.
IF NOT EXISTS (
    SELECT 1
    FROM sys.columns c
    JOIN sys.types t ON t.user_type_id = c.user_type_id
    WHERE c.object_id = OBJECT_ID(N'dbo.Users')
      AND c.name = N'Permissions'
      AND t.name = N'int'
      AND c.is_nullable = 0)
    THROW 51525, 'dbo.Users.Permissions exists but is not a NOT NULL int column.', 1;

-- ── 2. Legacy-role backfill (AddUserPermissions) ─────────────────────────────
-- Dynamic SQL throughout: the column may have been added in this same batch, and SQL Server
-- binds names before it runs the ALTER.
DECLARE @anyPermissionsSet bit = 0;
EXEC sys.sp_executesql
    N'SELECT @found = CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE Permissions <> 0)
                           THEN 1 ELSE 0 END;',
    N'@found bit OUTPUT',
    @found = @anyPermissionsSet OUTPUT;

IF @backfillApplied = 0 AND @anyPermissionsSet = 0
BEGIN
    EXEC sys.sp_executesql
        N'UPDATE dbo.Users
             SET Permissions = CASE [Role]
                 WHEN N''CaseManager'' THEN 1
                 WHEN N''Supervisor''  THEN 3
                 WHEN N''Director''    THEN 7
                 WHEN N''Admin''       THEN 15
                 ELSE 0
             END;
          SELECT @rows = @@ROWCOUNT;',
        N'@rows int OUTPUT',
        @rows = @backfillRows OUTPUT;
END;
ELSE IF @backfillApplied = 0 AND @anyPermissionsSet = 1
BEGIN
    -- Backfilled by some earlier route without its history row. Record the row, change no data.
    SET @backfillSkipped = 1;
END;

-- ── 3. Agency-wide supervision correction ────────────────────────────────────
-- Director held agency-wide review WITHOUT any administration route under the old role string,
-- so backfilling it to 7 (which includes Administration) was a privilege increase on upgrade.
IF @correctionApplied = 0
BEGIN
    EXEC sys.sp_executesql
        N'UPDATE dbo.Users SET Permissions = 19
           WHERE [Role] = N''Director'' AND Permissions = 7;
          SELECT @rows = @@ROWCOUNT;',
        N'@rows int OUTPUT',
        @rows = @directorsCorrected OUTPUT;

    EXEC sys.sp_executesql
        N'UPDATE dbo.Users SET Permissions = 31
           WHERE [Role] = N''Admin'' AND Permissions = 15;
          SELECT @rows = @@ROWCOUNT;',
        N'@rows int OUTPUT',
        @rows = @adminsCorrected OUTPUT;
END;

-- ── History reconciliation ───────────────────────────────────────────────────
DECLARE @history TABLE (MigrationId nvarchar(150) PRIMARY KEY);
INSERT @history(MigrationId) VALUES (@addUserPermissions), (@separateAgencyWideSuper);

INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
SELECT h.MigrationId, N'10.0.5'
FROM @history h
WHERE NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory e WHERE e.MigrationId = h.MigrationId);
SET @historyRowsWritten = @@ROWCOUNT;

-- ── Proofs ───────────────────────────────────────────────────────────────────
-- Reported rather than repaired. On a correct run all three are 0; a non-zero value on the
-- idempotency pass means the data did not land, which is worth seeing rather than papering over.
DECLARE @directorsAtLegacyValue int = 0;
DECLARE @adminsAtLegacyValue    int = 0;
DECLARE @unsupportedBits        int = 0;

EXEC sys.sp_executesql
    N'SELECT @d = COUNT(*) FROM dbo.Users WHERE [Role] = N''Director'' AND Permissions = 7;',
    N'@d int OUTPUT', @d = @directorsAtLegacyValue OUTPUT;
EXEC sys.sp_executesql
    N'SELECT @a = COUNT(*) FROM dbo.Users WHERE [Role] = N''Admin'' AND Permissions = 15;',
    N'@a int OUTPUT', @a = @adminsAtLegacyValue OUTPUT;
-- 31 is AllAgencyPermissions. Anything outside that mask fails UserPermissionRules.IsSupported,
-- and ValidatedActorFilter rejects the session rather than guessing.
EXEC sys.sp_executesql
    N'SELECT @u = COUNT(*) FROM dbo.Users WHERE (Permissions & ~31) <> 0;',
    N'@u int OUTPUT', @u = @unsupportedBits OUTPUT;

IF @whatIfOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    @columnsAdded            AS ColumnsAdded,
    @backfillRows            AS UsersBackfilled,
    @backfillSkipped         AS BackfillSkippedAsAlreadyPresent,
    @directorsCorrected      AS DirectorsCorrected,
    @adminsCorrected         AS AdminsCorrected,
    @historyRowsWritten      AS HistoryRowsWritten,
    @directorsAtLegacyValue  AS ProofDirectorsStillAt7,
    @adminsAtLegacyValue     AS ProofAdminsStillAt15,
    @unsupportedBits         AS ProofUsersWithUnsupportedBits,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The user permissions migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName                    = $reader.GetString(0)
        EnvironmentName                 = $reader.GetString(1)
        ColumnsAdded                    = $reader.GetInt32(2)
        UsersBackfilled                 = $reader.GetInt32(3)
        BackfillSkippedAsAlreadyPresent = $reader.GetBoolean(4)
        DirectorsCorrected              = $reader.GetInt32(5)
        AdminsCorrected                 = $reader.GetInt32(6)
        HistoryRowsWritten              = $reader.GetInt32(7)
        ProofDirectorsStillAt7          = $reader.GetInt32(8)
        ProofAdminsStillAt15            = $reader.GetInt32(9)
        ProofUsersWithUnsupportedBits   = $reader.GetInt32(10)
        RolledBack                      = $reader.GetBoolean(11)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
