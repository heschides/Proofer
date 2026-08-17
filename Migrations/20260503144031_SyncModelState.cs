using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class SyncModelState : Migration
    {
        // Intentionally a no-op. As generated, this migration repeated every operation in
        // AddSupervisorFieldsToNote (20260503122020) from earlier the same day: dropping
        // Incentives.ExcludedDatesJson and creating the ExemptDates table plus its index.
        // Replaying them fails on any database built from scratch — SQL 4924 on the drop,
        // then "table already exists" on the create. It has no unique content of its own.
        //
        // The body is emptied rather than the file deleted so the migration ID stays in the
        // chain: existing databases already record it as applied and must not see a gap.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
