using Microsoft.EntityFrameworkCore;
using Sati.Models.Assessments;
using System.Text.Json;

namespace Sati.Data;

public sealed class ComprehensiveAssessmentService(IDbContextFactory<SatiContext> contextFactory)
    : IComprehensiveAssessmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var assessment = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId && a.AuthorUserId == authorUserId)
            .Where(a => a.Status == AssessmentStatus.Draft || a.Status == AssessmentStatus.Returned)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync();
        if (assessment is not null) return assessment;

        var latestVersion = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId)
            .Select(a => (int?)a.Version).MaxAsync() ?? 0;
        assessment = new ComprehensiveAssessment
        {
            PersonId = personId, AuthorUserId = authorUserId, Version = latestVersion + 1,
            DocumentJson = JsonSerializer.Serialize(new AssessmentDocument(), JsonOptions)
        };
        db.ComprehensiveAssessments.Add(assessment);
        await db.SaveChangesAsync();
        return assessment;
    }

    public async Task SaveDocumentAsync(int assessmentId, AssessmentDocument document)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var assessment = await db.ComprehensiveAssessments.SingleAsync(a => a.Id == assessmentId);
        if (assessment.Status is AssessmentStatus.Approved or AssessmentStatus.Superseded)
            throw new InvalidOperationException("Approved assessment versions cannot be changed.");
        assessment.DocumentJson = JsonSerializer.Serialize(document, JsonOptions);
        assessment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SubmitForReviewAsync(int assessmentId, int authorUserId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var assessment = await db.ComprehensiveAssessments.SingleAsync(a => a.Id == assessmentId);
        if (assessment.AuthorUserId != authorUserId)
            throw new InvalidOperationException("Only the assigned author may submit this assessment.");
        if (assessment.Status is not (AssessmentStatus.Draft or AssessmentStatus.Returned))
            throw new InvalidOperationException("This assessment is not editable.");
        assessment.Status = AssessmentStatus.ReadyForReview;
        assessment.SubmittedAt = DateTime.UtcNow;
        assessment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
