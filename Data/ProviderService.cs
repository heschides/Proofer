using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    // Directory CRUD for service providers. Plain per-method context like the
    // other services — no snapshot/projection cleverness needed, Provider is
    // small and blob-free (contrast ATRequestService, which projects to dodge
    // the PNG). GetPassthroughProvidersAsync is the one filtered read both the
    // AT page and Settings default-picker share.
    public class ProviderService : IProviderService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public ProviderService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<List<Provider>> GetAllAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Providers
                .Where(p => p.AgencyId == CurrentAgencyId())
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<List<Provider>> GetPassthroughProvidersAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.Providers
                .Where(p => p.AgencyId == CurrentAgencyId() && p.ProvidesPassthroughService)
                .OrderBy(p => p.Name)
                .ToListAsync();
        }

        public async Task<Provider> AddAsync(Provider provider)
        {
            await using var context = _contextFactory.CreateDbContext();
            provider.AgencyId = CurrentAgencyId();
            context.Providers.Add(provider);
            await context.SaveChangesAsync();
            return provider;
        }

        public async Task<Provider> UpdateAsync(Provider provider)
        {
            await using var context = _contextFactory.CreateDbContext();
            var tracked = await context.Providers.SingleOrDefaultAsync(
                x => x.Id == provider.Id && x.AgencyId == CurrentAgencyId())
                ?? throw new InvalidOperationException("The provider is outside the current agency.");
            context.Entry(tracked).CurrentValues.SetValues(provider);
            tracked.Id = provider.Id;
            tracked.AgencyId = CurrentAgencyId();
            await context.SaveChangesAsync();
            return tracked;
        }

        // No guard against deleting the Settings default: the FK is ON DELETE
        // SET NULL, so removing the current default provider simply clears the
        // setting (the picker then shows "none" until reset). Deliberate — a
        // hard block would strand you unable to delete a retired agency.
        public async Task DeleteAsync(Provider provider)
        {
            await using var context = _contextFactory.CreateDbContext();
            var tracked = await context.Providers.SingleOrDefaultAsync(
                x => x.Id == provider.Id && x.AgencyId == CurrentAgencyId())
                ?? throw new InvalidOperationException("The provider is outside the current agency.");
            context.Providers.Remove(tracked);
            await context.SaveChangesAsync();
        }

        private int CurrentAgencyId() => _sessionService.CurrentUser?.AgencyId
            ?? throw new InvalidOperationException("A signed-in user is required to access providers.");
    }
}
