using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddQ4RDaysBeforeAnniversary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                            name: "Q4RDaysBeforeAnniversary",
                            table: "Settings",
                            type: "int",
                            nullable: false,
                            defaultValue: 0);

            // The AddColumn above backfills the existing Settings row to 0.
            // Q4R is due 5 days before the anniversary, so correct that one
            // row in place. This is a one-time data correction, not a column
            // default — the column stays default-0 so future schema reads are
            // honest, and the seed in SettingsService handles fresh installs.
            migrationBuilder.Sql("UPDATE Settings SET Q4RDaysBeforeAnniversary = 5;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Q4RDaysBeforeAnniversary",
                table: "Settings");
        }
    }
}
