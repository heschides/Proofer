using Sati.Models;

namespace Sati.Data
{
    public interface IATRequestService
    {
        // Queue: metadata only, no blob. TotalCost applies the current
        // Settings.PassthroughRate inside the projection.
        Task<List<ATRequestListItem>> GetAllForUserAsync(int userId);

        // Full request with line items, for opening one. No blob.
        Task<ATRequest?> GetByIdAsync(int id);

        // The one method that materializes SnapshotPng. Null if no request or no
        // snapshot yet.
        Task<byte[]?> GetSnapshotAsync(int id);

        Task<ATRequest> AddAsync(ATRequest request);
        Task<ATRequest> UpdateAsync(ATRequest request);
        Task DeleteAsync(ATRequest request);
    }
}