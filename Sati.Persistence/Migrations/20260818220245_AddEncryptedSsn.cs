using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddEncryptedSsn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "SsnCiphertext",
                table: "People",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsnKeyId",
                table: "People",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SsnLastFour",
                table: "People",
                type: "nvarchar(4)",
                maxLength: 4,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SsnNonce",
                table: "People",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SsnTag",
                table: "People",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "SsnWrappedKey",
                table: "People",
                type: "varbinary(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SsnCiphertext",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SsnKeyId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SsnLastFour",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SsnNonce",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SsnTag",
                table: "People");

            migrationBuilder.DropColumn(
                name: "SsnWrappedKey",
                table: "People");
        }
    }
}
