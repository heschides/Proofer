using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// What a submission stage means to whoever has to work it.
///
/// The submission list used to answer this with <c>Stage != Reconciled</c>, and nothing in
/// Sati has ever set <c>Reconciled</c>. Every batch was outstanding forever, the
/// "outstanding only" filter excluded nothing, and the column claiming to distinguish them
/// was true on every row.
/// </summary>
public sealed class BillingSubmissionProgressTests
{
    /// <summary>
    /// A period holding claim lines but no submission events is not a failed submission.
    /// It needs its own state, created by the submissions view model from the absence of
    /// events rather than returned by stage classification.
    /// </summary>
    [Fact]
    public void NotSubmittedExistsAboveEveryEventBasedStateButNoStageClassifiesToIt()
    {
        Assert.True(Enum.TryParse("NotSubmitted", out BillingSubmissionProgress notSubmitted));
        Assert.Equal(0, BillingSubmissionProgressRules.SortOrder(notSubmitted));
        Assert.Equal("Not submitted", BillingSubmissionProgressRules.Describe(notSubmitted));
        Assert.Equal("Oldest service date", BillingSubmissionProgressRules.DescribeActivity(notSubmitted));
        Assert.True(BillingSubmissionProgressRules.OldestFirst(notSubmitted));

        Assert.All(Enum.GetValues<BillingSubmissionStage>(), stage =>
            Assert.NotEqual(notSubmitted, BillingSubmissionProgressRules.Classify(stage)));
    }

    [Theory]
    [InlineData(BillingSubmissionStage.TransportFailed)]
    [InlineData(BillingSubmissionStage.FunctionalRejected)]
    [InlineData(BillingSubmissionStage.ClaimRejected)]
    [InlineData(BillingSubmissionStage.PartiallyAccepted)]
    public void AnythingThatFailedNeedsAttention(BillingSubmissionStage stage) =>
        Assert.Equal(BillingSubmissionProgress.NeedsAttention,
            BillingSubmissionProgressRules.Classify(stage));

    /// <summary>
    /// A partial acceptance needs work even though most of it succeeded. The rejected
    /// claims will never be paid unless somebody corrects and resends them, and grouping
    /// this with "accepted" is how that work goes missing.
    /// </summary>
    [Fact]
    public void PartialAcceptanceIsWorkRatherThanProgress() =>
        Assert.Equal(BillingSubmissionProgress.NeedsAttention,
            BillingSubmissionProgressRules.Classify(BillingSubmissionStage.PartiallyAccepted));

    [Theory]
    [InlineData(BillingSubmissionStage.Generated)]
    [InlineData(BillingSubmissionStage.Transmitted)]
    [InlineData(BillingSubmissionStage.FunctionalAccepted)]
    [InlineData(BillingSubmissionStage.ClaimAccepted)]
    public void AcceptedSoFarIsWaitingRatherThanFinished(BillingSubmissionStage stage) =>
        Assert.Equal(BillingSubmissionProgress.AwaitingPayer,
            BillingSubmissionProgressRules.Classify(stage));

    [Theory]
    [InlineData(BillingSubmissionStage.Paid)]
    [InlineData(BillingSubmissionStage.Reconciled)]
    public void PaymentSettlesABatch(BillingSubmissionStage stage) =>
        Assert.Equal(BillingSubmissionProgress.Settled,
            BillingSubmissionProgressRules.Classify(stage));

    /// <summary>
    /// The bug this replaced. Paid must settle, or "hide settled" hides nothing and the
    /// list can never distinguish finished work from outstanding work.
    /// </summary>
    [Fact]
    public void PaidSettlesEvenThoughNothingEverSetsReconciled()
    {
        Assert.Equal(BillingSubmissionProgress.Settled,
            BillingSubmissionProgressRules.Classify(BillingSubmissionStage.Paid));
        Assert.NotEqual(BillingSubmissionProgress.Settled,
            BillingSubmissionProgressRules.Classify(BillingSubmissionStage.Transmitted));
    }

    /// <summary>
    /// A stage nobody can classify is exactly the one a person should look at. Defaulting
    /// an unknown value to settled would hide it from every queue.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("SomethingNewFromThePayer")]
    public void AnUnrecognisedStageNeedsAttentionRatherThanBeingHidden(string? stageName) =>
        Assert.Equal(BillingSubmissionProgress.NeedsAttention,
            BillingSubmissionProgressRules.Classify(stageName));

    [Fact]
    public void TheStageNameTheApiReturnsRoundTrips() =>
        Assert.Equal(BillingSubmissionProgress.Settled,
            BillingSubmissionProgressRules.Classify(nameof(BillingSubmissionStage.Paid)));

    /// <summary>
    /// Unsubmitted work sorts above failed work, waiting, and done. This is the ordering
    /// the list relies on, so it is pinned rather than left to enum declaration order.
    /// </summary>
    [Fact]
    public void WorkSortsAboveWaitingAndWaitingAboveDone()
    {
        var order = new[]
            {
                BillingSubmissionProgress.Settled,
                BillingSubmissionProgress.NeedsAttention,
                BillingSubmissionProgress.NotSubmitted,
                BillingSubmissionProgress.AwaitingPayer
            }
            .OrderBy(BillingSubmissionProgressRules.SortOrder)
            .ToList();

        Assert.Equal(
            [
                BillingSubmissionProgress.NotSubmitted,
                BillingSubmissionProgress.NeedsAttention,
                BillingSubmissionProgress.AwaitingPayer,
                BillingSubmissionProgress.Settled
            ],
            order);
    }

    /// <summary>
    /// Age means urgency for anything unfinished, so the oldest goes on top. For settled
    /// work the most recent payment is the interesting one.
    /// </summary>
    [Fact]
    public void OldestFirstEverywhereExceptSettled()
    {
        Assert.True(BillingSubmissionProgressRules.OldestFirst(BillingSubmissionProgress.NotSubmitted));
        Assert.True(BillingSubmissionProgressRules.OldestFirst(BillingSubmissionProgress.NeedsAttention));
        Assert.True(BillingSubmissionProgressRules.OldestFirst(BillingSubmissionProgress.AwaitingPayer));
        Assert.False(BillingSubmissionProgressRules.OldestFirst(BillingSubmissionProgress.Settled));
    }

    /// <summary>
    /// One timestamp column means different things by row, so each row can say which.
    /// </summary>
    [Fact]
    public void EachStateNamesWhatItsActivityDateMeans()
    {
        Assert.Equal("Oldest service date",
            BillingSubmissionProgressRules.DescribeActivity(BillingSubmissionProgress.NotSubmitted));
        Assert.Equal("Waiting since",
            BillingSubmissionProgressRules.DescribeActivity(BillingSubmissionProgress.NeedsAttention));
        Assert.Equal("Sent",
            BillingSubmissionProgressRules.DescribeActivity(BillingSubmissionProgress.AwaitingPayer));
        Assert.Equal("Paid",
            BillingSubmissionProgressRules.DescribeActivity(BillingSubmissionProgress.Settled));
    }

    /// <summary>Every stage classifies. A new one must not fall through to a default.</summary>
    [Fact]
    public void EveryStageIsClassified()
    {
        foreach (var stage in Enum.GetValues<BillingSubmissionStage>())
        {
            var progress = BillingSubmissionProgressRules.Classify(stage);
            Assert.True(Enum.IsDefined(progress), $"{stage} did not classify to a defined progress.");
        }
    }
}
