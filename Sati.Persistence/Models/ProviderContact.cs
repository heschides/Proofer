namespace Sati.Models
{
    /// <summary>
    /// A named person at a provider — the referral coordinator, the office manager, the nurse who
    /// returns calls.
    /// <para>
    /// Distinct from <see cref="Provider.PrimaryContact"/> and <see cref="Provider.Phone"/>, which
    /// are the organization's general directory contact: the main line you call when you do not
    /// need anybody in particular. Those are facts about the organization; these are facts about
    /// people who work there, and a directory entry usually accumulates several.
    /// </para>
    /// <para>
    /// Also distinct from <see cref="PersonContact"/> with
    /// <see cref="PersonContactKind.HealthcareProvider"/>, which is a person in one consumer's
    /// support network. A provider contact belongs to the shared agency directory and is visible
    /// to every case manager; it carries no consumer information for that reason.
    /// </para>
    /// </summary>
    public sealed class ProviderContact
    {
        public int Id { get; private set; }
        public int ProviderId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>What they do there — "Referral coordinator", "Billing".</summary>
        public string? Role { get; set; }

        public string? Phone { get; set; }
        public string? Extension { get; set; }
        public string? Email { get; set; }

        /// <summary>The one to try first. At most one per provider.</summary>
        public bool IsPrimary { get; set; }

        public int SortOrder { get; set; }

        public static ProviderContact Rehydrate(int id) => new() { Id = id };

        public string DisplayLabel => string.IsNullOrWhiteSpace(Role) ? Name : $"{Name} — {Role}";
    }
}
