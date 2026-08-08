using Sati.Models.Assessments;

namespace Sati.Data;

public interface IComprehensiveAssessmentService
{
    Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId);
    Task SaveDocumentAsync(int assessmentId, AssessmentDocument document);
    Task SubmitForReviewAsync(int assessmentId, int authorUserId);
}
