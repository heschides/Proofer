using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Data;
using Sati.Migrations;
using Sati.Models;
using Xunit;

namespace Sati.Api.Tests;

public sealed class ChatPersistenceTests
{
    [Fact]
    public void BothContextsDeclareTheSameChatSchemaAndRestrictiveRelationships()
    {
        using var local = new SatiContext(new DbContextOptionsBuilder<SatiContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=True;TrustServerCertificate=True").Options);
        using var api = new ApiDbContext(new DbContextOptionsBuilder<ApiDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=True;TrustServerCertificate=True").Options);
        var localModel = local.GetService<IDesignTimeModel>().Model;
        var apiModel = api.GetService<IDesignTimeModel>().Model;
        var expectedTables = new[] { "ChatRooms", "ChatRoomMembers", "ChatMessages", "ChatChanges", "ChatReadMarkers" };
        foreach (var table in expectedTables)
        {
            var localType = Assert.Single(localModel.GetEntityTypes(), type => type.GetTableName() == table);
            var apiType = Assert.Single(apiModel.GetEntityTypes(), type => type.GetTableName() == table);
            Assert.Equal(Describe(localType), Describe(apiType));
            Assert.All(apiType.GetForeignKeys(), key => Assert.Equal(DeleteBehavior.Restrict, key.DeleteBehavior));
        }
        Assert.True(apiModel.FindEntityType(typeof(ServerChatRoom))!
            .FindProperty(nameof(ServerChatRoom.Revision))!.IsConcurrencyToken);
    }

    [Fact]
    public void MigrationAddsOnlyChatTablesAndNeverChangesExistingData()
    {
        var operations = new AddTeamChat().UpOperations;
        var tables = operations.OfType<CreateTableOperation>().ToList();
        Assert.Equal(5, tables.Count);
        Assert.All(tables, table =>
        {
            Assert.StartsWith("Chat", table.Name);
            Assert.All(table.ForeignKeys, key => Assert.Equal(
                Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, key.OnDelete));
        });
        Assert.All(operations, operation => Assert.True(
            operation is CreateTableOperation or CreateIndexOperation));
        Assert.Contains(operations.OfType<CreateIndexOperation>(), index =>
            index.Table == "ChatChanges" && index.IsUnique && index.Filter!.Contains("redaction"));
    }

    [Fact]
    public void RollbackRefusesRetainedRoomsBeforeDroppingAnyChatTable()
    {
        var operations = new AddTeamChat().DownOperations;
        var guard = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Contains("IF EXISTS (SELECT 1 FROM dbo.ChatRooms)", guard.Sql);
        Assert.Contains("THROW 51000", guard.Sql);
        Assert.Equal(5, operations.Skip(1).OfType<DropTableOperation>().Count());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MessagesAndChangesCannotBeEditedOrDeleted(bool change, bool delete)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        if (change)
        {
            var record = await db.ChatChanges.SingleAsync();
            if (delete) db.Remove(record);
            else record.Kind = "redaction";
        }
        else
        {
            var record = await db.ChatMessages.SingleAsync();
            if (delete) db.Remove(record);
            else record.Body = "rewritten history";
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task TheMigrationContextProtectsMessagesBeforeAnyDatabaseCommand()
    {
        await using var db = new SatiContext(new DbContextOptionsBuilder<SatiContext>()
            .UseSqlite("Data Source=:memory:").Options);
        var message = new ChatMessage { Id = 20, RoomId = 1, AgencyId = 1, Body = "original", Sequence = 2 };
        db.Attach(message);
        message.Body = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MembershipCanCloseOnceAndRejoiningRequiresAnotherEpisode()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var db = fixture.Open())
        {
            var member = await db.ChatRoomMembers.SingleAsync();
            member.RemovedAtUtc = DateTime.UtcNow;
            member.RemovedByUserId = 1;
            await db.SaveChangesAsync();
            member.RemovedAtUtc = null;
            member.RemovedByUserId = null;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        await using (var db = fixture.Open())
        {
            db.ChatRoomMembers.Add(new ServerChatRoomMember
            {
                RoomId = 1, AgencyId = 1, UserId = 1, AddedByUserId = 1,
                AddedAtUtc = DateTime.UtcNow, VisibleAfterSequence = 2
            });
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.ChatRoomMembers.CountAsync());
            Assert.Single(await db.ChatRoomMembers.Where(member => member.RemovedAtUtc == null).ToListAsync());
        }
    }

    [Fact]
    public async Task AReadMarkerCannotMoveBackward()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var marker = new ServerChatReadMarker
        {
            RoomId = 1, AgencyId = 1, UserId = 1, LastSeenSequence = 2, LastSeenAtUtc = DateTime.UtcNow
        };
        db.ChatReadMarkers.Add(marker);
        await db.SaveChangesAsync();
        marker.LastSeenSequence = 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConcurrentRoomChangesCannotShareACommittedSequence()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var first = fixture.Open();
        await using var second = fixture.Open();
        var a = await first.ChatRooms.SingleAsync(room => room.Id == 1);
        var b = await second.ChatRooms.SingleAsync(room => room.Id == 1);
        a.Revision++;
        a.Name = "first accepted name";
        await first.SaveChangesAsync();
        b.Revision++;
        b.Name = "stale name";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task AChildCannotClaimAnAgencyDifferentFromItsRoom()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        db.ChatRoomMembers.Add(new ServerChatRoomMember
        {
            RoomId = 1, AgencyId = 2, UserId = 2, AddedByUserId = 2, AddedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task AChangeCannotReferenceAnotherRoomsMessage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        db.ChatChanges.Add(new ServerChatChange
        {
            RoomId = 2, AgencyId = 1, Sequence = 2, Kind = "redaction", MessageId = 1,
            ActorUserId = 1, ChangedAtUtc = DateTime.UtcNow, RedactionReason = "wrong room reference"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RetryingAMessageCannotInsertAnotherCopy()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        var original = await db.ChatMessages.SingleAsync();
        db.ChatMessages.Add(new ServerChatMessage
        {
            RoomId = 1, AgencyId = 1, Sequence = 3, AuthorUserId = 1,
            AuthorDisplayName = "Worker", ClientMessageId = original.ClientMessageId,
            PostedAtUtc = DateTime.UtcNow, Body = "same attempt"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ASecondRedactionCannotReplaceTheFirstReason()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        db.ChatChanges.Add(new ServerChatChange
        {
            RoomId = 1, AgencyId = 1, Sequence = 3, Kind = "redaction", MessageId = 1,
            ActorUserId = 1, ChangedAtUtc = DateTime.UtcNow, RedactionReason = "first recorded reason"
        });
        await db.SaveChangesAsync();
        db.ChatChanges.Add(new ServerChatChange
        {
            RoomId = 1, AgencyId = 1, Sequence = 4, Kind = "redaction", MessageId = 1,
            ActorUserId = 1, ChangedAtUtc = DateTime.UtcNow, RedactionReason = "replacement reason"
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task ConsumerDeletionCannotCascadeIntoChatHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var db = fixture.Open();
        await Assert.ThrowsAsync<SqliteException>(() => db.People.Where(person => person.Id == 1).ExecuteDeleteAsync());
        Assert.Equal(1, await db.ChatMessages.CountAsync());
        Assert.Equal(1, await db.People.CountAsync());
    }

    private static string[] Describe(IEntityType entity) =>
        entity.GetProperties().Select(property => $"property:{property.Name}:{property.ClrType}:{property.IsNullable}:" +
            $"{property.GetMaxLength()}:{property.IsConcurrencyToken}:{property.GetColumnType()}")
            .Concat(entity.GetKeys().Select(key => "key:" + string.Join(',', key.Properties.Select(property => property.Name))))
            .Concat(entity.GetIndexes().Select(index => "index:" + string.Join(',', index.Properties.Select(property => property.Name)) +
                $":{index.IsUnique}:{index.GetFilter()}"))
            .Concat(entity.GetForeignKeys().Select(key => "foreign:" + key.PrincipalEntityType.GetTableName() + ":" +
                string.Join(',', key.Properties.Select(property => property.Name)) + ":" +
                string.Join(',', key.PrincipalKey.Properties.Select(property => property.Name)) + ":" + key.DeleteBehavior))
            .Order(StringComparer.Ordinal).ToArray();

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public ApiDbContext Open() => new(new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(_connection).Options);

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture._connection.OpenAsync();
            await using var db = fixture.Open();
            await db.Database.EnsureCreatedAsync();
            db.Agencies.AddRange(new ServerAgency { Id = 1, Name = "First" }, new ServerAgency { Id = 2, Name = "Second" });
            db.Users.AddRange(
                new ServerUser { Id = 1, AgencyId = 1, Username = "worker", DisplayName = "Worker", Role = "Admin", Permissions = UserPermissions.AllAgencyPermissions },
                new ServerUser { Id = 2, AgencyId = 2, Username = "other", DisplayName = "Other", Role = "CaseManager", Permissions = UserPermissions.CaseManagement });
            db.People.Add(new ServerPerson { Id = 1, AgencyId = 1, UserId = 1, FirstName = "Synthetic", LastName = "Consumer" });
            db.ChatRooms.AddRange(
                new ServerChatRoom { Id = 1, AgencyId = 1, PersonId = 1, Name = "Consumer team", Revision = 2, CreatedByUserId = 1, CreatedAtUtc = DateTime.UtcNow },
                new ServerChatRoom { Id = 2, AgencyId = 1, Name = "Another room", CreatedByUserId = 1, CreatedAtUtc = DateTime.UtcNow });
            db.ChatRoomMembers.Add(new ServerChatRoomMember
            {
                Id = 1, RoomId = 1, AgencyId = 1, UserId = 1, AddedByUserId = 1, AddedAtUtc = DateTime.UtcNow
            });
            db.ChatMessages.Add(new ServerChatMessage
            {
                Id = 1, RoomId = 1, AgencyId = 1, Sequence = 2, AuthorUserId = 1, AuthorDisplayName = "Worker",
                ClientMessageId = Guid.NewGuid(), PostedAtUtc = DateTime.UtcNow, Body = "Synthetic message"
            });
            db.ChatChanges.Add(new ServerChatChange
            {
                Id = 1, RoomId = 1, AgencyId = 1, Sequence = 2, MessageId = 1, Kind = "message", ActorUserId = 1, ChangedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return fixture;
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
