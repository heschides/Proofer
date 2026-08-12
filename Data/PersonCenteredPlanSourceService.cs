using Microsoft.EntityFrameworkCore;
using Sati.Models.Assessments;
using System.Text.Json;

namespace Sati.Data;

public sealed class PersonCenteredPlanSourceService(IDbContextFactory<SatiContext> contextFactory)
    : IPersonCenteredPlanSourceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<PersonCenteredPlanSource?> GetSourceAsync(int personId, int preferredAuthorUserId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        // An approved assessment is the authoritative PCP source. Until approval
        // exists, expose only the signed-in author's working version and label it
        // clearly as provisional in the PCP workspace.
        var assessment = await db.ComprehensiveAssessments
            .AsNoTracking()
            .Where(item => item.PersonId == personId && item.Status == AssessmentStatus.Approved)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync();

        assessment ??= await db.ComprehensiveAssessments
            .AsNoTracking()
            .Where(item => item.PersonId == personId && item.AuthorUserId == preferredAuthorUserId)
            .Where(item => item.Status == AssessmentStatus.Draft
                || item.Status == AssessmentStatus.Returned
                || item.Status == AssessmentStatus.ReadyForReview)
            .OrderByDescending(item => item.Version)
            .FirstOrDefaultAsync();

        if (assessment is null)
            return null;

        var document = JsonSerializer.Deserialize<AssessmentDocument>(assessment.DocumentJson, JsonOptions)
            ?? new AssessmentDocument();
        return new PersonCenteredPlanSource(
            assessment.Id,
            assessment.Version,
            assessment.Status,
            assessment.UpdatedAt,
            document);
    }
}
