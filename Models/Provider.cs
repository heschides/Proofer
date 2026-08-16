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
}
