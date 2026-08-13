using Sati.Models;
using Sati.Models.Billing;

namespace Sati.Data.Billing
{
    public interface IBillingService
    {
        Task<BillingPeriod> GetOrCreateBillingPeriodAsync(int userId, int month, int year);
        Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(int userId);
        Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync();
        Task<ClaimLine> CreateClaimLineAsync(int noteId, bool isComplianceException = false, string? complianceExceptionReason = null);
        Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(int userId);
        Task SubmitBillingPeriodAsync(int billingPeriodId);
        Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync();
        BillingValidationResult ValidateNoteForBilling(Note note);
        Task<BillingConfiguration> GetBillingConfigurationAsync();
        Task SaveBillingConfigurationAsync(BillingConfiguration configuration);
    }
}
