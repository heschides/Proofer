using Sati.Models.Assessments;

namespace Sati.Data;

public interface IComprehensiveAssessmentService
{
    Task<ComprehensiveAssessment?> GetLatestForAgendaAsync(int personId);
    Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId);
    Task SaveDocumentAsync(ComprehensiveAssessment assessment, AssessmentDocument document);
    Task SubmitForReviewAsync(ComprehensiveAssessment assessment);
}
