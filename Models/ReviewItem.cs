namespace Sati
{
    // The five things tracked per client per quarter. NoteReviewHome and
    // NoteReviewCommunity are distinct categories rather than one category with
    // a subtype, because the home/community split is the only provider
    // distinction that exists in data — which actual provider a slot refers to
    // lives with the case manager, not in Sati.
    public enum ReviewCategory
    {
        Employment,
        Medical,
        Dental,
        GoalReview,
        NoteReviewHome,
        NoteReviewCommunity
    }

    // Derived display state. Not stored — the dates are the source of truth.
    public enum ReviewStage
    {
        NotStarted,
        Requested,
        Received,
        Logged
    }

    // One tracked obligation: a client, a cycle quarter, a category, and a
    // three-stage workflow (Requested → Received → Logged) with a date per
    // stage. Deliberately NOT a Form: Forms are done/not-done against a
    // computed due date; this is a multi-stage workflow with its own audit
    // trail.
    public class ReviewItem
    {
        public int Id { get; private set; }
        public int PersonId { get; set; }
        public Person? Person { get; set; }

        // The anniversary date opening the cycle this item belongs to — the
        // same cycleStart that Person.GetCurrentCycleBoundaries returns. Stored
        // explicitly rather than derived from a year number so cycle identity
        // is unambiguous and can't drift from the Form convention.
        public DateTime CycleAnchor { get; private set; }

        // 1–4 within the cycle anchored above.
        public int Quarter { get; private set; }

        public ReviewCategory Category { get; private set; }

        public DateTime? RequestedDate { get; private set; }
        public DateTime? ReceivedDate { get; private set; }
        public DateTime? LoggedDate { get; private set; }

        // Slot index within its category for this cycle — 0 for single-slot
        // categories, 0..n for note reviews where a client has multiple
        // arrangements of the same type. Distinguishes two NoteReviewCommunity
        // items in the same quarter without needing a provider entity.
        public int SlotIndex { get; private set; }

        // EF materialization constructor.
        private ReviewItem() { }

        // The only birth path. Mirrors Form's generation constructor.
        public ReviewItem(int personId, DateTime cycleAnchor, int quarter,
                          ReviewCategory category, int slotIndex = 0)
        {
            if (quarter is < 1 or > 4)
                throw new ArgumentOutOfRangeException(nameof(quarter), quarter,
                    "Quarter must be 1 through 4.");

            PersonId = personId;
            CycleAnchor = cycleAnchor.Date;
            Quarter = quarter;
            Category = category;
            SlotIndex = slotIndex;
        }

        // -------------------------------------------------------------------------
        // Derived state
        // -------------------------------------------------------------------------

        public ReviewStage Stage =>
            LoggedDate.HasValue ? ReviewStage.Logged
            : ReceivedDate.HasValue ? ReviewStage.Received
            : RequestedDate.HasValue ? ReviewStage.Requested
            : ReviewStage.NotStarted;

        public bool IsComplete => LoggedDate.HasValue;

        // -------------------------------------------------------------------------
        // Sanctioned mutators
        // -------------------------------------------------------------------------
        //
        // The only writers of workflow state. Stages are independently settable
        // rather than strictly sequential: real life produces notes that arrive
        // unrequested and information logged from a hallway conversation. The UI
        // presents them in order; the model doesn't forbid gaps.

        public void MarkRequested(DateTime date) => RequestedDate = date.Date;
        public void MarkReceived(DateTime date) => ReceivedDate = date.Date;
        public void MarkLogged(DateTime date) => LoggedDate = date.Date;

        public void ClearRequested() => RequestedDate = null;
        public void ClearReceived() => ReceivedDate = null;
        public void ClearLogged() => LoggedDate = null;

        public void Reset()
        {
            RequestedDate = null;
            ReceivedDate = null;
            LoggedDate = null;
        }

        // -------------------------------------------------------------------------
        // Display
        // -------------------------------------------------------------------------

        public static string CategoryDisplayName(ReviewCategory category) => category switch
        {
            ReviewCategory.Employment => "Employment",
            ReviewCategory.Medical => "Medical",
            ReviewCategory.Dental => "Dental",
            ReviewCategory.GoalReview => "Goals",
            ReviewCategory.NoteReviewHome => "Home Notes",
            ReviewCategory.NoteReviewCommunity => "Community Notes",
            _ => category.ToString()
        };
    }
}