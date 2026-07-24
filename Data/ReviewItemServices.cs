using Microsoft.EntityFrameworkCore;
using Sati.Models;

namespace Sati.Data
{
    public class ReviewItemService : IReviewItemService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;

        public ReviewItemService(IDbContextFactory<SatiContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<ReviewItem>> GetForCaseloadAsync(int userId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.ReviewItems
                .Where(r => r.Person!.UserId == userId)
                .ToListAsync();
        }

        public async Task<List<ReviewItem>> GetForPersonAsync(int personId)
        {
            await using var context = _contextFactory.CreateDbContext();
            return await context.ReviewItems
                .Where(r => r.PersonId == personId)
                .ToListAsync();
        }

        // Generation runs against one context for the whole caseload. Existing
        // items are fetched per person rather than in bulk because the caseload
        // is small and per-person keeps the generator's signature honest — it
        // reasons about one client at a time.
        public async Task<int> EnsureCurrentCycleItemsAsync(IEnumerable<Person> people, DateTime today)
        {
            await using var context = _contextFactory.CreateDbContext();
            var created = 0;

            foreach (var person in people)
            {
                var boundaries = person.GetCurrentCycleBoundaries(today);
                if (boundaries is null)
                    continue;

                var anchor = boundaries.Value.cycleStart;

                var existing = await context.ReviewItems
                    .Where(r => r.PersonId == person.Id && r.CycleAnchor == anchor)
                    .ToListAsync();

                var missing = ReviewItemGenerator.GenerateMissing(person, anchor, existing);
                if (missing.Count == 0)
                    continue;

                context.ReviewItems.AddRange(missing);
                created += missing.Count;
            }

            if (created > 0)
                await context.SaveChangesAsync();

            return created;
        }

        // Loads, mutates through the sanctioned mutators, saves. The item is
        // re-read here rather than attached from the caller's detached copy so
        // that the private setters stay the only write path and no stale
        // in-memory state gets written back over newer data.
        public async Task<ReviewItem> SetStageDateAsync(int reviewItemId, ReviewStage stage, DateTime? date)
        {
            await using var context = _contextFactory.CreateDbContext();

            var item = await context.ReviewItems.FindAsync(reviewItemId)
                ?? throw new InvalidOperationException($"ReviewItem {reviewItemId} not found.");

            switch (stage)
            {
                case ReviewStage.Requested:
                    if (date.HasValue) item.MarkRequested(date.Value); else item.ClearRequested();
                    break;
                case ReviewStage.Received:
                    if (date.HasValue) item.MarkReceived(date.Value); else item.ClearReceived();
                    break;
                case ReviewStage.Logged:
                    if (date.HasValue) item.MarkLogged(date.Value); else item.ClearLogged();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(stage), stage,
                        "NotStarted is a derived state, not a settable one.");
            }

            await context.SaveChangesAsync();
            return item;
        }
    }
}