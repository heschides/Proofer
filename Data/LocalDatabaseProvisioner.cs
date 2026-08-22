using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Sati.Data;

/// <summary>
/// Creates only a genuinely absent local Production database. An existing database is never
/// migrated, marked, renamed, or otherwise adopted here; it must pass the ordinary identity gate.
/// </summary>
public sealed class LocalDatabaseProvisioner(DataEnvironmentInfo environment, SatiContext context)
{
    public async Task<bool> ProvisionIfMissingAsync(CancellationToken cancellationToken = default)
    {
        if (environment.UsesCloudApi || string.IsNullOrWhiteSpace(environment.ConnectionString))
            return false;

        var target = new SqlConnectionStringBuilder(environment.ConnectionString);
        var databaseName = target.InitialCatalog;
        if (!string.Equals(databaseName, environment.ExpectedDatabaseName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The local database target does not match the selected environment.");

        var master = new SqlConnectionStringBuilder(environment.ConnectionString) { InitialCatalog = "master" };
        await using (var connection = new SqlConnection(master.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name;";
            command.Parameters.AddWithValue("@name", databaseName);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 0)
                return false;
        }

        // EF creates the named database and applies the complete controlled migration chain.
        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.SatiDatabaseIdentity', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.SatiDatabaseIdentity
                (
                    Id tinyint NOT NULL CONSTRAINT PK_SatiDatabaseIdentity PRIMARY KEY,
                    EnvironmentName nvarchar(20) NOT NULL,
                    InstanceId uniqueidentifier NOT NULL,
                    CreatedAtUtc datetime2 NOT NULL,
                    CONSTRAINT CK_SatiDatabaseIdentity_SingleRow CHECK (Id = 1)
                );
            END;

            IF EXISTS (SELECT 1 FROM dbo.SatiDatabaseIdentity)
                THROW 51002, 'A new Sati database unexpectedly contains an identity row.', 1;

            INSERT dbo.SatiDatabaseIdentity (Id, EnvironmentName, InstanceId, CreatedAtUtc)
            VALUES (1, 'Production', NEWID(), SYSUTCDATETIME());
            """, cancellationToken);
        return true;
    }
}
