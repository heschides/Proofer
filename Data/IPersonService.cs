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

        Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId);
    }
}
