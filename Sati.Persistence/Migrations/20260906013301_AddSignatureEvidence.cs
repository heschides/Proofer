using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sati.Migrations
{
    /// <inheritdoc />
    public partial class AddSignatureEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                EXEC(N'CREATE VIEW dbo.SignatureSourceDocuments AS
                SELECT Id, AgencyId, PersonId, Kind, CycleStart, Origin, ContentSha256,
                       ByteCount, BlankFieldsJson, SupersededByArtifactId
                FROM dbo.DocumentArtifacts');
                IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NOT NULL
                   AND COL_LENGTH(N'dbo.SatiDatabaseIdentity', N'Id') IS NOT NULL
                   AND COL_LENGTH(N'dbo.SatiDatabaseIdentity', N'EnvironmentName') IS NOT NULL
                    EXEC(N'CREATE VIEW dbo.SignatureDatabaseEnvironment AS
                         SELECT CONVERT(nvarchar(128), DB_NAME()) AS DatabaseName, EnvironmentName
                         FROM dbo.SatiDatabaseIdentity WHERE Id = 1');
                """);

            migrationBuilder.DropIndex(
                name: "IX_DocumentArtifacts_AgencyId",
                table: "DocumentArtifacts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_PersonContacts_PersonId_Id",
                table: "PersonContacts",
                columns: new[] { "PersonId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_DocumentArtifacts_AgencyId_PersonId_Id",
                table: "DocumentArtifacts",
                columns: new[] { "AgencyId", "PersonId", "Id" });

            migrationBuilder.CreateTable(
                name: "FrozenSignatureDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    DocumentArtifactId = table.Column<int>(type: "int", nullable: false),
                    ContentSha256 = table.Column<string>(type: "char(64)", nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    StoredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StoredByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FrozenSignatureDocuments", x => x.Id);
                    table.UniqueConstraint("AK_FrozenSignatureDocuments_AgencyId_PersonId_Id", x => new { x.AgencyId, x.PersonId, x.Id });
                    table.CheckConstraint("CK_FrozenSignatureDocuments_Bytes", "[ByteCount] > 0 AND [ByteCount] <= 15728640");
                    table.ForeignKey(
                        name: "FK_FrozenSignatureDocuments_Agencies_AgencyId",
                        column: x => x.AgencyId,
                        principalTable: "Agencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenSignatureDocuments_DocumentArtifacts_AgencyId_PersonId_DocumentArtifactId",
                        columns: x => new { x.AgencyId, x.PersonId, x.DocumentArtifactId },
                        principalTable: "DocumentArtifacts",
                        principalColumns: new[] { "AgencyId", "PersonId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenSignatureDocuments_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FrozenSignatureDocuments_Users_StoredByUserId",
                        column: x => x.StoredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    PersonId = table.Column<int>(type: "int", nullable: false),
                    FrozenDocumentId = table.Column<int>(type: "int", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SignerCapacity = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SignerContactId = table.Column<int>(type: "int", nullable: true),
                    SignerName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DeliveryEmail = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    AuthorityEvidence = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TokenSha256 = table.Column<string>(type: "char(64)", nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PinSalt = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PinIterations = table.Column<int>(type: "int", nullable: false),
                    PinPepperWrapped = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: false),
                    PinKeyId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    FailedPinAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuthenticationVersion = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    DisclosureVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisclosureText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    IntentText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IssuedByUserId = table.Column<int>(type: "int", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TerminalReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReplacesRequestId = table.Column<int>(type: "int", nullable: true),
                    AuthorizationRevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuthorizationRevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ExternalAccessRevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExternalAccessRevocationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureRequests", x => x.Id);
                    table.UniqueConstraint("AK_SignatureRequests_AgencyId_Id", x => new { x.AgencyId, x.Id });
                    table.UniqueConstraint("AK_SignatureRequests_AgencyId_Id_FrozenDocumentId", x => new { x.AgencyId, x.Id, x.FrozenDocumentId });
                    table.CheckConstraint("CK_SignatureRequests_AuthorizationWithdrawal", "([AuthorizationRevokedAtUtc] IS NULL AND [AuthorizationRevocationReason] IS NULL) OR ([State] = 'Signed' AND [AuthorizationRevokedAtUtc] IS NOT NULL AND [AuthorizationRevocationReason] IS NOT NULL)");
                    table.CheckConstraint("CK_SignatureRequests_ExternalAccess", "([ExternalAccessRevokedAtUtc] IS NULL AND [ExternalAccessRevocationReason] IS NULL) OR ([State] = 'Signed' AND [ExternalAccessRevokedAtUtc] IS NOT NULL AND [ExternalAccessRevocationReason] IS NOT NULL)");
                    table.CheckConstraint("CK_SignatureRequests_Counters", "[Revision] > 0 AND [AuthenticationVersion] > 0 AND [FailedPinAttempts] BETWEEN 0 AND 5 AND [PinIterations] BETWEEN 100000 AND 2000000");
                    table.CheckConstraint("CK_SignatureRequests_Expiry", "[ExpiresAtUtc] > [IssuedAtUtc]");
                    table.CheckConstraint("CK_SignatureRequests_Lock", "([FailedPinAttempts] < 5 AND [LockedAtUtc] IS NULL) OR ([FailedPinAttempts] = 5 AND [LockedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_SignatureRequests_State", "[State] IN ('Issued','Viewed','Signed','Declined','ChangesRequested','Expired','Revoked')");
                    table.CheckConstraint("CK_SignatureRequests_Terminal", "([State] IN ('Issued','Viewed') AND [CompletedAtUtc] IS NULL) OR ([State] IN ('Signed','Declined','ChangesRequested','Expired','Revoked') AND [CompletedAtUtc] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SignatureRequests_FrozenSignatureDocuments_AgencyId_PersonId_FrozenDocumentId",
                        columns: x => new { x.AgencyId, x.PersonId, x.FrozenDocumentId },
                        principalTable: "FrozenSignatureDocuments",
                        principalColumns: new[] { "AgencyId", "PersonId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureRequests_PersonContacts_PersonId_SignerContactId",
                        columns: x => new { x.PersonId, x.SignerContactId },
                        principalTable: "PersonContacts",
                        principalColumns: new[] { "PersonId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureRequests_SignatureRequests_AgencyId_ReplacesRequestId",
                        columns: x => new { x.AgencyId, x.ReplacesRequestId },
                        principalTable: "SignatureRequests",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureRequests_Users_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Generation = table.Column<int>(type: "int", nullable: false),
                    PayloadCiphertext = table.Column<byte[]>(type: "varbinary(max)", maxLength: 16000, nullable: true),
                    PayloadNonce = table.Column<byte[]>(type: "varbinary(12)", maxLength: 12, nullable: true),
                    PayloadTag = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: true),
                    PayloadWrappedKey = table.Column<byte[]>(type: "varbinary(512)", maxLength: 512, nullable: true),
                    PayloadKeyId = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureOutbox", x => x.Id);
                    table.CheckConstraint("CK_SignatureOutbox_Counters", "[Revision] > 0 AND [Generation] > 0 AND [Attempts] >= 0");
                    table.CheckConstraint("CK_SignatureOutbox_Lease", "([LeaseId] IS NULL AND [LeaseUntilUtc] IS NULL) OR ([LeaseId] IS NOT NULL AND [LeaseUntilUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_SignatureOutbox_Payload", "([PayloadCiphertext] IS NULL AND [PayloadNonce] IS NULL AND [PayloadTag] IS NULL AND [PayloadWrappedKey] IS NULL AND [PayloadKeyId] IS NULL) OR ([PayloadCiphertext] IS NOT NULL AND [PayloadNonce] IS NOT NULL AND [PayloadTag] IS NOT NULL AND [PayloadWrappedKey] IS NOT NULL AND [PayloadKeyId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SignatureOutbox_SignatureRequests_AgencyId_RequestId",
                        columns: x => new { x.AgencyId, x.RequestId },
                        principalTable: "SignatureRequests",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    TokenSha256 = table.Column<string>(type: "char(64)", nullable: false),
                    AuthenticationVersion = table.Column<int>(type: "int", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DocumentReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AccessAcknowledgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureSessions", x => x.Id);
                    table.UniqueConstraint("AK_SignatureSessions_AgencyId_RequestId_Id", x => new { x.AgencyId, x.RequestId, x.Id });
                    table.CheckConstraint("CK_SignatureSessions_Access", "[AccessAcknowledgedAtUtc] IS NULL OR [DocumentReleasedAtUtc] IS NOT NULL");
                    table.CheckConstraint("CK_SignatureSessions_Counters", "[Revision] > 0 AND [AuthenticationVersion] > 0");
                    table.CheckConstraint("CK_SignatureSessions_Expiry", "[ExpiresAtUtc] > [IssuedAtUtc]");
                    table.ForeignKey(
                        name: "FK_SignatureSessions_SignatureRequests_AgencyId_RequestId",
                        columns: x => new { x.AgencyId, x.RequestId },
                        principalTable: "SignatureRequests",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureConsents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    DisclosureVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DisclosureText = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureConsents", x => x.Id);
                    table.UniqueConstraint("AK_SignatureConsents_AgencyId_RequestId_Id", x => new { x.AgencyId, x.RequestId, x.Id });
                    table.ForeignKey(
                        name: "FK_SignatureConsents_SignatureSessions_AgencyId_RequestId_SessionId",
                        columns: x => new { x.AgencyId, x.RequestId, x.SessionId },
                        principalTable: "SignatureSessions",
                        principalColumns: new[] { "AgencyId", "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ActorKind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<long>(type: "bigint", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureEvents", x => x.Id);
                    table.CheckConstraint("CK_SignatureEvents_Actor", "([ActorKind] = 'Staff' AND [ActorUserId] IS NOT NULL) OR ([ActorKind] IN ('Signer','System') AND [ActorUserId] IS NULL)");
                    table.CheckConstraint("CK_SignatureEvents_Sequence", "[Sequence] > 0");
                    table.ForeignKey(
                        name: "FK_SignatureEvents_SignatureRequests_AgencyId_RequestId",
                        columns: x => new { x.AgencyId, x.RequestId },
                        principalTable: "SignatureRequests",
                        principalColumns: new[] { "AgencyId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureEvents_SignatureSessions_AgencyId_RequestId_SessionId",
                        columns: x => new { x.AgencyId, x.RequestId, x.SessionId },
                        principalTable: "SignatureSessions",
                        principalColumns: new[] { "AgencyId", "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureCompletions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    FrozenDocumentId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<long>(type: "bigint", nullable: false),
                    ConsentId = table.Column<long>(type: "bigint", nullable: false),
                    TypedSignerName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IntentText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SignedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureCompletions", x => x.Id);
                    table.UniqueConstraint("AK_SignatureCompletions_AgencyId_RequestId_Id", x => new { x.AgencyId, x.RequestId, x.Id });
                    table.ForeignKey(
                        name: "FK_SignatureCompletions_SignatureConsents_AgencyId_RequestId_ConsentId",
                        columns: x => new { x.AgencyId, x.RequestId, x.ConsentId },
                        principalTable: "SignatureConsents",
                        principalColumns: new[] { "AgencyId", "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureCompletions_SignatureRequests_AgencyId_RequestId_FrozenDocumentId",
                        columns: x => new { x.AgencyId, x.RequestId, x.FrozenDocumentId },
                        principalTable: "SignatureRequests",
                        principalColumns: new[] { "AgencyId", "Id", "FrozenDocumentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignatureCompletions_SignatureSessions_AgencyId_RequestId_SessionId",
                        columns: x => new { x.AgencyId, x.RequestId, x.SessionId },
                        principalTable: "SignatureSessions",
                        principalColumns: new[] { "AgencyId", "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignaturePackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgencyId = table.Column<int>(type: "int", nullable: false),
                    RequestId = table.Column<int>(type: "int", nullable: false),
                    CompletionId = table.Column<int>(type: "int", nullable: false),
                    ContentSha256 = table.Column<string>(type: "char(64)", nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    BlobPath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignaturePackages", x => x.Id);
                    table.CheckConstraint("CK_SignaturePackages_Bytes", "[ByteCount] > 0 AND [ByteCount] <= 31457280");
                    table.ForeignKey(
                        name: "FK_SignaturePackages_SignatureCompletions_AgencyId_RequestId_CompletionId",
                        columns: x => new { x.AgencyId, x.RequestId, x.CompletionId },
                        principalTable: "SignatureCompletions",
                        principalColumns: new[] { "AgencyId", "RequestId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenSignatureDocuments_AgencyId_PersonId_DocumentArtifactId",
                table: "FrozenSignatureDocuments",
                columns: new[] { "AgencyId", "PersonId", "DocumentArtifactId" });

            migrationBuilder.CreateIndex(
                name: "IX_FrozenSignatureDocuments_DocumentArtifactId",
                table: "FrozenSignatureDocuments",
                column: "DocumentArtifactId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FrozenSignatureDocuments_PersonId",
                table: "FrozenSignatureDocuments",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_FrozenSignatureDocuments_StoredByUserId",
                table: "FrozenSignatureDocuments",
                column: "StoredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureCompletions_AgencyId_RequestId_ConsentId",
                table: "SignatureCompletions",
                columns: new[] { "AgencyId", "RequestId", "ConsentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureCompletions_AgencyId_RequestId_FrozenDocumentId",
                table: "SignatureCompletions",
                columns: new[] { "AgencyId", "RequestId", "FrozenDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureCompletions_AgencyId_RequestId_SessionId",
                table: "SignatureCompletions",
                columns: new[] { "AgencyId", "RequestId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureCompletions_RequestId",
                table: "SignatureCompletions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureConsents_AgencyId_RequestId_SessionId",
                table: "SignatureConsents",
                columns: new[] { "AgencyId", "RequestId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureConsents_SessionId",
                table: "SignatureConsents",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureEvents_ActorUserId",
                table: "SignatureEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureEvents_AgencyId_RequestId_SessionId",
                table: "SignatureEvents",
                columns: new[] { "AgencyId", "RequestId", "SessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureEvents_RequestId_Sequence",
                table: "SignatureEvents",
                columns: new[] { "RequestId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureOutbox_AgencyId_RequestId",
                table: "SignatureOutbox",
                columns: new[] { "AgencyId", "RequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureOutbox_RequestId_Purpose_Generation",
                table: "SignatureOutbox",
                columns: new[] { "RequestId", "Purpose", "Generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureOutbox_State_NextAttemptAtUtc",
                table: "SignatureOutbox",
                columns: new[] { "State", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SignaturePackages_AgencyId_RequestId_CompletionId",
                table: "SignaturePackages",
                columns: new[] { "AgencyId", "RequestId", "CompletionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignaturePackages_CompletionId",
                table: "SignaturePackages",
                column: "CompletionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignaturePackages_RequestId",
                table: "SignaturePackages",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_AgencyId_IssuedByUserId_ClientRequestId",
                table: "SignatureRequests",
                columns: new[] { "AgencyId", "IssuedByUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_AgencyId_PersonId_FrozenDocumentId",
                table: "SignatureRequests",
                columns: new[] { "AgencyId", "PersonId", "FrozenDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_AgencyId_PersonId_State",
                table: "SignatureRequests",
                columns: new[] { "AgencyId", "PersonId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_AgencyId_ReplacesRequestId",
                table: "SignatureRequests",
                columns: new[] { "AgencyId", "ReplacesRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_IssuedByUserId",
                table: "SignatureRequests",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_PersonId_SignerContactId",
                table: "SignatureRequests",
                columns: new[] { "PersonId", "SignerContactId" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureRequests_TokenSha256",
                table: "SignatureRequests",
                column: "TokenSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureSessions_TokenSha256",
                table: "SignatureSessions",
                column: "TokenSha256",
                unique: true);

            migrationBuilder.DropForeignKey(
                name: "FK_SignatureCompletions_SignatureConsents_AgencyId_RequestId_ConsentId",
                table: "SignatureCompletions");

            migrationBuilder.DropIndex(
                name: "IX_SignatureConsents_AgencyId_RequestId_SessionId",
                table: "SignatureConsents");

            migrationBuilder.DropIndex(
                name: "IX_SignatureCompletions_AgencyId_RequestId_ConsentId",
                table: "SignatureCompletions");

            migrationBuilder.DropIndex(
                name: "IX_SignatureCompletions_AgencyId_RequestId_SessionId",
                table: "SignatureCompletions");

            migrationBuilder.AddColumn<string>(
                name: "Purpose",
                table: "SignatureSessions",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Signing");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPolledAtUtc",
                table: "SignatureOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderOperationId",
                table: "SignatureOutbox",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderStatus",
                table: "SignatureOutbox",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SubmittedAtUtc",
                table: "SignatureOutbox",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SignatureConsents_AgencyId_RequestId_SessionId_Id",
                table: "SignatureConsents",
                columns: new[] { "AgencyId", "RequestId", "SessionId", "Id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SignatureSessions_Purpose",
                table: "SignatureSessions",
                sql: "[Purpose] IN ('Signing','Receipt')");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureOutbox_ProviderOperationId",
                table: "SignatureOutbox",
                column: "ProviderOperationId",
                unique: true,
                filter: "[ProviderOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureCompletions_AgencyId_RequestId_SessionId_ConsentId",
                table: "SignatureCompletions",
                columns: new[] { "AgencyId", "RequestId", "SessionId", "ConsentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_SignatureCompletions_SignatureConsents_AgencyId_RequestId_SessionId_ConsentId",
                table: "SignatureCompletions",
                columns: new[] { "AgencyId", "RequestId", "SessionId", "ConsentId" },
                principalTable: "SignatureConsents",
                principalColumns: new[] { "AgencyId", "RequestId", "SessionId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.FrozenSignatureDocuments)
                    THROW 51001, 'Signature records are retained. Disable the feature or roll forward; rollback cannot delete signature history.', 1;
                DROP VIEW dbo.SignatureSourceDocuments;
                DROP VIEW IF EXISTS dbo.SignatureDatabaseEnvironment;
                """);

            migrationBuilder.DropTable(
                name: "SignatureEvents");

            migrationBuilder.DropTable(
                name: "SignatureOutbox");

            migrationBuilder.DropTable(
                name: "SignaturePackages");

            migrationBuilder.DropTable(
                name: "SignatureCompletions");

            migrationBuilder.DropTable(
                name: "SignatureConsents");

            migrationBuilder.DropTable(
                name: "SignatureSessions");

            migrationBuilder.DropTable(
                name: "SignatureRequests");

            migrationBuilder.DropTable(
                name: "FrozenSignatureDocuments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_PersonContacts_PersonId_Id",
                table: "PersonContacts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_DocumentArtifacts_AgencyId_PersonId_Id",
                table: "DocumentArtifacts");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentArtifacts_AgencyId",
                table: "DocumentArtifacts",
                column: "AgencyId");
        }
    }
}
