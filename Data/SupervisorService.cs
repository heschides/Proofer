using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
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
        var requirements = await LoadComplianceRequirementsAsync();
        return notes.Where(note => note.Person.EvaluateComplianceGate(
            today, requirements: requirements).Passed);
    }

    public async Task<IEnumerable<Note>> GetNonCompliantNotesAsync(
        int supervisorId,
        bool allSupervisees = false)
    {
        var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
        var today = DateTime.Today;
        var requirements = await LoadComplianceRequirementsAsync();
        var nonCompliant = new List<Note>();
        foreach (var note in notes)
        {
            var result = note.Person.EvaluateComplianceGate(
                today, requirements: requirements);
            if (result.Passed)
                continue;
            note.ComplianceFailureReasons = result.Reasons;
            nonCompliant.Add(note);
        }
        return nonCompliant;
    }

    public async Task ApproveNoteAsync(int noteId, int supervisorId, int expectedRevision, int? maximumUnits = null)
    {
        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var note = await LoadReviewableNoteAsync(context, actor, noteId)
            ?? throw new InvalidOperationException($"Note {noteId} was not found in your review scope.");

        EnsureCurrentRevision(note, expectedRevision);
        if (!NoteWorkflow.CanSupervisorTransition((int?)note.Status, NoteWorkflow.Approved))
            throw new InvalidOperationException("Only logged notes can be approved.");

        var requirements = await context.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == actor.AgencyId)
            .Select(settings => (BillingComplianceRequirements?)settings.BillingComplianceRequirements)
            .SingleOrDefaultAsync() ?? BillingComplianceGate.DefaultRequirements;
        var (passed, reasons) = note.Person.EvaluateComplianceGate(
            DateTime.Today, requirements: requirements);
        if (!passed)
        {
            throw new InvalidOperationException(
                $"Cannot approve note {noteId}: {note.Person.FullName} does not meet " +
                $"compliance requirements. Failures: {string.Join("; ", reasons)}. " +
                "Use ApproveWithOverrideAsync if a supervisor exception is warranted.");
        }

        if (maximumUnits is int limit)
        {
            if (!NoteReviewRules.Eligible(limit, (int?)note.Status, note.NoteType?.ToString(),
                note.Narrative, note.EventDate, note.Minutes, note.StartTime, DateTime.Today))
                throw new InvalidOperationException("This note is not eligible for automatic approval.");
            await NoteService.EnsureServiceTimeAvailableAsync(context, note.Person.UserId, note, note.Id);
        }

        note.Status = NoteStatus.Approved;
        note.ApprovedById = actor.Id;
        note.ApprovedAt = DateTime.UtcNow;
        note.Revision++;
        await SaveNoteTransitionAsync(
            context, actor, LocalAuditActions.NoteApproved, noteId,
            maximumUnits is int threshold ? System.Text.Json.JsonSerializer.Serialize(new { maximumUnits = threshold, batch = true }) : "{}");
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
        if (!NoteWorkflow.CanSupervisorTransition((int?)note.Status, NoteWorkflow.Approved))
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
        if (!NoteWorkflow.CanSupervisorTransition((int?)note.Status, NoteWorkflow.Returned))
            throw new InvalidOperationException("Only logged notes can be returned.");

        note.Status = NoteStatus.Returned;
        note.ReturnedById = actor.Id;
        note.ReturnReason = reason;
        note.ReturnedAt = DateTime.UtcNow;
        note.Revision++;
        await SaveNoteTransitionAsync(
            context, actor, LocalAuditActions.NoteReturned, noteId);
    }

    public async Task<NoteReviewPage<Note>> GetReviewPageAsync(
        int supervisorId, int afterId = 0, int? throughId = null, int? userId = null)
    {
        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var agencyWide = UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions);
        var query = context.Notes.AsNoTracking().Where(note =>
            note.Status == NoteStatus.Logged && note.Person.AgencyId == actor.AgencyId &&
            (!userId.HasValue || note.Person.UserId == userId.Value) &&
            context.Users.Any(user => user.Id == note.Person.UserId && user.AgencyId == actor.AgencyId &&
                (user.Permissions & UserPermissions.CaseManagement) != 0 &&
                (agencyWide || user.SupervisorId == actor.Id)));
        var ceiling = throughId ?? await query.Select(note => (int?)note.Id).MaxAsync() ?? 0;
        var rows = await query.Where(note => note.Id > afterId && note.Id <= ceiling)
            .OrderBy(note => note.Id).Take(NoteReviewRules.PageSize + 1)
            .Include(note => note.Person).ThenInclude(person => person.Forms).ToListAsync();
        var more = rows.Count > NoteReviewRules.PageSize;
        rows = rows.Take(NoteReviewRules.PageSize).ToList();
        var requirements = await context.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == actor.AgencyId)
            .Select(settings => (BillingComplianceRequirements?)settings.BillingComplianceRequirements)
            .SingleOrDefaultAsync() ?? BillingComplianceGate.DefaultRequirements;
        foreach (var note in rows)
            note.ComplianceFailureReasons = note.Person.EvaluateComplianceGate(
                DateTime.Today, requirements: requirements).Reasons;
        return new(rows, more ? rows[^1].Id : null, ceiling);
    }

    private async Task<List<Note>> GetLoggedNotesAsync(int supervisorId, bool allSupervisees)
    {
        _ = allSupervisees;
        var actor = CurrentReviewer(supervisorId);
        await using var context = contextFactory.CreateDbContext();
        var canReviewAgency = UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions);
        var caseManagerIds = await context.Users.AsNoTracking()
            .Where(user => user.AgencyId == actor.AgencyId &&
                (user.Permissions & UserPermissions.CaseManagement) != 0 &&
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
        var canReviewAgency = UserPermissionRules.HasAgencyWideSupervisionPermissions(actor.Permissions);
        return context.Notes
            .Include(note => note.Person)
                .ThenInclude(person => person.Forms)
            .SingleOrDefaultAsync(note =>
                note.Id == noteId &&
                note.Person.AgencyId == actor.AgencyId &&
                context.Users.Any(user =>
                    user.Id == note.Person.UserId &&
                    user.AgencyId == actor.AgencyId &&
                    (user.Permissions & UserPermissions.CaseManagement) != 0 &&
                    (canReviewAgency || user.SupervisorId == actor.Id)));
    }

    private User CurrentReviewer(int requestedReviewerId)
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in reviewer is required.");
        if (actor.Id != requestedReviewerId || !actor.HasSupervisorPermissions)
        {
            throw new UnauthorizedAccessException("Only the signed-in reviewer may perform this action.");
        }
        return actor;
    }

    private async Task<BillingComplianceRequirements> LoadComplianceRequirementsAsync()
    {
        var actor = sessionService.CurrentUser
            ?? throw new UnauthorizedAccessException("A signed-in reviewer is required.");
        await using var context = contextFactory.CreateDbContext();
        return await context.Settings.AsNoTracking()
            .Where(settings => settings.AgencyId == actor.AgencyId)
            .Select(settings => (BillingComplianceRequirements?)settings.BillingComplianceRequirements)
            .SingleOrDefaultAsync() ?? BillingComplianceGate.DefaultRequirements;
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
        int noteId,
        string metadataJson = "{}")
    {
        LocalAuditTrail.Record(context, actor, action, "Note", noteId, metadataJson);
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
