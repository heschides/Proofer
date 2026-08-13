using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data;

public sealed class SupervisorService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : ISupervisorService
{
    public async Task<IEnumerable<Note>> GetPendingNotesAsync(
        int supervisorId,
        bool allSupervisees = false)
    {
        var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
        var today = DateTime.Today;
        return notes.Where(note => note.Person.EvaluateComplianceGate(today).Passed);
    }

    public async Task<IEnumerable<Note>> GetNonCompliantNotesAsync(
        int supervisorId,
        bool allSupervisees = false)
    {
        var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
        var today = DateTime.Today;
        return notes.Where(note => !note.Person.EvaluateComplianceGate(today).Passed);
    }

    public async Task ApproveNoteAsync(int noteId, int supervisorId, int expectedRevision)
    {
        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var note = await LoadReviewableNoteAsync(context, actor, noteId)
            ?? throw new InvalidOperationException($"Note {noteId} was not found in your review scope.");

        EnsureCurrentRevision(note, expectedRevision);
        if (note.Status != NoteStatus.Logged)
            throw new InvalidOperationException("Only logged notes can be approved.");

        var (passed, reasons) = note.Person.EvaluateComplianceGate(DateTime.Today);
        if (!passed)
        {
            throw new InvalidOperationException(
                $"Cannot approve note {noteId}: {note.Person.FullName} does not meet " +
                $"compliance requirements. Failures: {string.Join("; ", reasons)}. " +
                "Use ApproveWithOverrideAsync if a supervisor exception is warranted.");
        }

        note.Status = NoteStatus.Approved;
        note.ApprovedById = actor.Id;
        note.ApprovedAt = DateTime.UtcNow;
        note.Revision++;
        await SaveNoteTransitionAsync(
            context, actor, LocalAuditActions.NoteApproved, noteId);
    }

    public async Task ApproveWithOverrideAsync(
        int noteId,
        int supervisorId,
        string overrideReason,
        int expectedRevision)
    {
        var reason = overrideReason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 4_000)
            throw new ArgumentException("Override reason is required and must not exceed 4,000 characters.", nameof(overrideReason));

        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var note = await LoadReviewableNoteAsync(context, actor, noteId)
            ?? throw new InvalidOperationException($"Note {noteId} was not found in your review scope.");

        EnsureCurrentRevision(note, expectedRevision);
        if (note.Status != NoteStatus.Logged)
            throw new InvalidOperationException("Only logged notes can be approved.");

        var now = DateTime.UtcNow;
        note.Status = NoteStatus.Approved;
        note.ApprovedById = actor.Id;
        note.ApprovedAt = now;
        note.ComplianceOverride = true;
        note.OverrideReason = reason;
        note.OverrideApprovedById = actor.Id;
        note.OverrideApprovedAt = now;
        note.Revision++;
        await SaveNoteTransitionAsync(
            context, actor, LocalAuditActions.NoteApprovalOverridden, noteId);
    }

    public async Task ReturnNoteAsync(
        int noteId,
        int supervisorId,
        string reason,
        int expectedRevision)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 1 or > 4_000)
            throw new ArgumentException("A return reason is required and must not exceed 4,000 characters.", nameof(reason));

        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var note = await LoadReviewableNoteAsync(context, actor, noteId)
            ?? throw new InvalidOperationException($"Note {noteId} was not found in your review scope.");

        EnsureCurrentRevision(note, expectedRevision);
        if (note.Status != NoteStatus.Logged)
            throw new InvalidOperationException("Only logged notes can be returned.");

        note.Status = NoteStatus.Returned;
        note.ReturnedById = actor.Id;
        note.ReturnReason = reason;
        note.ReturnedAt = DateTime.UtcNow;
        note.Revision++;
        await SaveNoteTransitionAsync(
            context, actor, LocalAuditActions.NoteReturned, noteId);
    }

    private async Task<List<Note>> GetLoggedNotesAsync(int supervisorId, bool allSupervisees)
    {
        _ = allSupervisees;
        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var canReviewAgency = actor.Role is UserRole.Director or UserRole.Admin;
        var caseManagerIds = await context.Users.AsNoTracking()
            .Where(user => user.AgencyId == actor.AgencyId &&
                user.Role == UserRole.CaseManager &&
                (canReviewAgency || user.SupervisorId == actor.Id))
            .Select(user => user.Id)
            .ToListAsync();

        return await context.Notes.AsNoTracking()
            .Include(note => note.Person)
                .ThenInclude(person => person.Forms)
            .Where(note => note.Status == NoteStatus.Logged &&
                note.Person.AgencyId == actor.AgencyId &&
                caseManagerIds.Contains(note.Person.UserId))
            .OrderBy(note => note.EventDate)
            .ToListAsync();
    }

    private static Task<Note?> LoadReviewableNoteAsync(
        SatiContext context,
        User actor,
        int noteId)
    {
        var canReviewAgency = actor.Role is UserRole.Director or UserRole.Admin;
        return context.Notes
            .Include(note => note.Person)
                .ThenInclude(person => person.Forms)
            .SingleOrDefaultAsync(note =>
                note.Id == noteId &&
                note.Person.AgencyId == actor.AgencyId &&
                context.Users.Any(user =>
                    user.Id == note.Person.UserId &&
                    user.AgencyId == actor.AgencyId &&
                    user.Role == UserRole.CaseManager &&
                    (canReviewAgency || user.SupervisorId == actor.Id)));
    }

    private User CurrentReviewer(int requestedReviewerId)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in reviewer is required.");
        if (actor.Id != requestedReviewerId ||
            actor.Role is not (UserRole.Supervisor or UserRole.Director or UserRole.Admin))
        {
            throw new UnauthorizedAccessException("Only the signed-in reviewer may perform this action.");
        }
        return actor;
    }

    private static void EnsureCurrentRevision(Note note, int expectedRevision)
    {
        if (note.Revision != expectedRevision)
            throw new NoteConcurrencyException();
    }

    private static async Task SaveNoteTransitionAsync(
        SatiContext context,
        User actor,
        string action,
        int noteId)
    {
        LocalAuditTrail.Record(context, actor, action, "Note", noteId);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new NoteConcurrencyException(ex);
        }
    }
}
