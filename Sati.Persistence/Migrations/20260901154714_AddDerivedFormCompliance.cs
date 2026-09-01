using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddDerivedFormCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Form.IsCompliant becomes derived from CompletedDate. Before the column
            // goes, every row whose flag said "satisfied" while holding no date has to
            // be given the date that satisfied it, or the assertion is simply lost.
            //
            // 147 rows in SatiProduction were in that state. They came from
            // Person.AddMissingFormsForCycle, which created current-cycle annual
            // documents with a bare compliant flag: the cycle had started, therefore
            // those documents were in force. That belief is correct and its date is
            // knowable — it is the start of the cycle the form belongs to, which is
            // exactly what the sibling path Person.GenerateFormList already stamped.
            // Person.InForceSince is now the single owner of that rule; this backfills
            // the rows written before it existed.
            //
            // Deliberately narrow:
            //
            //   Reviews (Q1R-Q4R) are NOT backfilled, even if flagged. A quarterly
            //   review is an attestation that work happened; no date can be inferred
            //   for work nobody recorded, so the flag is discarded and the review
            //   becomes outstanding. Reviews are never created flagged, so this should
            //   affect nothing — it is stated rather than assumed.
            //
            //   A cycle that has not started yet is not backfilled. Nothing is in
            //   force before its cycle begins.
            //
            //   A person with no EffectiveDate is not backfilled. There is no cycle to
            //   date the document from, and inventing one is how this class of bug
            //   started.
            //
            // The mirror case — a date present with the flag clear — needs no work.
            // The date wins, which is what BillingComplianceGate already did; only the
            // checkbox changes, and it changes to agree with the gate.
            //
            // The audit insert runs first, while IsCompliant still exists, so the
            // evidence describes rows that were actually about to change.
            migrationBuilder.Sql("""
                WITH Cycle AS (
                    SELECT
                        f.Id,
                        f.Type,
                        f.DueDate,
                        f.PersonId,
                        CASE
                            WHEN DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate), p.EffectiveDate) >= f.DueDate
                                THEN DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate) - 1, p.EffectiveDate)
                            ELSE DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate), p.EffectiveDate)
                        END AS CycleStart
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    WHERE f.IsCompliant = 1
                      AND f.CompletedDate IS NULL
                      AND p.EffectiveDate IS NOT NULL
                      AND f.Type NOT IN (N'Q1R', N'Q2R', N'Q3R', N'Q4R')
                )
                INSERT INTO dbo.AuditEvents
                    (EventId, AgencyId, ActorUserId, Action, ResourceType, ResourceId,
                     OccurredAtUtc, CorrelationId, MetadataJson)
                SELECT
                    NEWID(),
                    ISNULL(p.AgencyId, 0),
                    0,
                    N'form.compliance-date-backfilled',
                    N'Form',
                    CAST(c.Id AS nvarchar(100)),
                    SYSUTCDATETIME(),
                    N'migration-AddDerivedFormCompliance',
                    N'{"reason":"compliant-without-completion-date","type":"'
                        + c.Type + N'","dueDate":"'
                        + CONVERT(nvarchar(10), c.DueDate, 23) + N'","completedDate":"'
                        + CONVERT(nvarchar(10), c.CycleStart, 23) + N'"}'
                FROM Cycle AS c
                INNER JOIN dbo.People AS p ON p.Id = c.PersonId
                WHERE CAST(c.CycleStart AS date) <= CAST(SYSDATETIME() AS date);
                """);

            migrationBuilder.Sql("""
                WITH Cycle AS (
                    SELECT
                        f.Id,
                        CASE
                            WHEN DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate), p.EffectiveDate) >= f.DueDate
                                THEN DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate) - 1, p.EffectiveDate)
                            ELSE DATEADD(YEAR, DATEDIFF(YEAR, p.EffectiveDate, f.DueDate), p.EffectiveDate)
                        END AS CycleStart
                    FROM dbo.Forms AS f
                    INNER JOIN dbo.People AS p ON p.Id = f.PersonId
                    WHERE f.IsCompliant = 1
                      AND f.CompletedDate IS NULL
                      AND p.EffectiveDate IS NOT NULL
                      AND f.Type NOT IN (N'Q1R', N'Q2R', N'Q3R', N'Q4R')
                )
                UPDATE f
                   SET CompletedDate = c.CycleStart
                  FROM dbo.Forms AS f
                  INNER JOIN Cycle AS c ON c.Id = f.Id
                 WHERE CAST(c.CycleStart AS date) <= CAST(SYSDATETIME() AS date);
                """);

            migrationBuilder.DropColumn(
                name: "IsCompliant",
                table: "Forms");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompliant",
                table: "Forms",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Restore the column to agree with the dates. Down cannot recover which
            // rows were flagged-without-a-date before Up ran — that state is exactly
            // what Up resolved — so it reconstructs the flag from the one fact that
            // survives. Rolling back does not undo the backfilled dates; the audit
            // events written by Up name every row that got one.
            migrationBuilder.Sql(
                "UPDATE dbo.Forms SET IsCompliant = 1 WHERE CompletedDate IS NOT NULL;");
        }
    }
}
