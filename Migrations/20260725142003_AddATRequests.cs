using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddATRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvergreenId",
                table: "People",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ATRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    ClientName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientEvergreenId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaseManagerName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaseManagerEmail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaseManagerPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CaseManagerAgency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VendorName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VendorBillingLocation = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VendorProgramContact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VendorBillingContact = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SalesTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ATRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ATRequests_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ATRequestItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ATRequestId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ItemCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ATRequestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ATRequestItems_ATRequests_ATRequestId",
                        column: x => x.ATRequestId,
                        principalTable: "ATRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ATRequestItems_ATRequestId",
                table: "ATRequestItems",
                column: "ATRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ATRequests_PersonId",
                table: "ATRequests",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ATRequestItems");

            migrationBuilder.DropTable(
                name: "ATRequests");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EvergreenId",
                table: "People");
        }
    }
}
