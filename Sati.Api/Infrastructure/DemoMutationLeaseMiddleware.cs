using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sati.Api.Data;
using Sati.Contracts.V1;

namespace Sati.Api.Infrastructure;

internal sealed class DemoMutationLeaseMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IConfiguration configuration,
        ApiDbContext db,
        IOptions<SatiApiOptions> options)
    {
        var isDemo =
            string.Equals(options.Value.ExpectedEnvironment, "Demo", StringComparison.Ordinal) &&
            string.Equals(options.Value.ExpectedDatabaseName, "SatiDemo", StringComparison.Ordinal);
        if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method) ||
            context.Request.Path == "/api/v1/admin/demo/reset" || !isDemo || !db.Database.IsSqlServer())
        {
            await next(context);
            return;
        }

        var connectionString = configuration.GetConnectionString("SatiDemo")!;
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(context.RequestAborted);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'SatiDemo.FullReset',
                @LockMode=N'Shared', @LockOwner=N'Session', @LockTimeout=0;
            SELECT @result;
            """;
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(context.RequestAborted));
        if (result < 0)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new ApiErrorDto(
                "demo_reset_in_progress",
                "The Demo is being restored. Sign in again in a few minutes.",
                context.TraceIdentifier), context.RequestAborted);
            return;
        }

        try { await next(context); }
        finally
        {
            await using var release = connection.CreateCommand();
            release.CommandText = "EXEC sys.sp_releaseapplock @Resource=N'SatiDemo.FullReset', @LockOwner=N'Session';";
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
