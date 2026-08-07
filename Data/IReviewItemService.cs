using Sati.Models;

namespace Sati.Data
{
    public interface IReviewItemService
    {
        // All review items for a caseload. No Person include — the caller
        // already has People loaded and joins by PersonId in memory.
        Task<List<ReviewItem>> GetForCaseloadAsync(int userId);

        Task<List<ReviewItem>> GetForPersonAsync(int personId);

        // Generates any missing items for each person's current cycle.
        // Returns the number created.
        Task<int> EnsureCurrentCycleItemsAsync(IEnumerable<Person> people, DateTime today);

        // Sets or clears one workflow date. Returns the updated item so the
        // caller can refresh its in-memory copy.
        Task<ReviewItem> SetStageDateAsync(int reviewItemId, ReviewStage stage, DateTime? date);
        Task<ReviewItem> SetAppointmentAsync(int reviewItemId, DateTime? date, string? providerName);
        Task<(Appointment? Medical, Appointment? Dental)> GetLatestAppointmentsAsync(int personId);
    }
}