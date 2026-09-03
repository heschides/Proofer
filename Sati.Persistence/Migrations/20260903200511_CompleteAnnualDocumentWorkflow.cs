using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class CompleteAnnualDocumentWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnnualPacketOpenDaysBefore",
                table: "Settings",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "SourceContentId",
                table: "DocumentArtifacts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SourceContentVersion",
                table: "DocumentArtifacts",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentAcknowledgments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentArtifactId = table.Column<int>(type: "int", nullable: false),
                    ReceivedOn = table.Column<DateTime>(type: "date", nullable: true),
                    GoodFaithEffortReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "int", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentAcknowledgments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentAcknowledgments_DocumentArtifacts_DocumentArtifactId",
                        column: x => x.DocumentArtifactId,
                        principalTable: "DocumentArtifacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentAcknowledgments_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAcknowledgments_DocumentArtifactId",
                table: "DocumentAcknowledgments",
                column: "DocumentArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentAcknowledgments_RecordedByUserId",
                table: "DocumentAcknowledgments",
                column: "RecordedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentAcknowledgments");

            migrationBuilder.DropColumn(
                name: "AnnualPacketOpenDaysBefore",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SourceContentId",
                table: "DocumentArtifacts");

            migrationBuilder.DropColumn(
                name: "SourceContentVersion",
                table: "DocumentArtifacts");
        }
    }
}
