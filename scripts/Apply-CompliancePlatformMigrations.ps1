<#
.SYNOPSIS
    Applies the 2026-09-03 compliance-platform schema (consumer deletion/archive/legal-hold
    foundation, form attestations, document artifacts/templates, safety plans, and the annual
    document workflow) to a long-lived Sati database.

.DESCRIPTION
    Covers seven migrations, applied in this order:

      20260903152847_AddFormAttestations               dbo.FormAttestations (+ pre-attestation backfill)
      20260903173950_AddDocumentArtifacts               dbo.DocumentArtifacts
      20260903175219_AddPersonCreatedAtAndStatus        People.CreatedAtUtc/Status/StatusNote/...
      20260903183136_AddLegalHolds                      dbo.LegalHolds
      20260903185920_AddDocumentTemplatesAndSafetyPlans dbo.DocumentTemplates (+ default privacy-notice seed)
      20260903190302_AddSafetyPlans                     dbo.SafetyPlans
      20260903200511_CompleteAnnualDocumentWorkflow     Settings.AnnualPacketOpenDaysBefore,
                                                         DocumentArtifacts.SourceContentId/Version,
                                                         dbo.DocumentAcknowledgments

    Unlike the older Apply-*Migrations.ps1 scripts in this directory, none of these seven objects
    predates this migration chain, so the SQL 2705 hazard those scripts guard against (an object
    that exists without its history row, because it was created outside EF) cannot occur here. The
    DDL below is EF's own generated idempotent script for exactly these seven migrations, extracted
    verbatim rather than hand-retyped, so its per-object existence guards (each block checks
    dbo.__EFMigrationsHistory for its own migration id before running) are already correct. This
    script adds what that generated script does not: fail-closed identity checks, an explicit
    precondition that the chain's prior migration is already applied (so a stale or unexpected
    Demo/Production state is refused rather than silently reinterpreted), one outer transaction
    instead of seven auto-committing ones, and the same dry-run/real-run/rerun discipline as the
    rest of this directory.

    The script is rerunnable. A second run against an already-migrated database applies nothing
    and reports every target migration id already present.

.EXAMPLE
    ./scripts/Apply-CompliancePlatformMigrations.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -AccessToken $token -WhatIfOnly

.EXAMPLE
    ./scripts/Apply-CompliancePlatformMigrations.ps1 -DatabaseName SatiDemo `
        -SqlServer sati-demo-satilogica-central.database.windows.net -AccessToken $token
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
$priorMigrationId = '20260902142303_AddVocationalRehabilitationAssignments'
$targetMigrationIds = @(
    '20260903152847_AddFormAttestations',
    '20260903173950_AddDocumentArtifacts',
    '20260903175219_AddPersonCreatedAtAndStatus',
    '20260903183136_AddLegalHolds',
    '20260903185920_AddDocumentTemplatesAndSafetyPlans',
    '20260903190302_AddSafetyPlans',
    '20260903200511_CompleteAnnualDocumentWorkflow'
)

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
    $command.Parameters.AddWithValue('@priorMigrationId', $priorMigrationId) | Out-Null
    $command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly) | Out-Null
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ── Fail closed on identity ──────────────────────────────────────────────────
IF DB_NAME() <> @expectedDatabase
    THROW 51600, 'The connected database is not the one requested.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.SatiDatabaseIdentity
    WHERE Id = 1 AND EnvironmentName = @expectedEnvironment)
    THROW 51601, 'The database identity marker does not match the requested environment.', 1;
IF OBJECT_ID(N'dbo.People', N'U') IS NULL
    THROW 51602, 'dbo.People does not exist; this is not the expected Sati schema.', 1;
IF OBJECT_ID(N'dbo.Forms', N'U') IS NULL
    THROW 51603, 'dbo.Forms does not exist; this is not the expected Sati schema.', 1;

-- ── Fail closed on chain position ────────────────────────────────────────────
-- Refuses rather than guesses if the database is not exactly where this script expects it,
-- since every statement below assumes the chain is contiguous up to this point.
IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @priorMigrationId)
    THROW 51604, 'The migration immediately before this script''s range is not recorded as applied. Refusing rather than guessing the database state.', 1;

BEGIN TRANSACTION;

-- The seven blocks below are EF's own generated idempotent script for exactly these seven
-- migrations (dotnet ef migrations script --idempotent), extracted verbatim. Each guards on
-- dbo.__EFMigrationsHistory for its own migration id, so a rerun after a partial or complete
-- prior application changes nothing already present.

'@
    $command.CommandText += (Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'Apply-CompliancePlatformMigrations.body.sql'))
    $command.CommandText += @'

-- ── Commit or roll back ──────────────────────────────────────────────────────
IF @whatIfOnly = 1
    ROLLBACK TRANSACTION;
ELSE
    COMMIT TRANSACTION;

SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    (SELECT COUNT(*) FROM dbo.__EFMigrationsHistory
     WHERE MigrationId IN (
        N'20260903152847_AddFormAttestations',
        N'20260903173950_AddDocumentArtifacts',
        N'20260903175219_AddPersonCreatedAtAndStatus',
        N'20260903183136_AddLegalHolds',
        N'20260903185920_AddDocumentTemplatesAndSafetyPlans',
        N'20260903190302_AddSafetyPlans',
        N'20260903200511_CompleteAnnualDocumentWorkflow')
    ) AS TargetMigrationsRecorded,
    CAST(@whatIfOnly AS bit) AS RolledBack;
'@

    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The compliance-platform migration verification row was not returned.'
    }
    $result = [pscustomobject][ordered]@{
        DatabaseName               = $reader.GetString(0)
        EnvironmentName            = $reader.GetString(1)
        TargetMigrationsRecorded   = $reader.GetInt32(2)
        TargetMigrationsExpected   = $targetMigrationIds.Count
        RolledBack                 = $reader.GetBoolean(3)
    }
    $reader.Close()
    $result | Format-List
    if ($result.TargetMigrationsRecorded -ne $targetMigrationIds.Count -and -not $WhatIfOnly) {
        throw "Expected all $($targetMigrationIds.Count) target migrations recorded after a real run, found $($result.TargetMigrationsRecorded)."
    }
}
finally {
    $connection.Dispose()
}
