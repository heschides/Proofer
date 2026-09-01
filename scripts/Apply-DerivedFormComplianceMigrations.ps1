<#
.SYNOPSIS
    Applies the 2026-09-01 form compliance schema to a long-lived Sati database.

.DESCRIPTION
    Covers two migrations that EF cannot safely apply to SatiDemo or SatiProduction:

      20260901150802_AddUniqueFormPersonTypeDueDateIndex
          Forms.Type narrowed to nvarchar(40); unique IX_Forms_PersonId_Type_DueDate

      20260901154714_AddDerivedFormCompliance
          Forms.CompletedDate backfilled where the old flag claimed compliance
          without a date; Forms.IsCompliant dropped

    SatiDemo and SatiProduction have acquired columns outside the migration chain, so
    __EFMigrationsHistory and the actual schema disagree in both directions. EF's
    generated idempotent script guards only on history. Every statement here guards on
    the real schema instead, and the script is rerunnable: it does only what is missing,
    verifies that anything already present has the expected semantics rather than merely
    the expected name, and fails closed on a database or environment identity mismatch.

    ASCII only. Windows PowerShell 5.1 reads a .ps1 without a BOM as ANSI, so a stray
    non-ASCII character corrupts a string literal elsewhere in the file.

.NOTES
    ORDER MATTERS, AND NOT FOR STYLE. The index cannot be created while duplicate
    (PersonId, Type, DueDate) rows exist, so this refuses rather than half-applying if
    it finds any. Local SatiProduction holds 492 such groups and is repaired by the
    desktop at launch (FormDuplicateRepair); SatiDemo had none when surveyed on
    2026-09-01, because its forms come from the API rather than the desktop path whose
    concurrent-load race produced them.

    THE BACKFILL IS A DATA CHANGE. Rows whose old IsCompliant flag was set with no
    CompletedDate are given the start date of the cycle the form belongs to - the date
    Person.AddMissingFormsForCycle already implied when it created them, and what the
    sibling path Person.GenerateFormList was already stamping. It is deliberately narrow:

      - Quarterly reviews are never backfilled. A review is an attestation that work
        happened and no date can be inferred for work nobody recorded.
      - A cycle that has not started is never backfilled. Nothing is in force before
        its cycle begins.
      - A person with no EffectiveDate is never backfilled. There is no cycle to date
        the document from, and inventing one is how this class of defect started.

    Every backfilled row gets a form.compliance-date-backfilled audit event, written
    while IsCompliant still exists so the evidence describes rows that really changed.

.EXAMPLE
    ./scripts/Apply-DerivedFormComplianceMigrations.ps1 -DatabaseName SatiProduction -WhatIfOnly

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net `
        --query accessToken --output tsv
    ./scripts/Apply-DerivedFormComplianceMigrations.ps1 `
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
    THROW 51600, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51601, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.Forms', N'U') IS NULL
    THROW 51602, 'dbo.Forms does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51603, 'dbo.People does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.AuditEvents', N'U') IS NULL
    THROW 51604, 'dbo.AuditEvents does not exist; the backfill could not leave evidence.', 1;

DECLARE @typeNarrowed int = 0;
DECLARE @indexesAdded int = 0;
DECLARE @rowsBackfilled int = 0;
DECLARE @auditEventsWritten int = 0;
DECLARE @columnsDropped int = 0;
DECLARE @historyRowsWritten int = 0;

DECLARE @history TABLE (MigrationId nvarchar(150) PRIMARY KEY);
INSERT @history(MigrationId) VALUES
    (N'20260901150802_AddUniqueFormPersonTypeDueDateIndex'),
    (N'20260901154714_AddDerivedFormCompliance');

BEGIN TRANSACTION;

-- 1. Refuse to proceed while duplicates exist --------------------------------
-- Checked before anything is written, so a database needing the desktop repair
-- is left exactly as found rather than half-migrated.
IF EXISTS (
    SELECT 1 FROM dbo.Forms
    GROUP BY PersonId, Type, DueDate
    HAVING COUNT(*) > 1)
    THROW 51605, 'dbo.Forms holds duplicate (PersonId, Type, DueDate) rows. Run the desktop duplicate repair first; see HANDOFF_DUPLICATE_COMPLIANCE_FORMS.md.', 1;

-- 2. Narrow Forms.Type so it can be indexed -----------------------------------
-- nvarchar(max) cannot participate in an index. Verify the data actually fits
-- before altering: the longest FormType name is ComprehensiveAssessment at 23.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Forms') AND name = N'Type' AND max_length = -1)
BEGIN
    IF EXISTS (SELECT 1 FROM dbo.Forms WHERE LEN(Type) > 40)
        THROW 51606, 'dbo.Forms holds a Type value longer than 40 characters; narrowing would truncate it.', 1;

    ALTER TABLE dbo.Forms ALTER COLUMN Type nvarchar(40) NOT NULL;
    SET @typeNarrowed = 1;
END;

-- Semantics, not just the name: a Type column that is already bounded must be
-- wide enough for every value the model can produce.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Forms') AND name = N'Type'
      AND max_length <> -1 AND max_length < 80)
    THROW 51607, 'dbo.Forms.Type is bounded but narrower than nvarchar(40).', 1;

-- 3. Unique index -------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Forms') AND name = N'IX_Forms_PersonId_Type_DueDate')
BEGIN
    EXEC(N'CREATE UNIQUE INDEX IX_Forms_PersonId_Type_DueDate
           ON dbo.Forms(PersonId, Type, DueDate);');
    SET @indexesAdded += 1;
END;

-- An index that exists under the right name but is not unique enforces nothing.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Forms')
      AND name = N'IX_Forms_PersonId_Type_DueDate'
      AND is_unique = 0)
    THROW 51608, 'IX_Forms_PersonId_Type_DueDate exists but is not unique.', 1;

-- 4. Backfill, then drop IsCompliant ------------------------------------------
-- Skipped entirely once the column is gone, which is what makes this rerunnable.
IF COL_LENGTH(N'dbo.Forms', N'IsCompliant') IS NOT NULL
BEGIN
    -- Dynamic SQL throughout this block: the batch is name-bound before the
    -- ALTER ... DROP COLUMN below runs, so a direct reference to IsCompliant in a
    -- later batch would fail to compile even though it is valid at execution time.
    DECLARE @cycle nvarchar(max) = N'
        WITH Cycle AS (
            SELECT f.Id, f.Type, f.DueDate, f.PersonId,
                CASE WHEN DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate), p.EffectiveDate) >= f.DueDate
                     THEN DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate) - 1, p.EffectiveDate)
                     ELSE DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate), p.EffectiveDate)
                END AS CycleStart
            FROM dbo.Forms AS f
            INNER JOIN dbo.People AS p ON p.Id = f.PersonId
            WHERE f.IsCompliant = 1
              AND f.CompletedDate IS NULL
              AND p.EffectiveDate IS NOT NULL
              AND f.Type NOT IN (N''Q1R'', N''Q2R'', N''Q3R'', N''Q4R'')
        )';

    -- Evidence first, while the flag still exists to describe.
    DECLARE @auditSql nvarchar(max) = @cycle + N'
        INSERT dbo.AuditEvents
            (EventId, AgencyId, ActorUserId, Action, ResourceType, ResourceId,
             OccurredAtUtc, CorrelationId, MetadataJson)
        SELECT NEWID(), ISNULL(p.AgencyId, 0), 0,
               N''form.compliance-date-backfilled'', N''Form'',
               CAST(c.Id AS nvarchar(100)), SYSUTCDATETIME(),
               N''migration-AddDerivedFormCompliance'',
               N''{"reason":"compliant-without-completion-date","type":"'' + c.Type
                 + N''","dueDate":"'' + CONVERT(nvarchar(10), c.DueDate, 23)
                 + N''","completedDate":"'' + CONVERT(nvarchar(10), c.CycleStart, 23) + N''"}''
        FROM Cycle AS c
        INNER JOIN dbo.People AS p ON p.Id = c.PersonId
        WHERE CAST(c.CycleStart AS date) <= CAST(SYSDATETIME() AS date);';
    EXEC sp_executesql @auditSql;
    SET @auditEventsWritten = @@ROWCOUNT;

    DECLARE @backfillSql nvarchar(max) = @cycle + N'
        UPDATE f SET CompletedDate = c.CycleStart
        FROM dbo.Forms AS f
        INNER JOIN Cycle AS c ON c.Id = f.Id
        WHERE CAST(c.CycleStart AS date) <= CAST(SYSDATETIME() AS date);';
    EXEC sp_executesql @backfillSql;
    SET @rowsBackfilled = @@ROWCOUNT;

    IF @auditEventsWritten <> @rowsBackfilled
        THROW 51609, 'The backfill and its audit trail disagree on how many rows changed.', 1;

    -- Any default constraint has to go before the column can be dropped.
    DECLARE @defaultName sysname = (
        SELECT dc.name FROM sys.default_constraints dc
        JOIN sys.columns col ON col.object_id = dc.parent_object_id
                            AND col.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Forms') AND col.name = N'IsCompliant');
    IF @defaultName IS NOT NULL
        EXEC(N'ALTER TABLE dbo.Forms DROP CONSTRAINT ' + @defaultName + N';');

    EXEC(N'ALTER TABLE dbo.Forms DROP COLUMN IsCompliant;');
    SET @columnsDropped = 1;
END;

-- 5. History ------------------------------------------------------------------
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
    @typeNarrowed AS TypeNarrowed,
    @indexesAdded AS IndexesAdded,
    @rowsBackfilled AS RowsBackfilled,
    @auditEventsWritten AS AuditEventsWritten,
    @columnsDropped AS IsCompliantDropped,
    @historyRowsWritten AS HistoryRowsWritten,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The form compliance migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        DatabaseName       = $reader.GetString(0)
        EnvironmentName    = $reader.GetString(1)
        TypeNarrowed       = $reader.GetInt32(2)
        IndexesAdded       = $reader.GetInt32(3)
        RowsBackfilled     = $reader.GetInt32(4)
        AuditEventsWritten = $reader.GetInt32(5)
        IsCompliantDropped = $reader.GetInt32(6)
        HistoryRowsWritten = $reader.GetInt32(7)
        RolledBack         = $reader.GetBoolean(8)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
