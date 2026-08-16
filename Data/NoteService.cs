using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Windows.UI;

namespace Sati.Data
{
    public class NoteService : INoteService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public NoteService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<Note> AddNoteAsync(Note note)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.Notes.Add(note);
            await context.SaveChangesAsync();
            return note;
        }

        public async Task DeleteNoteAsync(Note note)
        {
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.Notes.SingleOrDefaultAsync(candidate => candidate.Id == note.Id);
            if (stored is null || stored.Revision != note.Revision)
                throw new NoteConcurrencyException();
            context.Notes.Remove(stored);
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new NoteConcurrencyException(ex);
            }
        }

        public async Task UpdateNoteAsync(Note note)
        {
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.Notes.SingleOrDefaultAsync(candidate => candidate.Id == note.Id);
            if (stored is null || stored.Revision != note.Revision)
                throw new NoteConcurrencyException();

            CopyNoteValues(note, stored);
            stored.Revision++;
            try
            {
                await context.SaveChangesAsync();
                note.Revision = stored.Revision;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new NoteConcurrencyException(ex);
            }
        }

        private static void CopyNoteValues(Note source, Note target)
        {
            target.Narrative = source.Narrative;
            target.EventDate = source.EventDate;
            target.Status = source.Status;
            target.Minutes = source.Minutes;
            target.StartTime = source.StartTime;
            target.PersonId = source.PersonId;
            target.FormType = source.FormType;
            target.NoteType = source.NoteType;
            target.AgencyId = source.AgencyId;
            target.ReturnReason = source.ReturnReason;
            target.ReturnedById = source.ReturnedById;
            target.ApprovedById = source.ApprovedById;
            target.ApprovedAt = source.ApprovedAt;
            target.ReturnedAt = source.ReturnedAt;
            target.CaseManagerJustification = source.CaseManagerJustification;
            target.VisitDocumentationJson = source.VisitDocumentationJson;
            target.ComplianceOverride = source.ComplianceOverride;
            target.OverrideReason = source.OverrideReason;
            target.OverrideApprovedById = source.OverrideApprovedById;
            target.OverrideApprovedAt = source.OverrideApprovedAt;
        }

        public async Task<List<Note>> GetAllByPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Notes
                .Where(n => n.PersonId == personId)
                .ToListAsync();
        }

        public async Task UpdateAbandonedNotesAsync(int abandonedAfterDays)
        {
            await using var context = _contextFactory.CreateDbContext();
            var threshold = DateTime.Now.AddDays(-abandonedAfterDays);
            var abandonedNotes = await context.Notes
                .Where(n => n.Status == NoteStatus.Pending &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value < threshold)
                .ToListAsync();

            foreach (var note in abandonedNotes)
            {
                note.Status = NoteStatus.Abandoned;
                note.Revision++;
            }

            await context.SaveChangesAsync();
        }

        public async Task<List<Note>> GetMonthlyNotesAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var firstDay = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            return await context.Notes
                .Where(n => n.EventDate >= firstDay &&
                            n.EventDate <= lastDay &&
                            n.Person.UserId == userId)
                .ToListAsync();
        }
        public async Task<List<Note>> GetDayScheduleAsync(int userId, DateTime date)
        {
            await using var context = _contextFactory.CreateDbContext();
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);
            return await context.Notes
                .Include(n => n.Person)
                .Where(n => n.Person.UserId == userId &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value >= dayStart &&
                            n.EventDate.Value < dayEnd)
                .ToListAsync();
        }

        public async Task<List<Note>> GetByYearAsync(int userId, int year)
        {
            await using var context = _contextFactory.CreateDbContext();
            var firstDay = new DateTime(year, 1, 1);
            var lastDay = new DateTime(year, 12, 31);
            return await context.Notes
                .Include(n => n.Person)
                .Where(n => n.Person.UserId == userId &&
                            n.EventDate.HasValue &&
                            n.EventDate.Value >= firstDay &&
                            n.EventDate.Value <= lastDay)
                .ToListAsync();
        }
    }
}
