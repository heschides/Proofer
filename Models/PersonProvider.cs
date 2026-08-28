using System.ComponentModel.DataAnnotations.Schema;
using Sati.Contracts.V1;

namespace Sati.Models
{
    /// <summary>
    /// One consumer's relationship with a directory entry.
    /// <para>
    /// It stores the provider and the attributes of the <em>relationship</em> — the role,
    /// whether this is the primary care provider, when it began and ended, whether a
    /// release is on file — and deliberately stores <b>no copy of the practice or
    /// network</b>. Those are derived by walking <see cref="Provider.ParentProviderId"/>
    /// at read time, so correcting a directory entry corrects every profile that names it.
    /// Copying them here would mean a physician who changes practices leaves every profile
    /// showing the old one, silently and with no signal that it went stale.
    /// </para>
    /// <para>
    /// Live profile data, following the same split as <see cref="PersonContact"/>:
    /// documents snapshot the resolved chain at generation, this stays current. See
    /// DECISIONS.md, "A consumer's provider list stores the link, never the resolved chain".
    /// </para>
    /// </summary>
    public sealed class PersonProvider
    {
        public int Id { get; private set; }
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;

        /// <summary>
        /// Normally an individual clinician, but any tier is allowed: a consumer whose
        /// relationship is with a walk-in clinic rather than a named person selects the
        /// practice, and the derived chain simply starts higher.
        /// </summary>
        public int ProviderId { get; set; }

        /// <summary>What this provider is to the consumer — "Neurologist", "Dentist".</summary>
        public string? Role { get; set; }

        /// <summary>
        /// At most one may be true among a consumer's current links, enforced by
        /// <see cref="ConsumerProviderRules"/> and a filtered unique index rather than by
        /// the form remembering to clear the previous one.
        /// </summary>
        public bool IsPrimaryCare { get; set; }

        public DateTime? StartDate { get; set; }

        /// <summary>
        /// The single fact that says whether this relationship is current. There is no
        /// separate active flag: two columns meaning the same thing drift, and "who was
        /// treating her in 2024" is a question a case record has to be able to answer, so
        /// ending a relationship sets this rather than deleting the row.
        /// </summary>
        public DateTime? EndDate { get; set; }

        /// <summary>Whether a signed release covers talking to this provider.</summary>
        public bool HasActiveRelease { get; set; }

        /// <summary>The case manager's own ordering within the current list.</summary>
        public int SortOrder { get; set; }

        [NotMapped]
        public bool IsActive => ConsumerProviderRules.IsCurrent(EndDate);

        public static PersonProvider Rehydrate(int id) => new() { Id = id };
    }
}
