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
    WHERE MigrationId = N'20260830001538_AddRemittanceDeposits')
    THROW 51002, 'Apply the billing exchange history migration before seeding.', 1;
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

-- A separate synthetic consumer owns historical claims so the ten queue scenarios above remain
-- exactly three ready and seven blocked. These rows exercise display and reconciliation states;
-- they do not represent network activity and every exchange row carries IsSynthetic = 1.
INSERT dbo.People(
    FirstName, LastName, BirthDate, EffectiveDate, Bio, Waiver, UserId, AgencyId,
    DiagnosisCode, MaineCareId, PlaceOfService, Gender, Address, DayProgramCount,
    HasCommunitySupport1To1, HasCommunitySupportDayProgram,
    HasCommunitySupportSelfDirected, HasEmploymentSpecialist, HasHomeSupport,
    HasSelfDirectedHomeSupport, HasSharedLiving, HasWorkSupports, IsEmployed,
    OpenWithVR, HasGuardian, Revision, BillingStreet, BillingCity, BillingState, BillingZip)
VALUES (
    N'Billing', N'History Scenarios', DATEFROMPARTS(1990, 2, 15), @cycleStart,
    @marker + N':EXCHANGE_HISTORY', 1, @caseManagerUserId, @agencyId,
    N'F89', N'DEMOHISTORY01', 11, 0, N'200 Synthetic Street, Augusta, ME 04330', 1,
    0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
    N'200 Synthetic Street', N'Augusta', N'ME', N'04330');
DECLARE @historyPersonId int = SCOPE_IDENTITY();

INSERT dbo.Forms(Type, DueDate, IsCompliant, PersonId, CompletedDate, OpenedDate)
SELECT formType.Type, DATEFROMPARTS(YEAR(@today), 12, 31), 1, @historyPersonId,
       DATEADD(day, 1, @cycleStart), NULL
FROM @formTypes formType;

DECLARE @claimSnapshot nvarchar(max) = (
    SELECT 1 AS Version, @agencyId AS AgencyId, @historyPersonId AS PersonId,
           N'Billing' AS SubscriberFirstName, N'History Scenarios' AS SubscriberLastName,
           DATEFROMPARTS(1990, 2, 15) AS SubscriberBirthDate, N'U' AS SubscriberGenderCode,
           N'DEMOHISTORY01' AS SubscriberMemberId, N'200 Synthetic Street' AS SubscriberStreet,
           N'Augusta' AS SubscriberCity, N'ME' AS SubscriberState, N'04330' AS SubscriberZip,
           N'Sandbox Mode' AS BillingProviderName, N'1999999984' AS BillingProviderNpi,
           N'999999999' AS BillingProviderTaxId, N'1 Demo Way' AS BillingProviderStreet,
           N'Augusta' AS BillingProviderCity, N'ME' AS BillingProviderState,
           N'04330' AS BillingProviderZip, N'SATIDEMO2' AS SubmitterId,
           N'Demo Billing Desk' AS SubmitterContactName, N'2075550100' AS SubmitterContactPhone,
           N'MEDICAID MAINE' AS PayerName, N'MCDME' AS PayerId
    FOR JSON PATH, WITHOUT_ARRAY_WRAPPER);

DECLARE @history table (
    ScenarioIndex int PRIMARY KEY,
    PeriodId int NOT NULL,
    ClaimId int NOT NULL,
    ClaimReference nvarchar(80) NOT NULL,
    Charge decimal(18,2) NOT NULL);
DECLARE @historyIndex int = 1;
WHILE @historyIndex <= 8
BEGIN
    DECLARE @historyMonth date = DATEADD(month, -@historyIndex, DATEFROMPARTS(YEAR(@today), MONTH(@today), 1));
    DECLARE @historyServiceDate date = DATEADD(day, 9, @historyMonth);
    DECLARE @periodStatus int = CASE
        WHEN @historyIndex IN (5, 7) THEN 3
        WHEN @historyIndex = 6 THEN 2
        ELSE 1 END;
    DECLARE @submittedAt datetime2 = DATEADD(day, 2, CAST(@historyServiceDate AS datetime2));

    INSERT dbo.BillingPeriods(UserId, Month, Year, Status, SubmittedAt)
    VALUES (@caseManagerUserId, MONTH(@historyMonth), YEAR(@historyMonth), @periodStatus, @submittedAt);
    DECLARE @historyPeriodId int = SCOPE_IDENTITY();

    INSERT dbo.Notes(
        Narrative, Status, PersonId, EventDate, NoteType, AgencyId,
        ApprovedAt, ApprovedById, ComplianceOverride, Minutes,
        CaseManagerJustification, Revision)
    VALUES (
        N'Synthetic historical claim used only to demonstrate billing exchange states.',
        6, @historyPersonId, @historyServiceDate, 1, @agencyId,
        DATEADD(day, 1, CAST(@historyServiceDate AS datetime2)), @approverUserId, 0,
        10 + @historyIndex, @marker + N':HISTORY_' + CONVERT(nvarchar(10), @historyIndex), 1);
    DECLARE @historyNoteId int = SCOPE_IDENTITY();
    DECLARE @charge decimal(18,2) = 25.00 * (1.00 + (@historyIndex / 10.0));

    INSERT dbo.ClaimLines(
        NoteId, BillingPeriodId, DateOfService, ProcedureCode, ProcedureModifier,
        Units, ChargeAmount, ClientMaineCareId, RenderingProviderNpi,
        DiagnosisCode, PlaceOfService, ClaimSnapshotJson,
        IsComplianceException, ComplianceExceptionReason)
    VALUES (
        @historyNoteId, @historyPeriodId, @historyServiceDate, N'G9012', N'HI',
        CAST(1.00 + (@historyIndex / 10.0) AS decimal(18,2)), @charge, N'DEMOHISTORY01',
        N'1999999984', N'F89', 11, @claimSnapshot, 0, NULL);
    DECLARE @historyClaimId int = SCOPE_IDENTITY();

    INSERT @history VALUES (
        @historyIndex, @historyPeriodId, @historyClaimId,
        CONVERT(nvarchar(20), @historyPeriodId) + N'-' + CONVERT(nvarchar(20), @historyNoteId),
        @charge);
    SET @historyIndex += 1;
END;

INSERT dbo.BillingSubmissionEvents(
    AgencyId, BillingPeriodId, OccurredAtUtc, Stage, Reference,
    ResponseType, ResponseCode, Explanation, IsSynthetic)
SELECT @agencyId, history.PeriodId,
       DATEADD(hour, scenario.ScenarioIndex, DATEADD(day, 3, CAST(DATEFROMPARTS(
           YEAR(DATEADD(month, -scenario.ScenarioIndex, @today)),
           MONTH(DATEADD(month, -scenario.ScenarioIndex, @today)), 10) AS datetime2))),
       scenario.ScenarioIndex - 1,
       N'SYN-DEMO-' + RIGHT(N'00' + CONVERT(nvarchar(2), scenario.ScenarioIndex), 2),
       CASE scenario.ScenarioIndex
           WHEN 3 THEN N'Transport' WHEN 4 THEN N'999' WHEN 5 THEN N'999'
           WHEN 6 THEN N'277CA' WHEN 7 THEN N'277CA' WHEN 8 THEN N'277CA' END,
       CASE scenario.ScenarioIndex
           WHEN 3 THEN N'TIMEOUT' WHEN 4 THEN N'A' WHEN 5 THEN N'R'
           WHEN 6 THEN N'A1' WHEN 7 THEN N'A3' WHEN 8 THEN N'PARTIAL' END,
       CASE scenario.ScenarioIndex
           WHEN 1 THEN N'Test 837 generated and awaiting deliberate submission.'
           WHEN 2 THEN N'Test transmission recorded; acknowledgment is still pending.'
           WHEN 3 THEN N'Synthetic connection timeout; safe retry should reuse the same request identity.'
           WHEN 4 THEN N'Synthetic 999 accepted the transaction structure.'
           WHEN 5 THEN N'Synthetic 999 rejected the transaction structure for correction.'
           WHEN 6 THEN N'Synthetic 277CA accepted every claim for adjudication.'
           WHEN 7 THEN N'Synthetic 277CA rejected the claim before adjudication.'
           WHEN 8 THEN N'Synthetic 277CA accepted some claims and rejected others.' END,
       1
FROM @history history
JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8)) scenario(ScenarioIndex)
  ON scenario.ScenarioIndex = history.ScenarioIndex;

DECLARE @paidPeriodId int, @paidClaimReference nvarchar(80), @paidCharge decimal(18,2);
DECLARE @deniedPeriodId int, @deniedClaimReference nvarchar(80), @deniedCharge decimal(18,2);
DECLARE @partialPeriodId int, @partialClaimReference nvarchar(80), @partialCharge decimal(18,2);
SELECT @paidPeriodId = PeriodId, @paidClaimReference = ClaimReference, @paidCharge = Charge FROM @history WHERE ScenarioIndex = 6;
SELECT @deniedPeriodId = PeriodId, @deniedClaimReference = ClaimReference, @deniedCharge = Charge FROM @history WHERE ScenarioIndex = 7;
SELECT @partialPeriodId = PeriodId, @partialClaimReference = ClaimReference, @partialCharge = Charge FROM @history WHERE ScenarioIndex = 8;

INSERT dbo.RemittanceClaimOutcomes(
    AgencyId, BillingPeriodId, ClaimReference, PayerName, ReceivedAtUtc, PaymentDate,
    Status, BilledAmount, AllowedAmount, PaidAmount, AdjustmentAmount,
    PatientResponsibilityAmount, ReasonCode, Explanation, PaymentReference, IsSynthetic)
VALUES
    (@agencyId, @paidPeriodId, @paidClaimReference, N'SYNTHETIC PAYER', DATEADD(day,-2,SYSUTCDATETIME()), DATEADD(day,-3,@today),
     0, @paidCharge, @paidCharge * 0.80, @paidCharge * 0.80, @paidCharge * 0.20, 0.00, N'DEMO-PAID', N'Paid in full at the synthetic allowed amount.', N'SYN-EFT-001', 1),
    (@agencyId, @partialPeriodId, @partialClaimReference, N'SYNTHETIC PAYER', DATEADD(day,-35,SYSUTCDATETIME()), DATEADD(day,-35,@today),
     1, @partialCharge, @partialCharge * 0.80, @partialCharge * 0.60, @partialCharge * 0.20, @partialCharge * 0.20, N'PR-1', N'Partial synthetic payment with deductible responsibility.', N'SYN-EFT-002', 1),
    (@agencyId, @deniedPeriodId, @deniedClaimReference, N'SYNTHETIC PAYER', DATEADD(day,-65,SYSUTCDATETIME()), NULL,
     2, @deniedCharge, 0.00, 0.00, @deniedCharge, 0.00, N'CO-16', N'Denied after synthetic adjudication because information was missing or invalid.', NULL, 1),
    (@agencyId, @paidPeriodId, @paidClaimReference, N'SYNTHETIC PAYER', DATEADD(day,-95,SYSUTCDATETIME()), DATEADD(day,-95,@today),
     3, @paidCharge, @paidCharge * -0.80, @paidCharge * -0.80, @paidCharge * -0.20, 0.00, N'CO-45', N'Synthetic reversal of a previously posted contractual payment.', N'SYN-REV-001', 1),
    (@agencyId, NULL, N'SYN-UNKNOWN-CLAIM', N'SYNTHETIC PAYER', DATEADD(day,-125,SYSUTCDATETIME()), DATEADD(day,-125,@today),
     4, 42.00, 33.60, 33.60, 8.40, 0.00, N'OA-23', N'Incoming synthetic claim reference does not match a Sati billing period.', N'SYN-EFT-003', 1),
    (@agencyId, NULL, N'SYN-REVIEW-CLAIM', N'SYNTHETIC PAYER', DATEADD(day,-10,SYSUTCDATETIME()), DATEADD(day,-10,@today),
     5, 80.00, 60.00, 50.00, 15.00, 0.00, N'CO-96', N'Synthetic amounts do not balance and require billing review.', N'SYN-EFT-004', 1);

INSERT dbo.RemittanceDeposits(
    AgencyId, PaymentReference, PayerName, ReceivedAtUtc, PaymentDate,
    ClaimPaymentAmount, ProviderLevelAdjustmentAmount, ProviderLevelAdjustmentSummary,
    RemittancePaymentAmount, EftDepositAmount, IsSynthetic)
VALUES
    (@agencyId, N'SYN-EFT-001', N'SYNTHETIC PAYER', DATEADD(day,-2,SYSUTCDATETIME()), DATEADD(day,-3,@today),
     @paidCharge * 0.80, 0.00, N'No provider-level adjustment', @paidCharge * 0.80, @paidCharge * 0.80, 1),
    (@agencyId, N'SYN-EFT-002', N'SYNTHETIC PAYER', DATEADD(day,-35,SYSUTCDATETIME()), DATEADD(day,-35,@today),
     @partialCharge * 0.60, -5.00, N'WO — synthetic prior-overpayment takeback', (@partialCharge * 0.60) - 5.00, (@partialCharge * 0.60) - 5.50, 1),
    (@agencyId, N'SYN-EFT-003', N'SYNTHETIC PAYER', DATEADD(day,-125,SYSUTCDATETIME()), DATEADD(day,-125,@today),
     33.60, -8.40, N'WO — synthetic provider-level recoupment', 25.20, NULL, 1),
    (@agencyId, N'SYN-EFT-004', N'SYNTHETIC PAYER', DATEADD(day,-10,SYSUTCDATETIME()), DATEADD(day,-10,@today),
     50.00, -10.00, N'WO — synthetic takeback; intentionally unbalanced', 45.00, 45.00, 1);

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
