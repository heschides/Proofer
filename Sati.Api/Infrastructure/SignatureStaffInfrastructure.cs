using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sati.Api.Data;
using Sati.Contracts.V1;
using Sati.Signatures;

namespace Sati.Api.Infrastructure;

/// <summary>The shared workflow uses this request's clinical transaction; no second context can commit independently.</summary>
internal sealed class SignatureStaffRuntime(ApiDbContext db, SignatureFeature feature, SignatureOptions options,
    ISignatureBlobStore blobs, SigningPinProtector pins, SignatureOutboxProtector outbox, TimeProvider clock)
{
    public SignatureWorkflow Workflow { get; } = new(db, feature, options, blobs, pins, outbox, clock);
}

internal sealed class SignatureStaffSingleAttempt(ApiDbContext db) : ExecutionStrategy(db, 0, TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception) => false;
}

internal sealed class SignatureEnabledFilter(SignatureFeature feature) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache";
        context.HttpContext.Response.Headers.Pragma = "no-cache";
        if (!feature.Enabled) return Results.NotFound();
        try { return await next(context); }
        catch (SignatureWorkflowException error)
        { return Results.Json(new ApiErrorDto(error.Code, error.Message, string.Empty), statusCode: error.StatusCode); }
        catch (Microsoft.Data.SqlClient.SqlException error) when (error.Number == 1205)
        { return Conflict(); }
        catch (DbUpdateException error) when (error is DbUpdateConcurrencyException ||
            error.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true ||
            error.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627 or 1205)
        { return Conflict(); }
    }
    private static IResult Conflict() => Results.Conflict(new ApiErrorDto("signature_stale",
        "This signing record changed. Reload its status before trying again.", string.Empty));
}
