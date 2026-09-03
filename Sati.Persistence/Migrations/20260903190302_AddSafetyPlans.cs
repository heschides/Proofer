using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSafetyPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SafetyPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AuthorUserId = table.Column<int>(type: "int", nullable: false),
                    CycleStart = table.Column<DateTime>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DocumentJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafetyPlans_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SafetyPlans_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SafetyPlans_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SafetyPlans_ApprovedByUserId",
                table: "SafetyPlans",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyPlans_AuthorUserId",
                table: "SafetyPlans",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyPlans_PersonId_CycleStart_Version",
                table: "SafetyPlans",
                columns: new[] { "PersonId", "CycleStart", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SafetyPlans");
        }
    }
}
