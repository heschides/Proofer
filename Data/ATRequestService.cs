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
                    TotalCost = (a.Items.Sum(i => i.ItemCost * i.Quantity) + a.SalesTax) * (1 + rate),
                    SubmittedDate = a.SubmittedDate,
                    VendorName = a.VendorName,
                    CaseManagerName = a.CaseManagerName,
                    // Cheap existence bool — the row knows evidence exists without
                    // fetching the bytes.
                    HasSnapshot = a.SnapshotPng != null
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
            target.SubmittedDate = source.SubmittedDate;
            target.DecisionDate = source.DecisionDate;
            target.SetStatus(source.Status);

            target.Items.Clear();
            foreach (var item in source.Items)
            {
                target.Items.Add(new ATRequestItem
                {
                    Name = item.Name,
                    ItemCost = item.ItemCost,
                    Quantity = item.Quantity,
                    Url = item.Url
                });
            }
        }
    }
}
