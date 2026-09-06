using Sati.Models;

namespace Sati.Data

{
    public interface ISupervisorService
    {
        // Logged notes whose consumer passes the compliance gate.
        // These are ready for supervisor content review.
        Task<IEnumerable<Note>> GetPendingNotesAsync(int supervisorId, bool allSupervisees = false);

        // Logged notes whose consumer fails the compliance gate.
        // These sit here until compliance is met or abandonment threshold passes.
        Task<IEnumerable<Note>> GetNonCompliantNotesAsync(int supervisorId, bool allSupervisees = false);

        Task<Sati.Contracts.V1.NoteReviewPage<Note>> GetReviewPageAsync(
            int supervisorId, int afterId = 0, int? throughId = null,
            Sati.Contracts.V1.NoteReviewQuery? filter = null);

        Task<Sati.Contracts.V1.NoteReviewFilterOptions> GetReviewFilterOptionsAsync(int supervisorId);

        Task ApproveNoteAsync(int noteId, int supervisorId, int expectedRevision, int? maximumUnits = null);

        // Override path — supervisor judges billing appropriate despite compliance gap.
        // Requires written justification. Creates a flagged claim visible to billing.
        Task ApproveWithOverrideAsync(int noteId, int supervisorId, string overrideReason, int expectedRevision);

        Task ReturnNoteAsync(int noteId, int supervisorId, string reason, int expectedRevision);
    }
}
