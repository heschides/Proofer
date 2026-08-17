using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data;

public class NoteService(
    IDbContextFactory<SatiContext> contextFactory,
    ISessionService sessionService) : INoteService
{
    public async Task<Note> AddNoteAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ValidateCaseManagerInput(note);
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        if (!await OwnsPersonAsync(context, actor, note.PersonId))
            throw new UnauthorizedAccessException("You may create notes only for your own caseload.");

        note.AgencyId = actor.AgencyId;
        await EnsureServiceTimeAvailableAsync(context, actor.Id, note, null);
        context.Notes.Add(note);
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteCreated, "Note");
        await context.SaveChangesAsync();
        return note;
    }

    public async Task DeleteNoteAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        var stored = await context.Notes.Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == note.Id);
        if (stored is null || stored.Revision != note.Revision)
            throw new NoteConcurrencyException();
        if (!OwnsPerson(actor, stored.Person))
            throw new UnauthorizedAccessException("You may delete notes only from your own caseload.");
        if (!NoteWorkflow.CanCaseManagerDelete((int?)stored.Status))
            throw new InvalidOperationException("Submitted and workflow-controlled notes are retained as part of the clinical record.");

        context.Notes.Remove(stored);
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException ex) { throw new NoteConcurrencyException(ex); }
    }

    public async Task UpdateNoteAsync(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ValidateCaseManagerInput(note);
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        var stored = await context.Notes.Include(candidate => candidate.Person)
            .SingleOrDefaultAsync(candidate => candidate.Id == note.Id);
        if (stored is null || stored.Revision != note.Revision)
            throw new NoteConcurrencyException();
        if (!OwnsPerson(actor, stored.Person))
            throw new UnauthorizedAccessException("You may update notes only in your own caseload.");
        if (!NoteWorkflow.CanCaseManagerEdit((int?)stored.Status))
            throw new InvalidOperationException("Logged and approved notes cannot be edited. A supervisor must return a logged note before it can be corrected.");
        if (!NoteWorkflow.CanCaseManagerTransition((int?)stored.Status, (int?)note.Status))
            throw new InvalidOperationException(
                NoteWorkflow.DescribeRejectedTransition((int?)stored.Status, (int?)note.Status));

        await EnsureServiceTimeAvailableAsync(context, actor.Id, note, stored.Id);
        CopyCaseManagerValues(note, stored);
        stored.Revision++;
        LocalAuditTrail.Record(context, actor, LocalAuditActions.NoteUpdated, "Note", stored.Id);
        try
        {
            await context.SaveChangesAsync();
            note.Revision = stored.Revision;
        }
        catch (DbUpdateConcurrencyException ex) { throw new NoteConcurrencyException(ex); }
    }

    public async Task<List<Note>> GetAllByPersonAsync(int personId)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await EnsurePersonInScopeAsync(context, actor, personId);
        return await context.Notes.Where(n => n.PersonId == personId).ToListAsync();
    }

    /// <summary>
    /// Ages out the signed-in case manager's own unfinished drafts. The sweep is
    /// scoped to the caller's caseload: a dashboard refresh must never rewrite
    /// another case manager's record, let alone another agency's.
    /// </summary>
    public async Task UpdateAbandonedNotesAsync(int abandonedAfterDays)
    {
        if (abandonedAfterDays <= 0)
            throw new ArgumentOutOfRangeException(nameof(abandonedAfterDays),
                "The abandonment threshold must be a positive number of days.");

        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        var threshold = DateTime.Today.AddDays(-abandonedAfterDays);
        var notes = await context.Notes.Where(n => n.Person.UserId == actor.Id &&
            n.Person.AgencyId == actor.AgencyId &&
            n.Status == NoteStatus.Pending &&
            n.EventDate.HasValue && n.EventDate.Value < threshold).ToListAsync();
        foreach (var note in notes)
        {
            if (!NoteWorkflow.CanSystemAbandon((int?)note.Status))
                continue;
            note.Status = NoteStatus.Abandoned;
            note.Revision++;
        }
        await context.SaveChangesAsync();
    }

    public async Task<List<Note>> GetMonthlyNotesAsync(int userId)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await EnsureUserInScopeAsync(context, actor, userId);
        var firstDay = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var lastDay = firstDay.AddMonths(1).AddDays(-1);
        return await context.Notes.Where(n => n.EventDate >= firstDay && n.EventDate <= lastDay &&
            n.Person.UserId == userId).ToListAsync();
    }

    public async Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await EnsureUserInScopeAsync(context, actor, userId);
        var dayStart = date.Date;
        return await context.Notes.Include(n => n.Person).Where(n => n.Person.UserId == userId &&
            n.EventDate.HasValue && n.EventDate.Value >= dayStart && n.EventDate.Value < dayStart.AddDays(1)).ToListAsync();
    }

    public async Task<List<Note>> GetByYearAsync(int userId, int year)
    {
        var actor = CurrentActor();
        await using var context = contextFactory.CreateDbContext();
        await EnsureUserInScopeAsync(context, actor, userId);
        var firstDay = new DateTime(year, 1, 1);
        var lastDay = new DateTime(year, 12, 31);
        return await context.Notes.Include(n => n.Person).Where(n => n.Person.UserId == userId &&
            n.EventDate.HasValue && n.EventDate.Value >= firstDay && n.EventDate.Value <= lastDay).ToListAsync();
    }

    private static void CopyCaseManagerValues(Note source, Note target)
    {
        target.Narrative = source.Narrative;
        target.EventDate = source.EventDate;
        target.Status = source.Status;
        target.Minutes = source.Minutes;
        target.StartTime = source.StartTime;
        target.FormType = source.FormType;
        target.NoteType = source.NoteType;
        target.CaseManagerJustification = source.CaseManagerJustification;
        target.VisitDocumentationJson = source.VisitDocumentationJson;
    }

    private User CurrentActor() => sessionService.CurrentUser
        ?? throw new UnauthorizedAccessException("A signed-in case manager is required.");

    private static async Task EnsureUserInScopeAsync(SatiContext context, User actor, int userId)
    {
        if (!await LocalTenantAccess.CanAccessUserAsync(context, actor, userId))
            throw new UnauthorizedAccessException("You may read notes only for yourself or a case manager you supervise.");
    }

    private static async Task EnsurePersonInScopeAsync(SatiContext context, User actor, int personId)
    {
        if (!await LocalTenantAccess.CanAccessPersonAsync(context, actor, personId))
            throw new UnauthorizedAccessException("You may read notes only for clients in your scope.");
    }

    private static async Task<bool> OwnsPersonAsync(SatiContext context, User actor, int personId) =>
        await context.People.AnyAsync(person => person.Id == personId && person.UserId == actor.Id &&
            person.AgencyId == actor.AgencyId);

    private static bool OwnsPerson(User actor, Person person) =>
        person.UserId == actor.Id && person.AgencyId == actor.AgencyId;

    private static void ValidateCaseManagerInput(Note note)
    {
        if (note.Narrative is null || note.Narrative.Length > 1_000_000)
            throw new ArgumentException("Narrative is required and must not exceed 1,000,000 characters.", nameof(note));
        if (note.PersonId <= 0) throw new ArgumentException("A valid person is required.", nameof(note));
        if (note.Minutes is < 0 or > 1_440)
            throw new ArgumentException("Minutes must be between 0 and 1,440.", nameof(note));
        if (note.StartTime is int start && (start < 0 || start > ServiceTimeline.WindowLengthMinutes))
            throw new ArgumentException("Service start time must fall inside the logging window.", nameof(note));
        if (!NoteWorkflow.IsCaseManagerWritableStatus((int?)note.Status))
            throw new InvalidOperationException("That note status is controlled by a supervisor workflow.");
    }

    private static async Task EnsureServiceTimeAvailableAsync(
        SatiContext context, int userId, Note note, int? editingNoteId)
    {
        var candidate = ServiceTimeline.TryCreateBlock(editingNoteId ?? 0, note.StartTime,
            note.Minutes, note.Status?.ToString());
        if (candidate is null) return;
        var windowProblem = ServiceTimeline.DescribeWindowViolation(candidate.StartMinutes, candidate.Minutes);
        if (windowProblem is not null) throw new InvalidOperationException(windowProblem);
        if (note.EventDate is not DateTime eventDate) return;

        var dayStart = eventDate.Date;
        var blocks = (await context.Notes.Include(existing => existing.Person)
                .Where(existing => existing.Person.UserId == userId && existing.EventDate >= dayStart &&
                    existing.EventDate < dayStart.AddDays(1)).ToListAsync())
            .Select(existing => ServiceTimeline.TryCreateBlock(existing.Id, existing.StartTime,
                existing.Minutes, existing.Status?.ToString(), $"a note for {existing.Person.FullName}"))
            .OfType<ServiceBlock>();
        var conflicts = ServiceTimeline.FindConflicts(candidate, blocks);
        if (conflicts.Count > 0)
            throw new InvalidOperationException("This service time overlaps time already recorded on this date. " +
                string.Join(" ", conflicts.Select(conflict => conflict.Reason)));
    }
}
