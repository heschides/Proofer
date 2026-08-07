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
        Task DeleteAsync(Provider provider);
    }
}