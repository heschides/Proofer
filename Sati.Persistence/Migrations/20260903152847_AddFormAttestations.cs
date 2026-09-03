using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddFormAttestations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormAttestations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompletedOn = table.Column<DateTime>(type: "date", nullable: true),
                    ActorKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EvidenceNoteId = table.Column<int>(type: "int", nullable: true),
                    PrerequisiteStateJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAttestations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAttestations_Forms_FormId",
                        column: x => x.FormId,
                        principalTable: "Forms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormAttestations_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestations_ActorUserId",
                table: "FormAttestations",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAttestations_FormId_RecordedAtUtc",
                table: "FormAttestations",
                columns: new[] { "FormId", "RecordedAtUtc" });

            // Preserve every existing completion exactly as recorded without
            // inventing a historical actor or attestation time. The migration time
            // says when Sati created this provenance row; CompletedOn carries the
            // original completion date.
            migrationBuilder.Sql(
                """
                INSERT INTO [FormAttestations]
                    ([FormId], [Kind], [CompletedOn], [ActorKind], [ActorUserId],
                     [RecordedAtUtc], [EvidenceNoteId], [PrerequisiteStateJson], [Reason])
                SELECT [Id], N'Attested', CAST([CompletedDate] AS date), N'System', NULL,
                       SYSUTCDATETIME(), NULL, NULL, N'pre-attestation record'
                FROM [Forms]
                WHERE [CompletedDate] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAttestations");
        }
    }
}
