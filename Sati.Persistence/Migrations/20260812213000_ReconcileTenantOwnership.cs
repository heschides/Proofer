using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations;

[DbContext(typeof(SatiContext))]
[Migration("20260812213000_ReconcileTenantOwnership")]
public partial class ReconcileTenantOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            IF COL_LENGTH(N'dbo.Settings', N'AgencyId') IS NULL
                EXEC(N'ALTER TABLE dbo.Settings ADD AgencyId int NULL;');

            IF COL_LENGTH(N'dbo.Providers', N'AgencyId') IS NULL
                EXEC(N'ALTER TABLE dbo.Providers ADD AgencyId int NULL;');

            UPDATE person
            SET AgencyId = ownerUser.AgencyId
            FROM dbo.People AS person
            INNER JOIN dbo.Users AS ownerUser ON ownerUser.Id = person.UserId
            WHERE person.AgencyId IS NULL OR person.AgencyId <> ownerUser.AgencyId;

            UPDATE note
            SET AgencyId = person.AgencyId
            FROM dbo.Notes AS note
            INNER JOIN dbo.People AS person ON person.Id = note.PersonId
            WHERE note.AgencyId IS NULL OR note.AgencyId <> person.AgencyId;

            IF EXISTS (
                SELECT 1
                FROM dbo.People AS person
                INNER JOIN dbo.Users AS ownerUser ON ownerUser.Id = person.UserId
                WHERE person.AgencyId IS NULL OR person.AgencyId <> ownerUser.AgencyId)
                THROW 51002, 'Person tenant ownership remains incomplete or inconsistent.', 1;

            IF EXISTS (
                SELECT 1
                FROM dbo.Notes AS note
                INNER JOIN dbo.People AS person ON person.Id = note.PersonId
                WHERE note.AgencyId IS NULL OR note.AgencyId <> person.AgencyId)
                THROW 51003, 'Note tenant ownership remains incomplete or inconsistent.', 1;

            EXEC(N'
                DECLARE @DefaultAgencyId int = COALESCE(
                    (SELECT TOP (1) AgencyId
                     FROM dbo.Users
                     GROUP BY AgencyId
                     ORDER BY COUNT_BIG(*) DESC, AgencyId),
                    (SELECT TOP (1) Id FROM dbo.Agencies ORDER BY Id));

                IF @DefaultAgencyId IS NULL
                    THROW 51001, ''Tenant ownership cannot be reconciled because no agency exists.'', 1;

                UPDATE dbo.Settings SET AgencyId = @DefaultAgencyId WHERE AgencyId IS NULL;
                UPDATE dbo.Providers SET AgencyId = @DefaultAgencyId WHERE AgencyId IS NULL;

                IF EXISTS (SELECT 1 FROM dbo.Settings WHERE AgencyId IS NULL) OR
                   EXISTS (SELECT 1 FROM dbo.Providers WHERE AgencyId IS NULL)
                    THROW 51004, ''Settings or provider tenant ownership remains incomplete.'', 1;

                IF EXISTS (SELECT 1 FROM dbo.Settings GROUP BY AgencyId HAVING COUNT_BIG(*) > 1)
                    THROW 51005, ''More than one settings row exists for an agency.'', 1;

                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N''dbo.Settings'') AND name = N''AgencyId'' AND is_nullable = 1)
                    ALTER TABLE dbo.Settings ALTER COLUMN AgencyId int NOT NULL;

                IF EXISTS (
                    SELECT 1
                    FROM sys.columns
                    WHERE object_id = OBJECT_ID(N''dbo.Providers'') AND name = N''AgencyId'' AND is_nullable = 1)
                    ALTER TABLE dbo.Providers ALTER COLUMN AgencyId int NOT NULL;

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N''dbo.Settings'') AND name = N''IX_Settings_AgencyId'' AND is_unique = 0)
                    DROP INDEX IX_Settings_AgencyId ON dbo.Settings;

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N''dbo.Settings'') AND name = N''IX_Settings_AgencyId'')
                    CREATE UNIQUE INDEX IX_Settings_AgencyId ON dbo.Settings(AgencyId);

                IF NOT EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N''dbo.Providers'') AND name = N''IX_Providers_AgencyId_Name'')
                    CREATE INDEX IX_Providers_AgencyId_Name ON dbo.Providers(AgencyId, Name);

                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_Settings_Agencies_AgencyId'')
                    ALTER TABLE dbo.Settings WITH CHECK
                        ADD CONSTRAINT FK_Settings_Agencies_AgencyId
                        FOREIGN KEY (AgencyId) REFERENCES dbo.Agencies(Id);

                IF NOT EXISTS (
                    SELECT 1 FROM sys.foreign_keys WHERE name = N''FK_Providers_Agencies_AgencyId'')
                    ALTER TABLE dbo.Providers WITH CHECK
                        ADD CONSTRAINT FK_Providers_Agencies_AgencyId
                        FOREIGN KEY (AgencyId) REFERENCES dbo.Agencies(Id);
                ');
            """);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Tenant ownership repair is intentionally retained on rollback. Reverting
        // these assignments would recreate ambiguous, cross-agency records.
    }
}
