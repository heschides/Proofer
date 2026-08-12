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
        var editableAssessment = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId && a.AuthorUserId == authorUserId)
            .Where(a => a.Status == AssessmentStatus.Draft || a.Status == AssessmentStatus.Returned)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync();

        var latestApproved = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId && a.Status == AssessmentStatus.Approved)
            .OrderByDescending(a => a.Version)
            .FirstOrDefaultAsync();

        // A draft can predate an imported or newly approved assessment. Keep any
        // real author work, but retire an empty stale shell so it cannot hide the
        // newer approved content forever.
        if (editableAssessment is not null)
        {
            var isStaleEmptyDraft = latestApproved is not null
                && editableAssessment.Version <= latestApproved.Version
                && !HasDocumentContent(editableAssessment.DocumentJson);
            if (!isStaleEmptyDraft) return editableAssessment;

            editableAssessment.Status = AssessmentStatus.Superseded;
            editableAssessment.UpdatedAt = DateTime.UtcNow;
        }

        var latestVersion = await db.ComprehensiveAssessments
            .Where(a => a.PersonId == personId)
            .Select(a => (int?)a.Version).MaxAsync() ?? 0;
        var assessment = new ComprehensiveAssessment
        {
            PersonId = personId, AuthorUserId = authorUserId, Version = latestVersion + 1,
            DocumentJson = latestApproved?.DocumentJson
                ?? JsonSerializer.Serialize(new AssessmentDocument(), JsonOptions)
        };
        db.ComprehensiveAssessments.Add(assessment);
        await db.SaveChangesAsync();
        return assessment;
    }

    private static bool HasDocumentContent(string documentJson)
    {
        try
        {
            var document = JsonSerializer.Deserialize<AssessmentDocument>(documentJson, JsonOptions);
            return document is not null
                && (document.Contributors.Count > 0
                    || document.Needs.Count > 0
                    || document.Answers.Values.Any(HasAnswerContent));
        }
        catch (JsonException)
        {
            // Never discard an unparseable draft automatically; it may contain
            // recoverable author work that needs manual review.
            return true;
        }
    }

    private static bool HasAnswerContent(AssessmentAnswer answer) =>
        answer.Status != AssessmentAnswerStatus.NotYetAnswered
        || !string.IsNullOrWhiteSpace(answer.Narrative)
        || answer.Supports != SupportMethod.None
        || !string.IsNullOrWhiteSpace(answer.SupportDetails)
        || !string.IsNullOrWhiteSpace(answer.ExceptionReason)
        || !string.IsNullOrWhiteSpace(answer.DissentingOpinion)
        || answer.YesNoResponse.HasValue
        || answer.FollowUpYesNoResponse.HasValue
        || !string.IsNullOrWhiteSpace(answer.Details)
        || answer.TherapySessionFormat != TherapySessionFormat.NotSelected
        || answer.WantsOtherSessionFormat.HasValue
        || answer.WantsFrequencyChange.HasValue
        || answer.TherapyFrequencyDirection != TherapyFrequencyDirection.NotSelected
        || answer.ActivitySupportLevels.Values.Any(level => level != ActivitySupportLevel.Independent)
        || answer.ActivitySkillsTraining.Values.Any(selected => selected);

    public async Task SaveDocumentAsync(
        ComprehensiveAssessment assessment,
        AssessmentDocument document)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var stored = await db.ComprehensiveAssessments.SingleAsync(a => a.Id == assessment.Id);
        if (stored.Revision != assessment.Revision)
            throw new DbUpdateConcurrencyException("This assessment was changed by someone else. Reload it before saving.");
        if (stored.Status is AssessmentStatus.Approved or AssessmentStatus.Superseded)
            throw new InvalidOperationException("Approved assessment versions cannot be changed.");
        stored.DocumentJson = JsonSerializer.Serialize(document, JsonOptions);
        stored.UpdatedAt = DateTime.UtcNow;
        stored.Revision++;
        await db.SaveChangesAsync();
        assessment.DocumentJson = stored.DocumentJson;
        assessment.UpdatedAt = stored.UpdatedAt;
        assessment.Revision = stored.Revision;
    }

    public async Task SubmitForReviewAsync(ComprehensiveAssessment assessment)
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var stored = await db.ComprehensiveAssessments.SingleAsync(a => a.Id == assessment.Id);
        if (stored.Revision != assessment.Revision)
            throw new DbUpdateConcurrencyException("This assessment was changed by someone else. Reload it before submitting.");
        if (stored.AuthorUserId != assessment.AuthorUserId)
            throw new InvalidOperationException("Only the assigned author may submit this assessment.");
        if (stored.Status is not (AssessmentStatus.Draft or AssessmentStatus.Returned))
            throw new InvalidOperationException("This assessment is not editable.");
        stored.Status = AssessmentStatus.ReadyForReview;
        stored.SubmittedAt = DateTime.UtcNow;
        stored.UpdatedAt = stored.SubmittedAt.Value;
        stored.Revision++;
        await db.SaveChangesAsync();
        assessment.Status = stored.Status;
        assessment.SubmittedAt = stored.SubmittedAt;
        assessment.UpdatedAt = stored.UpdatedAt;
        assessment.Revision = stored.Revision;
    }
}
