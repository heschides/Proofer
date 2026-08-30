using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddTestConsumerMarker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTestData",
                table: "People",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Every record in the isolated Demo environment is synthetic by design.
            // Existing Production/local records remain false because their purpose
            // cannot be inferred safely from a name or date.
            migrationBuilder.Sql("""
                IF DB_NAME() = N'SatiDemo'
                   AND OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NOT NULL
                   AND EXISTS
                   (
                       SELECT 1
                       FROM dbo.SatiDatabaseIdentity
                       WHERE Id = 1 AND EnvironmentName = N'Demo'
                   )
                BEGIN
                    UPDATE dbo.People SET IsTestData = 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTestData",
                table: "People");
        }
    }
}
