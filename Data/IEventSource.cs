using Sati.Models;

namespace Sati
{
    // Read-surface UpcomingEventService.GenerateEvents needs from a "person-like"
    // source. Implemented by the Person entity (zero behavior change — it already has
    // all of these) and by the blob-free PersonSummary DTO. Lets the events engine run
    // against a lightweight projection without duplicating cycle logic.
    //
    // GetCurrentCycleForm is on the interface because GenerateEvents calls it; both
    // implementers delegate to the SAME static helper (Person.FindCurrentCycleForm),
    // so there is no shadow copy of cycle-membership math.
    public interface IEventSource
    {
        DateTime? EffectiveDate { get; }
        string FullName { get; }
        List<Form> Forms { get; }
        IEnumerable<INoteInfo> Notes { get; }
        Form? GetCurrentCycleForm(FormType type, DateTime? asOf = null);
    }
}