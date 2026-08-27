using System.ComponentModel;

namespace Sati.Models
{
    public enum VisitSetting
    {
        [Description("Not documented")] NotDocumented,
        [Description("Consumer's home")] ConsumerHome,
        [Description("Community setting")] Community,
        [Description("Agency office")] AgencyOffice,
        [Description("Provider location")] ProviderLocation,
        [Description("Other")] Other
    }

    public enum VisitAppearance
    {
        [Description("Not documented")] NotDocumented,
        [Description("Neat and appropriately dressed")] NeatAndAppropriatelyDressed,
        [Description("Appearance concern observed")] ConcernObserved,
        [Description("Not observed")] NotObserved
    }

    public enum VisitParticipation
    {
        [Description("Not documented")] NotDocumented,
        [Description("Participated throughout")] ParticipatedThroughout,
        [Description("Participated with support")] ParticipatedWithSupport,
        [Description("Limited participation")] LimitedParticipation,
        [Description("Declined to participate")] Declined,
        [Description("Not assessed")] NotAssessed
    }

    public enum VisitSafetyObservation
    {
        [Description("Not documented")] NotDocumented,
        [Description("No health or safety concerns observed")] NoConcernsObserved,
        [Description("Health or safety concern observed")] ConcernObserved,
        [Description("Not assessed")] NotAssessed
    }

    public enum VisitPresence
    {
        [Description("Not documented")] NotDocumented,
        [Description("Present")] Present,
        [Description("Not present")] NotPresent
    }

    /// <summary>
    /// Immutable-by-convention attendee data stored with a note. SourceContactId
    /// permits an editor to re-select a current contact, while the text snapshot is
    /// the historical record if that profile contact later changes or is archived.
    /// </summary>
    public sealed class VisitAttendeeSnapshot
    {
        public int? SourceContactId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Role { get; set; }
        public string? Organization { get; set; }
    }

    /// <summary>
    /// Structured facts explicitly selected by a case manager for a Visit note.
    /// Stored as JSON on Note because it is a small note-owned snapshot and is not
    /// shared reference data.
    /// </summary>
    public sealed class VisitDocumentation
    {
        // The singular values are retained for notes written before multi-select
        // Visit controls existed. New notes also write the collections below; a
        // current client prefers those collections and falls back to the singular
        // value when it opens older JSON.
        public VisitSetting Setting { get; set; }
        public VisitAppearance Appearance { get; set; }
        public VisitParticipation Participation { get; set; }
        public VisitSafetyObservation SafetyObservation { get; set; }
        public List<VisitSetting> Settings { get; set; } = [];
        public List<VisitAppearance> Appearances { get; set; } = [];
        public List<VisitParticipation> Participations { get; set; } = [];
        public List<VisitSafetyObservation> SafetyObservations { get; set; } = [];

        // Nullable preserves existing JSON booleans while making new documentation explicit:
        // null means the case manager did not document presence either way.
        public bool? ConsumerPresent { get; set; }
        public bool ExpressedPreferences { get; set; }
        public bool AskedQuestions { get; set; }
        public bool MadeChoices { get; set; }
        public bool CommunicationSupportUsed { get; set; }
        public bool GoalsReviewed { get; set; }
        public bool ServicesDiscussed { get; set; }
        public bool DocumentsReviewed { get; set; }

        public string? SettingDetails { get; set; }
        public string? ObservationDetails { get; set; }
        public string? AdditionalAttendees { get; set; }
        public List<VisitAttendeeSnapshot> Attendees { get; set; } = [];

        public IReadOnlyList<VisitSetting> EffectiveSettings() =>
            EffectiveSelections(Settings, Setting, VisitSetting.NotDocumented);

        public IReadOnlyList<VisitAppearance> EffectiveAppearances() =>
            EffectiveSelections(Appearances, Appearance, VisitAppearance.NotDocumented);

        public IReadOnlyList<VisitParticipation> EffectiveParticipations() =>
            EffectiveSelections(Participations, Participation, VisitParticipation.NotDocumented);

        public IReadOnlyList<VisitSafetyObservation> EffectiveSafetyObservations() =>
            EffectiveSelections(SafetyObservations, SafetyObservation, VisitSafetyObservation.NotDocumented);

        private static IReadOnlyList<T> EffectiveSelections<T>(
            IEnumerable<T>? selections,
            T legacySelection,
            T notDocumented)
            where T : struct, Enum
        {
            var documented = (selections ?? [])
                .Where(value => Enum.IsDefined(value) && !EqualityComparer<T>.Default.Equals(value, notDocumented))
                .Distinct()
                .ToList();

            if (documented.Count > 0)
                return documented;

            return Enum.IsDefined(legacySelection) &&
                   !EqualityComparer<T>.Default.Equals(legacySelection, notDocumented)
                ? [legacySelection]
                : [];
        }
    }
}
