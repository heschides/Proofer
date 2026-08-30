using Sati.Models;

namespace Sati
{
    // Blob-free, read-only projection of a Person for the supervisor sidebar.
    // Implements IEventSource so UpcomingEventService.GenerateEvents runs against it
    // directly. GetCurrentCycleForm delegates to Person.FindCurrentCycleForm — the
    // single source of truth for cycle-membership — so there is no shadow copy here.
    // Carries Forms whole (no blob columns on Form) and Notes as the minimal
    // NoteSummary surface. Never touches Bio/Journal/Narrative.
    public sealed class PersonSummary : IEventSource
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public DateTime? EffectiveDate { get; set; }

        public string FullName => $"{FirstName} {LastName}".Trim();

        public List<Form> Forms { get; set; } = [];

        // Backing list is NoteSummary; IEventSource.Notes exposes it as INoteInfo.
        public List<NoteSummary> NoteSummaries { get; set; } = [];
        IEnumerable<INoteInfo> IEventSource.Notes => NoteSummaries;

        public Form? GetCurrentCycleForm(FormType type, DateTime? asOf = null)
            => Person.FindCurrentCycleForm(Forms, EffectiveDate, type, asOf);
    }

    // Minimal blob-free note shape for event generation. Implements INoteInfo.
    public sealed class NoteSummary : INoteInfo
    {
        public NoteStatus? Status { get; set; }
        public DateTime? EventDate { get; set; }
        public NoteType? NoteType { get; set; }
    }
}