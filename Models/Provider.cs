using Sati.Contracts.V1;

namespace Sati.Models
{
    // A service provider in the agency directory: waiver, healthcare, or other.
    // Reference data (shared, admin-curated in the multi-user future), so it's a
    // plain POCO like Agency — no factory, no snapshot. An ATRequest snapshots
    // FROM a provider at select-time; the provider itself stays the live source.
    public class Provider
    {
        public int Id { get; set; }
        public int AgencyId { get; set; }

        public ProviderType Type { get; set; } = ProviderType.Waiver;

        public string Name { get; set; } = string.Empty;

        // ── Affiliation ──────────────────────────────────────────────────────
        // Which tier a medical entry occupies, and the one link that expresses who
        // it belongs to. Null Kind on anything that is not healthcare.
        //
        // One parent rather than separate Practice and Network columns: two typed
        // columns cannot express a hospitalist who belongs to a network with no
        // practice between, and adding a network column to individuals to cover
        // that lets an individual's network disagree with their practice's. The
        // tier rule, the cycle guard, and the ancestor walk all live in
        // ProviderAffiliation so the desktop and the API cannot answer differently.
        //
        // ParentProviderId is deliberately NOT gated to healthcare in the schema.
        // Waiver providers have the same shape — an agency owning programs owning
        // staff — and the link is the part that cannot be retrofitted cheaply. Only
        // the Individual/Practice/Network vocabulary is medical, and vocabulary is
        // cheap to add later. See DECISIONS.md, 2026-08-28.
        public MedicalProviderKind? MedicalKind { get; set; }
        public int? ParentProviderId { get; set; }

        // ── Durable organization identity ────────────────────────────────────
        // A directory entry names a real organization that may later become a
        // tenant on the platform in its own right. When that happens the two
        // records must be recognized as the same organization and LINKED, never
        // merged or replaced — see DECISIONS.md, "Provider directory entries are
        // local knowledge about a shared organization".
        //
        // These identifiers are the only part of that design that cannot be added
        // retroactively: a name typed today with no identifier can only ever be
        // matched by fuzzy comparison. Both are optional because a directory entry
        // is often created from a phone call before any paperwork exists, and both
        // are recorded because either may be the one the organization actually
        // supplies.
        public string? Npi { get; set; }
        public string? MaineCareProviderId { get; set; }

        // Structured address, mirroring Agency (normalized reference data).
        public string? Street { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
        public string? Zip { get; set; }

        // General directory contact.
        public string? PrimaryContact { get; set; }
        public string? Phone { get; set; }

        // Waiver services offered. [Flags] bitmask — inert until client↔provider
        // links land, but modeled now per the provider-form design.
        public WaiverService OfferedServices { get; set; } = WaiverService.None;

        // Orthogonal to OfferedServices: whether this provider acts as a passthrough
        // agency for AT payment requests. Filters the AT dropdown and, in the
        // provider form, reveals the three fields below.
        public bool ProvidesPassthroughService { get; set; }

        // Passthrough billing fields — flat strings now, nav properties later.
        // These get copied onto an ATRequest's Vendor* snapshot when this provider
        // is selected. Unused unless ProvidesPassthroughService is true.
        public string? BillingLocationEis { get; set; }
        public string? ProgramContact { get; set; }
        public string? BillingContact { get; set; }
    }

    // The bridge from directory rows to the shape ProviderAffiliation reasons about.
    // Both the services and the editor go through here, so neither builds its own
    // reduced view of a provider and drifts from the other.
    public static class ProviderAffiliationExtensions
    {
        public static ProviderAffiliationNode ToAffiliationNode(this Provider provider) =>
            new(provider.Id, provider.Name, provider.ParentProviderId, provider.MedicalKind);

        public static List<ProviderAffiliationNode> ToAffiliationNodes(this IEnumerable<Provider> providers) =>
            providers.Select(ToAffiliationNode).ToList();
    }
}
