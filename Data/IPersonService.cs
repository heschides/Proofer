using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IPersonService
    {
        Task<Person> AddPersonAsync(Person person);
        Task<List<Person>> GetAllPeopleAsync(int userId);
        Task<Person> EditPersonAsync(Person person);

        // Journal is loaded and saved on its own, never through the full-graph
        // person load — see Person.Journal for the rationale.
        Task<string?> GetJournalAsync(int personId);
        Task SaveJournalAsync(int personId, string? journal);

        // Adds a stamped reminder to the TOP of the journal and returns the
        // journal's new full text. Separate from SaveJournalAsync because that
        // call replaces the whole journal: composing the entry here and writing
        // the result back would erase whatever another session typed in between.
        // The writer stamps the time — callers do not pass one — and
        // Sati.Contracts.V1.JournalEntry owns the format and the placement.
        Task<string?> AddJournalReminderAsync(int personId, string text);

        Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId);
    }
}
