namespace Sati.Models
{
    // Minimal read-surface of a Note that UpcomingEventService.GenerateEvents needs.
    // Implemented by the real Note entity (zero behavior change) and by the blob-free
    // NoteSummary carried on PersonSummary — so the events engine can run against a
    // lightweight projection without a shadow copy of its logic.
    public interface INoteInfo
    {
        NoteStatus? Status { get; }
        DateTime? EventDate { get; }
        NoteType? NoteType { get; }
    }
}