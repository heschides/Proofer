using System;
using System.Collections.Generic;
using System.Text;
using Sati.Contracts.V1;

namespace Sati.Data
{
    /// <summary>
    /// The journal as it stands after a reminder was added.
    /// </summary>
    /// <param name="Journal">The journal's new full text, as the writer stored it.</param>
    public readonly record struct JournalReminderResult(string? Journal);

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
        Task<JournalReminderResult> AddJournalReminderAsync(int personId, string text);

        Task<List<PersonSummary>> GetPeopleForSummaryAsync(int userId);

        // Moves a consumer to another case manager's caseload. Separate from EditPersonAsync
        // because ownership is not a profile field: it decides who may read the record at all,
        // it is the only Person change a supervisor makes to someone else's consumer, and
        // Person.UserId is deliberately not settable through the ordinary save path.
        //
        // Sati.Contracts.V1.CaseloadTransferRules owns who may do this. Both implementations
        // load the participants themselves and consult it; neither trusts the caller to have
        // checked, because the desktop path has no server in front of it.
        Task<CaseloadOwnershipDto> TransferOwnershipAsync(
            int personId,
            int targetUserId,
            int expectedRevision);

        // Which of these Credible ids the agency already holds. The dedupe check behind bulk
        // import; agency-scoped, because the duplicate an importing supervisor most needs to
        // catch is one already sitting on a case manager's caseload rather than their own.
        //
        // Returns no name and no person id — only the ids that matched, and the owner's display
        // name where the caller could already see that caseload.
        Task<IReadOnlyList<CredibleClientMatchDto>> FindCredibleMatchesAsync(
            IReadOnlyList<string> credibleClientIds);
    }
}
