using Sati.Data;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The startup schema update, which runs before the splash screen on every machine
/// using a local database.
///
/// The sequencing is the whole point, so it is tested rather than trusted: back up
/// before migrating, only when there is something to lose, never migrate after a
/// failed backup, and never let a failure read as success. Two of those machines —
/// the login holding real consumer records and a partner's laptop — have no
/// developer present when this runs, so "it threw before the window opened" is not
/// an acceptable outcome.
/// </summary>
public sealed class LocalDatabaseUpdaterTests
{
    [Fact]
    public async Task ACurrentSchemaIsLeftAlone()
    {
        var db = new FakeMaintenance();

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Equal(LocalDatabaseUpdateOutcome.AlreadyCurrent, result.Outcome);
        Assert.False(db.BackedUp);
        Assert.False(db.Migrated);
    }

    /// <summary>
    /// The development database on the primary login is this case, and it is migrated
    /// most often. Backing up an empty database would cost time and disk on every
    /// launch to protect nothing.
    /// </summary>
    [Fact]
    public async Task AnEmptyDatabaseMigratesWithoutABackup()
    {
        var db = new FakeMaintenance { Pending = ["20260818220245_AddEncryptedSsn"], HasRecords = false };

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Equal(LocalDatabaseUpdateOutcome.Applied, result.Outcome);
        Assert.False(db.BackedUp);
        Assert.True(db.Migrated);
        Assert.Null(result.BackupPath);
    }

    [Fact]
    public async Task ADatabaseWithRecordsIsBackedUpBeforeItIsMigrated()
    {
        var db = new FakeMaintenance { Pending = ["20260818220245_AddEncryptedSsn"], HasRecords = true };

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Equal(LocalDatabaseUpdateOutcome.Applied, result.Outcome);
        Assert.Equal(["backup", "migrate"], db.Order);
        Assert.Equal(FakeMaintenance.BackupFile, result.BackupPath);
    }

    /// <summary>
    /// Migrating after a failed backup would pick the least recoverable order of
    /// events available: a schema change on real records with nothing to restore from.
    /// </summary>
    [Fact]
    public async Task AFailedBackupStopsBeforeMigrating()
    {
        var db = new FakeMaintenance
        {
            Pending = ["20260818220245_AddEncryptedSsn"],
            HasRecords = true,
            BackupThrows = new IOException("The backup device is full."),
        };

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Equal(LocalDatabaseUpdateOutcome.Failed, result.Outcome);
        Assert.False(db.Migrated);
    }

    /// <summary>
    /// The diverged-history case: a database that has acquired columns outside the
    /// migration chain, which has happened to SatiProduction before. The updater does
    /// not try to repair it — that needs judgement about which side is right — but it
    /// must surface the backup so the data is recoverable by someone who is not a
    /// developer.
    /// </summary>
    [Fact]
    public async Task AFailedMigrationReportsTheBackupItTook()
    {
        var db = new FakeMaintenance
        {
            Pending = ["20260818220245_AddEncryptedSsn"],
            HasRecords = true,
            MigrateThrows = new InvalidOperationException(
                "Column names in each table must be unique. 'SsnLastFour' is specified more than once."),
        };

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Equal(LocalDatabaseUpdateOutcome.Failed, result.Outcome);
        Assert.Equal(FakeMaintenance.BackupFile, result.BackupPath);
        Assert.Contains(FakeMaintenance.BackupFile, result.FailureMessage());
        Assert.Contains("have not been changed", result.FailureMessage());
    }

    /// <summary>
    /// If the migration history cannot even be read, the next step would be DDL
    /// against a database whose state was never established.
    /// </summary>
    [Fact]
    public async Task AnUnreadableHistoryStopsBeforeAnythingElse()
    {
        var db = new FakeMaintenance { PendingThrows = new InvalidOperationException("Login failed.") };

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Equal(LocalDatabaseUpdateOutcome.Failed, result.Outcome);
        Assert.False(db.BackedUp);
        Assert.False(db.Migrated);
    }

    /// <summary>
    /// The message is read by a case manager mid-crash, so it has to say what happened
    /// to their data and who to contact, without a stack trace as the first sentence.
    /// </summary>
    [Fact]
    public async Task TheFailureMessageIsAddressedToSomeoneWhoIsNotADeveloper()
    {
        var db = new FakeMaintenance
        {
            Pending = ["20260818220245_AddEncryptedSsn"],
            HasRecords = true,
            MigrateThrows = new InvalidOperationException("SQL error 2705"),
        };

        var message = (await new LocalDatabaseUpdater(db).UpdateAsync()).FailureMessage();

        Assert.Contains("Your records have not been changed", message);
        Assert.Contains("Josh", message);
        Assert.False(message.StartsWith("SQL error", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASuccessfulUpdateHasNoFailureMessage()
    {
        var db = new FakeMaintenance { Pending = ["x"], HasRecords = true };

        var result = await new LocalDatabaseUpdater(db).UpdateAsync();

        Assert.Empty(result.FailureMessage());
    }

    private sealed class FakeMaintenance : ILocalDatabaseMaintenance
    {
        public const string BackupFile = @"C:\Users\Test\AppData\Local\Sati\schema-backups\SatiProduction.bak";

        public IReadOnlyList<string> Pending { get; set; } = [];
        public bool HasRecords { get; set; }
        public Exception? PendingThrows { get; set; }
        public Exception? BackupThrows { get; set; }
        public Exception? MigrateThrows { get; set; }

        public List<string> Order { get; } = [];
        public bool BackedUp => Order.Contains("backup");
        public bool Migrated => Order.Contains("migrate");

        public Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken = default) =>
            PendingThrows is not null ? Task.FromException<IReadOnlyList<string>>(PendingThrows) : Task.FromResult(Pending);

        public Task<bool> HasRecordsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(HasRecords);

        public Task<string> BackUpAsync(CancellationToken cancellationToken = default)
        {
            if (BackupThrows is not null)
                return Task.FromException<string>(BackupThrows);
            Order.Add("backup");
            return Task.FromResult(BackupFile);
        }

        public Task MigrateAsync(CancellationToken cancellationToken = default)
        {
            if (MigrateThrows is not null)
                return Task.FromException(MigrateThrows);
            Order.Add("migrate");
            return Task.CompletedTask;
        }
    }
}
