using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;
using Sati.Contracts.V1;
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
            EnsureCanCreateOrEdit();
            await using var context = _contextFactory.CreateDbContext();
            provider.AgencyId = CurrentAgencyId();
            await GuardDuplicateIdentifierAsync(context, provider, null);
            await GuardAffiliationAsync(context, provider, 0);
            context.Providers.Add(provider);
            await context.SaveChangesAsync();
            return provider;
        }

        public async Task<Provider> UpdateAsync(Provider provider)
        {
            EnsureCanCreateOrEdit();
            await using var context = _contextFactory.CreateDbContext();
            var tracked = await context.Providers.SingleOrDefaultAsync(
                x => x.Id == provider.Id && x.AgencyId == CurrentAgencyId())
                ?? throw new InvalidOperationException("The provider is outside the current agency.");
            await GuardDuplicateIdentifierAsync(context, provider, provider.Id);
            await GuardAffiliationAsync(context, provider, provider.Id);
            context.Entry(tracked).CurrentValues.SetValues(provider);
            tracked.Id = provider.Id;
            tracked.AgencyId = CurrentAgencyId();
            await context.SaveChangesAsync();
            return tracked;
        }

        // Affiliation is decided by ProviderAffiliation, not here. The transitional local
        // path repeats the call rather than trusting the API to be the only caller, and
        // loads only this agency's rows — which is what makes a parent from another
        // agency fail as "not in this directory" rather than linking across a tenant.
        private async Task GuardAffiliationAsync(SatiContext context, Provider provider, int childId)
        {
            var kindProblem = ProviderAffiliation.ValidateKind(
                provider.Type == ProviderType.Healthcare, provider.MedicalKind);
            if (kindProblem is not null)
                throw new InvalidOperationException(kindProblem);

            if (provider.ParentProviderId is null)
                return;

            var directory = (await context.Providers.AsNoTracking()
                    .Where(candidate => candidate.AgencyId == CurrentAgencyId())
                    .ToListAsync())
                .ToAffiliationNodes();

            var parentProblem = ProviderAffiliation.ValidateParent(
                childId, provider.MedicalKind, provider.ParentProviderId, directory);
            if (parentProblem is not null)
                throw new InvalidOperationException(parentProblem);
        }

        // The transitional local path repeats the API's rule rather than trusting the
        // API to be the only caller. Scope is one agency: the same organization in
        // several agencies' directories is correct, not a duplicate.
        private async Task GuardDuplicateIdentifierAsync(
            SatiContext context, Provider provider, int? editingProviderId)
        {
            var npi = Blank(provider.Npi);
            var maineCareProviderId = Blank(provider.MaineCareProviderId);
            if (npi is null && maineCareProviderId is null)
                return;

            var agencyId = CurrentAgencyId();
            var clash = await context.Providers.AsNoTracking()
                .Where(candidate => candidate.AgencyId == agencyId &&
                                    (editingProviderId == null || candidate.Id != editingProviderId) &&
                                    ((npi != null && candidate.Npi == npi) ||
                                     (maineCareProviderId != null && candidate.MaineCareProviderId == maineCareProviderId)))
                .FirstOrDefaultAsync();

            if (clash is null)
                return;

            var which = npi is not null && clash.Npi == npi
                ? "National Provider Identifier"
                : "MaineCare provider identifier";
            throw new InvalidOperationException(
                $"\"{clash.Name}\" is already in this agency's provider directory with the same {which}. " +
                "Edit that entry rather than creating a second one, so the organization stays a single record.");
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // No guard against deleting the Settings default: the FK is ON DELETE
        // SET NULL, so removing the current default provider simply clears the
        // setting (the picker then shows "none" until reset). Deliberate — a
        // hard block would strand you unable to delete a retired agency.
        public async Task DeleteAsync(Provider provider)
        {
            // Admin only. The directory is shared, so a delete here reaches other case managers'
            // consumers — the transitional local path enforces that rather than relying on the
            // API being the only caller.
            var actor = CurrentActor();
            if (!ProviderDirectoryRules.CanDeleteOrMerge(actor.Permissions))
                throw new UnauthorizedAccessException(ProviderDirectoryRules.DeleteRequiresAdminMessage);

            await using var context = _contextFactory.CreateDbContext();
            var actorIsCurrentAdmin = await context.Users.AsNoTracking().AnyAsync(user =>
                user.Id == actor.Id && user.AgencyId == actor.AgencyId &&
                (user.Permissions & UserPermissions.Administration) != 0);
            if (!actorIsCurrentAdmin)
                throw new UnauthorizedAccessException(ProviderDirectoryRules.DeleteRequiresAdminMessage);
            var tracked = await context.Providers.SingleOrDefaultAsync(
                x => x.Id == provider.Id && x.AgencyId == actor.AgencyId)
                ?? throw new InvalidOperationException("The provider is outside the current agency.");

            // Deleting a parent would either orphan its subtree or, under SET NULL, promote
            // every child to top level with nothing in the UI revealing that the hierarchy
            // split. Name the affiliated entries and let the person decide.
            var affiliated = await context.Providers.AsNoTracking()
                .Where(child => child.AgencyId == CurrentAgencyId() && child.ParentProviderId == tracked.Id)
                .OrderBy(child => child.Name)
                .Select(child => child.Name)
                .ToListAsync();
            if (affiliated.Count > 0)
                throw new InvalidOperationException(
                    ProviderAffiliation.AffiliatedChildrenMessage(tracked.Name, affiliated));

            // Also refused while any consumer record references it, ended links included —
            // the row still points here, and the history is why the row was kept. A count
            // rather than names: the directory is not where who-sees-whom is disclosed.
            var onRecords = await context.PersonProviders.AsNoTracking()
                .CountAsync(link => link.ProviderId == tracked.Id);
            if (onRecords > 0)
                throw new InvalidOperationException(
                    ConsumerProviderRules.ProviderOnConsumerRecordsMessage(tracked.Name, onRecords));

            context.Providers.Remove(tracked);
            await context.SaveChangesAsync();
        }

        // ── Named contacts ───────────────────────────────────────────────────

        public async Task<List<ProviderContact>> GetContactsAsync(int providerId)
        {
            await using var context = _contextFactory.CreateDbContext();
            await EnsureOwnedProviderAsync(context, providerId);

            return await context.ProviderContacts.AsNoTracking()
                .Where(contact => contact.ProviderId == providerId)
                .OrderByDescending(contact => contact.IsPrimary)
                .ThenBy(contact => contact.SortOrder)
                .ThenBy(contact => contact.Id)
                .ToListAsync();
        }

        public async Task<ProviderContact> SaveContactAsync(ProviderContact contact)
        {
            EnsureCanCreateOrEdit();
            await using var context = _contextFactory.CreateDbContext();
            await EnsureOwnedProviderAsync(context, contact.ProviderId);

            contact.Name = contact.Name.Trim();
            contact.Role = Blank(contact.Role);
            contact.Phone = Blank(contact.Phone);
            contact.Extension = Blank(contact.Extension);
            contact.Email = Blank(contact.Email);

            var errors = ProviderDirectoryRules.ValidateContact(new SaveProviderContactRequest(
                contact.Name,
                contact.Role,
                contact.Phone,
                contact.Extension,
                contact.Email,
                contact.IsPrimary,
                contact.SortOrder));
            if (errors.Count > 0)
                throw new InvalidOperationException(errors.Values.SelectMany(messages => messages).First());

            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);

            // One "try this person first" per entry. Demoting the previous primary is what the
            // Admin means by promoting this one, so it happens here rather than being refused.
            if (contact.IsPrimary)
            {
                await context.ProviderContacts
                    .Where(other => other.ProviderId == contact.ProviderId &&
                                    other.Id != contact.Id && other.IsPrimary)
                    .ExecuteUpdateAsync(update => update.SetProperty(other => other.IsPrimary, false));
            }

            if (contact.Id == 0)
            {
                context.ProviderContacts.Add(contact);
                await context.SaveChangesAsync();
                await transaction.CommitAsync();
                return contact;
            }

            var tracked = await context.ProviderContacts.SingleOrDefaultAsync(
                    candidate => candidate.Id == contact.Id && candidate.ProviderId == contact.ProviderId)
                ?? throw new InvalidOperationException("That contact is no longer on this provider.");

            tracked.Name = contact.Name;
            tracked.Role = contact.Role;
            tracked.Phone = contact.Phone;
            tracked.Extension = contact.Extension;
            tracked.Email = contact.Email;
            tracked.IsPrimary = contact.IsPrimary;
            tracked.SortOrder = contact.SortOrder;
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return tracked;
        }

        // Removing one contact is ordinary editing, not directory curation: it takes away a phone
        // number, not an entry other people's consumers point at.
        public async Task RemoveContactAsync(int providerId, int contactId)
        {
            EnsureCanCreateOrEdit();
            await using var context = _contextFactory.CreateDbContext();
            await EnsureOwnedProviderAsync(context, providerId);

            var contact = await context.ProviderContacts.SingleOrDefaultAsync(
                    candidate => candidate.Id == contactId && candidate.ProviderId == providerId)
                ?? throw new InvalidOperationException("That contact is no longer on this provider.");

            context.ProviderContacts.Remove(contact);
            await context.SaveChangesAsync();
        }

        // ── Merge ────────────────────────────────────────────────────────────

        public async Task<string> MergeAsync(int survivingProviderId, int mergedProviderId)
        {
            var actor = CurrentActor();
            if (!ProviderDirectoryRules.CanDeleteOrMerge(actor.Permissions))
                throw new UnauthorizedAccessException(ProviderDirectoryRules.MergeRequiresAdminMessage);

            await using var context = _contextFactory.CreateDbContext();
            var actorIsCurrentAdmin = await context.Users.AsNoTracking().AnyAsync(user =>
                user.Id == actor.Id && user.AgencyId == actor.AgencyId &&
                (user.Permissions & UserPermissions.Administration) != 0);
            if (!actorIsCurrentAdmin)
                throw new UnauthorizedAccessException(ProviderDirectoryRules.MergeRequiresAdminMessage);

            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable);
            var agencyId = actor.AgencyId;

            var surviving = await context.Providers.SingleOrDefaultAsync(
                    p => p.Id == survivingProviderId && p.AgencyId == agencyId)
                ?? throw new InvalidOperationException("The surviving provider is outside the current agency.");
            var merged = await context.Providers.SingleOrDefaultAsync(
                    p => p.Id == mergedProviderId && p.AgencyId == agencyId)
                ?? throw new InvalidOperationException("The provider being merged is outside the current agency.");

            var problem = ProviderDirectoryRules.ValidateMerge(
                surviving.ToAffiliationNode(), merged.ToAffiliationNode());
            if (problem is not null)
                throw new InvalidOperationException(problem);

            GuardIdentifierConflict(surviving.Npi, merged.Npi, "National Provider Identifier");
            GuardIdentifierConflict(
                surviving.MaineCareProviderId, merged.MaineCareProviderId, "MaineCare provider identifier");

            // Merging an entry that sits above the survivor would leave the survivor inside its
            // own chain once its children are repointed.
            var directory = (await context.Providers.AsNoTracking()
                .Where(p => p.AgencyId == agencyId).ToListAsync()).ToAffiliationNodes();
            if (ProviderAffiliation.ResolveAncestors(surviving.Id, directory)
                .Any(node => node.Id == merged.Id))
            {
                throw new InvalidOperationException(ProviderDirectoryRules.MergeWouldCreateLoopMessage);
            }

            var duplicateCurrentLinks = await (
                from incoming in context.PersonProviders.AsNoTracking()
                join existing in context.PersonProviders.AsNoTracking()
                    on incoming.PersonId equals existing.PersonId
                where incoming.ProviderId == merged.Id && incoming.EndDate == null &&
                      existing.ProviderId == surviving.Id && existing.EndDate == null
                select incoming.PersonId).Distinct().CountAsync();
            if (duplicateCurrentLinks > 0)
            {
                throw new InvalidOperationException(
                    ProviderDirectoryRules.MergeConsumerLinkConflictMessage(duplicateCurrentLinks));
            }

            var affiliatedMoved = await context.Providers
                .Where(child => child.AgencyId == agencyId && child.ParentProviderId == merged.Id)
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(child => child.ParentProviderId, surviving.Id));

            var consumerLinksMoved = await context.PersonProviders
                .Where(link => link.ProviderId == merged.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(link => link.ProviderId, surviving.Id));

            var contactsMoved = await context.ProviderContacts
                .Where(contact => contact.ProviderId == merged.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(contact => contact.ProviderId, surviving.Id)
                    // The survivor's own primary keeps that place; an incoming one becomes an
                    // ordinary contact rather than two rows both claiming it.
                    .SetProperty(contact => contact.IsPrimary, false));

            await context.Settings
                .Where(settings => settings.AgencyId == agencyId &&
                                   settings.DefaultPassthroughProviderId == merged.Id)
                .ExecuteUpdateAsync(update =>
                    update.SetProperty(s => s.DefaultPassthroughProviderId, surviving.Id));

            // Nothing repoints AssessmentNeed.ProviderId. A document froze that entry on purpose,
            // and rewriting it would change what an approved assessment says.

            // Identifiers and affiliation are adopted only where the survivor has none, so a merge
            // never overwrites a fact somebody deliberately recorded on the surviving entry.
            surviving.Npi ??= merged.Npi;
            surviving.MaineCareProviderId ??= merged.MaineCareProviderId;
            surviving.ParentProviderId ??= merged.ParentProviderId == surviving.Id
                ? null
                : merged.ParentProviderId;

            context.Providers.Remove(merged);
            LocalAuditTrail.Record(
                context,
                actor,
                LocalAuditActions.ProviderMerged,
                "Provider",
                surviving.Id,
                JsonSerializer.Serialize(new
                {
                    mergedProviderId = merged.Id,
                    affiliatedMoved,
                    consumerLinksMoved,
                    contactsMoved
                }));
            await context.SaveChangesAsync();
            await transaction.CommitAsync();

            return ProviderDirectoryRules.MergeSummary(
                surviving.Name, merged.Name, affiliatedMoved, consumerLinksMoved, contactsMoved);
        }

        private static void GuardIdentifierConflict(string? surviving, string? merged, string which)
        {
            if (Blank(surviving) is { } left && Blank(merged) is { } right &&
                !string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    ProviderDirectoryRules.ConflictingIdentifierMessage(which));
            }
        }

        private async Task EnsureOwnedProviderAsync(SatiContext context, int providerId)
        {
            var owned = await context.Providers.AsNoTracking()
                .AnyAsync(p => p.Id == providerId && p.AgencyId == CurrentAgencyId());
            if (!owned)
                throw new InvalidOperationException("The provider is outside the current agency.");
        }

        private void EnsureCanCreateOrEdit()
        {
            if (!ProviderDirectoryRules.CanCreateOrEdit(CurrentPermissions()))
                throw new UnauthorizedAccessException(
                    "Your account cannot change the provider directory.");
        }

        private UserPermissions CurrentPermissions() =>
            _sessionService.CurrentUser?.Permissions
            ?? throw new InvalidOperationException("A signed-in user is required to access providers.");

        private User CurrentActor() => _sessionService.CurrentUser
            ?? throw new InvalidOperationException("A signed-in user is required to access providers.");

        private int CurrentAgencyId() => _sessionService.CurrentUser?.AgencyId
            ?? throw new InvalidOperationException("A signed-in user is required to access providers.");
    }
}
