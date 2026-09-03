using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalHolds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CaseReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IssuedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    EffectiveAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlacedByUserId = table.Column<int>(type: "int", nullable: false),
                    PlacedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsReleased = table.Column<bool>(type: "bit", nullable: false),
                    ReleasedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleaseNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalHolds_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalHolds_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalHolds_Users_PlacedByUserId",
                        column: x => x.PlacedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalHolds_Users_ReleasedByUserId",
                        column: x => x.ReleasedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalHolds_AgencyId",
                table: "LegalHolds",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalHolds_PersonId_IsReleased",
                table: "LegalHolds",
                columns: new[] { "PersonId", "IsReleased" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalHolds_PlacedByUserId",
                table: "LegalHolds",
                column: "PlacedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalHolds_ReleasedByUserId",
                table: "LegalHolds",
                column: "ReleasedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalHolds");
        }
    }
}
