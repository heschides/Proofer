using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace Sati.Data;

public enum SatiDataEnvironment
{
    Production,
    Demo
}

public sealed record DataEnvironmentInfo(
    SatiDataEnvironment Environment,
    string ExpectedDatabaseName,
    string? ConnectionStringName = null,
    string? ConnectionString = null,
    Uri? ApiBaseAddress = null)
{
    public bool IsDemo => Environment == SatiDataEnvironment.Demo;
    public bool UsesCloudApi => ApiBaseAddress is not null;
    public string DisplayName => IsDemo ? "DEMO" : "PRODUCTION";
    public string WindowTitle => IsDemo ? "Sati — DEMO" : "Sati";
}

public static class DataEnvironmentResolver
{
    public static DataEnvironmentInfo Resolve(
        IConfiguration configuration,
        SatiDataEnvironment environment)
    {
        var expectedDatabaseName = configuration[$"DataEnvironments:{environment}:ExpectedDatabaseName"];
        if (string.IsNullOrWhiteSpace(expectedDatabaseName))
            throw new InvalidOperationException($"Expected database name for {environment} is not configured.");

        if (environment == SatiDataEnvironment.Demo)
        {
            var rawBaseAddress = configuration["ApiEnvironments:Demo:BaseAddress"];
            if (!Uri.TryCreate(rawBaseAddress, UriKind.Absolute, out var baseAddress) ||
                !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The Demo API must have a valid HTTPS base address.");
            }

            return new(environment, expectedDatabaseName, ApiBaseAddress: baseAddress);
        }

        if (environment != SatiDataEnvironment.Production)
            throw new ArgumentOutOfRangeException(nameof(environment), environment, null);

        const string connectionName = "SatiProduction";
        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.Equals(builder.InitialCatalog, expectedDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The {environment} build is configured for database '{builder.InitialCatalog}', " +
                $"but only '{expectedDatabaseName}' is permitted.");
        }

        return new(environment, expectedDatabaseName, connectionName, connectionString);
    }
}

public sealed class DatabaseIdentityValidator(DataEnvironmentInfo environment)
{
    public async Task ValidateAsync(CancellationToken cancellationToken = default)
    {
        if (environment.UsesCloudApi || string.IsNullOrWhiteSpace(environment.ConnectionString))
            throw new InvalidOperationException("Direct database validation is not permitted for the Demo environment.");

        await using var connection = new SqlConnection(environment.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DB_NAME(), EnvironmentName, InstanceId
            FROM dbo.SatiDatabaseIdentity
            WHERE Id = 1;
            """;

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The database identity marker is missing.");

            var actualDatabaseName = reader.GetString(0);
            var actualEnvironment = reader.GetString(1);

            if (!string.Equals(actualDatabaseName, environment.ExpectedDatabaseName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Connected database is '{actualDatabaseName}', expected '{environment.ExpectedDatabaseName}'.");

            if (!string.Equals(actualEnvironment, environment.Environment.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Database is marked '{actualEnvironment}', but {environment.Environment} was selected.");
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            throw new InvalidOperationException(
                "The target database has no Sati environment marker. Startup was stopped before migrations or login.", ex);
        }
    }
}
