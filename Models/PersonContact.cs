using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sati.Models
{
    public enum PersonContactKind
    {
        [Description("Family or friend")]
        Personal,

        [Description("Guardian")]
        Guardian,

        [Description("Authorized representative")]
        AuthorizedRepresentative,

        [Description("Service provider")]
        ServiceProvider,

        /// <summary>
        /// A person to contact <em>at</em> a healthcare provider — an office manager, a care
        /// coordinator, a nurse who returns calls. Not the clinician as a provider: since
        /// 2026-08-28 that is a <see cref="PersonProvider"/> link to a directory entry, which
        /// carries the practice and network with it. Kept rather than retired because existing
        /// rows are real people somebody recorded, and because "who do I actually phone at that
        /// office" is a question the provider directory does not answer.
        /// </summary>
        [Description("Contact at a healthcare provider")]
        HealthcareProvider,

        [Description("Other support")]
        Other
    }

    /// <summary>
    /// A person in a consumer's support network. This is live profile data; notes
    /// snapshot selected attendees so later edits here never rewrite history.
    /// </summary>
    public sealed class PersonContact
    {
        public int Id { get; private set; }
        public int PersonId { get; set; }
        public Person Person { get; set; } = null!;

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public PersonContactKind Kind { get; set; } = PersonContactKind.Personal;
        public string? Relationship { get; set; }
        public string? Organization { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }

        public bool IsEmergencyContact { get; set; }
        public bool HasActiveRelease { get; set; }
        public bool IsActive { get; set; } = true;

        public static PersonContact Rehydrate(int id) => new() { Id = id };

        [NotMapped]
        public string FullName => $"{FirstName} {LastName}".Trim();

        [NotMapped]
        public string DisplayLabel
        {
            get
            {
                var role = !string.IsNullOrWhiteSpace(Relationship)
                    ? Relationship
                    : Kind.GetDescription();
                return string.IsNullOrWhiteSpace(role) ? FullName : $"{FullName} — {role}";
            }
        }
    }

    internal static class PersonContactEnumExtensions
    {
        public static string GetDescription(this PersonContactKind value)
        {
            var member = typeof(PersonContactKind).GetMember(value.ToString()).FirstOrDefault();
            return member?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                       .OfType<DescriptionAttribute>()
                       .FirstOrDefault()?.Description
                   ?? value.ToString();
        }
    }
}
