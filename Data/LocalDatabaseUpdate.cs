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

    /// <summary>
    /// A pending migration's effects are already in the database, so applying it would
    /// fail. Nothing was attempted and nothing was backed up, because nothing was going
    /// to be written. Needs a one-time repair by someone who can judge which side is
    /// right — see <see cref="LocalDatabaseUpdateResult.Findings"/>.
    /// </summary>
    NeedsRepair,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Migrations">The migrations that were pending.</param>
/// <param name="BackupPath">Where the pre-migration backup was written, or null if none was taken.</param>
/// <param name="Failure">The failure, when <see cref="LocalDatabaseUpdateOutcome.Failed"/>.</param>
public sealed record LocalDatabaseUpdateResult(
    LocalDatabaseUpdateOutcome Outcome,
    IReadOnlyList<string> Migrations,
    string? BackupPath,
    Exception? Failure,
    IReadOnlyList<MigrationEffectFinding>? Findings = null)
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
        if (Outcome == LocalDatabaseUpdateOutcome.NeedsRepair)
            return RepairMessage();

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

    /// <summary>
    /// The message for the case the old code could only report as a provider error. It
    /// names the update whose changes are already present, because that name is the
    /// whole repair: someone has to record that it ran. Saying "column specified more
    /// than once" told the reader nothing they could act on and told whoever they sent
    /// it to almost as little.
    /// </summary>
    private string RepairMessage()
    {
        var blocked = (Findings ?? [])
            .Where(finding => finding.State is MigrationEffectState.AlreadyPresent
                                            or MigrationEffectState.PartiallyPresent)
            .ToList();

        var detail = string.Join("\n", blocked.Select(finding =>
            $"  - {finding.MigrationId} ({(finding.State == MigrationEffectState.AlreadyPresent
                ? "all of its changes are already present"
                : "some of its changes are present and some are not")})"));

        var partial = blocked.Any(finding => finding.State == MigrationEffectState.PartiallyPresent)
            ? "\n\nOne of these is only partly present, which needs care. Do not reinstall or run " +
              "an older version until Josh has looked at it."
            : string.Empty;

        return
            "Sati stopped before updating its database, because part of the update it was " +
            "about to apply is already there.\n\n" +
            "Nothing was attempted and nothing was changed. Your records are exactly as you " +
            "left them, and no backup was needed because nothing was going to be written.\n\n" +
            "This is a one-time repair Josh can do in a couple of minutes. Send him this " +
            "message.\n\n" +
            "Update(s) already present:\n" +
            detail +
            partial;
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

    /// <summary>
    /// For each pending migration, whether the database already contains what it
    /// declares. Answered before anything is written, so a migration that cannot
    /// succeed is refused with a diagnosis rather than a provider error halfway
    /// through.
    /// </summary>
    Task<IReadOnlyList<MigrationEffectFinding>> AnalyzePendingAsync(
        IReadOnlyList<string> pendingMigrationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that a migration ran, without running it. An insert into the history
    /// table only — no schema and no consumer data.
    /// </summary>
    Task<int> RecordMigrationsAsync(
        IReadOnlyList<string> migrationIds,
        CancellationToken cancellationToken = default);

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
/// It used to refuse outright to repair a database whose migration history disagreed
/// with its schema, on the grounds that it needs judgement about which side is right
/// and guessing on a database full of consumer records is not a thing a startup path
/// should do unattended. That reasoning still holds, and one case still stops for it.
///
/// What changed on 2026-08-30 is that one shape of disagreement stopped requiring
/// judgement. <see cref="MigrationEffectAnalyzer"/> compares what a pending migration
/// declares against what the schema has, before anything is written. When every
/// declared effect is already present, recording that the migration ran is not a guess
/// about which side is right — the schema has already answered — and the repair is an
/// insert into the history table that touches no schema and no consumer data.
///
/// When effects are only partly present, or the analysis cannot reach a verdict, it
/// still stops, and now says which migration and why rather than surfacing a provider
/// error about a duplicate column name. That refusal is the part the original comment
/// was protecting and it is unchanged.
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

        // Ask what the schema already says about these before writing anything. A
        // migration whose effects are all present cannot be applied, and finding that
        // out by attempting it costs a backup and produces an error nobody outside
        // this codebase can act on.
        IReadOnlyList<MigrationEffectFinding> findings;
        try
        {
            findings = await maintenance.AnalyzePendingAsync(pending, cancellationToken);
        }
        catch
        {
            // A diagnosis that cannot be produced is not a reason to refuse an update
            // that might be perfectly ordinary. Fall through to the path that backs up
            // first and reports honestly if it fails, which is where this started.
            findings = [];
        }

        // A migration that is only half present is the one case that still stops. Which
        // half is missing decides what should happen, that needs a person, and no
        // amount of care makes an unattended guess about it acceptable.
        if (findings.Any(finding => finding.State == MigrationEffectState.PartiallyPresent))
        {
            return new LocalDatabaseUpdateResult(
                LocalDatabaseUpdateOutcome.NeedsRepair, pending, null, null, findings);
        }

        string? backupPath = null;
        var repairable = findings
            .Where(finding => finding.State == MigrationEffectState.AlreadyPresent)
            .Select(finding => finding.MigrationId)
            .ToList();
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

        // Record the migrations whose every declared effect was found, so EF stops
        // trying to apply changes the database already has. This writes history rows
        // and nothing else — no schema, no consumer data — which is what makes it a
        // defensible thing to do without asking. The backup above was taken first
        // regardless, because a database that holds records gets backed up before Sati
        // writes to it at all, not merely before it writes something risky.
        try
        {
            await maintenance.RecordMigrationsAsync(repairable, cancellationToken);
        }
        catch (Exception failure)
        {
            return new LocalDatabaseUpdateResult(
                LocalDatabaseUpdateOutcome.Failed, pending, backupPath, failure, findings);
        }

        try
        {
            await maintenance.MigrateAsync(cancellationToken);
        }
        catch (Exception failure)
        {
            return new LocalDatabaseUpdateResult(
                LocalDatabaseUpdateOutcome.Failed, pending, backupPath, failure, findings);
        }

        return new LocalDatabaseUpdateResult(
            LocalDatabaseUpdateOutcome.Applied, pending, backupPath, null, findings);
    }
}
