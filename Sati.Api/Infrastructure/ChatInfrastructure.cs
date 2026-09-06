using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class ChatOptions
{
    public bool Enabled { get; set; }
}

// This slice is deliberately unavailable in real-data environments. Deployment of
// code does not authorize PHI use or change the separate environment identity gate.
internal sealed class ChatFeature(IOptions<ChatOptions> options, IOptions<SatiApiOptions> sati)
{
    public bool Enabled => options.Value.Enabled &&
        ((sati.Value.ExpectedEnvironment == "Demo" && sati.Value.ExpectedDatabaseName == "SatiDemo") ||
         (sati.Value.ExpectedEnvironment == "Testing" && sati.Value.ExpectedDatabaseName == "SatiApiTests"));
}

// GET releases write access evidence, so they use the same no-replay discipline as
// writes. An ambiguous audit commit must never be silently re-executed.
internal sealed class ChatSingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => false;
}

internal sealed class ChatEnabledFilter(ChatFeature feature) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        if (!feature.Enabled) return Results.NotFound();
        try { return await next(context); }
        catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number == 1205)
        { return ChatConcurrency.Conflict(); }
        catch (DbUpdateException error) when (ChatConcurrency.IsCollision(error))
        { return ChatConcurrency.Conflict(); }
    }
}

internal static class ChatConcurrency
{
    public static IResult Conflict() => Results.Conflict(new ApiErrorDto("chat_stale",
        "This room changed. Refresh it before trying again.", string.Empty));
    public static bool IsCollision(DbUpdateException error) => error is DbUpdateConcurrencyException ||
        error.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true ||
        error.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205;
}

internal sealed class ChatNotifications
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();
    private readonly object _gate = new();

    public Subscription? Subscribe(int agencyId, int userId, IReadOnlySet<int> roomIds)
    {
        lock (_gate)
        {
            if (_subscriptions.Values.Count(x => x.AgencyId == agencyId && x.UserId == userId) >= 2)
                return null;
            var subscription = new Subscription(this, agencyId, userId, roomIds);
            _subscriptions[subscription.Id] = subscription;
            return subscription;
        }
    }

    public void Publish(int agencyId, int roomId)
    {
        foreach (var entry in _subscriptions.Values)
            if (entry.AgencyId == agencyId && entry.RoomIds.Contains(roomId))
                entry.Signals.Writer.TryWrite(true);
    }

    internal sealed class Subscription(ChatNotifications owner, int agencyId, int userId,
        IReadOnlySet<int> roomIds) : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public int AgencyId { get; } = agencyId;
        public int UserId { get; } = userId;
        public IReadOnlySet<int> RoomIds { get; set; } = roomIds;
        // A notice only asks for a current fetch; combining notices loses no data.
        public Channel<bool> Signals { get; } = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
        public void Dispose()
        {
            owner._subscriptions.TryRemove(Id, out _);
            Signals.Writer.TryComplete();
        }
    }
}
