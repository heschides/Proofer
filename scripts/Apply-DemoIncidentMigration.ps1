param(
    [ValidateSet('SatiDemo')]
    [string]$DatabaseName = 'SatiDemo',
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$AccessToken
)

$ErrorActionPreference = 'Stop'
if ($DatabaseName -cne 'SatiDemo') {
    throw 'The incident migration runner is restricted to SatiDemo.'
}

$connectionString = if ([string]::IsNullOrWhiteSpace($AccessToken)) {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=30;"
}
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if (-not [string]::IsNullOrWhiteSpace($AccessToken)) {
    $connection.AccessToken = $AccessToken
}
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 120
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 51200, 'The incident migration can run only in SatiDemo.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 51201, 'The selected database does not have the Demo identity marker.', 1;

DECLARE @migrationId nvarchar(150) = N'20260813162000_AddIncidentHealthPipeline';
DECLARE @alreadyApplied bit = CASE WHEN EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = @migrationId) THEN 1 ELSE 0 END;

IF @alreadyApplied = 0 AND OBJECT_ID(N'dbo.IncidentGroups', N'U') IS NOT NULL
    THROW 51202, 'IncidentGroups exists without its migration-history row; manual review is required.', 1;
IF @alreadyApplied = 1 AND OBJECT_ID(N'dbo.IncidentGroups', N'U') IS NULL
    THROW 51203, 'The migration is recorded but IncidentGroups is missing; manual review is required.', 1;

IF @alreadyApplied = 0
BEGIN
    BEGIN TRANSACTION;

    CREATE TABLE dbo.IncidentGroups (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        Source nvarchar(20) NOT NULL,
        Severity nvarchar(20) NOT NULL,
        Operation nvarchar(80) NOT NULL,
        FirstRelease nvarchar(30) NOT NULL,
        LastRelease nvarchar(30) NOT NULL,
        ExceptionFingerprint nvarchar(64) NOT NULL,
        Status nvarchar(20) NOT NULL,
        OccurrenceCount int NOT NULL,
        FirstSeenUtc datetime2 NOT NULL,
        LastSeenUtc datetime2 NOT NULL,
        LastReference nvarchar(40) NOT NULL,
        LastActorRole nvarchar(30) NOT NULL,
        CONSTRAINT PK_IncidentGroups PRIMARY KEY (Id),
        CONSTRAINT FK_IncidentGroups_Agencies_AgencyId
            FOREIGN KEY (AgencyId) REFERENCES dbo.Agencies(Id) ON DELETE NO ACTION
    );

    CREATE INDEX IX_IncidentGroups_AgencyId_LastSeenUtc
        ON dbo.IncidentGroups(AgencyId, LastSeenUtc);
    CREATE UNIQUE INDEX IX_IncidentGroups_AgencyId_Source_Operation_ExceptionFingerprint
        ON dbo.IncidentGroups(AgencyId, Source, Operation, ExceptionFingerprint);

    INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
    VALUES (@migrationId, N'10.0.5');

    COMMIT TRANSACTION;
END;

SELECT
    CAST(CASE WHEN @alreadyApplied = 0 THEN 1 ELSE 0 END AS bit) AS AppliedNow,
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
    (SELECT COUNT_BIG(*) FROM dbo.IncidentGroups) AS IncidentGroupCount,
    (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory) AS MigrationCount;
'@
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) {
        throw 'The migration verification row was not returned.'
    }
    [pscustomobject][ordered]@{
        AppliedNow = $reader.GetBoolean(0)
        DatabaseName = $reader.GetString(1)
        EnvironmentName = $reader.GetString(2)
        IncidentGroupCount = $reader.GetInt64(3)
        MigrationCount = $reader.GetInt64(4)
    }
    $reader.Close()
}
finally {
    $connection.Dispose()
}
