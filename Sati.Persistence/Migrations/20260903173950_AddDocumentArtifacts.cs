using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentArtifacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CycleStart = table.Column<DateTime>(type: "date", nullable: false),
                    Origin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GeneratedByUserId = table.Column<int>(type: "int", nullable: false),
                    ContentSha256 = table.Column<string>(type: "char(64)", nullable: true),
                    ByteCount = table.Column<long>(type: "bigint", nullable: true),
                    SuggestedFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    TemplateOwner = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TemplateKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TemplateVersion = table.Column<int>(type: "int", nullable: true),
                    BlankFieldsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ExternalNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SupersededByArtifactId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentArtifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentArtifacts_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentArtifacts_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentArtifacts_Users_GeneratedByUserId",
                        column: x => x.GeneratedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_AgencyId",
                table: "DocumentArtifacts",
                column: "AgencyId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_GeneratedByUserId",
                table: "DocumentArtifacts",
                column: "GeneratedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_OneLivePerCycle",
                table: "DocumentArtifacts",
                columns: new[] { "PersonId", "Kind", "CycleStart" },
                unique: true,
                filter: "[SupersededByArtifactId] IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentArtifacts");
        }
    }
}
