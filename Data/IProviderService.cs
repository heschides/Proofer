using Sati.Models;

namespace Sati.Data
{
    public interface IProviderService
    {
        // Full directory, name-ordered. Backs the Providers tab list.
        Task<List<Provider>> GetAllAsync();

        // Only providers flagged as passthrough agencies, name-ordered. Backs both
        // the AT page dropdown and the Settings default-provider picker.
        Task<List<Provider>> GetPassthroughProvidersAsync();

        Task<Provider> AddAsync(Provider provider);
        Task<Provider> UpdateAsync(Provider provider);

        /// <summary>Admin only: other case managers' consumers may be linked to this entry.</summary>
        Task DeleteAsync(Provider provider);

        // ── Named contacts at a provider ─────────────────────────────────────
        // Distinct from Provider.PrimaryContact/Phone, which are the organization's general
        // directory contact. These are the people who work there, and an entry accumulates
        // several: a referral coordinator, someone in billing, the nurse who returns calls.

        Task<List<ProviderContact>> GetContactsAsync(int providerId);
        Task<ProviderContact> SaveContactAsync(ProviderContact contact);

        /// <remarks>
        /// The provider is passed rather than derived from the contact, so a contact id belonging
        /// to a different entry fails the agency check instead of selecting the scope it is then
        /// checked against.
        /// </remarks>
        Task RemoveContactAsync(int providerId, int contactId);

        /// <summary>
        /// Admin only: folds one directory entry into another and removes it. Affiliated entries,
        /// consumer links, and contacts move to the survivor. Documents that already named the
        /// merged entry are deliberately left alone — they froze it on purpose.
        /// </summary>
        Task<string> MergeAsync(int survivingProviderId, int mergedProviderId);
    }
}