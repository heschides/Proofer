using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Permissions",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Preserve the access existing agency users had, while separating billing
            // from the administrator label for all future permission changes.
            migrationBuilder.Sql(
                """
                UPDATE [Users]
                SET [Permissions] = CASE [Role]
                    WHEN 'CaseManager' THEN 1
                    WHEN 'Supervisor' THEN 3
                    WHEN 'Director' THEN 7
                    WHEN 'Admin' THEN 15
                    ELSE 0
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Permissions",
                table: "Users");
        }
    }
}
