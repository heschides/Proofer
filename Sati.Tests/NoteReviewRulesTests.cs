using Sati.Contracts.V1;
using Xunit;
namespace Sati.Tests;

public sealed class NoteReviewRulesTests
{
    [Theory]
    [InlineData("Contact", "Narrative", 60, 0, true)]
    [InlineData("Phone", "Narrative", 60, 0, true)]
    [InlineData("Email", "Narrative", 60, 0, true)]
    [InlineData("Contact", "Narrative", 61, 0, false)]
    [InlineData("Contact", " ", 15, 0, false)]
    [InlineData("Reminder", "Narrative", 15, 0, false)]
    [InlineData(null, "Narrative", 15, 0, false)]
    [InlineData("Visit", "Narrative", 15, 1, false)]
    [InlineData("Form", "Narrative", 0, 0, false)]
    public void AutomaticApprovalLeavesIncompleteOrOutOfThresholdNotesForReview(
        string? type, string narrative, int minutes, int futureDays, bool expected)
    {
        Assert.Equal(expected, NoteReviewRules.Eligible(4, NoteWorkflow.Logged, type,
            narrative, DateTime.Today.AddDays(futureDays), minutes, null, DateTime.Today));
    }
}
