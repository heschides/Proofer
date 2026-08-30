namespace Sati.Contracts.V1;

/// <summary>
/// Where a submitted batch stands, from the point of view of whoever has to work it.
/// </summary>
public enum BillingSubmissionProgress
{
    /// <summary>
    /// Claim lines have been promoted into a billing period, but that period has no
    /// submission activity yet. The period still needs to be submitted and sent.
    /// </summary>
    NotSubmitted,

    /// <summary>
    /// Something went wrong and a person has to act: the file failed to send, the
    /// clearinghouse rejected its syntax, or the payer rejected some or all of the claims.
    /// </summary>
    NeedsAttention,

    /// <summary>
    /// Sent and accepted so far, with nothing to do but wait for the payer. Not a problem,
    /// and not finished either.
    /// </summary>
    AwaitingPayer,

    /// <summary>Payment has been reported against it.</summary>
    Settled
}

/// <summary>
/// Sole owner of what a <see cref="BillingSubmissionStage"/> means to a biller.
/// </summary>
/// <remarks>
/// <para>
/// The submission list previously answered this with
/// <c>Stage != Reconciled</c>, and nothing in Sati has ever set <c>Reconciled</c>. Every
/// batch was therefore outstanding forever, the "outstanding only" filter excluded
/// nothing, and the column that claimed to distinguish them was true on every row. The
/// screen showed a distinction that did not exist.
/// </para>
/// <para>
/// Four states rather than a finished/unfinished flag, because "not finished" covers
/// three situations a biller treats differently: work that has not been submitted, a
/// rejection that needs correction, and an accepted claim that needs only patience.
/// Sorting those together is what makes a queue ambiguous.
/// </para>
/// <para>
/// <c>Reconciled</c> is included in <see cref="BillingSubmissionProgress.Settled"/> for
/// when something sets it, but nothing does yet: reconciliation means an EFT amount has
/// been matched against a deposit, and no code performs that match. Until then a paid
/// batch settles at <c>Paid</c>, which is the furthest the system can honestly claim.
/// </para>
/// </remarks>
public static class BillingSubmissionProgressRules
{
    public static BillingSubmissionProgress Classify(BillingSubmissionStage stage) => stage switch
    {
        BillingSubmissionStage.TransportFailed
            or BillingSubmissionStage.FunctionalRejected
            or BillingSubmissionStage.ClaimRejected
            or BillingSubmissionStage.PartiallyAccepted => BillingSubmissionProgress.NeedsAttention,

        BillingSubmissionStage.Paid
            or BillingSubmissionStage.Reconciled => BillingSubmissionProgress.Settled,

        // Generated, Transmitted, FunctionalAccepted, ClaimAccepted.
        _ => BillingSubmissionProgress.AwaitingPayer
    };

    /// <summary>
    /// Parses the stage name the API returns. An unrecognised value is treated as needing
    /// attention rather than as settled: a batch nobody can classify is exactly the one a
    /// person should look at, and defaulting the other way would hide it.
    /// </summary>
    public static BillingSubmissionProgress Classify(string? stageName) =>
        Enum.TryParse<BillingSubmissionStage>(stageName, out var stage)
            ? Classify(stage)
            : BillingSubmissionProgress.NeedsAttention;

    /// <summary>The heading a group of batches sits under.</summary>
    public static string Describe(BillingSubmissionProgress progress) => progress switch
    {
        BillingSubmissionProgress.NotSubmitted => "Not submitted",
        BillingSubmissionProgress.NeedsAttention => "Needs attention",
        BillingSubmissionProgress.AwaitingPayer => "Awaiting payer",
        _ => "Settled"
    };

    /// <summary>
    /// What the date column means for a batch in this state, so one column can be labelled
    /// honestly instead of showing a timestamp whose meaning changes by row.
    /// </summary>
    public static string DescribeActivity(BillingSubmissionProgress progress) => progress switch
    {
        BillingSubmissionProgress.NotSubmitted => "Oldest service date",
        BillingSubmissionProgress.NeedsAttention => "Waiting since",
        BillingSubmissionProgress.AwaitingPayer => "Sent",
        _ => "Paid"
    };

    /// <summary>
    /// Sort weight. Work not yet submitted first, then failed work, then what is merely
    /// waiting, then what is done.
    /// </summary>
    public static int SortOrder(BillingSubmissionProgress progress) => progress switch
    {
        BillingSubmissionProgress.NotSubmitted => 0,
        BillingSubmissionProgress.NeedsAttention => 1,
        BillingSubmissionProgress.AwaitingPayer => 2,
        _ => 3
    };

    /// <summary>
    /// Whether the oldest row in this group belongs at the top. Something stuck for three
    /// weeks is more urgent than something stuck for a day, but the most recent payment is
    /// the more interesting one to see first.
    /// </summary>
    public static bool OldestFirst(BillingSubmissionProgress progress) =>
        progress != BillingSubmissionProgress.Settled;
}
