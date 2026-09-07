using Sati.Models;
using Sati.Models.Billing;
using Sati.Contracts.V1;

namespace Sati.Data.Billing
{
    public interface IBillingService
    {
        bool SupportsMockClearinghouse => false;
        Task<BillingPeriod> GetOrCreateBillingPeriodAsync(AgencyActor actor, int userId, int month, int year);
        Task<IEnumerable<BillingPeriod>> GetBillingPeriodsAsync(AgencyActor actor, int userId);
        Task<IEnumerable<BillingPeriod>> GetAllBillingPeriodsAsync(AgencyActor actor);
        Task<ClaimLine> CreateClaimLineAsync(AgencyActor actor, int noteId, bool isComplianceException = false, string? complianceExceptionReason = null);
        Task<IEnumerable<ClaimLine>> GetUnbilledClaimLinesAsync(AgencyActor actor, int userId);
        Task SubmitBillingPeriodAsync(AgencyActor actor, int billingPeriodId);
        Task ReturnBillingPeriodToDraftAsync(AgencyActor actor, int billingPeriodId);
        Task<IEnumerable<Note>> GetApprovedUnbilledNotesAsync(AgencyActor actor);
        BillingValidationResult ValidateNoteForBilling(Note note);
        Task<BillingConfiguration> GetBillingConfigurationAsync(AgencyActor actor);
        Task SaveBillingConfigurationAsync(AgencyActor actor, BillingConfiguration configuration);
        Task<IReadOnlyList<BillingSubmissionHistoryDto>> GetSubmissionHistoryAsync(AgencyActor actor);
        Task<MockClearinghouseResultDto> SubmitToMockClearinghouseAsync(
            AgencyActor actor,
            int billingPeriodId,
            MockClearinghouseScenario scenario) => Task.FromException<MockClearinghouseResultDto>(
                new NotSupportedException("The mock clearinghouse is available only in Demo."));
        Task<IReadOnlyList<RemittanceClaimOutcomeDto>> GetRemittanceOutcomesAsync(AgencyActor actor);
        Task<IReadOnlyList<RemittanceDepositDto>> GetRemittanceDepositsAsync(AgencyActor actor);
    }
}
