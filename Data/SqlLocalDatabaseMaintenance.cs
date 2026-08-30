using System.Globalization;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sati.Data;

/// <summary>
/// The real database operations behind <see cref="LocalDatabaseUpdater"/>, against
/// the local SQL Server or LocalDB instance that Local Production uses.
///
/// Never used for Demo. The deployed API owns the Azure schema and the client is not
/// permitted to alter it — see the guard in <c>App.xaml.cs</c>.
/// </summary>
public sealed class SqlLocalDatabaseMaintenance(SatiContext context) : ILocalDatabaseMaintenance
{
    /// <summary>
    /// Backups live beside the user's own application data rather than in the repo or
    /// Program Files. LocalDB runs as the signed-in user, so this is a path it can
    /// actually write to — a backup directed somewhere the service cannot reach fails
    /// at exactly the wrong moment.
    /// </summary>
    public static string BackupDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sati",
        "schema-backups");

    public async Task<IReadOnlyList<string>> PendingMigrationsAsync(
        CancellationToken cancellationToken = default) =>
        [.. await context.Database.GetPendingMigrationsAsync(cancellationToken)];

    public Task<IReadOnlyList<MigrationEffectFinding>> AnalyzePendingAsync(
        IReadOnlyList<string> pendingMigrationIds,
        CancellationToken cancellationToken = default) =>
        MigrationEffectAnalyzer.AnalyzeAsync(context, pendingMigrationIds, cancellationToken);

    /// <summary>
    /// Records that a migration ran, without running it. Only ever an insert into the
    /// history table: no schema and no consumer data is touched, which is what makes
    /// this safe to do unattended when the effects have been proven present.
    ///
    /// The insert is built by EF's own history repository rather than hand-written, so
    /// the row matches what EF would have written itself, product version included.
    /// </summary>
    public async Task<int> RecordMigrationsAsync(
        IReadOnlyList<string> migrationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migrationIds);
        if (migrationIds.Count == 0)
            return 0;

        var history = context.GetService<IHistoryRepository>();
        var productVersion = ProductInfo.GetVersion();
        var written = 0;

        foreach (var migrationId in migrationIds)
        {
            var script = history.GetInsertScript(new HistoryRow(migrationId, productVersion));
            await context.Database.ExecuteSqlRawAsync(script, cancellationToken);
            written++;
        }

        return written;
    }

    /// <summary>
    /// Consumer records are the thing worth protecting, so their presence is what
    /// decides whether a backup is taken. A database with users and settings but no
    /// people is a fresh install, and re-creating one costs nothing.
    /// </summary>
    public Task<bool> HasRecordsAsync(CancellationToken cancellationToken = default) =>
        context.People.AsNoTracking().AnyAsync(cancellationToken);

    public async Task<string> BackUpAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BackupDirectory);

        var databaseName = context.Database.GetDbConnection().Database;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture);
        var path = Path.Combine(BackupDirectory, $"{databaseName}-{stamp}.bak");

        // Parameterised as values, with the database name quoted separately: BACKUP
        // will not accept a parameter for its target, and the name comes from the
        // connection string rather than from anything a user typed.
        var quotedName = databaseName.Replace("]", "]]", StringComparison.Ordinal);
        var escapedPath = path.Replace("'", "''", StringComparison.Ordinal);

        await context.Database.ExecuteSqlRawAsync(
            $"BACKUP DATABASE [{quotedName}] TO DISK = '{escapedPath}' WITH INIT, SKIP, NOFORMAT;",
            cancellationToken);

        return path;
    }

    public Task MigrateAsync(CancellationToken cancellationToken = default) =>
        context.Database.MigrateAsync(cancellationToken);
}
