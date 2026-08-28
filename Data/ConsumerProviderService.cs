using Microsoft.EntityFrameworkCore;
using Sati.Contracts.V1;
using Sati.Models;

namespace Sati.Data
{
    /// <summary>
    /// The transitional desktop path for a consumer's provider list.
    /// <para>
    /// Every method takes a caller-supplied id, so every method re-establishes access from
    /// the signed-in user rather than trusting it. The rules themselves live in
    /// <see cref="ConsumerProviderRules"/> and are the same ones the API applies.
    /// </para>
    /// </summary>
    public sealed class ConsumerProviderService : IConsumerProviderService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public ConsumerProviderService(
            IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<List<PersonProvider>> GetByPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await EnsureOwnedPersonAsync(context, personId);

            return await context.PersonProviders.AsNoTracking()
                .Where(link => link.PersonId == personId)
                .OrderByDescending(link => link.IsPrimaryCare)
                .ThenBy(link => link.SortOrder)
                .ThenBy(link => link.Id)
                .ToListAsync();
        }

        public async Task<PersonProvider> SaveAsync(PersonProvider link)
        {
            await using var context = _contextFactory.CreateDbContext();
            await EnsureOwnedPersonAsync(context, link.PersonId);

            link.Role = Normalize(link.Role);
            await GuardAsync(context, link);

            if (link.Id == 0)
            {
                context.PersonProviders.Add(link);
                await context.SaveChangesAsync();
                return link;
            }

            var tracked = await context.PersonProviders.SingleOrDefaultAsync(
                    candidate => candidate.Id == link.Id && candidate.PersonId == link.PersonId)
                ?? throw new InvalidOperationException(
                    "That provider entry is no longer on this consumer's record.");

            tracked.ProviderId = link.ProviderId;
            tracked.Role = link.Role;
            tracked.IsPrimaryCare = link.IsPrimaryCare;
            tracked.StartDate = link.StartDate;
            tracked.EndDate = link.EndDate;
            tracked.HasActiveRelease = link.HasActiveRelease;
            tracked.SortOrder = link.SortOrder;
            await context.SaveChangesAsync();
            return tracked;
        }

        public async Task EndAsync(int personId, int linkId, DateTime endDate)
        {
            await using var context = _contextFactory.CreateDbContext();
            var link = await LoadOwnedLinkAsync(context, personId, linkId);
            link.EndDate = endDate.Date;
            await context.SaveChangesAsync();
        }

        public async Task RemoveAsync(int personId, int linkId)
        {
            await using var context = _contextFactory.CreateDbContext();
            var link = await LoadOwnedLinkAsync(context, personId, linkId);
            context.PersonProviders.Remove(link);
            await context.SaveChangesAsync();
        }

        // The rules the API applies, repeated rather than assumed. The provider lookup is
        // scoped to the consumer's own agency, so a directory entry from another tenant
        // fails as absent instead of quietly linking across the boundary.
        private async Task GuardAsync(SatiContext context, PersonProvider link)
        {
            var agencyId = CurrentAgencyId();
            var provider = await context.Providers.AsNoTracking()
                .SingleOrDefaultAsync(candidate =>
                    candidate.Id == link.ProviderId && candidate.AgencyId == agencyId);
            if (provider is null)
                throw new InvalidOperationException(ConsumerProviderRules.ProviderOutsideAgencyMessage());

            var existing = await context.PersonProviders.AsNoTracking()
                .Where(candidate => candidate.PersonId == link.PersonId && candidate.Id != link.Id)
                .ToListAsync();

            if (link.Id == 0 && existing.Count >= ConsumerProviderRules.MaxProvidersPerConsumer)
                throw new InvalidOperationException(ConsumerProviderRules.TooManyProvidersMessage());

            if (!ConsumerProviderRules.IsCurrent(link.EndDate))
                return;

            var duplicate = existing.FirstOrDefault(candidate =>
                candidate.ProviderId == link.ProviderId &&
                ConsumerProviderRules.IsCurrent(candidate.EndDate));
            if (duplicate is not null)
                throw new InvalidOperationException(
                    ConsumerProviderRules.DuplicateCurrentLinkMessage(provider.Name));

            if (!link.IsPrimaryCare)
                return;

            var currentPrimaryId = existing
                .FirstOrDefault(candidate =>
                    candidate.IsPrimaryCare && ConsumerProviderRules.IsCurrent(candidate.EndDate))
                ?.ProviderId;
            if (currentPrimaryId is null)
                return;

            var name = await context.Providers.AsNoTracking()
                .Where(candidate => candidate.Id == currentPrimaryId)
                .Select(candidate => candidate.Name)
                .SingleOrDefaultAsync() ?? "Another provider";
            throw new InvalidOperationException(ConsumerProviderRules.PrimaryCareConflictMessage(name));
        }

        // Ownership is checked against the consumer the caller named, and the row must
        // belong to that consumer. Reading the person off the row first would let the
        // supplied link id choose the scope it is then validated against.
        private async Task<PersonProvider> LoadOwnedLinkAsync(SatiContext context, int personId, int linkId)
        {
            await EnsureOwnedPersonAsync(context, personId);
            return await context.PersonProviders.SingleOrDefaultAsync(
                    candidate => candidate.Id == linkId && candidate.PersonId == personId)
                ?? throw new InvalidOperationException(
                    "That provider entry is no longer on this consumer's record.");
        }

        // Caseload ownership plus agency, matching the API's OwnsPersonAsync. A link id
        // arriving from anywhere resolves to a person before it resolves to a row.
        private async Task EnsureOwnedPersonAsync(SatiContext context, int personId)
        {
            var user = _sessionService.CurrentUser
                ?? throw new InvalidOperationException(
                    "A signed-in user is required to read a consumer's providers.");

            var owned = await context.People.AsNoTracking().AnyAsync(person =>
                person.Id == personId &&
                person.UserId == user.Id &&
                person.AgencyId == user.AgencyId);
            if (!owned)
                throw new UnauthorizedAccessException(
                    "That consumer is not on your caseload.");
        }

        private int CurrentAgencyId() => _sessionService.CurrentUser?.AgencyId
            ?? throw new InvalidOperationException(
                "A signed-in user is required to read a consumer's providers.");

        private static string? Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
