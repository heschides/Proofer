<#
.SYNOPSIS
    Applies the additive billing exchange tables to the identity-validated SatiDemo database.

.DESCRIPTION
    SatiDemo has a long-lived schema whose migration history can differ from the actual tables.
    This runner therefore guards on the real schema instead of using EF's history-only script.
    It is intentionally Demo-only, rerunnable, and supports a rollback-only dry run.

.EXAMPLE
    $token = az account get-access-token --resource https://database.windows.net --query accessToken --output tsv
    ./scripts/Apply-BillingExchangeMigrations.ps1 -AccessToken $token -WhatIfOnly
##>
param(
    [ValidateSet('SatiDemo')]
    [string]$DatabaseName = 'SatiDemo',
    [string]$SqlServer = 'sati-demo-satilogica-central.database.windows.net',
    [string]$AccessToken,
    [switch]$WhatIfOnly
)

$ErrorActionPreference = 'Stop'
$connectionString = if ([string]::IsNullOrWhiteSpace($AccessToken)) {
    "Server=$SqlServer;Database=$DatabaseName;Integrated Security=true;Encrypt=false;Connect Timeout=15;"
}
else {
    "Server=$SqlServer;Database=$DatabaseName;Encrypt=true;TrustServerCertificate=false;Connect Timeout=90;"
}
$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
if (-not [string]::IsNullOrWhiteSpace($AccessToken)) { $connection.AccessToken = $AccessToken }
$connection.Open()

try {
    $command = $connection.CreateCommand()
    $command.CommandTimeout = 180
    [void]$command.Parameters.AddWithValue('@whatIfOnly', [bool]$WhatIfOnly)
    $command.CommandText = @'
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'SatiDemo'
    THROW 51600, 'The billing exchange migration is restricted to SatiDemo.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity WHERE Id = 1 AND EnvironmentName = N'Demo')
    THROW 51601, 'The database identity marker is not Demo.', 1;
IF OBJECT_ID(N'dbo.BillingPeriods', N'U') IS NULL OR OBJECT_ID(N'dbo.Agencies', N'U') IS NULL
    THROW 51602, 'The connected database is missing required Sati tables.', 1;

DECLARE @tablesAdded int = 0;
DECLARE @indexesAdded int = 0;
DECLARE @foreignKeysAdded int = 0;
DECLARE @historyRowsWritten int = 0;
DECLARE @history TABLE (MigrationId nvarchar(150) PRIMARY KEY);
INSERT @history(MigrationId) VALUES
    (N'20260829231646_AddBillingExchangeHistory'),
    (N'20260830001538_AddRemittanceDeposits');

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.BillingSubmissionEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.BillingSubmissionEvents
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        BillingPeriodId int NOT NULL,
        OccurredAtUtc datetime2 NOT NULL,
        Stage int NOT NULL,
        Reference nvarchar(80) NULL,
        ResponseType nvarchar(20) NULL,
        ResponseCode nvarchar(30) NULL,
        Explanation nvarchar(500) NULL,
        IsSynthetic bit NOT NULL,
        CONSTRAINT PK_BillingSubmissionEvents PRIMARY KEY (Id),
        CONSTRAINT FK_BillingSubmissionEvents_BillingPeriods_BillingPeriodId
            FOREIGN KEY (BillingPeriodId) REFERENCES dbo.BillingPeriods(Id) ON DELETE NO ACTION
    );
    SET @tablesAdded += 1;
END
ELSE IF COL_LENGTH(N'dbo.BillingSubmissionEvents', N'IsSynthetic') IS NULL
    THROW 51603, 'BillingSubmissionEvents exists with an incompatible schema.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.BillingSubmissionEvents')
              AND name = N'IX_BillingSubmissionEvents_AgencyId_OccurredAtUtc')
BEGIN
    CREATE INDEX IX_BillingSubmissionEvents_AgencyId_OccurredAtUtc
        ON dbo.BillingSubmissionEvents(AgencyId, OccurredAtUtc);
    SET @indexesAdded += 1;
END;

IF OBJECT_ID(N'dbo.RemittanceClaimOutcomes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemittanceClaimOutcomes
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        BillingPeriodId int NULL,
        ClaimReference nvarchar(80) NOT NULL,
        PayerName nvarchar(100) NOT NULL,
        ReceivedAtUtc datetime2 NOT NULL,
        PaymentDate datetime2 NULL,
        Status int NOT NULL,
        BilledAmount decimal(18,2) NOT NULL,
        AllowedAmount decimal(18,2) NULL,
        PaidAmount decimal(18,2) NOT NULL,
        AdjustmentAmount decimal(18,2) NOT NULL,
        PatientResponsibilityAmount decimal(18,2) NOT NULL,
        ReasonCode nvarchar(30) NULL,
        Explanation nvarchar(500) NULL,
        PaymentReference nvarchar(80) NULL,
        IsSynthetic bit NOT NULL,
        CONSTRAINT PK_RemittanceClaimOutcomes PRIMARY KEY (Id),
        CONSTRAINT FK_RemittanceClaimOutcomes_BillingPeriods_BillingPeriodId
            FOREIGN KEY (BillingPeriodId) REFERENCES dbo.BillingPeriods(Id) ON DELETE NO ACTION
    );
    SET @tablesAdded += 1;
END
ELSE IF COL_LENGTH(N'dbo.RemittanceClaimOutcomes', N'IsSynthetic') IS NULL
    THROW 51604, 'RemittanceClaimOutcomes exists with an incompatible schema.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RemittanceClaimOutcomes')
              AND name = N'IX_RemittanceClaimOutcomes_AgencyId_ReceivedAtUtc')
BEGIN
    CREATE INDEX IX_RemittanceClaimOutcomes_AgencyId_ReceivedAtUtc
        ON dbo.RemittanceClaimOutcomes(AgencyId, ReceivedAtUtc);
    SET @indexesAdded += 1;
END;

IF OBJECT_ID(N'dbo.RemittanceDeposits', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RemittanceDeposits
    (
        Id bigint IDENTITY(1,1) NOT NULL,
        AgencyId int NOT NULL,
        PaymentReference nvarchar(80) NOT NULL,
        PayerName nvarchar(100) NOT NULL,
        ReceivedAtUtc datetime2 NOT NULL,
        PaymentDate datetime2 NULL,
        ClaimPaymentAmount decimal(18,2) NOT NULL,
        ProviderLevelAdjustmentAmount decimal(18,2) NOT NULL,
        ProviderLevelAdjustmentSummary nvarchar(500) NULL,
        RemittancePaymentAmount decimal(18,2) NOT NULL,
        EftDepositAmount decimal(18,2) NULL,
        IsSynthetic bit NOT NULL,
        CONSTRAINT PK_RemittanceDeposits PRIMARY KEY (Id)
    );
    SET @tablesAdded += 1;
END
ELSE IF COL_LENGTH(N'dbo.RemittanceDeposits', N'EftDepositAmount') IS NULL
    THROW 51605, 'RemittanceDeposits exists with an incompatible schema.', 1;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.RemittanceDeposits')
              AND name = N'IX_RemittanceDeposits_AgencyId_ReceivedAtUtc')
BEGIN
    CREATE INDEX IX_RemittanceDeposits_AgencyId_ReceivedAtUtc
        ON dbo.RemittanceDeposits(AgencyId, ReceivedAtUtc);
    SET @indexesAdded += 1;
END;

INSERT dbo.__EFMigrationsHistory(MigrationId, ProductVersion)
SELECT pending.MigrationId, N'10.0.5'
FROM @history AS pending
WHERE NOT EXISTS (SELECT 1 FROM dbo.__EFMigrationsHistory existing WHERE existing.MigrationId = pending.MigrationId);
SET @historyRowsWritten = @@ROWCOUNT;

IF @whatIfOnly = 1 ROLLBACK TRANSACTION;
ELSE COMMIT TRANSACTION;

SELECT DB_NAME() AS DatabaseName,
       (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1) AS EnvironmentName,
       @tablesAdded AS TablesAdded,
       @indexesAdded AS IndexesAdded,
       @foreignKeysAdded AS ForeignKeysAdded,
       @historyRowsWritten AS HistoryRowsWritten,
       CAST(@whatIfOnly AS bit) AS RolledBack;
'@
    $reader = $command.ExecuteReader()
    if (-not $reader.Read()) { throw 'The migration verification row was not returned.' }
    [pscustomobject][ordered]@{
        DatabaseName = $reader.GetString(0)
        EnvironmentName = $reader.GetString(1)
        TablesAdded = $reader.GetInt32(2)
        IndexesAdded = $reader.GetInt32(3)
        ForeignKeysAdded = $reader.GetInt32(4)
        HistoryRowsWritten = $reader.GetInt32(5)
        RolledBack = $reader.GetBoolean(6)
    }
    $reader.Close()
}
finally { $connection.Dispose() }
