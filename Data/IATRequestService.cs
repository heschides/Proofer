using Sati.Models;

namespace Sati.Data
{
    public interface IATRequestService
    {
        // Queue: metadata only, no blob. TotalCost applies the current
        // Settings.PassthroughRate inside the projection.
        Task<List<ATRequestListItem>> GetAllForUserAsync(int userId);

        // The same read narrowed to one client, for the AT requests section of a
        // client's profile. Follows the client rather than the case manager, so a
        // transferred client keeps their filed requests.
        Task<List<ATRequestListItem>> GetAllForPersonAsync(int personId);

        // Full request with line items, for opening one. No blob.
        Task<ATRequest?> GetByIdAsync(int id);

        // The one method that materializes SnapshotPng. Null if no request or no
        // snapshot yet.
        Task<byte[]?> GetSnapshotAsync(int id);

        Task<ATRequest> AddAsync(ATRequest request);

        // Throws AtRequestLockedException when the STORED request is already
        // published. Publishing is itself an update — the attestation travels with
        // the request — so this is the last save a request accepts until it is
        // reopened.
        Task<ATRequest> UpdateAsync(ATRequest request);

        // Save the request and record the case manager's attestation in one
        // operation, then lock it.
        //
        // Publication is a named operation rather than "an update that happens to
        // carry a signature" because of where the trust boundary sits. Over HTTP,
        // an attestation supplied in a request body is a claim by the caller about
        // who signed; the server has to derive the signer from the authenticated
        // token instead. Making it its own method means neither implementation can
        // quietly accept the client's version.
        //
        // Throws AtRequestLockedException if the stored request is already
        // published, AtRequestConcurrencyException if it moved underneath us.
        Task<ATRequest> PublishAsync(ATRequest request, User caseManager);

        // Clears a published request's attestation and returns it to Development.
        // The only sanctioned way past the publication lock. No-op if the request
        // was not published.
        Task<ATRequest> ReopenAsync(ATRequest request);

        Task DeleteAsync(ATRequest request);
    }
}