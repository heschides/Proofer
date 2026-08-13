param(
    [ValidateSet('SatiDemo')]
    [string]$DatabaseName = 'SatiDemo',
    [string]$SqlServer = '(localdb)\MSSQLLocalDB',
    [string]$AccessToken,
    [int]$AgencyId = 2,
    [int]$CaseManagerUserId = 5,
    [int]$ApproverUserId = 1007
)

$ErrorActionPreference = 'Stop'

# This seed is deliberately Demo-only. It creates synthetic, recognizable billing examples and
# refuses to run against Production. It never changes passwords or creates login credentials.
if ($DatabaseName -ne 'SatiDemo') {
    throw 'The billing pipeline seed is restricted to SatiDemo.'
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
    $command.CommandTimeout = 60
    [void]$command.Parameters.Add('@agencyId', [System.Data.SqlDbType]::Int)
    [void]$command.Parameters.Add('@caseManagerUserId', [System.Data.SqlDbType]::Int)
    [void]$command.Parameters.Add('@approverUserId', [System.Data.SqlDbType]::Int)
    $command.Parameters['@agencyId'].Value = $AgencyId
    $command.Parameters['@caseManagerUserId'].Value = $CaseManagerUserId
    $command.Parameters['@approverUserId'].Value = $ApproverUserId
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 51000, 'The billing seed can run only in SatiDemo.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE EnvironmentName = N'Demo')
    THROW 51001, 'The selected database does not have the Demo identity marker.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260813110000_AddBillingPipelineConfiguration')
    THROW 51002, 'Apply the billing pipeline migration before seeding.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.Agencies WHERE Id = @agencyId)
    THROW 51003, 'The selected Demo agency does not exist.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.Users
    WHERE Id = @caseManagerUserId AND AgencyId = @agencyId AND Role = N'CaseManager')
    THROW 51004, 'The selected Demo case manager does not belong to the selected agency.', 1;
IF NOT EXISTS (
    SELECT 1 FROM dbo.Users
    WHERE Id = @approverUserId AND AgencyId = @agencyId AND Role IN (N'Supervisor', N'Director', N'Admin'))
    THROW 51005, 'The selected Demo approver is not authorized in the selected agency.', 1;

DECLARE @marker nvarchar(100) = N'SYNTHETIC BILLING LAB V1';
IF EXISTS (
    SELECT 1
    FROM dbo.ClaimLines claim
    JOIN dbo.Notes note ON note.Id = claim.NoteId
    JOIN dbo.People person ON person.Id = note.PersonId
    WHERE person.Bio LIKE @marker + N'%')
    THROW 51006, 'Seeded billing rows already entered a claim. Restore/reset Demo before reseeding.', 1;

BEGIN TRANSACTION;

DELETE note
FROM dbo.Notes note
JOIN dbo.People person ON person.Id = note.PersonId
WHERE person.Bio LIKE @marker + N'%';

DELETE form
FROM dbo.Forms form
JOIN dbo.People person ON person.Id = form.PersonId
WHERE person.Bio LIKE @marker + N'%';

DELETE FROM dbo.People WHERE Bio LIKE @marker + N'%';

-- Representative synthetic values only. Payer enrollment IDs and rates must be replaced and
-- verified against the agency contract/clearinghouse before any real submission.
UPDATE dbo.Agencies
SET Npi = N'1999999984',
    TaxId = N'999999999',
    Street = N'1 Demo Way',
    City = N'Augusta',
    State = N'ME',
    Zip = N'04330',
    BillingProcedureCode = N'G9012',
    BillingModifier = N'HI',
    BillingUnitRate = 25.00,
    EdiSubmitterId = N'SATIDEMO2',
    EdiPayerName = N'MEDICAID MAINE',
    EdiPayerId = N'MCDME',
    EdiContactName = N'Demo Billing Desk',
    EdiContactPhone = N'2075550100'
WHERE Id = @agencyId;

DECLARE @today date = CAST(GETDATE() AS date);
DECLARE @serviceDate date = DATEADD(day, -3, @today);
DECLARE @cycleStart date = DATEFROMPARTS(YEAR(@today), 1, 1);

DECLARE @scenarios table (
    Scenario nvarchar(40) NOT NULL,
    LastName nvarchar(50) NOT NULL,
    Minutes int NOT NULL,
    MemberId nvarchar(30) NULL,
    Diagnosis nvarchar(20) NULL,
    PlaceOfService int NULL,
    BillingStreet nvarchar(55) NULL,
    BlocksCurrentCompliance bit NOT NULL,
    BlocksServiceWindow bit NOT NULL
);

INSERT @scenarios VALUES
    (N'READY_10_MIN',       N'Ready Ten',          10, N'DEMO000001', N'F89', 11, N'10 Ready Street', 0, 0),
    (N'READY_20_MIN',       N'Ready Twenty',       20, N'DEMO000002', N'F89', 11, N'20 Ready Street', 0, 0),
    (N'READY_30_MIN',       N'Ready Thirty',       30, N'DEMO000003', N'F89', 11, N'30 Ready Street', 0, 0),
    (N'MISSING_MEMBER_ID',  N'Blocked Member ID',  20, NULL,          N'F89', 11, N'40 Blocked Street', 0, 0),
    (N'INVALID_DIAGNOSIS',  N'Blocked Diagnosis',  20, N'DEMO000005', N'BAD!',11, N'50 Blocked Street', 0, 0),
    (N'MISSING_PLACE',      N'Blocked Place',      20, N'DEMO000006', N'F89', NULL,N'60 Blocked Street', 0, 0),
    (N'MISSING_ADDRESS',    N'Blocked Address',    20, N'DEMO000007', N'F89', 11, NULL,                 0, 0),
    (N'CURRENT_COMPLIANCE', N'Blocked Compliance', 20, N'DEMO000008', N'F89', 11, N'80 Blocked Street', 1, 0),
    (N'SERVICE_WINDOW',     N'Blocked Window',     20, N'DEMO000009', N'F89', 11, N'90 Blocked Street', 0, 1),
    (N'ZERO_DURATION',      N'Blocked Duration',    0, N'DEMO000010', N'F89', 11, N'100 Blocked Street',0, 0);

DECLARE @seedPeople table (Scenario nvarchar(40) PRIMARY KEY, PersonId int NOT NULL);

MERGE dbo.People AS target
USING @scenarios AS source
ON 1 = 0
WHEN NOT MATCHED THEN INSERT (
    FirstName, LastName, BirthDate, EffectiveDate, Bio, Waiver, UserId, AgencyId,
    DiagnosisCode, MaineCareId, PlaceOfService, Gender, Address, DayProgramCount,
    HasCommunitySupport1To1, HasCommunitySupportDayProgram,
    HasCommunitySupportSelfDirected, HasEmploymentSpecialist, HasHomeSupport,
    HasSelfDirectedHomeSupport, HasSharedLiving, HasWorkSupports, IsEmployed,
    OpenWithVR, HasGuardian, Revision, BillingStreet, BillingCity, BillingState, BillingZip)
VALUES (
    N'Billing', source.LastName, DATEFROMPARTS(1990, 1, 15), @cycleStart,
    @marker + N':' + source.Scenario, 1, @caseManagerUserId, @agencyId,
    source.Diagnosis, source.MemberId, source.PlaceOfService, 0,
    COALESCE(source.BillingStreet, N'Address intentionally missing') + N', Augusta, ME 04330',
    1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
    source.BillingStreet, CASE WHEN source.BillingStreet IS NULL THEN NULL ELSE N'Augusta' END,
    CASE WHEN source.BillingStreet IS NULL THEN NULL ELSE N'ME' END,
    CASE WHEN source.BillingStreet IS NULL THEN NULL ELSE N'04330' END)
OUTPUT source.Scenario, inserted.Id INTO @seedPeople(Scenario, PersonId);

DECLARE @formTypes table (Type nvarchar(50) PRIMARY KEY);
INSERT @formTypes VALUES
    (N'Q1R'), (N'Q2R'), (N'Q3R'), (N'Q4R'), (N'PCP'),
    (N'ComprehensiveAssessment'), (N'Reclassification'), (N'SafetyPlan');

INSERT dbo.Forms(Type, DueDate, IsCompliant, PersonId, CompletedDate, OpenedDate)
SELECT formType.Type,
       CASE
           WHEN scenario.BlocksCurrentCompliance = 1 AND formType.Type = N'SafetyPlan'
               THEN DATEADD(day, -10, @today)
           WHEN scenario.BlocksServiceWindow = 1 AND formType.Type = N'Q2R'
               THEN DATEADD(day, -10, @today)
           ELSE DATEFROMPARTS(YEAR(@today), 12, 31)
       END,
       CASE WHEN scenario.BlocksCurrentCompliance = 1 AND formType.Type = N'SafetyPlan' THEN 0 ELSE 1 END,
       seeded.PersonId,
       CASE
           WHEN scenario.BlocksCurrentCompliance = 1 AND formType.Type = N'SafetyPlan' THEN NULL
           WHEN scenario.BlocksServiceWindow = 1 AND formType.Type = N'Q2R' THEN DATEADD(day, -1, @today)
           ELSE DATEADD(day, 1, @cycleStart)
       END,
       NULL
FROM @scenarios scenario
JOIN @seedPeople seeded ON seeded.Scenario = scenario.Scenario
CROSS JOIN @formTypes formType;

INSERT dbo.Notes(
    Narrative, Status, PersonId, EventDate, NoteType, AgencyId,
    ApprovedAt, ApprovedById, ComplianceOverride, Minutes,
    CaseManagerJustification, Revision)
SELECT N'Synthetic billing pipeline verification note. No real person or service is represented.',
       6, seeded.PersonId, @serviceDate, 1, @agencyId,
       SYSUTCDATETIME(), @approverUserId, 0, scenario.Minutes,
       @marker + N':' + scenario.Scenario, 1
FROM @scenarios scenario
JOIN @seedPeople seeded ON seeded.Scenario = scenario.Scenario;

COMMIT TRANSACTION;

SELECT scenario.Scenario,
       seeded.PersonId,
       note.Id AS NoteId,
       note.EventDate,
       scenario.Minutes
FROM @scenarios scenario
JOIN @seedPeople seeded ON seeded.Scenario = scenario.Scenario
JOIN dbo.Notes note ON note.PersonId = seeded.PersonId
ORDER BY CASE WHEN scenario.Scenario LIKE N'READY%' THEN 0 ELSE 1 END, scenario.Scenario;
'@

    $reader = $command.ExecuteReader()
    $result = New-Object System.Data.DataTable
    $result.Load($reader)
    $result | Format-Table -AutoSize
    Write-Output "BILLING_SEED_COMPLETE database=$DatabaseName agency=$AgencyId rows=$($result.Rows.Count)"
}
finally {
    if ($connection.State -ne [System.Data.ConnectionState]::Closed) {
        $connection.Close()
    }
    $connection.Dispose()
}
