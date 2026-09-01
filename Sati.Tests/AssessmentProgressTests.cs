using Sati.Models.Assessments;
using Sati.ViewModels.ClientDocuments;
using Xunit;

namespace Sati.Tests;

public sealed class AssessmentProgressTests
{
    [Fact]
    public void EmptyAssessmentUsesTheEditorsFullQuestionCount()
    {
        var progress = ComprehensiveAssessmentViewModel.CalculateProgress(
            new AssessmentDocument());

        Assert.Equal(0, progress.AnsweredCount);
        Assert.Equal(25, progress.TotalCount);
        Assert.Equal("0 of 25 questions addressed", progress.Text);
    }

    [Fact]
    public void AgendaProgressUsesTheEditorsAddressedRules()
    {
        var document = new AssessmentDocument
        {
            Answers = new Dictionary<string, AssessmentAnswer>
            {
                ["self-view"] = new()
                {
                    Status = AssessmentAnswerStatus.Answered,
                    Narrative = "The person described their priorities."
                },
                ["communication-assessment-received"] = new()
                {
                    Status = AssessmentAnswerStatus.Answered,
                    YesNoResponse = true
                },
                ["relationships"] = new()
                {
                    Status = AssessmentAnswerStatus.NotApplicable,
                    ExceptionReason = "The person declined this topic."
                },
                ["dissent"] = new()
                {
                    Status = AssessmentAnswerStatus.FollowUpRequired,
                    ExceptionReason = "Follow-up remains necessary."
                }
            }
        };

        var progress = ComprehensiveAssessmentViewModel.CalculateProgress(document);

        Assert.Equal(3, progress.AnsweredCount);
        Assert.Equal(25, progress.TotalCount);
    }
}
