using Sati.Contracts.V1;
using Xunit;

namespace Sati.Tests;

/// <summary>
/// The permitted moves are written out here independently of the implementation,
/// so this file is a specification rather than a restatement. Every ordered pair
/// of statuses is exercised, for the case manager, for the supervisor, and for
/// the overdue sweep.
/// </summary>
public sealed class NoteWorkflowTransitionTests
{
    private const int Scheduled = 0;
    private const int Pending = 1;
    private const int Logged = 2;
    private const int Held = 3;
    private const int Cancelled = 4;
    private const int Delayed = 5;
    private const int Approved = 6;
    private const int Returned = 7;
    private const int Abandoned = 8;
    private const int Blocked = 9;

    private static readonly int[] AllStatuses =
        [Scheduled, Pending, Logged, Held, Cancelled, Delayed, Approved, Returned, Abandoned, Blocked];

    /// <summary>Every status a case manager may assign.</summary>
    private static readonly int[] Authored =
        [Scheduled, Pending, Logged, Held, Cancelled, Delayed, Blocked];

    private static readonly Dictionary<int, int[]> ExpectedCaseManagerMoves = new()
    {
        // Work in progress moves freely among the statuses its author may assign.
        [Scheduled] = Authored,
        [Pending] = Authored,
        [Delayed] = Authored,
        [Held] = Authored,
        [Blocked] = Authored,

        // A returned note is re-dispositioned freely too, but cannot be saved back
        // as Returned: that status is the supervisor's word, not the author's.
        [Returned] = Authored,

        // Closed work reopens as a draft before it can be submitted again.
        [Cancelled] = [Pending, Cancelled],
        [Abandoned] = [Pending],

        // Under review, and final. Neither is editable by its author at all.
        [Logged] = [],
        [Approved] = []
    };

    private static readonly Dictionary<int, int[]> ExpectedSupervisorMoves = new()
    {
        [Logged] = [Approved, Returned]
    };

    [Fact]
    public void EveryCaseManagerTransitionMatchesTheSpecification()
    {
        foreach (var current in AllStatuses)
        {
            foreach (var target in AllStatuses)
            {
                var expected = ExpectedCaseManagerMoves[current].Contains(target);
                var actual = NoteWorkflow.CanCaseManagerTransition(current, target);
                Assert.True(expected == actual,
                    $"{NoteWorkflow.StatusName(current)} -> {NoteWorkflow.StatusName(target)}: " +
                    $"expected {expected}, was {actual}.");
            }
        }
    }

    [Fact]
    public void EverySupervisorTransitionMatchesTheSpecification()
    {
        foreach (var current in AllStatuses)
        {
            foreach (var target in AllStatuses)
            {
                var expected = ExpectedSupervisorMoves.TryGetValue(current, out var allowed) &&
                    allowed.Contains(target);
                var actual = NoteWorkflow.CanSupervisorTransition(current, target);
                Assert.True(expected == actual,
                    $"supervisor {NoteWorkflow.StatusName(current)} -> {NoteWorkflow.StatusName(target)}: " +
                    $"expected {expected}, was {actual}.");
            }
        }
    }

    [Fact]
    public void NoCaseManagerMoveReachesASupervisorOrSystemOwnedStatus()
    {
        foreach (var current in AllStatuses)
        {
            foreach (var target in new[] { Approved, Returned, Abandoned })
            {
                Assert.False(NoteWorkflow.CanCaseManagerTransition(current, target),
                    $"a case manager reached {NoteWorkflow.StatusName(target)} from " +
                    $"{NoteWorkflow.StatusName(current)}.");
            }
        }
    }

    [Fact]
    public void ApprovedIsTerminalForEveryActor()
    {
        foreach (var target in AllStatuses)
        {
            Assert.False(NoteWorkflow.CanCaseManagerTransition(Approved, target));
            Assert.False(NoteWorkflow.CanSupervisorTransition(Approved, target));
        }

        Assert.False(NoteWorkflow.CanCaseManagerEdit(Approved));
        Assert.False(NoteWorkflow.CanCaseManagerDelete(Approved));
    }

    [Fact]
    public void ApprovalIsReachableOnlyFromASubmittedNote()
    {
        foreach (var current in AllStatuses)
        {
            var reachable = NoteWorkflow.CanSupervisorTransition(current, Approved);
            Assert.Equal(current == Logged, reachable);
        }
    }

    [Fact]
    public void OnlyAnUnfinishedDraftAgesOut()
    {
        foreach (var current in AllStatuses)
            Assert.Equal(current == Pending, NoteWorkflow.CanSystemAbandon(current));

        Assert.False(NoteWorkflow.CanSystemAbandon(null));
    }

    [Fact]
    public void NoStatusIsADeadEnd()
    {
        // Every status a case manager can hold must be able to reach review again
        // within two moves. Strictness that traps a note is a defect, not a control.
        foreach (var current in AllStatuses)
        {
            if (current is Logged or Approved)
                continue;

            var direct = NoteWorkflow.CanCaseManagerTransition(current, Logged);
            var viaDraft = NoteWorkflow.CanCaseManagerTransition(current, Pending) &&
                NoteWorkflow.CanCaseManagerTransition(Pending, Logged);
            Assert.True(direct || viaDraft,
                $"{NoteWorkflow.StatusName(current)} cannot reach review again.");
        }
    }

    [Fact]
    public void EditingAndDeletionGuardsCoverEveryStatus()
    {
        foreach (var current in AllStatuses)
        {
            Assert.Equal(current is not (Logged or Approved), NoteWorkflow.CanCaseManagerEdit(current));
            Assert.Equal(
                current is Scheduled or Pending or Cancelled or Delayed,
                NoteWorkflow.CanCaseManagerDelete(current));
        }
    }

    [Fact]
    public void OnlyCaseManagerAuthoredStatusesAreWritable()
    {
        foreach (var status in AllStatuses)
        {
            Assert.Equal(
                status is not (Approved or Returned or Abandoned),
                NoteWorkflow.IsCaseManagerWritableStatus(status));
        }

        Assert.False(NoteWorkflow.IsCaseManagerWritableStatus(null));
        Assert.False(NoteWorkflow.IsCaseManagerWritableStatus(10));
        Assert.False(NoteWorkflow.IsCaseManagerWritableStatus(-1));
    }

    [Fact]
    public void AStatuslessLegacyNoteCanStillBeCorrected()
    {
        // Notes predating the status column must remain fixable, but only into a
        // status their author is allowed to assign.
        foreach (var target in AllStatuses)
        {
            Assert.Equal(
                NoteWorkflow.IsCaseManagerWritableStatus(target),
                NoteWorkflow.CanCaseManagerTransition(null, target));
        }

        Assert.True(NoteWorkflow.CanCaseManagerEdit(null));
        Assert.False(NoteWorkflow.CanCaseManagerTransition(null, null));
        Assert.False(NoteWorkflow.CanSupervisorTransition(null, Approved));
    }

    [Fact]
    public void AnUnknownStatusIsNeverWritableInEitherDirection()
    {
        foreach (var bogus in new int?[] { -1, 10, 99, int.MaxValue, int.MinValue })
        {
            Assert.False(NoteWorkflow.CanCaseManagerTransition(bogus, Logged));
            Assert.False(NoteWorkflow.CanCaseManagerTransition(Pending, bogus));
            Assert.False(NoteWorkflow.CanSupervisorTransition(bogus, Approved));
            Assert.False(NoteWorkflow.CanSupervisorTransition(Logged, bogus));
        }
    }

    [Fact]
    public void FutureAndCompletedDocumentsStayCompliantWheneverTheSuiteRuns()
    {
        // Future incomplete documents do not block, regardless of the date on
        // which this suite happens to run.
        var annualTypes = new[]
        {
            "PCP", "ComprehensiveAssessment", "Reclassification", "SafetyPlan"
        };

        foreach (var offsetDays in new[] { 0, 30, 137, 200, 400, 1_000, 3_650 })
        {
            var today = new DateTime(2026, 8, 17).AddDays(offsetDays);
            var cycleStart = today.AddMonths(-1);
            var forms = annualTypes.Select(type =>
                new ComplianceFormSnapshot(type, cycleStart.AddMonths(6), null));

            var result = BillingComplianceGate.Evaluate(cycleStart, forms, today);

            Assert.True(result.Passed,
                $"day {offsetDays}: {string.Join("; ", result.Reasons)}");
        }

        // A completed document also stays complete after a calendar boundary.
        var pinnedForms = annualTypes.Select(type =>
            new ComplianceFormSnapshot(type, new DateTime(2026, 12, 31), new DateTime(2026, 12, 30)));
        var afterTheBoundary = BillingComplianceGate.Evaluate(
            new DateTime(2026, 1, 1), pinnedForms, new DateTime(2027, 6, 1));
        Assert.True(afterTheBoundary.Passed);
    }

    [Fact]
    public void EveryRejectionExplainsItself()
    {
        foreach (var current in AllStatuses)
        {
            foreach (var target in AllStatuses)
            {
                if (NoteWorkflow.CanCaseManagerTransition(current, target))
                    continue;
                var message = NoteWorkflow.DescribeRejectedTransition(current, target);
                Assert.False(string.IsNullOrWhiteSpace(message));
                Assert.DoesNotContain("Unknown", message, StringComparison.Ordinal);
            }
        }
    }
}
