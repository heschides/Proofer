using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// A consumer's medical provider list. Reads return every link, current and ended —
    /// the caller decides what to show, and past providers are part of the record rather
    /// than something to hide at the query.
    /// </summary>
    public interface IConsumerProviderService
    {
        Task<List<PersonProvider>> GetByPersonAsync(int personId);

        /// <summary>Adds a new link, or updates an existing one.</summary>
        Task<PersonProvider> SaveAsync(PersonProvider link);

        /// <summary>
        /// Ends a relationship as of <paramref name="endDate"/>. The row is kept: who was
        /// treating someone in a given year is a question a case record has to answer.
        /// </summary>
        /// <remarks>
        /// The consumer is passed rather than derived from the link, so a link id from
        /// another consumer's record fails the ownership check instead of selecting the
        /// scope it is then checked against.
        /// </remarks>
        Task EndAsync(int personId, int linkId, DateTime endDate);

        /// <summary>
        /// Removes a link outright. This is for correcting a mis-entry — a provider added
        /// to the wrong consumer — not for ending a real relationship, which is
        /// <see cref="EndAsync"/>.
        /// </summary>
        Task RemoveAsync(int personId, int linkId);
    }
}
