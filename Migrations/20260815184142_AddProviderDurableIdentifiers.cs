using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderDurableIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MaineCareProviderId",
                table: "Providers",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Npi",
                table: "Providers",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_AgencyId_MaineCareProviderId",
                table: "Providers",
                columns: new[] { "AgencyId", "MaineCareProviderId" },
                unique: true,
                filter: "[MaineCareProviderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Providers_AgencyId_Npi",
                table: "Providers",
                columns: new[] { "AgencyId", "Npi" },
                unique: true,
                filter: "[Npi] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Providers_AgencyId_MaineCareProviderId",
                table: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_Providers_AgencyId_Npi",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "MaineCareProviderId",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "Npi",
                table: "Providers");
        }
    }
}
