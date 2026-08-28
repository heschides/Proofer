using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <summary>
    /// Provider affiliation: which tier a medical directory entry occupies, and the one
    /// self-reference that says who it belongs to.
    /// <para>
    /// Both columns are nullable with no backfill. Every existing row is legitimately
    /// unaffiliated — a directory with no hierarchy is the state this migration starts
    /// from, not missing data — and guessing a tier from a name would be exactly the
    /// fuzzy matching the durable identifiers exist to avoid.
    /// </para>
    /// <para>
    /// The foreign key is Restrict rather than SetNull. SetNull would let deleting a
    /// network silently promote every practice beneath it to top level, splitting the
    /// hierarchy with nothing in the interface revealing it; the services refuse the
    /// delete and name the affiliated entries instead.
    /// </para>
    /// </summary>
    public partial class AddProviderAffiliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MedicalKind",
                table: "Providers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ParentProviderId",
                table: "Providers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Providers_ParentProviderId",
                table: "Providers",
                column: "ParentProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Providers_Providers_ParentProviderId",
                table: "Providers",
                column: "ParentProviderId",
                principalTable: "Providers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Providers_Providers_ParentProviderId",
                table: "Providers");

            migrationBuilder.DropIndex(
                name: "IX_Providers_ParentProviderId",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "MedicalKind",
                table: "Providers");

            migrationBuilder.DropColumn(
                name: "ParentProviderId",
                table: "Providers");
        }
    }
}
