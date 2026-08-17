using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Sati.Api.Data;
using Sati.Api.Infrastructure;
using Xunit;

namespace Sati.Api.Tests;

/// <summary>
/// The drift check exists because a hosted database that is behind the model
/// fails one feature at a time, in whichever endpoint touches the new column
/// first, with nothing naming the actual cause. These prove it stays quiet on a
/// matching schema and speaks up on a mismatched one — a detector that only ever
/// returns healthy is worse than none.
/// </summary>
/// <remarks>
/// Each test builds its own database. Deliberately outside the shared API
/// collection: the point is to remove a column, and doing that to the seeded
/// database every other test reads would be a poor trade for a little setup.
/// </remarks>
public sealed class SchemaDriftHealthCheckTests
{
    [Fact]
    public async Task AMatchingSchemaIsReportedHealthy()
    {
        await using var database = await DriftFixture.CreateAsync();

        var result = await database.CheckAsync();

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task AColumnTheModelExpectsButTheDatabaseLacksIsReportedByName()
    {
        // Exactly what a missing migration looks like: the model knows about
        // Providers.Npi, the table does not have it. This is the shape that took
        // GET /providers down on the hosted Demo API.
        await using var database = await DriftFixture.CreateAsync();
        await database.ExecuteAsync("DROP INDEX IX_Providers_AgencyId_Npi");
        await database.ExecuteAsync("ALTER TABLE Providers DROP COLUMN Npi");

        var result = await database.CheckAsync();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Providers.Npi", result.Description);
    }

    [Fact]
    public async Task EveryMissingColumnIsNamed()
    {
        await using var database = await DriftFixture.CreateAsync();
        await database.ExecuteAsync("ALTER TABLE ATRequests DROP COLUMN PassthroughRate");
        await database.ExecuteAsync("ALTER TABLE ATRequestItems DROP COLUMN ScreenshotPng");

        var result = await database.CheckAsync();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("ATRequests.PassthroughRate", result.Description);
        Assert.Contains("ATRequestItems.ScreenshotPng", result.Description);
        Assert.Contains("2 object(s)", result.Description);
    }

    [Fact]
    public async Task AMissingTableIsReportedRatherThanEveryColumnInIt()
    {
        await using var database = await DriftFixture.CreateAsync();
        await database.ExecuteAsync("DROP TABLE Providers");

        var result = await database.CheckAsync();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("Providers (entire table)", result.Description);
        Assert.DoesNotContain("Providers.Npi", result.Description);
    }

    private sealed class DriftFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<ApiDbContext> _options;

        private DriftFixture(SqliteConnection connection, DbContextOptions<ApiDbContext> options)
        {
            _connection = connection;
            _options = options;
        }

        public static async Task<DriftFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<ApiDbContext>().UseSqlite(connection).Options;
            await using var db = new ApiDbContext(options);
            await db.Database.EnsureCreatedAsync();
            return new DriftFixture(connection, options);
        }

        public async Task ExecuteAsync(string sql)
        {
            await using var db = new ApiDbContext(_options);
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        public Task<HealthCheckResult> CheckAsync()
        {
            var check = new SchemaDriftHealthCheck(
                new SingleOptionsContextFactory(_options),
                NullLogger<SchemaDriftHealthCheck>.Instance);
            return check.CheckHealthAsync(new HealthCheckContext());
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
    }

    private sealed class SingleOptionsContextFactory(DbContextOptions<ApiDbContext> options)
        : IDbContextFactory<ApiDbContext>
    {
        public ApiDbContext CreateDbContext() => new(options);

        public Task<ApiDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
