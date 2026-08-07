namespace Sati
{
    // The appointment recorded during a Medical or Dental quarterly check-in:
    // when the client last saw that provider, and who it was. Stored as its own
    // row rather than as columns on ReviewItem so the four categories that never
    // have an appointment (Goals, Employment, the two note reviews) carry no
    // meaningless null columns — a row exists here ONLY because an appointment
    // exists. One optional appointment per Medical/Dental ReviewItem; that item's
    // Category already says doctor vs. dentist, so this row needn't repeat it.
    public class Appointment
    {
        public int Id { get; private set; }

        // The Medical or Dental review item this appointment was recorded on.
        // Unique — one appointment per review item — enforced by the index in
        // OnModelCreating, which is what makes the relationship one-to-one rather
        // than one-to-many.
        public int ReviewItemId { get; private set; }
        public ReviewItem? ReviewItem { get; set; }

        // The date of the appointment itself — NOT a workflow stage date. A fact
        // about the client's care, distinct from the Requested/Received/Logged
        // dates that track the case manager's follow-up on the review item.
        public DateTime Date { get; private set; }

        // The doctor or dentist seen. Nullable because the date can be known
        // before the name is ("I saw my doctor last Tuesday"). This is optional
        // data, not a structural null: the row's existence is never in question.
        public string? ProviderName { get; private set; }

        // EF materialization constructor.
        private Appointment() { }

        // The only birth path. Mirrors ReviewItem's generation constructor.
        public Appointment(int reviewItemId, DateTime date, string? providerName = null)
        {
            ReviewItemId = reviewItemId;
            Date = date.Date;
            ProviderName = Normalize(providerName);
        }

        // The only writer of appointment state, mirroring ReviewItem's mutators —
        // the private setters stay reachable from the service layer alone.
        public void Update(DateTime date, string? providerName)
        {
            Date = date.Date;
            ProviderName = Normalize(providerName);
        }

        // Empty-to-null so a blanked-out provider box stores null, not "", keeping
        // "no name recorded" a single representable state.
        private static string? Normalize(string? name)
        {
            var trimmed = name?.Trim();
            return string.IsNullOrEmpty(trimmed) ? null : trimmed;
        }
    }
}