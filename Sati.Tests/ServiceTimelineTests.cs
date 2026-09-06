using Sati.Contracts.V1;
using Sati.ViewModels.Children;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The service-day rule: one case manager, one calendar date, no minute claimed
/// twice. These cover the shared owner both the desktop client and the API call.
/// </summary>
public sealed class ServiceTimelineTests
{
    // Offsets are minutes from the 7:00 AM window start.
    private const int NineAm = 120;
    private const int NineFifteen = 135;

    [Fact]
    public void WindowRunsFromSevenAmToSevenPm()
    {
        Assert.Equal("7:00 AM", ServiceTimeline.Describe(0));
        Assert.Equal("12:00 PM", ServiceTimeline.Describe(300));
        Assert.Equal("7:00 PM", ServiceTimeline.Describe(ServiceTimeline.WindowLengthMinutes));
        Assert.Equal(720, ServiceTimeline.WindowLengthMinutes);
    }

    [Fact]
    public void PartiallyOverlappingBlocksConflict()
    {
        var recorded = new ServiceBlock(1, NineAm, 15);        // 9:00 – 9:15
        var candidate = new ServiceBlock(2, NineAm + 10, 10);  // 9:10 – 9:20

        Assert.True(ServiceTimeline.Overlaps(candidate, recorded));
        Assert.Single(ServiceTimeline.FindConflicts(candidate, [recorded]));
    }

    [Fact]
    public void ContainedBlockConflicts()
    {
        var recorded = new ServiceBlock(1, NineAm, 60);
        var candidate = new ServiceBlock(2, NineAm + 10, 5);

        Assert.Single(ServiceTimeline.FindConflicts(candidate, [recorded]));
    }

    [Fact]
    public void BackToBackBlocksDoNotConflict()
    {
        var recorded = new ServiceBlock(1, NineAm, 15);        // ends 9:15
        var candidate = new ServiceBlock(2, NineFifteen, 15);  // starts 9:15

        Assert.False(ServiceTimeline.Overlaps(candidate, recorded));
        Assert.Empty(ServiceTimeline.FindConflicts(candidate, [recorded]));
    }

    [Fact]
    public void ANoteNeverConflictsWithItself()
    {
        var recorded = new ServiceBlock(7, NineAm, 30);
        var candidate = new ServiceBlock(7, NineAm, 45);

        Assert.Empty(ServiceTimeline.FindConflicts(candidate, [recorded]));
    }

    [Fact]
    public void EarliestAvailableStartUsesTheFirstGapLongEnoughForTheWork()
    {
        var occupied = new[]
        {
            new ServiceBlock(1, 0, 30),
            new ServiceBlock(2, 45, 30)
        };

        Assert.Equal(30, ServiceTimeline.FindEarliestAvailableStart(15, occupied));
        Assert.Equal(75, ServiceTimeline.FindEarliestAvailableStart(30, occupied));
    }

    [Fact]
    public void EarliestAvailableStartCanIgnoreTheScheduledNoteBeingContinued()
    {
        var scheduled = new ServiceBlock(7, 0, 45);

        Assert.Equal(0, ServiceTimeline.FindEarliestAvailableStart(45, [scheduled], excludedNoteId: 7));
    }

    [Fact]
    public void EarliestAvailableStartReturnsNullWhenNoWindowFits()
    {
        var wholeDay = new ServiceBlock(1, 0, ServiceTimeline.WindowLengthMinutes);

        Assert.Null(ServiceTimeline.FindEarliestAvailableStart(15, [wholeDay]));
        Assert.Null(ServiceTimeline.FindEarliestAvailableStart(0, []));
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("Delayed")]
    [InlineData("Abandoned")]
    public void StatusesThatEndTheServiceReleaseTheirTime(string status)
    {
        Assert.False(ServiceTimeline.OccupiesTime(status));
        Assert.Null(ServiceTimeline.TryCreateBlock(1, NineAm, 15, status));
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("Pending")]
    [InlineData("Logged")]
    [InlineData("Approved")]
    [InlineData("Returned")]
    [InlineData("HeldForCompliance")]
    [InlineData("ComplianceBlocked")]
    [InlineData(null)]
    public void CommittedAndInProgressStatusesHoldTheirTime(string? status)
    {
        Assert.True(ServiceTimeline.OccupiesTime(status));
        Assert.NotNull(ServiceTimeline.TryCreateBlock(1, NineAm, 15, status));
    }

    [Fact]
    public void NotesWithoutATimeOrDurationClaimNothing()
    {
        Assert.Null(ServiceTimeline.TryCreateBlock(1, null, 15, "Logged"));
        Assert.Null(ServiceTimeline.TryCreateBlock(1, NineAm, null, "Logged"));
        Assert.Null(ServiceTimeline.TryCreateBlock(1, NineAm, 0, "Logged"));
    }

    [Fact]
    public void ServiceRunningPastSevenPmIsRejected()
    {
        // 6:30 PM plus an hour.
        Assert.NotNull(ServiceTimeline.DescribeWindowViolation(690, 60));
        Assert.Null(ServiceTimeline.DescribeWindowViolation(690, 30));
    }

    [Fact]
    public void ConflictReasonNamesTheTimeAndTheOtherNote()
    {
        var recorded = new ServiceBlock(1, NineAm, 15, "a note for Jane Doe");
        var candidate = new ServiceBlock(2, NineAm + 5, 15);

        var conflict = Assert.Single(ServiceTimeline.FindConflicts(candidate, [recorded]));

        Assert.Contains("9:00 AM", conflict.Reason);
        Assert.Contains("9:15 AM", conflict.Reason);
        Assert.Contains("Jane Doe", conflict.Reason);
    }

    [Fact]
    public void StartOptionsCoverTheWholeDayOnTheSlotGrid()
    {
        var options = ServiceStartOption.BuildDay();

        Assert.Equal(ServiceTimeline.WindowLengthMinutes / ServiceTimeline.SlotMinutes, options.Count);
        Assert.Equal(0, options[0].Minutes);
        Assert.Equal("7:00 AM", options[0].Display);
        Assert.Equal("6:55 PM", options[^1].Display);
    }

    [Fact]
    public void SegmentsArePositionedAsFractionsOfTheDay()
    {
        // 1:00 PM for one hour: six hours in, one hour of twelve wide.
        var segment = ServiceDaySegment.FromBlock(
            new ServiceBlock(1, 360, 60), ServiceDaySegmentKind.Recorded);

        Assert.Equal(0.5, segment.StartFraction, 3);
        Assert.Equal(1d / 12d, segment.WidthFraction, 3);
        Assert.StartsWith("Already recorded:", segment.Description);
    }

    [Fact]
    public void ASegmentRunningPastTheDayIsClampedToTheTrack()
    {
        var segment = ServiceDaySegment.FromBlock(
            new ServiceBlock(1, 690, 120), ServiceDaySegmentKind.Conflict);

        Assert.True(segment.StartFraction + segment.WidthFraction <= 1d);
        Assert.StartsWith("Overlapping time:", segment.Description);
    }
}
