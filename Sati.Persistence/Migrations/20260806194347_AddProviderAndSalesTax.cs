using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAndSalesTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultPassthroughProviderId",
                table: "Settings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesTaxRate",
                table: "Settings",
                type: "decimal(5,4)",
                nullable: false,
                defaultValue: 0.055m);

            migrationBuilder.CreateTable(
                name: "Providers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Street = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: true),
                    Zip = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    PrimaryContact = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    OfferedServices = table.Column<int>(type: "int", nullable: false),
                    ProvidesPassthroughService = table.Column<bool>(type: "bit", nullable: false),
                    BillingLocationEis = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ProgramContact = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    BillingContact = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Providers", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Providers",
                columns: new[] { "Id", "BillingContact", "BillingLocationEis", "City", "Name", "OfferedServices", "Phone", "PrimaryContact", "ProgramContact", "ProvidesPassthroughService", "State", "Street", "Type", "Zip" },
                values: new object[] { 1, null, null, null, "Maine AT Solutions", 0, null, null, null, true, null, null, "Waiver", null });

            migrationBuilder.CreateIndex(
                name: "IX_Settings_DefaultPassthroughProviderId",
                table: "Settings",
                column: "DefaultPassthroughProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Settings_Providers_DefaultPassthroughProviderId",
                table: "Settings",
                column: "DefaultPassthroughProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Settings_Providers_DefaultPassthroughProviderId",
                table: "Settings");

            migrationBuilder.DropTable(
                name: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_Settings_DefaultPassthroughProviderId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "DefaultPassthroughProviderId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SalesTaxRate",
                table: "Settings");
        }
    }
}
