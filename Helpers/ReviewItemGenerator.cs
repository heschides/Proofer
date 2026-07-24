using Sati.Models;

namespace Sati
{
    // Owns the "what review items should exist for this client in this cycle"
    // question. Pure function: (Person, cycleAnchor, existing items) → the items
    // that are missing. Stateless and static because Person has no ReviewItems
    // navigation — reviews are deliberately kept off the caseload load path, so
    // generation can't be an instance method the way EnsureCurrentCycleForms is.
    public static class ReviewItemGenerator
    {
        // Categories that always produce one item per quarter, unconditionally.
        private static readonly ReviewCategory[] UniversalCategories =
        [
            ReviewCategory.Medical,
            ReviewCategory.Dental,
            ReviewCategory.GoalReview
        ];

        // Returns the items required for the cycle but not present in `existing`.
        // Idempotent: called repeatedly with the same inputs, returns empty after
        // the first save. The unique index on
        // (PersonId, CycleAnchor, Quarter, Category, SlotIndex) is the backstop.
        public static IReadOnlyList<ReviewItem> GenerateMissing(
            Person person, DateTime cycleAnchor, IEnumerable<ReviewItem> existing)
        {
            var anchor = cycleAnchor.Date;

            // Only items for THIS cycle count as present. An item from last
            // cycle must not suppress generation of this cycle's equivalent.
            var present = existing
                .Where(r => r.CycleAnchor.Date == anchor)
                .Select(r => (r.Quarter, r.Category, r.SlotIndex))
                .ToHashSet();

            var required = BuildRequired(person);
            var missing = new List<ReviewItem>();

            foreach (var (quarter, category, slotIndex) in required)
            {
                if (present.Contains((quarter, category, slotIndex)))
                    continue;

                missing.Add(new ReviewItem(person.Id, anchor, quarter, category, slotIndex));
            }

            return missing;
        }

        // The full set of items a client should have for one cycle.
        private static List<(int Quarter, ReviewCategory Category, int SlotIndex)> BuildRequired(Person person)
        {
            var required = new List<(int, ReviewCategory, int)>();

            for (var quarter = 1; quarter <= 4; quarter++)
            {
                foreach (var category in UniversalCategories)
                    required.Add((quarter, category, 0));

                if (person.RequiresEmploymentTracking)
                    required.Add((quarter, ReviewCategory.Employment, 0));
            }

            required.AddRange(BuildNoteReviews(person));
            return required;
        }

        // Exactly four note reviews per cycle regardless of how many support
        // arrangements exist — the state requires four, not one per provider.
        // Arrangements are ordered home-then-community and assigned round-robin
        // to quarters. A client with more than four arrangements leaves some
        // unreviewed; that's compliant (any four suffice) and the UI marks it
        // with an asterisk rather than the model tracking the surplus.
        //
        // Clients with no reviewable arrangements — no supports, or self-directed
        // only, which is exempt — get no note review items at all.
        private static IEnumerable<(int Quarter, ReviewCategory Category, int SlotIndex)> BuildNoteReviews(Person person)
        {
            var arrangements = new List<(ReviewCategory Category, int SlotIndex)>();

            for (var i = 0; i < person.HomeNoteSlots; i++)
                arrangements.Add((ReviewCategory.NoteReviewHome, i));

            for (var i = 0; i < person.CommunityNoteSlots; i++)
                arrangements.Add((ReviewCategory.NoteReviewCommunity, i));

            if (arrangements.Count == 0)
                yield break;

            for (var quarter = 1; quarter <= 4; quarter++)
            {
                var (category, slotIndex) = arrangements[(quarter - 1) % arrangements.Count];
                yield return (quarter, category, slotIndex);
            }
        }
    }
}