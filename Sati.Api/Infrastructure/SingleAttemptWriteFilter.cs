using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

/// <summary>
/// Establishes an EF execution scope before a write endpoint starts a transaction.
/// A failed/ambiguous commit must be checked by the caller, not automatically replayed
/// with a second artifact, receipt, attestation, or audit event. Reads retain retries.
/// </summary>
internal sealed class SingleAttemptWriteFilter(IDbContextFactory<ApiDbContext> factory) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
            return await next(context);
        await using var db = await factory.CreateDbContextAsync(context.HttpContext.RequestAborted);
        return await new SingleAttempt(db).ExecuteAsync(async () => await next(context));
    }

    private sealed class SingleAttempt(ApiDbContext context) : ExecutionStrategy(context, 0, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => false;
    }
}
