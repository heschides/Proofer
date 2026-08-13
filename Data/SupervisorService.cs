using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class SupervisorService : ISupervisorService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly IUserService _userService;

        public SupervisorService(IDbContextFactory<SatiContext> contextFactory, IUserService userService)
        {
            _contextFactory = contextFactory;
            _userService = userService;
        }

        // -------------------------------------------------------------------------
        // Queue retrieval
        // -------------------------------------------------------------------------

        // Returns Logged notes whose consumer passes the compliance gate.
        // These go to the supervisor approval queue — content review only,
        // structural compliance already verified.
        public async Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false)
        {
            var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
            var today = DateTime.Today;
            return notes.Where(n => n.Person.EvaluateComplianceGate(today).Passed);
        }

        // Returns Logged notes whose consumer fails the compliance gate.
        // These sit in the non-compliant queue until compliance is met or
        // the abandonment threshold passes.
        public async Task<IEnumerable<Note>> GetNonCompliantNotesAsync(int supervisorId, bool allSupervisees = false)
        {
            var notes = await GetLoggedNotesAsync(supervisorId, allSupervisees);
            var today = DateTime.Today;
            return notes.Where(n => !n.Person.EvaluateComplianceGate(today).Passed);
        }

        // -------------------------------------------------------------------------
        // Approval actions
        // -------------------------------------------------------------------------

        public async Task ApproveNoteAsync(int noteId, int supervisorId, int expectedRevision)
        {
            await using var context = _contextFactory.CreateDbContext();

            var note = await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                .FirstOrDefaultAsync(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Note {noteId} not found.");

            EnsureCurrentRevision(note, expectedRevision);
            if (note.Status != NoteStatus.Logged)
                throw new InvalidOperationException("Only logged notes can be approved.");

            // Hard compliance guard — service enforces the rule even if UI
            // pre-filters. Cannot be bypassed by calling this method directly.
            var (passed, reasons) = note.Person.EvaluateComplianceGate(DateTime.Today);
            if (!passed)
                throw new InvalidOperationException(
                    $"Cannot approve note {noteId}: {note.Person.FullName} does not meet " +
                    $"compliance requirements. Failures: {string.Join("; ", reasons)}. " +
                    $"Use ApproveWithOverrideAsync if a supervisor exception is warranted.");

            note.Status = NoteStatus.Approved;
            note.ApprovedById = supervisorId;
            note.ApprovedAt = DateTime.UtcNow;
            note.Revision++;

            await SaveNoteTransitionAsync(context);
        }

        // Override path — for notes in the non-compliant queue where the
        // supervisor has judged that billing is appropriate despite the gap.
        // Requires a written justification. Creates a flagged claim line that
        // the billing department must acknowledge before submission.
        public async Task ApproveWithOverrideAsync(int noteId, int supervisorId, string overrideReason, int expectedRevision)
        {
            if (string.IsNullOrWhiteSpace(overrideReason))
                throw new ArgumentException("Override reason is required.", nameof(overrideReason));

            await using var context = _contextFactory.CreateDbContext();

            var note = await context.Notes
                .Include(n => n.Person)
                .FirstOrDefaultAsync(n => n.Id == noteId)
                ?? throw new InvalidOperationException($"Note {noteId} not found.");

            EnsureCurrentRevision(note, expectedRevision);
            if (note.Status != NoteStatus.Logged)
                throw new InvalidOperationException("Only logged notes can be approved.");

            note.Status = NoteStatus.Approved;
            note.ApprovedById = supervisorId;
            note.ApprovedAt = DateTime.UtcNow;
            note.ComplianceOverride = true;
            note.OverrideReason = overrideReason;
            note.OverrideApprovedById = supervisorId;
            note.OverrideApprovedAt = DateTime.UtcNow;
            note.Revision++;

            await SaveNoteTransitionAsync(context);
        }

        public async Task ReturnNoteAsync(int noteId, int supervisorId, string reason, int expectedRevision)
        {
            await using var context = _contextFactory.CreateDbContext();
            var note = await context.Notes.FindAsync(noteId)
                ?? throw new InvalidOperationException($"Note {noteId} not found.");

            EnsureCurrentRevision(note, expectedRevision);
            if (note.Status != NoteStatus.Logged)
                throw new InvalidOperationException("Only logged notes can be returned.");

            note.Status = NoteStatus.Returned;
            note.ReturnedById = supervisorId;
            note.ReturnReason = reason;
            note.ReturnedAt = DateTime.UtcNow;
            note.Revision++;

            await SaveNoteTransitionAsync(context);
        }

        private static void EnsureCurrentRevision(Note note, int expectedRevision)
        {
            if (note.Revision != expectedRevision)
                throw new NoteConcurrencyException();
        }

        private static async Task SaveNoteTransitionAsync(SatiContext context)
        {
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new NoteConcurrencyException(ex);
            }
        }

     
      
        // -------------------------------------------------------------------------
        // Private helpers
        // -------------------------------------------------------------------------

        // Shared query for both queue methods — loads Logged notes with Person
        // and Forms included so IsComplianceGatePassed can evaluate in memory
        // without additional DB round trips.
        private async Task<IEnumerable<Note>> GetLoggedNotesAsync(int supervisorId, bool allSupervisees)
        {
            await using var context = _contextFactory.CreateDbContext();

            var superviseeIds = allSupervisees
                ? await context.Users
                    .Where(u => u.Role == UserRole.CaseManager)
                    .Select(u => u.Id)
                    .ToListAsync()
                : (await _userService.GetSuperviseesAsync(supervisorId))
                    .Select(u => u.Id)
                    .ToList();

            return await context.Notes
                .Include(n => n.Person)
                    .ThenInclude(p => p.Forms)
                .Where(n => n.Status == NoteStatus.Logged
                         && superviseeIds.Contains(n.Person.UserId))
                .OrderBy(n => n.EventDate)
                .ToListAsync();
        }
    }
}
