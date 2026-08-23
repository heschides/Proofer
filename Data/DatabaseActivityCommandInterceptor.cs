using Microsoft.EntityFrameworkCore.Diagnostics;
using Sati.Services;
using System.Collections.Concurrent;
using System.Data.Common;

namespace Sati.Data;

/// <summary>
/// Tracks Local Production database commands. Reader leases remain open until EF disposes the
/// reader, covering materialization rather than only the instant SQL execution returns.
/// </summary>
public sealed class DatabaseActivityCommandInterceptor(IDatabaseActivityTracker tracker)
    : DbCommandInterceptor
{
    private readonly ConcurrentDictionary<Guid, IDisposable> _activities = new();

    internal void TrackStarted(Guid commandId)
    {
        var activity = tracker.Begin();
        if (!_activities.TryAdd(commandId, activity))
            activity.Dispose();
    }

    internal void TrackCompleted(Guid commandId)
    {
        if (_activities.TryRemove(commandId, out var activity))
            activity.Dispose();
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        TrackStarted(eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        TrackStarted(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        TrackStarted(eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        TrackStarted(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        TrackCompleted(eventData.CommandId);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        TrackCompleted(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        TrackStarted(eventData.CommandId);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        TrackStarted(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        TrackCompleted(eventData.CommandId);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        TrackCompleted(eventData.CommandId);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult DataReaderDisposing(
        DbCommand command,
        DataReaderDisposingEventData eventData,
        InterceptionResult result)
    {
        TrackCompleted(eventData.CommandId);
        return result;
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        TrackCompleted(eventData.CommandId);
    }

    public override Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TrackCompleted(eventData.CommandId);
        return Task.CompletedTask;
    }

    public override void CommandCanceled(DbCommand command, CommandEndEventData eventData)
    {
        TrackCompleted(eventData.CommandId);
    }

    public override Task CommandCanceledAsync(
        DbCommand command,
        CommandEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        TrackCompleted(eventData.CommandId);
        return Task.CompletedTask;
    }
}
