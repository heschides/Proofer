using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sati.Data;

#nullable disable

namespace Sati.Migrations
{
    /// <summary>
    /// Adds Notes.Minutes and Notes.StartTime, which the model has carried for some time
    /// without a migration to create them. The working database acquired the columns
    /// outside the chain, so the gap was invisible there; a database built from scratch
    /// got a Notes table the model could not query, and NoteService.UpdateAbandonedNotesAsync
    /// failed with "Invalid column name" on the first dashboard load after login.
    ///
    /// Hand-authored rather than scaffolded: Add-Migration diffs the model against
    /// SatiContextModelSnapshot, and the snapshot already lists both properties, so it
    /// would produce an empty migration. The snapshot therefore needs no update here.
    /// </summary>
    [DbContext(typeof(SatiContext))]
    [Migration("20260816120000_AddNoteMinutesAndStartTime")]
    public partial class AddNoteMinutesAndStartTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded rather than a plain AddColumn: databases that predate this migration
            // already have both columns, added outside the chain, but have no history row
            // for it. A bare AddColumn would fail there with SQL 2705 on the next startup.
            // StartTime is a minutes offset from 7AM, e.g. 60 = 8AM.
            migrationBuilder.Sql(@"
IF COL_LENGTH('Notes', 'Minutes') IS NULL
    ALTER TABLE [Notes] ADD [Minutes] int NULL;");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Notes', 'StartTime') IS NULL
    ALTER TABLE [Notes] ADD [StartTime] int NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF COL_LENGTH('Notes', 'Minutes') IS NOT NULL
    ALTER TABLE [Notes] DROP COLUMN [Minutes];");

            migrationBuilder.Sql(@"
IF COL_LENGTH('Notes', 'StartTime') IS NOT NULL
    ALTER TABLE [Notes] DROP COLUMN [StartTime];");
        }
    }
}
