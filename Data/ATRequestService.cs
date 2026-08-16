using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class ATRequestService : IATRequestService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISettingsService _settingsService;

        public ATRequestService(IDbContextFactory<SatiContext> contextFactory, ISettingsService settingsService)
        {
            _contextFactory = contextFactory;
            _settingsService = settingsService;
        }

        // The queue read. Projects to ATRequestListItem so the SnapshotPng blob
        // is NEVER materialized — EF only selects columns the projection names.
        // Filters via Person.UserId (caseload ownership follows the client on
        // transfer), while CaseManagerName carries the frozen submitter for display.
        public async Task<List<ATRequestListItem>> GetAllForUserAsync(int userId)
        {
            var settings = await _settingsService.LoadAsync();
            var rate = settings.PassthroughRate;

            await using var context = _contextFactory.CreateDbContext();

            return await context.ATRequests
                .Where(a => a.Person!.UserId == userId)
                .OrderByDescending(a => a.SubmittedDate ?? DateTime.MaxValue)
                .Select(a => new ATRequestListItem
                {
                    Id = a.Id,
                    ClientName = a.ClientName,
                    Status = a.Status,
                    // MIRRORED MATH — canonical definition is ATRequestCalculator.Total.
                    // Re-expressed here because EF can't translate a method call into
                    // SQL. If the passthrough formula changes, it changes THERE and
                    // HERE. (subtotal + tax) * (1 + rate), summed in SQL without
                    // loading item rows.
                    TotalCost = (a.Items.Sum(i => i.ItemCost * i.Quantity) + a.SalesTax)
                                * (1 + (a.PassthroughRate ?? rate)),
                    SubmittedDate = a.SubmittedDate,
                    VendorName = a.VendorName,
                    CaseManagerName = a.CaseManagerName,
                    // Cheap existence bool — the row knows evidence exists without
                    // fetching the bytes.
                    HasSnapshot = a.SnapshotPng != null,
                    SignedByName = a.SignedByName,
                    SignedAtUtc = a.SignedAtUtc
                })
                .ToListAsync();
        }

        // The same queue read, narrowed to one client — the list shown on a
        // client's profile. Shares ATRequestListItem and the projection discipline
        // with GetAllForUserAsync: no item rows, no blobs.
        //
        // Filtered on the REQUEST's PersonId rather than through Person.UserId,
        // because this list belongs to the client rather than to whoever currently
        // carries them. The caller is responsible for having established that this
        // client is theirs to look at; on the API path that is TenantAccess.
        public async Task<List<ATRequestListItem>> GetAllForPersonAsync(int personId)
        {
            var settings = await _settingsService.LoadAsync();
            var rate = settings.PassthroughRate;

            await using var context = _contextFactory.CreateDbContext();

            return await context.ATRequests
                .Where(a => a.PersonId == personId)
                .OrderByDescending(a => a.SubmittedDate ?? DateTime.MaxValue)
                .ThenByDescending(a => a.Id)
                .Select(a => new ATRequestListItem
                {
                    Id = a.Id,
                    ClientName = a.ClientName,
                    Status = a.Status,
                    // MIRRORED MATH — see GetAllForUserAsync. The frozen rate wins
                    // where one exists, so a published row keeps showing the total
                    // it was filed at even after the agency rate moves.
                    TotalCost = (a.Items.Sum(i => i.ItemCost * i.Quantity) + a.SalesTax)
                                * (1 + (a.PassthroughRate ?? rate)),
                    SubmittedDate = a.SubmittedDate,
                    VendorName = a.VendorName,
                    CaseManagerName = a.CaseManagerName,
                    HasSnapshot = a.SnapshotPng != null,
                    SignedByName = a.SignedByName,
                    SignedAtUtc = a.SignedAtUtc
                })
                .ToListAsync();
        }

        // Full request for opening one: includes Items (the PDF regenerates from
        // these), still excludes the blob — SnapshotPng is fetched only via
        // GetSnapshotAsync. AsSplitQuery mirrors PersonService's handling of the
        // parent+children load to avoid a cartesian blowup on the join.
        public async Task<ATRequest?> GetByIdAsync(int id)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.ATRequests
                .Include(a => a.Items)
                .AsSplitQuery()
                .FirstOrDefaultAsync(a => a.Id == id);
        }

        // The ONLY method that materializes SnapshotPng. Projects to just the
        // blob so nothing else on the row is dragged along. Null if the request
        // doesn't exist OR has no snapshot yet — caller can't tell the two apart,
        // which is fine: both mean "no image to show."
        public async Task<byte[]?> GetSnapshotAsync(int id)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.ATRequests
                .Where(a => a.Id == id)
                .Select(a => a.SnapshotPng)
                .FirstOrDefaultAsync();
        }

        public async Task<ATRequest> AddAsync(ATRequest request)
        {
            await using var context = _contextFactory.CreateDbContext();
            context.ATRequests.Add(request);
            await context.SaveChangesAsync();
            return request;
        }

        public async Task<ATRequest> UpdateAsync(ATRequest request)
        {
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.ATRequests
                .Include(candidate => candidate.Items)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.Id);
            if (stored is null || stored.Revision != request.Revision)
                throw new AtRequestConcurrencyException();

            // The lock, tested against the STORED row rather than the caller's copy.
            // That ordering is the whole point: the incoming request is whatever the
            // client chose to send, so asking it whether it is published asks the
            // party being restricted. Publishing itself still works — at that moment
            // the stored row is not yet published and the caller's copy is.
            if (stored.IsPublished)
                throw new AtRequestLockedException();

            CopyMutableValues(request, stored);
            stored.Revision++;
            try
            {
                await context.SaveChangesAsync();
                request.Revision = stored.Revision;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new AtRequestConcurrencyException(ex);
            }
            return request;
        }

        // Save-and-publish. Locally the caller is the same process that owns the
        // database, so "derive the signer from the session" means taking the User
        // the desktop passes; over HTTP the equivalent method reads it from the
        // token. Either way the attestation is stamped HERE, against the stored
        // row, not accepted as an incoming value.
        public async Task<ATRequest> PublishAsync(ATRequest request, User caseManager)
        {
            var signedAtUtc = DateTime.UtcNow;

            // Read at publication rather than taken from the caller: the rate the
            // request is filed under is an agency fact, and the client's copy of it
            // may be however old the open editor is.
            var settings = await _settingsService.LoadAsync();
            var passthroughRate = settings.PassthroughRate;

            // A request published without ever having been saved: the attestation
            // goes on before the insert, so it lands in one round trip.
            if (request.Id == 0)
            {
                request.Publish(caseManager, signedAtUtc, passthroughRate);
                return await AddAsync(request);
            }

            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.ATRequests
                .Include(candidate => candidate.Items)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.Id);
            if (stored is null || stored.Revision != request.Revision)
                throw new AtRequestConcurrencyException();
            if (stored.IsPublished)
                throw new AtRequestLockedException();

            // The caller's pending edits are saved as part of publishing, so what
            // is attested to is what the case manager was looking at.
            CopyMutableValues(request, stored);
            stored.Publish(caseManager, signedAtUtc, passthroughRate);
            stored.Revision++;
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new AtRequestConcurrencyException(ex);
            }

            // Mirror the committed attestation back onto the caller's instance so
            // the editor reflects the stored record rather than a hopeful copy.
            request.RehydrateAttestation(
                stored.SignedByName, stored.SignedByRole, stored.SignedByUserId,
                stored.SignedAtUtc, stored.AttestationStatement, stored.PassthroughRate);
            request.SubmittedDate = stored.SubmittedDate;
            request.SetStatus(stored.Status);
            request.Revision = stored.Revision;
            return request;
        }

        // Reopen a published request for correction. The one path allowed past the
        // UpdateAsync lock, and it does not carry the caller's edits — it only
        // clears the attestation and returns the request to Development. The client
        // then edits and publishes again, so a correction always produces a fresh
        // attestation rather than inheriting the old one.
        public async Task<ATRequest> ReopenAsync(ATRequest request)
        {
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.ATRequests
                .Include(candidate => candidate.Items)
                .SingleOrDefaultAsync(candidate => candidate.Id == request.Id);
            if (stored is null || stored.Revision != request.Revision)
                throw new AtRequestConcurrencyException();
            if (!stored.IsPublished)
                return request;

            stored.ReopenForCorrection();
            stored.Revision++;
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new AtRequestConcurrencyException(ex);
            }

            request.ReopenForCorrection();
            request.Revision = stored.Revision;
            return request;
        }

        public async Task DeleteAsync(ATRequest request)
        {
            await using var context = _contextFactory.CreateDbContext();
            var stored = await context.ATRequests.SingleOrDefaultAsync(candidate => candidate.Id == request.Id);
            if (stored is null || stored.Revision != request.Revision)
                throw new AtRequestConcurrencyException();
            context.ATRequests.Remove(stored);
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new AtRequestConcurrencyException(ex);
            }
        }

        private static void CopyMutableValues(ATRequest source, ATRequest target)
        {
            target.VendorName = source.VendorName;
            target.VendorBillingLocation = source.VendorBillingLocation;
            target.VendorProgramContact = source.VendorProgramContact;
            target.VendorBillingContact = source.VendorBillingContact;
            target.SalesTax = source.SalesTax;
            target.SalesTaxOverridden = source.SalesTaxOverridden;
            target.SubmittedDate = source.SubmittedDate;
            target.DecisionDate = source.DecisionDate;
            target.SetStatus(source.Status);

            // Carried because the publish save is an update: the attestation is
            // written in memory by ATRequest.Publish and reaches the database
            // through this copy. Reopening does NOT come through here — it clears
            // the attestation on the stored row directly (see ReopenAsync).
            target.RehydrateAttestation(
                source.SignedByName, source.SignedByRole, source.SignedByUserId,
                source.SignedAtUtc, source.AttestationStatement, source.PassthroughRate);
            if (source.SnapshotPng is not null)
                target.AttachSnapshot(source.SnapshotPng);

            target.Items.Clear();
            foreach (var item in source.Items)
            {
                target.Items.Add(new ATRequestItem
                {
                    Name = item.Name,
                    ItemCost = item.ItemCost,
                    Quantity = item.Quantity,
                    Url = item.Url,
                    ScreenshotPng = item.ScreenshotPng
                });
            }
        }
    }
}
