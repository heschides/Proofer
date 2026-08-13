using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Models.Assessments;
using System.Text.Json;

namespace Sati.Data;

public sealed class ComprehensiveAssessmentService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService)
    : IComprehensiveAssessmentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ComprehensiveAssessment> GetOrCreateDraftAsync(int personId, int authorUserId)
    {
        var actor = CurrentAuthor(authorUserId);
        await using var db = await contextFactory.CreateDbContextAsync();
        var canAuthor = await db.People.AsNoTracking().AnyAsync(person =>
            person.Id == personId && person.UserId == actor.Id &&
            person.AgencyId == actor.AgencyId);
        if (!canAuthor)
            throw new UnauthorizedAccessException("Only the assigned case manager may author this assessment.");
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
        LocalAuditTrail.Record(db, actor, LocalAuditActions.AssessmentCreated, "ComprehensiveAssessment");
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
        var actor = CurrentAuthor(assessment.AuthorUserId);
        await using var db = await contextFactory.CreateDbContextAsync();
        var stored = await db.ComprehensiveAssessments
            .Include(candidate => candidate.Person)
            .SingleAsync(candidate => candidate.Id == assessment.Id);
        EnsureCanAuthor(actor, stored);
        if (stored.Revision != assessment.Revision)
            throw new DbUpdateConcurrencyException("This assessment was changed by someone else. Reload it before saving.");
        if (stored.Status is AssessmentStatus.Approved or AssessmentStatus.Superseded)
            throw new InvalidOperationException("Approved assessment versions cannot be changed.");
        stored.DocumentJson = JsonSerializer.Serialize(document, JsonOptions);
        stored.UpdatedAt = DateTime.UtcNow;
        stored.Revision++;
        LocalAuditTrail.Record(
            db, actor, LocalAuditActions.AssessmentUpdated, "ComprehensiveAssessment", stored.Id);
        await db.SaveChangesAsync();
        assessment.DocumentJson = stored.DocumentJson;
        assessment.UpdatedAt = stored.UpdatedAt;
        assessment.Revision = stored.Revision;
    }

    public async Task SubmitForReviewAsync(ComprehensiveAssessment assessment)
    {
        var actor = CurrentAuthor(assessment.AuthorUserId);
        await using var db = await contextFactory.CreateDbContextAsync();
        var stored = await db.ComprehensiveAssessments
            .Include(candidate => candidate.Person)
            .SingleAsync(candidate => candidate.Id == assessment.Id);
        EnsureCanAuthor(actor, stored);
        if (stored.Revision != assessment.Revision)
            throw new DbUpdateConcurrencyException("This assessment was changed by someone else. Reload it before submitting.");
        if (stored.Status is not (AssessmentStatus.Draft or AssessmentStatus.Returned))
            throw new InvalidOperationException("This assessment is not editable.");
        stored.Status = AssessmentStatus.ReadyForReview;
        stored.SubmittedAt = DateTime.UtcNow;
        stored.UpdatedAt = stored.SubmittedAt.Value;
        stored.Revision++;
        LocalAuditTrail.Record(
            db, actor, LocalAuditActions.AssessmentSubmitted, "ComprehensiveAssessment", stored.Id);
        await db.SaveChangesAsync();
        assessment.Status = stored.Status;
        assessment.SubmittedAt = stored.SubmittedAt;
        assessment.UpdatedAt = stored.UpdatedAt;
        assessment.Revision = stored.Revision;
    }

    private User CurrentAuthor(int requestedAuthorUserId)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");
        if (actor.Role != UserRole.CaseManager || actor.Id != requestedAuthorUserId)
            throw new UnauthorizedAccessException("Only the signed-in case manager may author an assessment.");
        return actor;
    }

    private static void EnsureCanAuthor(User actor, ComprehensiveAssessment assessment)
    {
        if (assessment.AuthorUserId != actor.Id ||
            assessment.Person.UserId != actor.Id ||
            assessment.Person.AgencyId != actor.AgencyId)
        {
            throw new UnauthorizedAccessException("Only the assigned case manager may change this assessment.");
        }
    }
}
