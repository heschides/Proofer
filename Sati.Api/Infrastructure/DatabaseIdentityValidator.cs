using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sati.Api.Data;

namespace Sati.Api.Infrastructure;

internal sealed class DatabaseIdentityValidator(
    IDbContextFactory<ApiDbContext> contextFactory,
    IOptions<SatiApiOptions> options)
{
    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME(), EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1;";
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The Sati database identity marker is missing.");

        var databaseName = reader.GetString(0);
        var environmentName = reader.GetString(1);
        if (!string.Equals(databaseName, options.Value.ExpectedDatabaseName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(environmentName, options.Value.ExpectedEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Database identity mismatch. Connected to '{databaseName}' marked '{environmentName}'.");
        }
    }
}

internal sealed class DatabaseIdentityHostedService(
    DatabaseIdentityValidator validator,
    ILogger<DatabaseIdentityHostedService> logger) : IHostedService
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await validator.ValidateAsync(cancellationToken);
                return;
            }
            catch (SqlException ex) when (IsTransient(ex) && attempt <= RetryDelays.Length)
            {
                var delay = RetryDelays[attempt - 1];
                logger.LogWarning(
                    ex,
                    "Transient database identity validation failure on attempt {Attempt}. Retrying in {DelaySeconds} seconds.",
                    attempt,
                    delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(SqlException exception) =>
        exception.IsTransient || exception.Number == -2;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DatabaseIdentityHealthCheck(DatabaseIdentityValidator validator) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await validator.ValidateAsync(cancellationToken);
            return HealthCheckResult.Healthy("SatiDemo identity validated.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SatiDemo identity validation failed.", ex);
        }
    }
}
