using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <summary>
    /// A consumer's medical provider list: one row per relationship with a directory entry.
    /// <para>
    /// No practice or network column. Those are resolved from the directory at read time, so
    /// a physician who changes practices is corrected once rather than leaving a stale copy
    /// on every profile that names her.
    /// </para>
    /// <para>
    /// <c>EndDate</c> alone says whether a link is current — there is no active flag, because
    /// two columns meaning the same thing drift. Both unique indexes filter on
    /// <c>EndDate IS NULL</c>: a consumer may have had several primary care providers over
    /// the years, and may return to a provider they previously left, so only the current
    /// links are constrained.
    /// </para>
    /// <para>
    /// The provider foreign key is <c>Restrict</c> — a directory entry someone is currently
    /// seeing may not be deleted out from under them — while the person key cascades,
    /// because a consumer's records go with the consumer.
    /// </para>
    /// </summary>
    public partial class AddConsumerProviderList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PersonProviders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    ProviderId = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    IsPrimaryCare = table.Column<bool>(type: "bit", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    HasActiveRelease = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonProviders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonProviders_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonProviders_Providers_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "Providers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonProviders_OneCurrentLinkPerProvider",
                table: "PersonProviders",
                columns: new[] { "PersonId", "ProviderId" },
                unique: true,
                filter: "[EndDate] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PersonProviders_OneCurrentPrimaryCare",
                table: "PersonProviders",
                column: "PersonId",
                unique: true,
                filter: "[IsPrimaryCare] = 1 AND [EndDate] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PersonProviders_PersonId_EndDate",
                table: "PersonProviders",
                columns: new[] { "PersonId", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonProviders_ProviderId",
                table: "PersonProviders",
                column: "ProviderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonProviders");
        }
    }
}
