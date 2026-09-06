using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamChat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatRooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ArchivedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRooms", x => x.Id);
                    table.UniqueConstraint("AK_ChatRooms_AgencyId_Id", x => new { x.AgencyId, x.Id });
                    table.CheckConstraint("CK_ChatRooms_Revision", "[Revision] > 0");
                    table.ForeignKey(
                        name: "FK_ChatRooms_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRooms_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRooms_Users_ArchivedByUserId",
                        column: x => x.ArchivedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRooms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    AuthorUserId = table.Column<int>(type: "int", nullable: false),
                    AuthorDisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ClientMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.UniqueConstraint("AK_ChatMessages_RoomId_AgencyId_Id", x => new { x.RoomId, x.AgencyId, x.Id });
                    table.CheckConstraint("CK_ChatMessages_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatRooms_AgencyId_RoomId",
                        columns: x => new { x.AgencyId, x.RoomId },
                        principalTable: "ChatRooms",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatMessages_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatReadMarkers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LastSeenSequence = table.Column<long>(type: "bigint", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReadMarkers", x => x.Id);
                    table.CheckConstraint("CK_ChatReadMarkers_SeenSequence", "[LastSeenSequence] >= 0");
                    table.ForeignKey(
                        name: "FK_ChatReadMarkers_ChatRooms_AgencyId_RoomId",
                        columns: x => new { x.AgencyId, x.RoomId },
                        principalTable: "ChatRooms",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatReadMarkers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatRoomMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    VisibleAfterSequence = table.Column<long>(type: "bigint", nullable: false),
                    AddedByUserId = table.Column<int>(type: "int", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RemovedByUserId = table.Column<int>(type: "int", nullable: true),
                    RemovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRoomMembers", x => x.Id);
                    table.CheckConstraint("CK_ChatRoomMembers_Removal", "([RemovedAtUtc] IS NULL AND [RemovedByUserId] IS NULL) OR ([RemovedAtUtc] IS NOT NULL AND [RemovedByUserId] IS NOT NULL AND [RemovedAtUtc] >= [AddedAtUtc])");
                    table.CheckConstraint("CK_ChatRoomMembers_VisibleSequence", "[VisibleAfterSequence] >= 0");
                    table.ForeignKey(
                        name: "FK_ChatRoomMembers_ChatRooms_AgencyId_RoomId",
                        columns: x => new { x.AgencyId, x.RoomId },
                        principalTable: "ChatRooms",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRoomMembers_Users_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRoomMembers_Users_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatRoomMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ChatChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TargetUserId = table.Column<int>(type: "int", nullable: true),
                    RedactionReason = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatChanges", x => x.Id);
                    table.CheckConstraint("CK_ChatChanges_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_ChatChanges_ChatMessages_RoomId_AgencyId_MessageId",
                        columns: x => new { x.RoomId, x.AgencyId, x.MessageId },
                        principalTable: "ChatMessages",
                        principalColumns: new[] { "RoomId", "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatChanges_ChatRooms_AgencyId_RoomId",
                        columns: x => new { x.AgencyId, x.RoomId },
                        principalTable: "ChatRooms",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatChanges_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChatChanges_Users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_ActorUserId",
                table: "ChatChanges",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_AgencyId_RoomId",
                table: "ChatChanges",
                columns: new[] { "AgencyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_MessageId",
                table: "ChatChanges",
                column: "MessageId",
                unique: true,
                filter: "[Kind] = 'redaction' AND [MessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_RoomId_AgencyId_MessageId",
                table: "ChatChanges",
                columns: new[] { "RoomId", "AgencyId", "MessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_RoomId_Sequence",
                table: "ChatChanges",
                columns: new[] { "RoomId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatChanges_TargetUserId",
                table: "ChatChanges",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_AgencyId_RoomId",
                table: "ChatMessages",
                columns: new[] { "AgencyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_AuthorUserId",
                table: "ChatMessages",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_RoomId_AuthorUserId_ClientMessageId",
                table: "ChatMessages",
                columns: new[] { "RoomId", "AuthorUserId", "ClientMessageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_RoomId_Sequence",
                table: "ChatMessages",
                columns: new[] { "RoomId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadMarkers_AgencyId_RoomId",
                table: "ChatReadMarkers",
                columns: new[] { "AgencyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadMarkers_RoomId_UserId",
                table: "ChatReadMarkers",
                columns: new[] { "RoomId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadMarkers_UserId",
                table: "ChatReadMarkers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMembers_AddedByUserId",
                table: "ChatRoomMembers",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMembers_AgencyId_RoomId",
                table: "ChatRoomMembers",
                columns: new[] { "AgencyId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMembers_RemovedByUserId",
                table: "ChatRoomMembers",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMembers_RoomId_UserId",
                table: "ChatRoomMembers",
                columns: new[] { "RoomId", "UserId" },
                unique: true,
                filter: "[RemovedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRoomMembers_UserId_RemovedAtUtc",
                table: "ChatRoomMembers",
                columns: new[] { "UserId", "RemovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_AgencyId_ArchivedAtUtc",
                table: "ChatRooms",
                columns: new[] { "AgencyId", "ArchivedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_ArchivedByUserId",
                table: "ChatRooms",
                column: "ArchivedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_CreatedByUserId",
                table: "ChatRooms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_PersonId",
                table: "ChatRooms",
                column: "PersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A deployment rollback must not become a records-retention purge.
            // Disable the feature or roll forward once a room has been created.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.ChatRooms)
                    THROW 51000, 'Team chat contains retained records. Disable the feature or roll forward; rollback cannot delete chat history.', 1;
                """);

            migrationBuilder.DropTable(
                name: "ChatChanges");

            migrationBuilder.DropTable(
                name: "ChatReadMarkers");

            migrationBuilder.DropTable(
                name: "ChatRoomMembers");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatRooms");
        }
    }
}
