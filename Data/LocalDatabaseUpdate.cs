namespace Sati.Data;

/// <summary>What the startup schema update did, or tried to do.</summary>
public enum LocalDatabaseUpdateOutcome
{
    /// <summary>The schema was already current. The ordinary case.</summary>
    AlreadyCurrent,

    /// <summary>Migrations were applied. <see cref="LocalDatabaseUpdateResult.BackupPath"/> may be null if there was nothing to lose.</summary>
    Applied,

    /// <summary>The migration failed. The database is unchanged or restorable from the backup.</summary>
    Failed,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Migrations">The migrations that were pending.</param>
/// <param name="BackupPath">Where the pre-migration backup was written, or null if none was taken.</param>
/// <param name="Failure">The failure, when <see cref="LocalDatabaseUpdateOutcome.Failed"/>.</param>
public sealed record LocalDatabaseUpdateResult(
    LocalDatabaseUpdateOutcome Outcome,
    IReadOnlyList<string> Migrations,
    string? BackupPath,
    Exception? Failure)
{
    public static LocalDatabaseUpdateResult Current { get; } =
        new(LocalDatabaseUpdateOutcome.AlreadyCurrent, [], null, null);

    /// <summary>
    /// What to tell someone who is not a developer, when something went wrong. Names
    /// the backup, because the only thing that matters at that moment is that the
    /// data is recoverable and by whom.
    /// </summary>
    public string FailureMessage()
    {
        if (Outcome != LocalDatabaseUpdateOutcome.Failed)
            return string.Empty;

        var restore = BackupPath is null
            ? "No backup was needed because the database had no records to lose."
            : $"A backup taken before the attempt is at:\n{BackupPath}";

        return
            "Sati could not update its database to match this version of the program, " +
            "so it has stopped rather than run against a database it does not understand.\n\n" +
            $"{restore}\n\n" +
            "Your records have not been changed. Send this message to Josh before reinstalling " +
            "or running an older version.\n\n" +
            $"Technical detail: {Failure?.Message}";
    }
}

/// <summary>
/// The database operations the startup update needs, behind an interface so the
/// sequencing can be tested without a SQL Server.
///
/// The sequence is the part worth testing — back up before migrating, only when
/// there is something to lose, and never leave a failure looking like success — and
/// that logic should not require a live database to exercise.
/// </summary>
public interface ILocalDatabaseMaintenance
{
    Task<IReadOnlyList<string>> PendingMigrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether this database holds consumer records, which decides whether a backup is warranted.</summary>
    Task<bool> HasRecordsAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a full backup and returns its path.</summary>
    Task<string> BackUpAsync(CancellationToken cancellationToken = default);

    Task MigrateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies pending local migrations at startup, with a backup first when the
/// database holds real records.
///
/// The desktop has always migrated Local Production on launch, which is right for a
/// tool whose users are case managers rather than developers — nobody should have to
/// run a script to open their caseload. What it lacked was a safety net, and the
/// stakes are not the same on every machine: an empty development database has
/// nothing to lose, while the login holding real consumer records, or a partner's
/// laptop, has everything to lose and nobody present who could diagnose a failure.
///
/// So the sequence is: do nothing unless migrations are actually pending; back up
/// first if there are records; and on failure, report a message naming the backup
/// rather than throwing before the splash screen and leaving an app that will not
/// start and says nothing about why.
///
/// It deliberately does NOT try to repair a database whose migration history
/// disagrees with its schema. That has happened to SatiProduction before, it needs
/// judgement about which side is right, and guessing on a database full of consumer
/// records is not a thing a startup path should do unattended. It stops and says so.
/// </summary>
public sealed class LocalDatabaseUpdater(ILocalDatabaseMaintenance maintenance)
{
    public async Task<LocalDatabaseUpdateResult> UpdateAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> pending;
        try
        {
            pending = await maintenance.PendingMigrationsAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            // Being unable to read migration history is itself a reason not to
            // proceed: the next step would be DDL against a database whose state we
            // just failed to establish.
            return new LocalDatabaseUpdateResult(
                LocalDatabaseUpdateOutcome.Failed, [], null, failure);
        }

        if (pending.Count == 0)
            return LocalDatabaseUpdateResult.Current;

        string? backupPath = null;
        try
        {
            // A database with no records has nothing worth the time or the disk. The
            // development database on the primary login is exactly this case, and it
            // is the one that gets migrated most often.
            if (await maintenance.HasRecordsAsync(cancellationToken))
                backupPath = await maintenance.BackUpAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            // A backup that could not be written is a stop, not a warning. Migrating
            // anyway would be choosing the least recoverable order of events.
            return new LocalDatabaseUpdateResult(
                LocalDatabaseUpdateOutcome.Failed, pending, null, failure);
        }

        try
        {
            await maintenance.MigrateAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            return new LocalDatabaseUpdateResult(
                LocalDatabaseUpdateOutcome.Failed, pending, backupPath, failure);
        }

        return new LocalDatabaseUpdateResult(
            LocalDatabaseUpdateOutcome.Applied, pending, backupPath, null);
    }
}
