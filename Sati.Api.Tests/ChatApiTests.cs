using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Endpoints;
using Sati.Api.Infrastructure;
using Sati.Contracts.V1;
using Xunit;

namespace Sati.Api.Tests;

[Collection(SatiApiCollection.Name)]
public sealed class ChatApiTests(SatiApiFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        using var client = await factory.CreateAuthenticatedClientAsync("admin-one");
        factory.Services.GetRequiredService<IOptions<ChatOptions>>().Value.Enabled = true;
    }
    public Task DisposeAsync()
    {
        factory.Services.GetRequiredService<IOptions<ChatOptions>>().Value.Enabled = false;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task DisabledChatCannotReadOrWriteAndReportsAvailability()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        factory.Services.GetRequiredService<IOptions<ChatOptions>>().Value.Enabled = false;
        Assert.False((await admin.GetFromJsonAsync<ChatAvailabilityDto>("/api/v1/chat/availability"))!.Enabled);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/v1/chat/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/v1/chat/rooms",
            new CreateChatRoomRequest("Disabled", null, null, [11]))).StatusCode);
    }

    [Fact]
    public void ProductionCannotBeEnabledByTheFeatureFlag()
    {
        var options = Options.Create(new ChatOptions { Enabled = true });
        Assert.False(new ChatFeature(options, Options.Create(new SatiApiOptions {
            ExpectedEnvironment = "Production", ExpectedDatabaseName = "SatiProduction" })).Enabled);
        Assert.False(new ChatFeature(options, Options.Create(new SatiApiOptions {
            ExpectedEnvironment = "Demo", ExpectedDatabaseName = "SatiProduction" })).Enabled);
        Assert.False(new ChatFeature(Options.Create(new ChatOptions()), Options.Create(new SatiApiOptions())).Enabled);
    }

    [Theory]
    [InlineData("supervisor-one")]
    [InlineData("case-manager-two")]
    public async Task NonmembersAndForeignTenantsCannotReadPostOrEnumerateARoom(string username)
    {
        var room = await CreateRoom();
            using var client = await factory.CreateAuthenticatedClientAsync(username);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/messages",
                new PostChatMessageRequest(room.Revision, Guid.NewGuid(), "Synthetic unauthorized message"))).StatusCode);
            var visible = await client.GetFromJsonAsync<List<ChatRoomDto>>("/api/v1/chat/rooms");
            Assert.DoesNotContain(visible!, x => x.Id == room.Id);
    }

    [Fact]
    public async Task SupervisorCannotCreateRoomOrSelfGrantMembership()
    {
        var room = await CreateRoom();
        using var supervisor = await factory.CreateAuthenticatedClientAsync("supervisor-one");
        Assert.Equal(HttpStatusCode.Forbidden, (await supervisor.PostAsJsonAsync("/api/v1/chat/rooms",
            new CreateChatRoomRequest("Unpermitted", null, null, [13]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await supervisor.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/members",
            new AddChatMemberRequest(room.Revision, 13))).StatusCode);
    }

    [Fact]
    public async Task ForeignAndBillingOnlyMembersCannotBeInvited()
    {
        var room = await CreateRoom();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        foreach (var id in new[] { 22, 15, 31 })
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/members",
                new AddChatMemberRequest(room.Revision, id))).StatusCode);
    }

    [Fact]
    public async Task RevokingEligiblePermissionsImmediatelyBlocksExistingMembership()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        try
        {
            await factory.ChangeUserPermissionsAsync(12, UserPermissions.None);
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/messages",
                new PostChatMessageRequest(room.Revision, Guid.NewGuid(), "Should not be saved"))).StatusCode);
        }
        finally { await factory.ChangeUserPermissionsAsync(12, UserPermissions.CaseManagement); }
    }

    [Fact]
    public async Task ConsumerRoomDoesNotBroadenExistingCaseloadAccess()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var room = await CreateRoom(personId: 101);
        Assert.Equal(101, room.PersonId);
        using (var nameScope = factory.Services.CreateScope())
        {
            var expectedName = await nameScope.ServiceProvider.GetRequiredService<ApiDbContext>().People
                .Where(x => x.Id == 101 && x.AgencyId == 1).Select(x => new { x.FirstName, x.LastName }).SingleAsync();
            Assert.Equal($"{expectedName.FirstName?.Trim()} {expectedName.LastName?.Trim()}".Trim(), room.ConsumerDisplayName);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/members",
            new AddChatMemberRequest(room.Revision, 19))).StatusCode);
        var candidates = await admin.GetFromJsonAsync<List<ChatCandidateDto>>("/api/v1/chat/candidates?personId=101");
        Assert.Contains(candidates!, x => x.UserId == 12);
        Assert.DoesNotContain(candidates!, x => x.UserId == 19 || x.UserId == 22 || x.UserId == 31);
        // A persisted membership alone is deliberately insufficient after caseload ownership changes.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var person = await db.People.SingleAsync(x => x.Id == 101);
        var oldOwner = person.UserId;
        try
        {
            person.UserId = 19; person.Revision++; await db.SaveChangesAsync();
            using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
            Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages")).StatusCode);
        }
        finally { person.UserId = oldOwner; person.Revision++; await db.SaveChangesAsync(); }
    }

    [Fact]
    public async Task SendIsIdempotentAndReusingTheKeyForDifferentContentIsRefused()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        var request = new PostChatMessageRequest(room.Revision, Guid.NewGuid(), "One immutable original");
        var first = await Send(member, room.Id, request);
        var replay = await Send(member, room.Id, request);
        Assert.Equal(first.Id, replay.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await member.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/messages",
            request with { Body = "Different content" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.Equal(1, await db.ChatMessages.CountAsync(x => x.RoomId == room.Id));
        Assert.Equal(1, await db.ChatChanges.CountAsync(x => x.RoomId == room.Id && x.Kind == "message"));
    }

    [Fact]
    public async Task StaleWriterCannotSaveAndCursorPagesSurviveRedactionOfOldMessages()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var first = await Send(member, room.Id, new(room.Revision, Guid.NewGuid(), "Earlier retained original"));
        var stale = await member.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/messages",
            new PostChatMessageRequest(room.Revision, Guid.NewGuid(), "Must not land"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var second = await Send(member, room.Id, new(first.Sequence, Guid.NewGuid(), "Next retained original"));
        var firstPage = await admin.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?afterSequence=0&take=1");
        Assert.True(firstPage!.HasMore);
        Assert.Equal(first.Id, Assert.Single(firstPage.Changes).Message.Id);
        var secondPage = await admin.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?afterSequence={firstPage.NextSequence}&take=1");
        Assert.False(secondPage!.HasMore);
        Assert.Equal(second.Id, Assert.Single(secondPage.Changes).Message.Id);
        var redact = await admin.PostAsJsonAsync($"/api/v1/chat/messages/{first.Id}/redact",
            new RedactChatMessageRequest(second.Sequence, "Synthetic wrong-room correction"));
        redact.EnsureSuccessStatusCode();
        var page = await admin.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?afterSequence={secondPage.NextSequence}");
        var tombstone = Assert.Single(page!.Changes);
        Assert.Equal("redaction", tombstone.Kind);
        Assert.Equal(first.Id, tombstone.Message.Id);
        Assert.Null(tombstone.Message.Body);
        using var scope = factory.Services.CreateScope();
        Assert.Equal("Earlier retained original", await scope.ServiceProvider.GetRequiredService<ApiDbContext>()
            .ChatMessages.Where(x => x.Id == first.Id).Select(x => x.Body).SingleAsync());
    }

    [Fact]
    public async Task ReadsProduceExactServerReleaseAuditWithoutAnySeenPost()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var message = await Send(member, room.Id, new(room.Revision, Guid.NewGuid(), "Never copy this synthetic narrative into audit"));
        var response = await admin.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var release = Assert.Single(await factory.GetAuditEventsAsync("chat.messages-released"),
            x => x.ResourceId == room.Id.ToString() && x.ActorUserId == 11);
        Assert.DoesNotContain(message.Body!, release.MetadataJson);
        using var metadata = JsonDocument.Parse(release.MetadataJson);
        Assert.Equal(message.Id, metadata.RootElement.GetProperty("messages")[0].GetProperty("Id").GetInt64());
        using var scope = factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApiDbContext>().ChatReadMarkers.AnyAsync(x => x.RoomId == room.Id));
    }

    [Fact]
    public async Task FailedAuditSavePreventsBodyRelease()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        await Send(member, room.Id, new(room.Revision, Guid.NewGuid(), "Do not release after audit failure"));
        using var rejectingHost = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ApiDbContext>(); services.RemoveAll<DbContextOptions<ApiDbContext>>();
            services.RemoveAll<IDbContextFactory<ApiDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApiDbContext>>();
            services.AddDbContextFactory<ApiDbContext>(options => options.UseSqlite(
                "Data Source=SatiApiTests;Mode=Memory;Cache=Shared;Default Timeout=30")
                .AddInterceptors(new RejectChatAudit()));
            services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());
            services.PostConfigure<ChatOptions>(options => options.Enabled = true);
        }));
        using var client = rejectingHost.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        client.DefaultRequestHeaders.Authorization = admin.DefaultRequestHeaders.Authorization;
        var response = await client.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("Do not release after audit failure", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(await factory.GetAuditEventsAsync("chat.messages-released"), x => x.ResourceId == room.Id.ToString());
    }

    [Fact]
    public async Task RemovalAndReinvitationCannotRecoverEarlierHistory()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var beforeMembership = (await member.GetFromJsonAsync<List<ChatRoomDto>>("/api/v1/chat/rooms"))!
            .Single(x => x.Id == room.Id).MembershipId;
        Assert.NotNull(beforeMembership);
        var original = await Send(member, room.Id, new(room.Revision, Guid.NewGuid(), "Original membership episode"));
        var remove = await member.DeleteAsync($"/api/v1/chat/rooms/{room.Id}/members/12?expectedRevision={original.Sequence}");
        remove.EnsureSuccessStatusCode();
        var removed = (await remove.Content.ReadFromJsonAsync<ChatRoomDto>())!;
        Assert.Null(removed.MembershipId);
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages")).StatusCode);
        var add = await admin.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/members", new AddChatMemberRequest(removed.Revision, 12));
        add.EnsureSuccessStatusCode();
        var restored = (await add.Content.ReadFromJsonAsync<ChatRoomDto>())!;
        var afterMembership = (await member.GetFromJsonAsync<List<ChatRoomDto>>("/api/v1/chat/rooms"))!
            .Single(x => x.Id == room.Id).MembershipId;
        Assert.NotNull(afterMembership);
        Assert.NotEqual(beforeMembership, afterMembership);
        var page = await member.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?afterSequence=0");
        Assert.Empty(page!.Changes);
        Assert.Equal(afterMembership, page.MembershipId);
        var history = (await member.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?beforeSequence={long.MaxValue}"))!;
        Assert.Empty(history.Changes);
        Assert.Equal(afterMembership, history.MembershipId);
        var redact = await admin.PostAsJsonAsync($"/api/v1/chat/messages/{original.Id}/redact",
            new RedactChatMessageRequest(restored.Revision, "Retained correction after reinvitation"));
        redact.EnsureSuccessStatusCode();
        Assert.Empty((await member.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?afterSequence=0"))!.Changes);
        Assert.Empty((await member.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?beforeSequence={long.MaxValue}"))!.Changes);
    }

    [Fact]
    public async Task BackwardHistoryIsBoundedAuditedAndReturnsCurrentRedactions()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var first = await Send(member, room.Id, new(room.Revision, Guid.NewGuid(), "First historical message"));
        var second = await Send(member, room.Id, new(first.Sequence, Guid.NewGuid(), "Second historical message"));
        var redact = await admin.PostAsJsonAsync($"/api/v1/chat/messages/{first.Id}/redact",
            new RedactChatMessageRequest(second.Sequence, "Historical redaction explanation"));
        redact.EnsureSuccessStatusCode();
        var recent = await admin.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?beforeSequence={long.MaxValue}&take=1");
        Assert.Equal(second.Id, Assert.Single(recent!.Changes).Message.Id);
        Assert.True(recent.HasMore);
        var older = await admin.GetFromJsonAsync<ChatPageDto>($"/api/v1/chat/rooms/{room.Id}/messages?beforeSequence={recent.NextSequence}&take=1");
        Assert.Equal(first.Id, Assert.Single(older!.Changes).Message.Id);
        Assert.Null(older.Changes[0].Message.Body);
        Assert.False(older.HasMore);
        var audit = (await factory.GetAuditEventsAsync("chat.messages-released"))
            .Where(x => x.ResourceId == room.Id.ToString()).ToList();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, entry => Assert.DoesNotContain("historical message", entry.MetadataJson));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(
            $"/api/v1/chat/rooms/{room.Id}/messages?beforeSequence=4&afterSequence=1")).StatusCode);
    }

    [Fact]
    public async Task FullPageAuditFitsSqlMetadataLimitAndCoversEveryReleasedMessage()
    {
        var room = await CreateRoom();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var storedRoom = await db.ChatRooms.SingleAsync(x => x.Id == room.Id);
            // Bypass the HTTP send limiter only for fixture construction; the read
            // uses the real route and the same SQL-sized audit column contract.
            for (var index = 0; index < ChatLimits.MaxPageSize; index++)
            {
                storedRoom.Revision++;
                var message = new ServerChatMessage { RoomId = room.Id, AgencyId = 1, Sequence = storedRoom.Revision,
                    AuthorUserId = 12, AuthorDisplayName = "Synthetic fixture", ClientMessageId = Guid.NewGuid(),
                    Body = "Synthetic historical body", PostedAtUtc = DateTime.UtcNow };
                db.ChatMessages.Add(message); await db.SaveChangesAsync();
                db.ChatChanges.Add(new ServerChatChange { RoomId = room.Id, AgencyId = 1,
                    Sequence = storedRoom.Revision, Kind = "message", MessageId = message.Id,
                    ActorUserId = 12, ChangedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
        }
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var page = await admin.GetFromJsonAsync<ChatPageDto>(
            $"/api/v1/chat/rooms/{room.Id}/messages?beforeSequence={long.MaxValue}&take=100");
        Assert.Equal(100, page!.Changes.Count);
        var events = (await factory.GetAuditEventsAsync("chat.messages-released"))
            .Where(x => x.ResourceId == room.Id.ToString()).ToList();
        Assert.Equal(4, events.Count);
        Assert.All(events, entry => Assert.True(entry.MetadataJson.Length <= 4000));
        var identities = events.SelectMany(entry =>
        {
            using var metadata = JsonDocument.Parse(entry.MetadataJson);
            return metadata.RootElement.GetProperty("messages").EnumerateArray()
                .Select(message => message.GetProperty("Id").GetInt64()).ToArray();
        }).ToArray();
        Assert.Equal(page.Changes.Select(x => x.Message.Id).Order(), identities.Order());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetainedChatBlocksConsumerDeletionBeforeAnyDependentRowChanges(bool testDataRoute)
    {
        var person = await factory.CreateTestConsumerGraphAsync();
        if (!testDataRoute)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
            var ordinary = await db.People.SingleAsync(x => x.Id == person.PersonId);
            ordinary.IsTestData = false;
            ordinary.CreatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await CreateRoom(person.PersonId);
        var before = await factory.GetTestConsumerGraphAsync(person.PersonId);
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var response = testDataRoute
            ? await admin.PostAsJsonAsync($"/api/v1/admin/test-data/consumers/{person.PersonId}/delete",
                new DeleteTestConsumerRequest(person.Revision, TestDataDeletionRules.ConsumerAttestation))
            : await admin.PostAsJsonAsync($"/api/v1/admin/consumers/{person.PersonId}/delete-in-window",
                new DeleteConsumerInWindowRequest(person.Revision, ConsumerDeletionRules.ConsumerAttestation,
                    "Synthetic test of retained-history protection"));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("consumer_has_chat_history", (await response.Content.ReadFromJsonAsync<ApiErrorDto>())!.Code);
        Assert.Equal(before, await factory.GetTestConsumerGraphAsync(person.PersonId));
    }

    [Fact]
    public async Task ArchivedRoomsRemainReadableButRejectNewMessages()
    {
        var room = await CreateRoom();
        using var member = await factory.CreateAuthenticatedClientAsync("case-manager-one");
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var original = await Send(member, room.Id, new(room.Revision, Guid.NewGuid(), "Retained before archival"));
        var archive = await admin.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/archive", new ChatRevisionRequest(original.Sequence));
        archive.EnsureSuccessStatusCode();
        Assert.True((await archive.Content.ReadFromJsonAsync<ChatRoomDto>())!.IsArchived);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/v1/chat/rooms/{room.Id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await member.PostAsJsonAsync($"/api/v1/chat/rooms/{room.Id}/messages",
            new PostChatMessageRequest(original.Sequence + 1, Guid.NewGuid(), "Too late"))).StatusCode);
    }

    [Fact]
    public async Task AnonymousAndPlatformSupportCannotUseChat()
    {
        using var anonymous = factory.CreateAnonymousClient();
        using var support = await factory.CreateAuthenticatedClientAsync("platform-operator");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/chat/rooms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.GetAsync("/api/v1/chat/rooms")).StatusCode);
    }

    [Fact]
    public void StreamLeaseHonorsBothTokenExpiryAndOriginalSessionCap()
    {
        var now = DateTimeOffset.UtcNow;
        ClaimsPrincipal Claims(DateTimeOffset exp, DateTimeOffset auth) => new(new ClaimsIdentity([
            new("exp", exp.ToUnixTimeSeconds().ToString()), new("sati_auth_time", auth.ToUnixTimeSeconds().ToString())]));
        Assert.False(ApiEndpoints.TryChatLease(Claims(now.AddSeconds(-1), now), 720, out _));
        Assert.False(ApiEndpoints.TryChatLease(Claims(now.AddMinutes(30), now.AddHours(-13)), 720, out _));
        Assert.False(ApiEndpoints.TryChatLease(Claims(now.AddMinutes(30), now.AddMinutes(5)), 720, out _));
        Assert.True(ApiEndpoints.TryChatLease(Claims(now.AddMinutes(30), now.AddHours(-11.9)), 720, out var deadline));
        Assert.True(deadline < now.AddMinutes(7));
    }

    [Fact]
    public async Task SocketCarriesOnlyGenericNoticesAndRejectsClientFrames()
    {
        await CreateRoom();
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var client = factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Authorization = admin.DefaultRequestHeaders.Authorization!.ToString();
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var socket = await client.ConnectAsync(new Uri("wss://localhost/api/v1/chat/stream"), ct.Token);
        var bytes = new byte[256];
        var receive = await socket.ReceiveAsync(bytes, ct.Token);
        Assert.Equal("{\"type\":\"changed\"}", Encoding.UTF8.GetString(bytes, 0, receive.Count));
        await socket.SendAsync("{\"type\":\"message\",\"body\":\"Must not be accepted\"}"u8.ToArray(), WebSocketMessageType.Text, true, ct.Token);
        var close = await socket.ReceiveAsync(bytes, ct.Token);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, close.CloseStatus);
    }

    private async Task<ChatRoomDto> CreateRoom(int? personId = null)
    {
        using var admin = await factory.CreateAuthenticatedClientAsync("admin-one");
        var response = await admin.PostAsJsonAsync("/api/v1/chat/rooms",
            new CreateChatRoomRequest("Synthetic room " + Guid.NewGuid().ToString("N")[..8], null, personId, [11, 12]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatRoomDto>())!;
    }
    private static async Task<ChatMessageDto> Send(HttpClient client, int roomId, PostChatMessageRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/chat/rooms/{roomId}/messages", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChatMessageDto>())!;
    }

    private sealed class RejectChatAudit : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ServerAuditEvent>().Any(x =>
                x.State == EntityState.Added && x.Entity.Action == "chat.messages-released"))
                throw new InvalidOperationException("Synthetic audit storage failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
