/* Manual deployment script, never application startup.
   Invoke with SQLCMD variables ExpectedDatabase and ExpectedEnvironment after the
   migration and environment marker have been reviewed. This script deliberately
   does not create an identity, assign a user to this role, or enable the feature.
   Use a dedicated managed-identity database user with only this role; never reuse
   the API account or grant db_owner/db_datareader/db_datawriter/schema rights.

   Testing permits only newly created SatiSignatureValidation_* databases. Real
   signing is disabled in this build; Production grants are intentionally refused.
   This table-level boundary limits a compromised portal to signature records,
   not to one request; application request/session checks remain essential. */
SET XACT_ABORT ON;
DECLARE @ExpectedDatabase sysname = N'$(ExpectedDatabase)';
DECLARE @ExpectedEnvironment nvarchar(20) = N'$(ExpectedEnvironment)';
IF DB_NAME() <> @ExpectedDatabase
    THROW 51020, 'The selected database does not match the reviewed target.', 1;
IF NOT ((@ExpectedDatabase = N'SatiDemo' AND @ExpectedEnvironment = N'Demo') OR
        (@ExpectedDatabase LIKE N'SatiSignatureValidation[_]%' AND @ExpectedEnvironment = N'Testing'))
    THROW 51021, 'This script supports Demo or a disposable synthetic validation database only.', 1;
IF OBJECT_ID(N'dbo.SignatureDatabaseEnvironment', N'V') IS NULL
    THROW 51022, 'The signature environment view is missing; install the validated environment marker first.', 1;
IF NOT EXISTS (SELECT 1 FROM dbo.SignatureDatabaseEnvironment
               WHERE DatabaseName = @ExpectedDatabase AND EnvironmentName = @ExpectedEnvironment)
    THROW 51023, 'The database environment marker does not match the reviewed target.', 1;

BEGIN TRANSACTION;
IF DATABASE_PRINCIPAL_ID(N'sati_signature_portal') IS NULL
    CREATE ROLE sati_signature_portal AUTHORIZATION dbo;

GRANT SELECT ON OBJECT::dbo.SignatureDatabaseEnvironment TO sati_signature_portal;
GRANT SELECT ON OBJECT::dbo.SignatureSourceDocuments TO sati_signature_portal;
GRANT SELECT ON OBJECT::dbo.FrozenSignatureDocuments TO sati_signature_portal;
GRANT SELECT ON OBJECT::dbo.SignatureRequests TO sati_signature_portal;
GRANT SELECT, INSERT ON OBJECT::dbo.SignatureSessions TO sati_signature_portal;
GRANT SELECT, INSERT ON OBJECT::dbo.SignatureConsents TO sati_signature_portal;
GRANT SELECT, INSERT ON OBJECT::dbo.SignatureEvents TO sati_signature_portal;
GRANT SELECT, INSERT ON OBJECT::dbo.SignatureCompletions TO sati_signature_portal;
GRANT SELECT ON OBJECT::dbo.SignaturePackages TO sati_signature_portal;
GRANT UPDATE (State, Revision, FailedPinAttempts, LockedAtUtc, AuthenticationVersion,
              CompletedAtUtc, TerminalReason) ON OBJECT::dbo.SignatureRequests TO sati_signature_portal;
GRANT UPDATE (Revision, DocumentReleasedAtUtc, AccessAcknowledgedAtUtc, ExpiresAtUtc)
    ON OBJECT::dbo.SignatureSessions TO sati_signature_portal;

DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.FrozenSignatureDocuments TO sati_signature_portal;
DENY INSERT, DELETE ON OBJECT::dbo.SignatureRequests TO sati_signature_portal;
DENY DELETE ON OBJECT::dbo.SignatureSessions TO sati_signature_portal;
DENY UPDATE, DELETE ON OBJECT::dbo.SignatureConsents TO sati_signature_portal;
DENY UPDATE, DELETE ON OBJECT::dbo.SignatureEvents TO sati_signature_portal;
DENY UPDATE, DELETE ON OBJECT::dbo.SignatureCompletions TO sati_signature_portal;
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.SignaturePackages TO sati_signature_portal;
DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.SignatureOutbox TO sati_signature_portal;

-- Explicitly deny all broader tables, including future clinical/chat tables that
-- exist when this script runs. The two dbo views retain ownership chaining, so
-- their narrow projections remain readable without exposing the source tables.
DECLARE @Table sysname;
DECLARE @Deny nvarchar(max);
DECLARE NonSignatureTables CURSOR LOCAL FAST_FORWARD FOR
    SELECT t.name FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE s.name = N'dbo' AND t.name NOT IN
        (N'FrozenSignatureDocuments', N'SignatureRequests', N'SignatureSessions',
         N'SignatureConsents', N'SignatureEvents', N'SignatureCompletions',
         N'SignaturePackages', N'SignatureOutbox');
OPEN NonSignatureTables;
FETCH NEXT FROM NonSignatureTables INTO @Table;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Deny = N'DENY SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.' + QUOTENAME(@Table) + N' TO sati_signature_portal;';
    EXEC sys.sp_executesql @Deny;
    FETCH NEXT FROM NonSignatureTables INTO @Table;
END;
CLOSE NonSignatureTables;
DEALLOCATE NonSignatureTables;
COMMIT;
