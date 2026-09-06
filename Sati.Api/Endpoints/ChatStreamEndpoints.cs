using System.Globalization;
using System.Net.WebSockets;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Sati.Api.Security;

namespace Sati.Api.Endpoints;

internal static partial class ApiEndpoints
{
    private static async Task StreamChat(HttpContext context, IDbContextFactory<ApiDbContext> factory,
        ChatNotifications notifications, IOptions<ApiAuthenticationOptions> authentication,
        ChatFeature feature)
    {
        if (!context.WebSockets.IsWebSocketRequest || !context.Request.IsHttps)
        { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }
        var actor = Actor.From(context.User);
        if (!TryChatLease(context.User, authentication.Value.MaxSessionMinutes, out var deadline))
        { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }
        var rooms = await ChatStreamRooms(factory, actor, context.RequestAborted);
        if (rooms is null || rooms.Count == 0)
        { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        using var subscription = notifications.Subscribe(actor.AgencyId, actor.UserId, rooms);
        if (subscription is null)
        { context.Response.StatusCode = StatusCodes.Status429TooManyRequests; return; }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var incoming = RejectChatFrames(socket, lifetime.Token);
        var nextValidation = DateTimeOffset.MinValue;
        try
        {
            while (socket.State == WebSocketState.Open && !incoming.IsCompleted && !lifetime.IsCancellationRequested)
            {
                if (!feature.Enabled || DateTimeOffset.UtcNow >= deadline) break;
                if (DateTimeOffset.UtcNow >= nextValidation)
                {
                    rooms = await ChatStreamRooms(factory, actor, lifetime.Token);
                    if (rooms is null || rooms.Count == 0) break;
                    subscription.RoomIds = rooms;
                    nextValidation = DateTimeOffset.UtcNow.AddSeconds(20);
                }
                // One writer and a bounded notice queue: slow clients cannot build an
                // unbounded body buffer. Every body stays on the audited HTTP path.
                using (var sendLimit = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
                {
                    sendLimit.CancelAfter(TimeSpan.FromSeconds(5));
                    await socket.SendAsync("{\"type\":\"changed\"}"u8.ToArray(), WebSocketMessageType.Text,
                        true, sendLimit.Token);
                }
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var remaining = deadline - DateTimeOffset.UtcNow;
                wait.CancelAfter(remaining < TimeSpan.FromSeconds(20) ? remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero
                    : TimeSpan.FromSeconds(20));
                var notice = subscription.Signals.Reader.ReadAsync(wait.Token).AsTask();
                var completed = await Task.WhenAny(notice, incoming);
                if (completed == incoming) break;
                try { await notice; } catch (OperationCanceledException) when (!lifetime.IsCancellationRequested) { }
            }
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var closeLimit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "Reconnect with a current session.", closeLimit.Token);
            }
        }
        catch (Exception error) when (error is OperationCanceledException or WebSocketException)
        { /* Expected transport/session termination; no payload or token enters logs. */ }
        finally
        {
            lifetime.Cancel();
            try { await incoming; } catch (Exception error) when (error is OperationCanceledException or WebSocketException) { }
        }
    }

    // No content-bearing client application frames, including auth frames. Renew
    // through the existing HTTP session then reconnect with its current bearer.
    private static async Task RejectChatFrames(WebSocket socket, CancellationToken ct)
    {
        // Any application frame or peer close ends the receive task. Only the
        // outer loop writes/closes: WebSocket permits one concurrent sender.
        await socket.ReceiveAsync(new byte[1], ct);
    }

    internal static bool TryChatLease(ClaimsPrincipal principal, int maxSessionMinutes, out DateTimeOffset deadline)
    {
        deadline = default;
        if (!long.TryParse(principal.FindFirstValue("exp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var exp) ||
            !long.TryParse(principal.FindFirstValue(TokenIssuer.AuthenticatedAtClaim), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var auth)) return false;
        try
        {
            var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(auth);
            var expires = DateTimeOffset.FromUnixTimeSeconds(exp);
            var cap = authenticatedAt.AddMinutes(maxSessionMinutes);
            deadline = expires < cap ? expires : cap;
            return authenticatedAt <= DateTimeOffset.UtcNow.AddSeconds(30) && deadline > DateTimeOffset.UtcNow;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static async Task<HashSet<int>?> ChatStreamRooms(IDbContextFactory<ApiDbContext> factory,
        Actor actor, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        if (!await TenantAccess.IsCurrentActorAsync(db, actor, ct)) return null;
        var candidates = await db.ChatRoomMembers.AsNoTracking().Where(x => x.UserId == actor.UserId &&
            x.AgencyId == actor.AgencyId && x.RemovedAtUtc == null).Select(x => x.RoomId).ToListAsync(ct);
        var rooms = new HashSet<int>();
        foreach (var id in candidates)
            if (await AccessibleChat(db, actor, id, ct) is not null) rooms.Add(id);
        return rooms;
    }
}
