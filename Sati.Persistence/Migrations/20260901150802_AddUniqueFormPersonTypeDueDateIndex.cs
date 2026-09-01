using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueFormPersonTypeDueDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Fail closed, and say why. Creating the unique index against a database
            // that still holds duplicates fails anyway, but with a bare index-violation
            // error that names neither the cause nor the remedy. This runs first so the
            // operator gets the actual instruction instead.
            //
            // The duplicates are real data with real completion dates on them; the
            // repair merges rather than deletes arbitrarily, which is why it is a
            // reviewed maintenance action and not a step inside this migration.
            migrationBuilder.Sql("""
                IF EXISTS (
                    SELECT 1 FROM dbo.Forms
                    GROUP BY PersonId, Type, DueDate
                    HAVING COUNT(*) > 1
                )
                    THROW 50000, 'dbo.Forms still contains duplicate (PersonId, Type, DueDate) rows. Run the duplicate compliance form repair (Settings -> Maintenance) and re-apply this migration. See HANDOFF_DUPLICATE_COMPLIANCE_FORMS.md.', 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Forms_PersonId",
                table: "Forms");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Forms",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Forms_PersonId_Type_DueDate",
                table: "Forms",
                columns: new[] { "PersonId", "Type", "DueDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Forms_PersonId_Type_DueDate",
                table: "Forms");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "Forms",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40);

            migrationBuilder.CreateIndex(
                name: "IX_Forms_PersonId",
                table: "Forms",
                column: "PersonId");
        }
    }
}
