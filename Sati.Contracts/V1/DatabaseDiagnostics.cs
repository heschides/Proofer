namespace Sati.Contracts.V1;

/// <summary>How the schema in front of us compares with what this build expects.</summary>
public enum SchemaCurrency
{
    /// <summary>Every migration this build knows about has been recorded as applied.</summary>
    Current,

    /// <summary>Migrations are pending. The desktop applies these at its next launch.</summary>
    Behind,

    /// <summary>
    /// The database records migrations this build does not contain, so it was written by a
    /// newer release than the one running.
    /// </summary>
    Ahead,

    /// <summary>Pending in one direction and unrecognised in the other.</summary>
    Diverged,

    /// <summary>Nothing could be read. Says so rather than implying health.</summary>
    Unknown
}

/// <param name="EnvironmentLabel">PRODUCTION or DEMO, as the shell already displays it.</param>
/// <param name="AccessPath">How the data is reached: a local database, or the hosted API.</param>
/// <param name="DatabaseName">The database actually connected to.</param>
/// <param name="IdentityMarker">
/// <c>dbo.SatiDatabaseIdentity</c>'s environment name — the same marker every migration
/// script fails closed on. Shown because "I am connected to what I think I am" is the
/// question underneath most of the others.
/// </param>
/// <param name="AppliedCount">Migrations the database records as applied.</param>
/// <param name="ExpectedCount">Migrations this build contains.</param>
/// <param name="NewestApplied">
/// The newest applied migration id. The id carries its own timestamp — 20260830001538 is
/// the timestamp — so showing the id is strictly more informative than showing a date.
/// </param>
/// <param name="Pending">Ids this build has that the database has not recorded.</param>
/// <param name="Unrecognised">Ids the database records that this build does not contain.</param>
/// <param name="ApiReleaseVersion">The hosted API's release, when one is being used.</param>
/// <param name="ApiContractRevision">The hosted API's contract revision, when one is being used.</param>
public sealed record DatabaseDiagnostics(
    string EnvironmentLabel,
    string AccessPath,
    string? DatabaseName,
    string? IdentityMarker,
    int AppliedCount,
    int ExpectedCount,
    string? NewestApplied,
    IReadOnlyList<string> Pending,
    IReadOnlyList<string> Unrecognised,
    string? ApiReleaseVersion = null,
    string? ApiContractRevision = null)
{
    public SchemaCurrency Currency =>
        (Pending.Count, Unrecognised.Count) switch
        {
            (0, 0) when ExpectedCount > 0 => SchemaCurrency.Current,
            (0, 0) => SchemaCurrency.Unknown,
            (> 0, 0) => SchemaCurrency.Behind,
            (0, > 0) => SchemaCurrency.Ahead,
            _ => SchemaCurrency.Diverged
        };

    /// <summary>
    /// The line worth reading first. It states the counts rather than a colour or an icon,
    /// because this gets read aloud over the phone at least as often as it gets looked at.
    /// </summary>
    public string Headline => Currency switch
    {
        SchemaCurrency.Current =>
            $"Up to date — all {ExpectedCount} database updates applied.",
        SchemaCurrency.Behind =>
            $"{AppliedCount} of {ExpectedCount} database updates applied. " +
            $"{Pending.Count} will be applied the next time Sati starts.",
        SchemaCurrency.Ahead =>
            $"This database has {Unrecognised.Count} update(s) that this version of Sati does not know " +
            "about. It was last used by a newer version.",
        SchemaCurrency.Diverged =>
            $"{Pending.Count} update(s) pending and {Unrecognised.Count} unrecognised. " +
            "Send this screen to Josh before continuing.",
        _ => "The database update history could not be read."
    };

    /// <summary>
    /// Whether this warrants telling somebody. Ahead and Diverged are not states a person
    /// should be left to notice on their own; Behind is ordinary and resolves at next launch.
    /// </summary>
    public bool NeedsAttention =>
        Currency is SchemaCurrency.Ahead or SchemaCurrency.Diverged or SchemaCurrency.Unknown;

    /// <summary>
    /// A copyable block for support. Every question asked across the 2026-08-30 session —
    /// which environment, which database, how many updates, which are pending — is answered
    /// here, so a screenshot replaces a round trip.
    /// </summary>
    public string SupportSummary
    {
        get
        {
            var lines = new List<string>
            {
                $"Environment : {EnvironmentLabel}",
                $"Access      : {AccessPath}",
                $"Database    : {DatabaseName ?? "(unknown)"}",
                $"Identity    : {IdentityMarker ?? "(unreadable)"}",
                $"Updates     : {AppliedCount} of {ExpectedCount} applied",
                $"Newest      : {NewestApplied ?? "(none recorded)"}"
            };

            if (ApiReleaseVersion is not null)
                lines.Add($"API release : {ApiReleaseVersion} (contract {ApiContractRevision ?? "?"})");
            if (Pending.Count > 0)
                lines.Add($"Pending     : {string.Join(", ", Pending)}");
            if (Unrecognised.Count > 0)
                lines.Add($"Unknown here: {string.Join(", ", Unrecognised)}");

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Builds the comparison from the two id sets, so the counts and the lists can never
    /// disagree with each other.
    /// </summary>
    public static DatabaseDiagnostics Compare(
        string environmentLabel,
        string accessPath,
        string? databaseName,
        string? identityMarker,
        IEnumerable<string> appliedMigrationIds,
        IEnumerable<string> expectedMigrationIds,
        string? apiReleaseVersion = null,
        string? apiContractRevision = null)
    {
        var applied = appliedMigrationIds
            .Select(id => id.Trim('﻿', ' '))
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        var expected = expectedMigrationIds
            .Select(id => id.Trim('﻿', ' '))
            .Where(id => id.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return new DatabaseDiagnostics(
            environmentLabel,
            accessPath,
            databaseName,
            identityMarker,
            applied.Count,
            expected.Count,
            applied.OrderBy(id => id, StringComparer.Ordinal).LastOrDefault(),
            expected.Except(applied, StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToList(),
            applied.Except(expected, StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal).ToList(),
            apiReleaseVersion,
            apiContractRevision);
    }
}
