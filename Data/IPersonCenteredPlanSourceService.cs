using Sati.Models.Assessments;

namespace Sati.Data;

public sealed record PersonCenteredPlanSource(
    int AssessmentId,
    int Version,
    AssessmentStatus Status,
    DateTime UpdatedAt,
    AssessmentDocument Document);

public interface IPersonCenteredPlanSourceService
{
    Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId);
}
