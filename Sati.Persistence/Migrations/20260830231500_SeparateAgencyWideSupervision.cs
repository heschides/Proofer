using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <summary>
    /// Corrects the AddUserPermissions backfill, which mapped the legacy Director label to
    /// case management + supervision + ADMINISTRATION (7).
    ///
    /// <para>
    /// Under the old role string every administration gate read <c>Role != "Admin"</c>, which
    /// denied Director, as did provider delete/merge and the desktop settings policy. What
    /// Director actually held was agency-wide note review. Backfilling administration therefore
    /// handed every existing Director the audit trail and CSV export, destructive test-data
    /// deletion, agency settings writes, person history, provider delete and merge, and the
    /// admin incident routes — a privilege increase on upgrade rather than the preservation the
    /// backfill intended.
    /// </para>
    ///
    /// <para>
    /// AgencyWideSupervision (16) now carries the supervisory reach, so Director becomes 19 and
    /// Admin becomes 31. Written as a separate migration rather than an edit to the applied one:
    /// editing a migration body already recorded in __EFMigrationsHistory is silently skipped on
    /// upgraded databases and applied on fresh ones, which is how fresh and upgraded deployments
    /// diverge.
    /// </para>
    /// </summary>
    public partial class SeparateAgencyWideSupervision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scoped to rows still carrying the exact value AddUserPermissions wrote, so a
            // deliberate permission edit made in between is left alone rather than clobbered.
            migrationBuilder.Sql(
                """
                UPDATE [Users] SET [Permissions] = 19
                WHERE [Role] = 'Director' AND [Permissions] = 7;

                UPDATE [Users] SET [Permissions] = 31
                WHERE [Role] = 'Admin' AND [Permissions] = 15;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [Users] SET [Permissions] = 15
                WHERE [Role] = 'Admin' AND [Permissions] = 31;

                UPDATE [Users] SET [Permissions] = 7
                WHERE [Role] = 'Director' AND [Permissions] = 19;
                """);
        }
    }
}
