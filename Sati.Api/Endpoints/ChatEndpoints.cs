using System.Data;
using System.Globalization;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;
using Sati.Contracts.V1;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static void MapChat(RouteGroupBuilder api)
    {
        api.MapGet("/chat/availability", (ChatFeature feature) => Results.Ok(new ChatAvailabilityDto(
            feature.Enabled, feature.Enabled ? "Synthetic-data team chat is enabled."
                : "Team chat is not enabled for this environment.")));
        var chat = api.MapGroup("/chat").AddEndpointFilter<ChatEnabledFilter>();
        chat.MapGet("/rooms", ListChatRooms);
        chat.MapGet("/candidates", ChatCandidates);
        chat.MapPost("/rooms", CreateChatRoom);
        chat.MapPut("/rooms/{roomId:int}", UpdateChatRoom);
        chat.MapPost("/rooms/{roomId:int}/archive", ArchiveChatRoom);
        chat.MapGet("/rooms/{roomId:int}/members", GetChatMembers);
        chat.MapPost("/rooms/{roomId:int}/members", AddChatMember);
        chat.MapDelete("/rooms/{roomId:int}/members/{userId:int}", RemoveChatMember);
        chat.MapGet("/rooms/{roomId:int}/messages", GetChatMessages);
        chat.MapPost("/rooms/{roomId:int}/messages", PostChatMessage).RequireRateLimiting("chat-post");
        chat.MapPost("/messages/{messageId:long}/redact", RedactChatMessage);
        chat.MapPost("/rooms/{roomId:int}/read", MarkChatSeen);
        chat.MapGet("/stream", StreamChat);
    }

    private static async Task<IResult> ListChatRooms(ClaimsPrincipal principal, ApiDbContext db,
        HttpContext context, CancellationToken ct)
    {
        PreventSensitiveResponseCaching(context);
        var actor = Actor.From(principal);
        if (!ChatAccess.IsEligible(actor.ToAgencyActor())) return Results.NotFound();
        var memberships = await db.ChatRoomMembers.AsNoTracking().Where(x => x.UserId == actor.UserId &&
            x.AgencyId == actor.AgencyId && x.RemovedAtUtc == null).ToListAsync(ct);
        var result = new List<ChatRoomDto>();
        foreach (var member in memberships)
        {
            var room = await db.ChatRooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == member.RoomId, ct);
            if (room is not null && ChatAccess.CanReadRoom(actor.ToAgencyActor(), Scope(room), Membership(member)) &&
                await ChatConsumerAccess(db, actor, room.PersonId, ct))
                result.Add(await ChatRoomDto(db, room, actor.UserId, member, ct));
        }
        return Results.Ok(result.OrderBy(x => x.Name).ToList());
    }

    private static async Task<IResult> ChatCandidates(int? personId, ClaimsPrincipal principal,
        ApiDbContext db, HttpContext context, CancellationToken ct)
    {
        PreventSensitiveResponseCaching(context);
        var actor = Actor.From(principal);
        if (!actor.HasAdminPermissions) return Results.Forbid();
        if (!await ChatConsumerAccess(db, actor, personId, ct)) return Results.NotFound();
        var users = await db.Users.AsNoTracking().Where(x => x.AgencyId == actor.AgencyId &&
            x.Role != "PlatformOperator").OrderBy(x => x.DisplayName).Take(ChatLimits.MaxMembers).ToListAsync(ct);
        var result = new List<ChatCandidateDto>();
        foreach (var user in users)
        {
            var target = ChatActor(user);
            if (ChatAccess.IsEligible(target.ToAgencyActor()) && await ChatConsumerAccess(db, target, personId, ct))
                result.Add(new(user.Id, user.DisplayName));
        }
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateChatRoom(CreateChatRoomRequest request, ClaimsPrincipal principal,
        ApiDbContext db, AuditTrail audit, ChatNotifications notifications, CancellationToken ct)
    {
        var actor = Actor.From(principal);
        if (!actor.HasAdminPermissions) return Results.Forbid();
        if (!ValidChatName(request.Name, request.Description) || request.MemberUserIds is null ||
            request.MemberUserIds.Count is < 1 or > ChatLimits.MaxMembers ||
            request.MemberUserIds.Distinct().Count() != request.MemberUserIds.Count)
            return InvalidChat("Choose a room name and between 1 and 250 distinct members.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await ChatConsumerAccess(db, actor, request.PersonId, ct)) return Results.NotFound();
        foreach (var userId in request.MemberUserIds)
            if (!await ValidChatTarget(db, actor.AgencyId, userId, request.PersonId, ct)) return Results.NotFound();
        var now = DateTime.UtcNow;
        var room = new ServerChatRoom { AgencyId = actor.AgencyId, Name = request.Name.Trim(),
            Description = ChatDescription(request.Description), PersonId = request.PersonId,
            CreatedByUserId = actor.UserId, CreatedAtUtc = now, Revision = 1 };
        db.ChatRooms.Add(room);
        await db.SaveChangesAsync(ct);
        foreach (var userId in request.MemberUserIds)
            db.ChatRoomMembers.Add(new ServerChatRoomMember { RoomId = room.Id, AgencyId = actor.AgencyId,
                UserId = userId, AddedByUserId = actor.UserId, AddedAtUtc = now, VisibleAfterSequence = 1 });
        db.ChatChanges.Add(Change(room, actor, "created"));
        audit.Record(actor, "chat.room-created", "ChatRoom", room.Id,
            JsonSerializer.Serialize(new { memberUserIds = request.MemberUserIds, room.PersonId }));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        notifications.Publish(room.AgencyId, room.Id);
        return Results.Ok(await ChatRoomDto(db, room, actor.UserId, null, ct));
    }

    private static Task<IResult> UpdateChatRoom(int roomId, UpdateChatRoomRequest request,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ChatNotifications notifications,
        CancellationToken ct) => ChangeChatRoom(roomId, request.ExpectedRevision, "updated", null,
            request.Name, request.Description, Actor.From(principal), db, audit, notifications, ct);

    private static Task<IResult> ArchiveChatRoom(int roomId, ChatRevisionRequest request,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ChatNotifications notifications,
        CancellationToken ct) => ChangeChatRoom(roomId, request.ExpectedRevision, "archived", null,
            null, null, Actor.From(principal), db, audit, notifications, ct);

    private static Task<IResult> AddChatMember(int roomId, AddChatMemberRequest request,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ChatNotifications notifications,
        CancellationToken ct) => ChangeChatRoom(roomId, request.ExpectedRevision, "member-added", request.UserId,
            null, null, Actor.From(principal), db, audit, notifications, ct);

    private static Task<IResult> RemoveChatMember(int roomId, int userId, long expectedRevision,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ChatNotifications notifications,
        CancellationToken ct) => ChangeChatRoom(roomId, expectedRevision, "member-removed", userId,
            null, null, Actor.From(principal), db, audit, notifications, ct);

    private static async Task<IResult> ChangeChatRoom(int id, long expectedRevision, string kind,
        int? userId, string? name, string? description, Actor actor, ApiDbContext db, AuditTrail audit,
        ChatNotifications notifications, CancellationToken ct)
    {
        if (kind == "updated" && !ValidChatName(name, description)) return InvalidChat("Enter a room name up to 80 characters.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var room = await db.ChatRooms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (room is null || !await ChatConsumerAccess(db, actor, room.PersonId, ct)) return Results.NotFound();
        var member = await ChatMember(db, room.Id, actor.UserId, ct);
        var selfRemoval = kind == "member-removed" && userId == actor.UserId &&
            ChatAccess.CanReadRoom(actor.ToAgencyActor(), Scope(room), Membership(member));
        if (!selfRemoval && !ChatAccess.CanAdministerRoom(actor.ToAgencyActor(), Scope(room))) return Results.NotFound();
        if (room.Revision != expectedRevision || room.ArchivedAtUtc is not null && kind != "member-removed") return ChatConflict();
        var nextSequence = checked(room.Revision + 1);
        if (kind == "member-added")
        {
            if (!await ValidChatTarget(db, actor.AgencyId, userId!.Value, room.PersonId, ct)) return Results.NotFound();
            if (await ChatMember(db, room.Id, userId.Value, ct) is not null) return ChatConflict();
            if (await db.ChatRoomMembers.CountAsync(x => x.RoomId == id && x.RemovedAtUtc == null, ct) >= ChatLimits.MaxMembers)
                return InvalidChat("This room has reached its membership limit.");
            db.ChatRoomMembers.Add(new ServerChatRoomMember { RoomId = id, AgencyId = actor.AgencyId,
                UserId = userId.Value, AddedByUserId = actor.UserId, AddedAtUtc = DateTime.UtcNow,
                VisibleAfterSequence = nextSequence });
        }
        else if (kind == "member-removed")
        {
            var target = await ChatMember(db, id, userId!.Value, ct);
            if (target is null || target.AgencyId != actor.AgencyId) return Results.NotFound();
            target.RemovedAtUtc = DateTime.UtcNow; target.RemovedByUserId = actor.UserId;
        }
        else if (kind == "archived")
        { room.ArchivedAtUtc = DateTime.UtcNow; room.ArchivedByUserId = actor.UserId; }
        else { room.Name = name!.Trim(); room.Description = ChatDescription(description); }
        room.Revision = nextSequence;
        var change = Change(room, actor, kind); change.TargetUserId = userId; db.ChatChanges.Add(change);
        audit.Record(actor, "chat." + (kind.StartsWith("member-", StringComparison.Ordinal) ? kind : "room-" + kind),
            "ChatRoom", id, JsonSerializer.Serialize(new { userId, sequence = nextSequence }));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException error) when (ChatWriteCollision(error)) { return ChatConflict(); }
        notifications.Publish(room.AgencyId, room.Id);
        return Results.Ok(await ChatRoomDto(db, room, actor.UserId, member, ct));
    }

    private static async Task<IResult> GetChatMembers(int roomId, ClaimsPrincipal principal,
        ApiDbContext db, HttpContext context, CancellationToken ct)
    {
        PreventSensitiveResponseCaching(context);
        var access = await AccessibleChat(db, Actor.From(principal), roomId, ct);
        if (access is null) return Results.NotFound();
        var actor = Actor.From(principal);
        var members = await (from member in db.ChatRoomMembers.AsNoTracking()
            join user in db.Users.AsNoTracking() on member.UserId equals user.Id
            where member.RoomId == roomId && member.AgencyId == actor.AgencyId &&
                  user.AgencyId == actor.AgencyId && member.RemovedAtUtc == null
            orderby user.DisplayName
            select new ChatMemberDto(user.Id, user.DisplayName, member.AddedAtUtc)).ToListAsync(ct);
        return Results.Ok(members);
    }

    private static async Task<IResult> GetChatMessages(int roomId, long? afterSequence, long? beforeSequence, int? take,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, HttpContext context, CancellationToken ct)
    {
        PreventSensitiveResponseCaching(context);
        if (afterSequence is < 0 || beforeSequence is <= 0 || (afterSequence is not null && beforeSequence is not null) ||
            take is < 1 or > ChatLimits.MaxPageSize) return InvalidChat("Choose a valid page size and one paging direction.");
        var actor = Actor.From(principal);
        return await new ChatSingleAttempt(db).ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var access = await AccessibleChat(db, actor, roomId, ct);
            if (access is null) return Results.NotFound();
            var (room, member) = access.Value;
            if (beforeSequence is not null)
            {
                var history = await db.ChatMessages.AsNoTracking().Where(x => x.RoomId == roomId &&
                    x.AgencyId == actor.AgencyId && x.Sequence > member.VisibleAfterSequence &&
                    x.Sequence < beforeSequence && x.Sequence <= room.Revision)
                    .OrderByDescending(x => x.Sequence).Take((take ?? ChatLimits.MaxPageSize) + 1).ToListAsync(ct);
                var more = history.Count > (take ?? ChatLimits.MaxPageSize);
                if (more) history.RemoveAt(history.Count - 1);
                history.Reverse();
                var page = new List<ChatChangeDto>();
                foreach (var message in history)
                    page.Add(new(message.Sequence, "message", await ChatMessageDto(db, message, ct)));
                if (page.Count > 0)
                {
                    RecordChatRelease(audit, actor, roomId, member.Id, page, "history");
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
                return Results.Ok(new ChatPageDto(page, history.FirstOrDefault()?.Sequence ?? member.VisibleAfterSequence,
                    more, room.Revision, member.Id));
            }
            var start = Math.Max(afterSequence ?? 0, member.VisibleAfterSequence);
            if (start > room.Revision) return ChatConflict();
            var count = take ?? ChatLimits.MaxPageSize;
            var changes = await db.ChatChanges.AsNoTracking().Where(x => x.RoomId == roomId &&
                x.AgencyId == actor.AgencyId && x.Sequence > start && x.Sequence <= room.Revision &&
                x.MessageId != null).OrderBy(x => x.Sequence).Take(count + 1).ToListAsync(ct);
            var hasMore = changes.Count > count;
            if (hasMore) changes.RemoveAt(changes.Count - 1);
            var result = new List<ChatChangeDto>();
            foreach (var change in changes)
            {
                var message = await db.ChatMessages.AsNoTracking().SingleAsync(x => x.Id == change.MessageId &&
                    x.RoomId == roomId && x.AgencyId == actor.AgencyId, ct);
                // Joining or rejoining never reveals earlier room history, including
                // later redaction events that refer to that earlier history.
                if (message.Sequence <= member.VisibleAfterSequence) continue;
                result.Add(new(change.Sequence, change.Kind, await ChatMessageDto(db, message, ct)));
            }
            var next = hasMore ? changes[^1].Sequence : room.Revision;
            if (result.Count > 0)
            {
                // This records precisely what the server released, not a claim that a
                // person read it. A missing/forged client seen marker cannot suppress it.
                RecordChatRelease(audit, actor, roomId, member.Id, result, "changes");
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
            return Results.Ok(new ChatPageDto(result, next, hasMore, room.Revision, member.Id));
        });
    }

    private static async Task<IResult> PostChatMessage(int roomId, PostChatMessageRequest request,
        ClaimsPrincipal principal, ApiDbContext db, ChatNotifications notifications, CancellationToken ct)
    {
        var body = request.Body?.Trim();
        if (request.ClientMessageId == Guid.Empty || string.IsNullOrWhiteSpace(body) || body.Length > ChatLimits.MaxBodyLength)
            return InvalidChat("Enter a message between 1 and 4,000 characters.");
        var actor = Actor.From(principal);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await AccessibleChat(db, actor, roomId, ct);
        if (access is null) return Results.NotFound();
        var (room, member) = access.Value;
        if (!ChatAccess.CanPostToRoom(actor.ToAgencyActor(), Scope(room), Membership(member))) return Results.NotFound();
        var existing = await db.ChatMessages.AsNoTracking().SingleOrDefaultAsync(x => x.RoomId == roomId &&
            x.AgencyId == actor.AgencyId && x.AuthorUserId == actor.UserId && x.ClientMessageId == request.ClientMessageId, ct);
        if (existing is not null)
        {
            if (existing.Sequence <= member.VisibleAfterSequence) return Results.NotFound();
            if (existing.Body != body) return Results.Conflict(new ApiErrorDto("chat_idempotency_mismatch",
                "This send identifier already belongs to a different message.", string.Empty));
            return Results.Ok(await ChatMessageDto(db, existing, ct));
        }
        if (room.Revision != request.ExpectedRevision) return ChatConflict();
        room.Revision = checked(room.Revision + 1);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId, ct);
        var message = new ServerChatMessage { RoomId = room.Id, AgencyId = actor.AgencyId,
            AuthorUserId = actor.UserId, AuthorDisplayName = user.DisplayName, ClientMessageId = request.ClientMessageId,
            Sequence = room.Revision, PostedAtUtc = DateTime.UtcNow, Body = body };
        db.ChatMessages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
            var change = Change(room, actor, "message"); change.MessageId = message.Id; db.ChatChanges.Add(change);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException error) when (ChatWriteCollision(error)) { return ChatConflict(); }
        notifications.Publish(room.AgencyId, room.Id);
        return Results.Ok(await ChatMessageDto(db, message, ct));
    }

    private static async Task<IResult> RedactChatMessage(long messageId, RedactChatMessageRequest request,
        ClaimsPrincipal principal, ApiDbContext db, AuditTrail audit, ChatNotifications notifications,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length is < 10 or > 240)
            return InvalidChat("Enter a redaction reason between 10 and 240 characters.");
        var actor = Actor.From(principal);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var message = await db.ChatMessages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId && x.AgencyId == actor.AgencyId, ct);
        if (message is null) return Results.NotFound();
        var access = await AccessibleChat(db, actor, message.RoomId, ct);
        if (access is null || message.Sequence <= access.Value.Member.VisibleAfterSequence ||
            !ChatAccess.CanRedact(actor.ToAgencyActor(), Scope(access.Value.Room), Membership(access.Value.Member))) return Results.NotFound();
        var room = access.Value.Room;
        if (await db.ChatChanges.AnyAsync(x => x.MessageId == messageId && x.Kind == "redaction", ct))
            return Results.Ok(await ChatMessageDto(db, message, ct));
        if (room.Revision != request.ExpectedRevision) return ChatConflict();
        room.Revision = checked(room.Revision + 1);
        var change = Change(room, actor, "redaction"); change.MessageId = messageId;
        change.RedactionReason = request.Reason.Trim(); db.ChatChanges.Add(change);
        audit.Record(actor, "chat.message-redacted", "ChatRoom", room.Id,
            JsonSerializer.Serialize(new { messageId, sequence = room.Revision }));
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException error) when (ChatWriteCollision(error)) { return ChatConflict(); }
        notifications.Publish(room.AgencyId, room.Id);
        return Results.Ok(await ChatMessageDto(db, message, ct));
    }

    private static async Task<IResult> MarkChatSeen(int roomId, ChatSeenRequest request,
        ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct)
    {
        var actor = Actor.From(principal);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var access = await AccessibleChat(db, actor, roomId, ct);
        if (access is null) return Results.NotFound();
        var (room, member) = access.Value;
        if (request.Sequence < member.VisibleAfterSequence || request.Sequence > room.Revision)
            return InvalidChat("The seen marker must belong to the current room history.");
        var marker = await db.ChatReadMarkers.SingleOrDefaultAsync(x => x.RoomId == roomId && x.UserId == actor.UserId && x.AgencyId == actor.AgencyId, ct);
        if (marker is null)
        {
            marker = new ServerChatReadMarker { RoomId = roomId, UserId = actor.UserId, AgencyId = actor.AgencyId,
                LastSeenSequence = request.Sequence, LastSeenAtUtc = DateTime.UtcNow };
            db.ChatReadMarkers.Add(marker);
        }
        else if (request.Sequence > marker.LastSeenSequence)
        { marker.LastSeenSequence = request.Sequence; marker.LastSeenAtUtc = DateTime.UtcNow; }
        try { await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch (DbUpdateException error) when (ChatWriteCollision(error)) { return ChatConflict(); }
        return Results.Ok(await ChatRoomDto(db, room, actor.UserId, member, ct));
    }

    private static async Task<(ServerChatRoom Room, ServerChatRoomMember Member)?> AccessibleChat(
        ApiDbContext db, Actor actor, int id, CancellationToken ct)
    {
        var room = await db.ChatRooms.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (room is null) return null;
        var member = await ChatMember(db, id, actor.UserId, ct);
        return ChatAccess.CanReadRoom(actor.ToAgencyActor(), Scope(room), Membership(member)) &&
            await ChatConsumerAccess(db, actor, room.PersonId, ct) ? (room, member!) : null;
    }

    private static async Task<bool> ChatConsumerAccess(ApiDbContext db, Actor actor, int? personId, CancellationToken ct)
    {
        if (!ChatAccess.IsEligible(actor.ToAgencyActor()) || !await TenantAccess.IsCurrentActorAsync(db, actor, ct)) return false;
        if (personId is null) return true;
        var person = await db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId && x.AgencyId == actor.AgencyId, ct);
        return person is not null && await TenantAccess.CanAccessUserAsync(db, actor, person.UserId, ct);
    }

    private static async Task<bool> ValidChatTarget(ApiDbContext db, int agencyId, int userId, int? personId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.AgencyId == agencyId && x.Role != "PlatformOperator", ct);
        return user is not null && await ChatConsumerAccess(db, ChatActor(user), personId, ct) &&
            await db.ChatRoomMembers.CountAsync(x => x.UserId == userId && x.AgencyId == agencyId &&
                x.RemovedAtUtc == null, ct) < ChatLimits.MaxRoomsPerUser;
    }

    private static Task<ServerChatRoomMember?> ChatMember(ApiDbContext db, int roomId, int userId, CancellationToken ct) =>
        db.ChatRoomMembers.SingleOrDefaultAsync(x => x.RoomId == roomId && x.UserId == userId && x.RemovedAtUtc == null, ct);
    private static ChatRoomScope Scope(ServerChatRoom room) => new(room.Id, room.AgencyId, room.ArchivedAtUtc is not null);
    private static ChatMembership? Membership(ServerChatRoomMember? member) => member is null ? null :
        new(member.RoomId, member.UserId, member.AgencyId, member.RemovedAtUtc is null);
    private static Actor ChatActor(ServerUser user) => new(user.Id, user.AgencyId, user.Role, user.DisplayName, user.Permissions);
    private static ServerChatChange Change(ServerChatRoom room, Actor actor, string kind) => new()
    { RoomId = room.Id, AgencyId = room.AgencyId, Sequence = room.Revision, Kind = kind,
        ActorUserId = actor.UserId, ChangedAtUtc = DateTime.UtcNow };

    private static async Task<ChatMessageDto> ChatMessageDto(ApiDbContext db, ServerChatMessage message, CancellationToken ct)
    {
        var redaction = await db.ChatChanges.AsNoTracking().SingleOrDefaultAsync(x => x.MessageId == message.Id &&
            x.RoomId == message.RoomId && x.AgencyId == message.AgencyId && x.Kind == "redaction", ct);
        return new(message.Id, message.RoomId, message.Sequence, message.AuthorUserId, message.AuthorDisplayName,
            message.PostedAtUtc, redaction is null ? message.Body : null, redaction?.ChangedAtUtc, redaction?.ActorUserId);
    }

    private static async Task<ChatRoomDto> ChatRoomDto(ApiDbContext db, ServerChatRoom room, int userId,
        ServerChatRoomMember? member, CancellationToken ct)
    {
        if (member?.RemovedAtUtc is not null) member = null;
        member ??= await ChatMember(db, room.Id, userId, ct);
        var seen = await db.ChatReadMarkers.AsNoTracking().Where(x => x.RoomId == room.Id && x.UserId == userId &&
            x.AgencyId == room.AgencyId).Select(x => (long?)x.LastSeenSequence).SingleOrDefaultAsync(ct) ?? 0;
        var floor = Math.Max(seen, member?.VisibleAfterSequence ?? room.Revision);
        var unread = member is null ? 0 : await db.ChatMessages.CountAsync(x => x.RoomId == room.Id &&
            x.AgencyId == room.AgencyId && x.Sequence > floor && x.AuthorUserId != userId, ct);
        string? consumerDisplayName = null;
        if (room.PersonId is int personId)
        {
            // The caller has already passed the existing consumer-access gate.
            // Project only the name needed to disambiguate similarly named rooms.
            var consumer = await db.People.AsNoTracking().Where(x => x.Id == personId && x.AgencyId == room.AgencyId)
                .Select(x => new { x.FirstName, x.LastName }).SingleOrDefaultAsync(ct);
            if (consumer is not null)
                consumerDisplayName = $"{consumer.FirstName?.Trim()} {consumer.LastName?.Trim()}".Trim();
        }
        return new(room.Id, room.Name, room.Description, room.PersonId, room.Revision, room.ArchivedAtUtc is not null,
            floor, unread, member?.Id, consumerDisplayName);
    }

    private static bool ValidChatName(string? name, string? description) => !string.IsNullOrWhiteSpace(name) &&
        name.Trim().Length <= 80 && (description?.Trim().Length ?? 0) <= 240;
    private static string? ChatDescription(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult InvalidChat(string message) => Results.BadRequest(new ApiErrorDto("chat_invalid", message, string.Empty));
    private static IResult ChatConflict() => ChatConcurrency.Conflict();
    private static IResult ChatRetainedConsumerConflict() => Results.Conflict(new ApiErrorDto(
        "consumer_has_chat_history", ConsumerDeletionRules.HasChatHistoryMessage,
        string.Empty));
    private static bool ChatWriteCollision(DbUpdateException error) => ChatConcurrency.IsCollision(error);

    private static void RecordChatRelease(AuditTrail audit, Actor actor, int roomId, int membershipId,
        IReadOnlyList<ChatChangeDto> messages, string direction)
    {
        // AuditEvent.MetadataJson is nvarchar(4000). A full 100-item page does
        // not fit in one row. All bounded chunks commit in the release transaction.
        var batchId = Guid.NewGuid();
        var part = 0;
        foreach (var chunk in messages.Chunk(25))
        {
            var metadata = JsonSerializer.Serialize(new { batchId, part = ++part, membershipId, direction,
                messages = chunk.Select(x => new { x.Message.Id, x.Sequence,
                    redacted = x.Message.RedactedAtUtc != null }) });
            if (metadata.Length > 4000) throw new InvalidOperationException("Chat access evidence exceeded its storage limit.");
            audit.Record(actor, "chat.messages-released", "ChatRoom", roomId, metadata);
        }
    }
}
